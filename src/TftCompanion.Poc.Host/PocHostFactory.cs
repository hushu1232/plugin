using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Core.Session;
using TftCompanion.Poc.Core.Storage;
using TftCompanion.Poc.Host.Channels;

namespace TftCompanion.Poc.Host;

/// <summary>
/// The v0.0.1 loopback compatibility Host. It contains no game strategy and
/// never accepts raw GEP payloads into its runtime state. The two physical
/// WebSocket routes share a verified Bridge runtime identity, but retain
/// independent bounded mailboxes so ingress pressure cannot block HideAll.
/// </summary>
public sealed class PocHostFactory : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly object stateGate = new();
    private readonly SemaphoreSlim renderDispatchGate = new(initialCount: 1, maxCount: 1);
    private readonly CancellationTokenSource shutdown = new();
    private readonly PocHostOptions options;
    private readonly StorageRootPolicy storageRootPolicy;
    private readonly ProtocolHandshake handshake;
    private readonly RenderMailbox renderMailbox;
    private readonly RenderLeaseManager leaseManager = new();
    private readonly FreshnessTracker freshnessTracker = new(() => DateTimeOffset.UtcNow);
    private readonly GapDetector gapDetector = new();
    private readonly string runtimeInstanceId = Guid.NewGuid().ToString("N");

    private WebApplication? app;
    private RuntimeContext? runtime;
    private ActiveRenderConnection? activeRender;
    private long runtimeEpoch;
    private long renderConnectionEpoch;
    private DateTimeOffset lastStatusPersistedAt = DateTimeOffset.MinValue;

    public PocHostFactory(
        PocHostOptions options,
        IStorageFileSystem storageFileSystem)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(storageFileSystem);

        if (options.Port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Port must be between 0 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.AllowedOrigin))
        {
            throw new ArgumentException("An exact allowed origin is required.", nameof(options));
        }

        if (!PairingTokenValidator.IsValid(options.PairingToken))
        {
            throw new ArgumentException("A 32-byte base64url pairing token is required.", nameof(options));
        }

        this.options = options;
        storageRootPolicy = new StorageRootPolicy(storageFileSystem);
        handshake = new ProtocolHandshake(options.AllowedOrigin, options.PairingToken);
        renderMailbox = new RenderMailbox(ProtocolConstants.RenderMailboxCapacity);
    }

    public int Port { get; private set; }

    public StorageHealth StorageHealth => storageRootPolicy.Health;

    public async Task StartAsync()
    {
        if (app is not null)
        {
            throw new InvalidOperationException("The PoC Host has already been started.");
        }

        storageRootPolicy.Initialize();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(IPAddress.Loopback, options.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });

        app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/ingest", HandleIngestRouteAsync);
        app.MapGet("/render", HandleRenderRouteAsync);

        await app.StartAsync();
        Port = ExtractAssignedPort();
        PersistSanitizedStatus(force: true);
    }

    public async Task StopAsync()
    {
        shutdown.Cancel();

        if (app is not null)
        {
            await app.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();

        if (app is not null)
        {
            await app.DisposeAsync();
            app = null;
        }

        renderDispatchGate.Dispose();
        shutdown.Dispose();
    }

    private int ExtractAssignedPort()
    {
        IServer server = app!.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
        string address = addresses?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not expose a loopback address.");

        return new Uri(address).Port;
    }

    private async Task HandleIngestRouteAsync(HttpContext context)
    {
        if (!TryValidateUpgradeRequest(context))
        {
            return;
        }

        WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        IngestConnection? connection = null;
        ConnectionRateLimiter rateLimiter = CreateConnectionRateLimiter();

        try
        {
            HelloMessage hello = await ReceiveHelloAsync(socket, ChannelKind.Ingest, rateLimiter);
            if (!handshake.TryValidateHello(hello, ChannelKind.Ingest, out string failureCode))
            {
                await RejectAndCloseAsync(socket, failureCode);
                return;
            }

            connection = await AttachIngestAsync(hello);
            await SendJsonAsync(socket, new
            {
                type = "welcome",
                sessionId = connection.SessionId,
                runtimeInstanceId,
                connectionEpoch = connection.Epoch,
                protocolVersion = ProtocolConstants.ProtocolVersion,
                schemaVersion = ProtocolConstants.SchemaVersion,
                channel = "ingest",
                resyncRequired = true
            });

            while (socket.State == WebSocketState.Open)
            {
                IngressMessage snapshot = await ReceiveSnapshotAsync(socket, rateLimiter);
                await ProcessSnapshotAsync(socket, connection, snapshot);
            }
        }
        catch (ProtocolViolationException violation)
        {
            await RejectAndCloseAsync(socket, violation.FailureCode);
        }
        catch (WebSocketException)
        {
            // A disconnected peer has no state authority and is handled below.
        }
        finally
        {
            if (connection is not null)
            {
                await DetachIngestAsync(connection);
            }

            await CloseIfOpenAsync(socket);
        }
    }

    private async Task HandleRenderRouteAsync(HttpContext context)
    {
        if (!TryValidateUpgradeRequest(context))
        {
            return;
        }

        WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        ActiveRenderConnection? connection = null;
        ConnectionRateLimiter rateLimiter = CreateConnectionRateLimiter();

        try
        {
            HelloMessage hello = await ReceiveHelloAsync(socket, ChannelKind.Render, rateLimiter);
            if (!handshake.TryValidateHello(hello, ChannelKind.Render, out string failureCode))
            {
                await RejectAndCloseAsync(socket, failureCode);
                return;
            }

            if (!TryAttachRender(socket, hello, out connection, out failureCode) || connection is null)
            {
                await RejectAndCloseAsync(socket, failureCode);
                return;
            }

            await SendJsonAsync(socket, new
            {
                type = "welcome",
                sessionId = connection.SessionId,
                runtimeInstanceId,
                connectionEpoch = connection.ConnectionEpoch,
                protocolVersion = ProtocolConstants.ProtocolVersion,
                schemaVersion = ProtocolConstants.SchemaVersion,
                channel = "render",
                renderLeaseId = connection.RenderLeaseId,
                resyncRequired = true
            });

            if (!await IssueHideAllAsync(connection, allowInactiveConnection: false))
            {
                throw new ProtocolViolationException("RENDER_MAILBOX_UNAVAILABLE");
            }

            while (socket.State == WebSocketState.Open)
            {
                RendererReceipt receipt = await ReceiveReceiptAsync(socket, rateLimiter);
                if (!TryApplyReceipt(connection, receipt, out bool shouldShow))
                {
                    await RejectAndCloseAsync(socket, "RECEIPT_IDENTITY_REJECTED");
                    return;
                }

                if (shouldShow && !await IssueShowMarkerAsync(connection))
                {
                    throw new ProtocolViolationException("RENDER_MAILBOX_UNAVAILABLE");
                }
            }
        }
        catch (ProtocolViolationException violation)
        {
            await RejectAndCloseAsync(socket, violation.FailureCode);
        }
        catch (WebSocketException)
        {
            // A closed renderer cannot advance any lease.
        }
        finally
        {
            if (connection is not null)
            {
                DetachRender(connection);
            }

            await CloseIfOpenAsync(socket);
        }
    }

    private bool TryValidateUpgradeRequest(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        if (!IPAddress.Loopback.Equals(context.Connection.RemoteIpAddress) ||
            context.Request.QueryString.HasValue ||
            !string.Equals(
                context.Request.Headers.Origin.ToString(),
                options.AllowedOrigin,
                StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        return true;
    }

    private static ConnectionRateLimiter CreateConnectionRateLimiter() => new(
        ProtocolConstants.MaximumMessagesPerSecond,
        TimeSpan.FromSeconds(1),
        () => DateTimeOffset.UtcNow);

    private async Task<HelloMessage> ReceiveHelloAsync(
        WebSocket socket,
        ChannelKind expectedChannel,
        ConnectionRateLimiter rateLimiter)
    {
        string text = await ReceiveTextFrameWithTimeoutAsync(socket, rateLimiter);
        if (!TryParseHello(text, out HelloMessage? hello, out string failureCode) ||
            !handshake.TryValidateChannelMessage(expectedChannel, "hello", out failureCode))
        {
            throw new ProtocolViolationException(failureCode);
        }

        return hello!;
    }

    private async Task<IngressMessage> ReceiveSnapshotAsync(
        WebSocket socket,
        ConnectionRateLimiter rateLimiter)
    {
        string text = await ReceiveTextFrameAsync(socket, shutdown.Token, rateLimiter);
        if (!TryParseSnapshot(text, out IngressMessage? snapshot, out string failureCode) ||
            !handshake.TryValidateChannelMessage(ChannelKind.Ingest, "stateSnapshot", out failureCode))
        {
            throw new ProtocolViolationException(failureCode);
        }

        return snapshot!;
    }

    private async Task<RendererReceipt> ReceiveReceiptAsync(
        WebSocket socket,
        ConnectionRateLimiter rateLimiter)
    {
        string text = await ReceiveTextFrameAsync(socket, shutdown.Token, rateLimiter);
        if (!TryParseReceipt(text, out RendererReceipt? receipt, out string failureCode) ||
            !handshake.TryValidateChannelMessage(ChannelKind.Render, "receipt", out failureCode))
        {
            throw new ProtocolViolationException(failureCode);
        }

        return receipt!;
    }

    private async Task<string> ReceiveTextFrameWithTimeoutAsync(
        WebSocket socket,
        ConnectionRateLimiter rateLimiter)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            return await ReceiveTextFrameAsync(socket, timeout.Token, rateLimiter);
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        {
            throw new ProtocolViolationException("HELLO_TIMEOUT");
        }
    }

    private async Task<string> ReceiveTextFrameAsync(
        WebSocket socket,
        CancellationToken cancellationToken,
        ConnectionRateLimiter rateLimiter)
    {
        byte[] buffer = new byte[Math.Min(8192, ProtocolConstants.MaximumTextFrameBytes)];
        int totalRead = 0;

        while (true)
        {
            if (totalRead == buffer.Length)
            {
                if (totalRead >= ProtocolConstants.MaximumTextFrameBytes)
                {
                    throw new ProtocolViolationException("FRAME_OVERSIZED");
                }

                Array.Resize(
                    ref buffer,
                    Math.Min(buffer.Length * 2, ProtocolConstants.MaximumTextFrameBytes));
            }

            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new ProtocolViolationException("BINARY_FRAME_REJECTED");
            }

            totalRead += result.Count;
            if (!handshake.TryValidateTextFrame(totalRead, out string failureCode))
            {
                throw new ProtocolViolationException(failureCode);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        try
        {
            if (!rateLimiter.TryAccept())
            {
                throw new ProtocolViolationException("RATE_LIMITED");
            }

            return StrictUtf8.GetString(buffer, 0, totalRead);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolViolationException("UTF8_INVALID");
        }
    }

    private async Task<IngestConnection> AttachIngestAsync(HelloMessage hello)
    {
        ActiveRenderConnection? previousRender;
        IngestConnection connection;

        lock (stateGate)
        {
            runtimeEpoch++;
            previousRender = activeRender;
            activeRender = null;

            if (previousRender is not null)
            {
                leaseManager.TerminateLease(previousRender.RenderLeaseId, previousRender.LeaseEpoch);
            }

            freshnessTracker.Reset();
            gapDetector.Reset();
            runtime = new RuntimeContext(
                sessionId: Guid.NewGuid().ToString("N"),
                bridgeInstanceId: hello.BridgeInstanceId!,
                epoch: runtimeEpoch);
            connection = new IngestConnection(
                runtime.SessionId,
                runtime.Epoch,
                new IngressMailbox(ProtocolConstants.IngressMailboxCapacity, runtime.SessionId));
        }

        if (previousRender is not null)
        {
            await IssueHideAllAsync(previousRender, allowInactiveConnection: true);
        }

        PersistSanitizedStatus(force: false);
        return connection;
    }

    private bool TryAttachRender(
        WebSocket socket,
        HelloMessage hello,
        out ActiveRenderConnection? connection,
        out string failureCode)
    {
        ActiveRenderConnection? previousRender;

        lock (stateGate)
        {
            if (runtime is null || !runtime.IngestOnline)
            {
                connection = null;
                failureCode = "INGEST_SESSION_REQUIRED";
                return false;
            }

            if (!string.Equals(runtime.BridgeInstanceId, hello.BridgeInstanceId, StringComparison.Ordinal))
            {
                connection = null;
                failureCode = "BRIDGE_INSTANCE_REJECTED";
                return false;
            }

            previousRender = activeRender;
            if (previousRender is not null)
            {
                leaseManager.TerminateLease(previousRender.RenderLeaseId, previousRender.LeaseEpoch);
            }

            long leaseEpoch = Interlocked.Increment(ref renderConnectionEpoch);
            RenderLease lease = leaseManager.CreateLease(leaseEpoch);
            connection = new ActiveRenderConnection(
                socket,
                runtime.SessionId,
                lease.LeaseId,
                leaseEpoch,
                minimumSnapshotRevision: runtime.SnapshotRevision + 1);
            activeRender = connection;
            failureCode = "NONE";
        }

        if (previousRender is not null)
        {
            _ = IssueHideAllAsync(previousRender, allowInactiveConnection: true);
        }

        PersistSanitizedStatus(force: false);
        return true;
    }

    private async Task ProcessSnapshotAsync(
        WebSocket ingressSocket,
        IngestConnection connection,
        IngressMessage snapshot)
    {
        if (!string.Equals(snapshot.SessionId, connection.SessionId, StringComparison.Ordinal))
        {
            throw new ProtocolViolationException("SESSION_MISMATCH");
        }

        if (!connection.Mailbox.TryWrite(new IngressEnvelope(connection.SessionId, snapshot)))
        {
            await MarkResyncRequiredAsync(ingressSocket, connection);
            return;
        }

        ActiveRenderConnection? renderToShow = null;
        bool resyncRequired = false;
        while (connection.Mailbox.TryRead(out IngressEnvelope? envelope))
        {
            if (envelope is null)
            {
                continue;
            }

            // Defense-in-depth: drop any envelope whose session does not match
            // the current connection. The session-scoped mailbox should already
            // prevent foreign-session envelopes from entering, but this check
            // ensures they can never be drained into the wrong runtime.
            if (!string.Equals(envelope.SessionId, connection.SessionId, StringComparison.Ordinal))
            {
                continue;
            }

            lock (stateGate)
            {
                if (runtime is null ||
                    runtime.SessionId != connection.SessionId ||
                    !runtime.IngestOnline ||
                    envelope.Snapshot.Sequence <= runtime.LastSequence)
                {
                    resyncRequired = true;
                    continue;
                }

                bool observedGap = runtime.LastSequence > 0 &&
                    envelope.Snapshot.Sequence > runtime.LastSequence + 1;
                gapDetector.RecordSequenceNumber(envelope.Snapshot.Sequence);

                // The gap-detecting frame is evidence of a discontinuity, not
                // evidence that the discontinuity has already been repaired.
                // Record its sequence so that only the next contiguous
                // authoritative snapshot can recover the stream, then force a
                // hide/resync before admitting further facts.
                if (observedGap)
                {
                    runtime.LastSequence = envelope.Snapshot.Sequence;
                    runtime.ResyncRequired = true;
                    resyncRequired = true;
                    break;
                }

                if (!envelope.Snapshot.IsAuthoritativeSnapshot)
                {
                    resyncRequired = true;
                    continue;
                }

                runtime.LastSequence = envelope.Snapshot.Sequence;
                runtime.SnapshotRevision++;
                runtime.MatchObserved = envelope.Snapshot.MatchObserved;
                runtime.RoundObserved = envelope.Snapshot.RoundObserved;
                runtime.ResyncRequired = false;
                freshnessTracker.RecordObservation();

                if (CanShowMarkerLocked(activeRender))
                {
                    renderToShow = activeRender;
                }
            }
        }

        if (resyncRequired)
        {
            await MarkResyncRequiredAsync(ingressSocket, connection);
            return;
        }

        if (renderToShow is not null)
        {
            await IssueShowMarkerAsync(renderToShow);
        }

        ScheduleFreshnessExpiry(connection.SessionId, CaptureSnapshotRevision(connection.SessionId));
        PersistSanitizedStatus(force: false);
    }

    private async Task MarkResyncRequiredAsync(WebSocket ingressSocket, IngestConnection connection)
    {
        ActiveRenderConnection? renderToHide = null;
        lock (stateGate)
        {
            if (runtime is not null && runtime.SessionId == connection.SessionId)
            {
                runtime.ResyncRequired = true;
                renderToHide = activeRender;
            }
        }

        await SendJsonAsync(ingressSocket, new { type = "resyncRequired" });
        if (renderToHide is not null)
        {
            await IssueHideAllAsync(renderToHide, allowInactiveConnection: false);
        }

        PersistSanitizedStatus(force: false);
    }

    private bool TryApplyReceipt(
        ActiveRenderConnection connection,
        RendererReceipt receipt,
        out bool shouldShow)
    {
        shouldShow = false;

        lock (stateGate)
        {
            if (activeRender != connection ||
                runtime is null ||
                receipt.RuntimeInstanceId != runtimeInstanceId ||
                receipt.ConnectionEpoch != connection.ConnectionEpoch ||
                receipt.SessionId != connection.SessionId ||
                receipt.RenderLeaseId != connection.RenderLeaseId)
            {
                return false;
            }

            if (receipt.ReceiptType == "hidden")
            {
                if (!string.Equals(receipt.CommandId, connection.PendingHideCommandId, StringComparison.Ordinal) ||
                    receipt.CommandSequence != connection.PendingHideCommandSequence)
                {
                    return false;
                }

                leaseManager.ConfirmHidden(connection.RenderLeaseId, connection.LeaseEpoch);
                connection.PendingHideCommandId = null;
                connection.PendingHideCommandSequence = null;
                connection.HiddenConfirmed = true;
                shouldShow = CanShowMarkerLocked(connection);
                return true;
            }

            if (receipt.ReceiptType == "shown")
            {
                if (!string.Equals(receipt.CommandId, connection.PendingShowCommandId, StringComparison.Ordinal) ||
                    receipt.CommandSequence != connection.PendingShowCommandSequence)
                {
                    return false;
                }

                leaseManager.ConfirmShown(connection.RenderLeaseId, connection.LeaseEpoch);
                connection.PendingShowCommandId = null;
                connection.PendingShowCommandSequence = null;
                return true;
            }

            return false;
        }
    }

    private async Task<bool> IssueHideAllAsync(
        ActiveRenderConnection connection,
        bool allowInactiveConnection)
    {
        string commandId;
        long commandSequence;
        lock (stateGate)
        {
            if (!allowInactiveConnection && activeRender != connection)
            {
                return false;
            }

            if (!leaseManager.TryIssueHideAll(connection.RenderLeaseId, connection.LeaseEpoch, out _) &&
                !allowInactiveConnection)
            {
                return false;
            }

            commandId = Guid.NewGuid().ToString("N");
            commandSequence = ++connection.LastCommandSequence;
            connection.PendingHideCommandId = commandId;
            connection.PendingHideCommandSequence = commandSequence;
            connection.PendingShowCommandId = null;
            connection.PendingShowCommandSequence = null;
            connection.HiddenConfirmed = false;
        }

        return await DispatchRenderCommandAsync(
            connection,
            "hideAll",
            commandId,
            commandSequence,
            allowInactiveConnection);
    }

    private async Task<bool> IssueShowMarkerAsync(ActiveRenderConnection connection)
    {
        string commandId;
        long commandSequence;
        lock (stateGate)
        {
            if (!CanShowMarkerLocked(connection) || connection.PendingShowCommandId is not null)
            {
                return true;
            }

            if (!leaseManager.TryShowMarker(connection.RenderLeaseId, connection.LeaseEpoch, out _))
            {
                return false;
            }

            commandId = Guid.NewGuid().ToString("N");
            commandSequence = ++connection.LastCommandSequence;
            connection.PendingShowCommandId = commandId;
            connection.PendingShowCommandSequence = commandSequence;
        }

        return await DispatchRenderCommandAsync(
            connection,
            "showMarker",
            commandId,
            commandSequence,
            allowInactiveConnection: false);
    }

    private async Task<bool> DispatchRenderCommandAsync(
        ActiveRenderConnection connection,
        string commandType,
        string commandId,
        long commandSequence,
        bool allowInactiveConnection)
    {
        await renderDispatchGate.WaitAsync(shutdown.Token);
        try
        {
            lock (stateGate)
            {
                if (!allowInactiveConnection && activeRender != connection)
                {
                    return false;
                }
            }

            bool queued = commandType switch
            {
                "hideAll" => renderMailbox.TryWriteHideAll(
                    runtimeInstanceId,
                    connection.ConnectionEpoch,
                    commandSequence,
                    connection.SessionId,
                    connection.RenderLeaseId,
                    commandId),
                "showMarker" => renderMailbox.TryWriteShowMarker(
                    runtimeInstanceId,
                    connection.ConnectionEpoch,
                    commandSequence,
                    connection.SessionId,
                    connection.RenderLeaseId,
                    commandId),
                _ => false
            };
            if (!queued || !renderMailbox.TryRead(out RenderCommandEnvelope? command) || command is null)
            {
                return false;
            }

            await SendJsonAsync(connection.Socket, new
            {
                type = command.CommandType,
                runtimeInstanceId = command.RuntimeInstanceId,
                connectionEpoch = command.ConnectionEpoch,
                commandSequence = command.CommandSequence,
                sessionId = command.SessionId,
                renderLeaseId = command.RenderLeaseId,
                commandId = command.CommandId
            });
            return true;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return false;
        }
        catch (WebSocketException)
        {
            return false;
        }
        finally
        {
            renderDispatchGate.Release();
        }
    }

    private async Task DetachIngestAsync(IngestConnection connection)
    {
        ActiveRenderConnection? renderToHide = null;
        lock (stateGate)
        {
            if (runtime is not null && runtime.SessionId == connection.SessionId && runtime.Epoch == connection.Epoch)
            {
                runtime.IngestOnline = false;
                runtime.ResyncRequired = true;
                renderToHide = activeRender;
            }
        }

        if (renderToHide is not null)
        {
            await IssueHideAllAsync(renderToHide, allowInactiveConnection: false);
        }

        PersistSanitizedStatus(force: false);
    }

    private void DetachRender(ActiveRenderConnection connection)
    {
        lock (stateGate)
        {
            if (activeRender == connection)
            {
                leaseManager.TerminateLease(connection.RenderLeaseId, connection.LeaseEpoch);
                activeRender = null;
                if (runtime is not null && runtime.SessionId == connection.SessionId)
                {
                    runtime.ResyncRequired = true;
                }
            }
        }

        PersistSanitizedStatus(force: false);
    }

    private bool CanShowMarkerLocked(ActiveRenderConnection? connection) =>
        connection is not null &&
        activeRender == connection &&
        runtime is not null &&
        runtime.SessionId == connection.SessionId &&
        runtime.IngestOnline &&
        !runtime.ResyncRequired &&
        connection.HiddenConfirmed &&
        runtime.SnapshotRevision >= connection.MinimumSnapshotRevision &&
        runtime.MatchObserved &&
        runtime.RoundObserved &&
        freshnessTracker.CurrentFreshness == FreshnessKind.Fresh;

    private long CaptureSnapshotRevision(string sessionId)
    {
        lock (stateGate)
        {
            return runtime is not null && runtime.SessionId == sessionId
                ? runtime.SnapshotRevision
                : 0;
        }
    }

    private void ScheduleFreshnessExpiry(string sessionId, long snapshotRevision)
    {
        _ = ExpireFreshnessAsync(sessionId, snapshotRevision);
    }

    private async Task ExpireFreshnessAsync(string sessionId, long snapshotRevision)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(ProtocolConstants.FreshnessTtlSeconds + 1),
                shutdown.Token);

            ActiveRenderConnection? renderToHide = null;
            lock (stateGate)
            {
                if (runtime is null ||
                    runtime.SessionId != sessionId ||
                    runtime.SnapshotRevision != snapshotRevision ||
                    freshnessTracker.CurrentFreshness != FreshnessKind.Stale)
                {
                    return;
                }

                runtime.ResyncRequired = true;
                renderToHide = activeRender;
            }

            if (renderToHide is not null)
            {
                await IssueHideAllAsync(renderToHide, allowInactiveConnection: false);
            }

            PersistSanitizedStatus(force: false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private void PersistSanitizedStatus(bool force)
    {
        SanitizedPocStatus status;
        lock (stateGate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!force && now - lastStatusPersistedAt < TimeSpan.FromSeconds(ProtocolConstants.StatusWriteIntervalSeconds))
            {
                return;
            }

            lastStatusPersistedAt = now;
            status = new SanitizedPocStatus(
                RuntimeEpoch: runtimeEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                BridgeOnline: runtime?.IngestOnline == true,
                RenderOnline: activeRender is not null,
                MatchObserved: runtime?.MatchObserved == true,
                RoundObserved: runtime?.RoundObserved == true,
                Freshness: freshnessTracker.CurrentFreshness.ToString(),
                GapState: runtime?.ResyncRequired == true ? "ResyncRequired" : gapDetector.CurrentGapState.ToString(),
                LastErrorCode: storageRootPolicy.Health == StorageHealth.Available ? "NONE" : "STORAGE_DEGRADED");
        }

        storageRootPolicy.TryPersist(status);
    }

    private static bool TryParseHello(string text, out HelloMessage? hello, out string failureCode)
    {
        hello = null;
        if (!TryParseRoot(text, "hello", HelloProperties, out JsonElement root, out failureCode) ||
            !TryGetInt32(root, "gameId", out int gameId) ||
            !TryGetInt32(root, "protocolVersion", out int protocolVersion) ||
            !TryGetInt32(root, "schemaVersion", out int schemaVersion) ||
            !TryGetString(root, "channel", out string channelText) ||
            !TryGetString(root, "origin", out string origin) ||
            !TryGetOptionalString(root, "pairingProof", out string? pairingProof) ||
            !TryGetString(root, "bridgeInstanceId", out string bridgeInstanceId))
        {
            failureCode = failureCode == "NONE" ? "HELLO_MALFORMED" : failureCode;
            return false;
        }

        ChannelKind channel = channelText switch
        {
            "ingest" => ChannelKind.Ingest,
            "render" => ChannelKind.Render,
            _ => (ChannelKind)(-1)
        };
        if (channel is not ChannelKind.Ingest and not ChannelKind.Render)
        {
            failureCode = "CHANNEL_MISMATCH";
            return false;
        }

        hello = new HelloMessage(
            gameId,
            protocolVersion,
            schemaVersion,
            channel,
            origin,
            pairingProof,
            bridgeInstanceId);
        failureCode = "NONE";
        return true;
    }

    private static bool TryParseSnapshot(string text, out IngressMessage? snapshot, out string failureCode)
    {
        snapshot = null;
        if (!TryParseRoot(text, "stateSnapshot", SnapshotProperties, out JsonElement root, out failureCode) ||
            !TryGetString(root, "sessionId", out string sessionId) ||
            !TryGetInt64(root, "sequence", out long sequence) ||
            !TryGetBoolean(root, "matchObserved", out bool matchObserved) ||
            !TryGetBoolean(root, "roundObserved", out bool roundObserved) ||
            !TryGetBoolean(root, "isAuthoritativeSnapshot", out bool authoritative) ||
            sequence <= 0)
        {
            failureCode = failureCode == "NONE" ? "SNAPSHOT_MALFORMED" : failureCode;
            return false;
        }

        snapshot = new IngressMessage(
            "stateSnapshot",
            sessionId,
            sequence,
            matchObserved,
            roundObserved,
            authoritative);
        failureCode = "NONE";
        return true;
    }

    private static bool TryParseReceipt(string text, out RendererReceipt? receipt, out string failureCode)
    {
        receipt = null;
        if (!TryParseRoot(text, "receipt", ReceiptProperties, out JsonElement root, out failureCode) ||
            !TryGetString(root, "receiptType", out string receiptType) ||
            !TryGetString(root, "runtimeInstanceId", out string runtimeId) ||
            !TryGetInt64(root, "connectionEpoch", out long connectionEpoch) ||
            !TryGetInt64(root, "commandSequence", out long commandSequence) ||
            !TryGetString(root, "sessionId", out string sessionId) ||
            !TryGetString(root, "renderLeaseId", out string renderLeaseId) ||
            !TryGetString(root, "commandId", out string commandId) ||
            connectionEpoch <= 0 ||
            commandSequence <= 0 ||
            (receiptType is not "hidden" and not "shown"))
        {
            failureCode = failureCode == "NONE" ? "RECEIPT_MALFORMED" : failureCode;
            return false;
        }

        receipt = new RendererReceipt(
            "receipt",
            runtimeId,
            connectionEpoch,
            commandSequence,
            sessionId,
            renderLeaseId,
            commandId,
            receiptType);
        failureCode = "NONE";
        return true;
    }

    private static readonly HashSet<string> HelloProperties =
    ["type", "gameId", "protocolVersion", "schemaVersion", "channel", "origin", "pairingProof", "bridgeInstanceId"];

    private static readonly HashSet<string> SnapshotProperties =
    ["type", "sessionId", "sequence", "matchObserved", "roundObserved", "isAuthoritativeSnapshot"];

    private static readonly HashSet<string> ReceiptProperties =
    ["type", "receiptType", "runtimeInstanceId", "connectionEpoch", "commandSequence", "sessionId", "renderLeaseId", "commandId"];

    private static bool TryParseRoot(
        string text,
        string expectedType,
        HashSet<string> allowedProperties,
        out JsonElement root,
        out string failureCode)
    {
        root = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement candidate = document.RootElement;
            if (candidate.ValueKind != JsonValueKind.Object ||
                !TryGetString(candidate, "type", out string type) ||
                !string.Equals(type, expectedType, StringComparison.Ordinal) ||
                candidate.EnumerateObject().Any(property => !allowedProperties.Contains(property.Name)))
            {
                failureCode = "MESSAGE_REJECTED";
                return false;
            }

            root = candidate.Clone();
            failureCode = "NONE";
            return true;
        }
        catch (JsonException)
        {
            failureCode = "JSON_MALFORMED";
            return false;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private static bool TryGetBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetOptionalString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetInt32(JsonElement root, string name, out int value)
    {
        value = default;
        return root.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement root, string name, out long value)
    {
        value = default;
        return root.TryGetProperty(name, out JsonElement property) && property.TryGetInt64(out value);
    }

    private static async Task SendJsonAsync(WebSocket socket, object message)
    {
        string json = JsonSerializer.Serialize(message, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task RejectAndCloseAsync(WebSocket socket, string failureCode)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await SendJsonAsync(socket, new { type = "error", code = failureCode });
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.PolicyViolation,
                "rejected",
                CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // The peer already closed while the Host was rejecting it.
        }
    }

    private static async Task CloseIfOpenAsync(WebSocket socket)
    {
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "done",
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Peer has already closed.
            }
        }
    }

    private sealed class RuntimeContext
    {
        public RuntimeContext(string sessionId, string bridgeInstanceId, long epoch)
        {
            SessionId = sessionId;
            BridgeInstanceId = bridgeInstanceId;
            Epoch = epoch;
        }

        public string SessionId { get; }
        public string BridgeInstanceId { get; }
        public long Epoch { get; }
        public bool IngestOnline { get; set; } = true;
        public bool ResyncRequired { get; set; } = true;
        public long LastSequence { get; set; }
        public long SnapshotRevision { get; set; }
        public bool MatchObserved { get; set; }
        public bool RoundObserved { get; set; }
    }

    private sealed record IngestConnection(string SessionId, long Epoch, IngressMailbox Mailbox);

    private sealed class ActiveRenderConnection
    {
        public ActiveRenderConnection(
            WebSocket socket,
            string sessionId,
            string renderLeaseId,
            long leaseEpoch,
            long minimumSnapshotRevision)
        {
            Socket = socket;
            SessionId = sessionId;
            RenderLeaseId = renderLeaseId;
            LeaseEpoch = leaseEpoch;
            MinimumSnapshotRevision = minimumSnapshotRevision;
        }

        public WebSocket Socket { get; }
        public string SessionId { get; }
        public string RenderLeaseId { get; }
        public long LeaseEpoch { get; }
        public long ConnectionEpoch => LeaseEpoch;
        public long MinimumSnapshotRevision { get; }
        public bool HiddenConfirmed { get; set; }
        public long LastCommandSequence { get; set; }
        public string? PendingHideCommandId { get; set; }
        public long? PendingHideCommandSequence { get; set; }
        public string? PendingShowCommandId { get; set; }
        public long? PendingShowCommandSequence { get; set; }
    }

    private sealed class ProtocolViolationException : Exception
    {
        public ProtocolViolationException(string failureCode)
        {
            FailureCode = failureCode;
        }

        public string FailureCode { get; }
    }
}
