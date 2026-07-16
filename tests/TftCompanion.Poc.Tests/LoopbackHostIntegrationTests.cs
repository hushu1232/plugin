using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Host;
using TftCompanion.Poc.Tests.TestSupport;

namespace TftCompanion.Poc.Tests;

[TestFixture]
[NonParallelizable]
public sealed class LoopbackHostIntegrationTests
{
    private const string AllowedOrigin = "overwolf-tool://tft-companion-poc";
    private const string PairingToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string BridgeInstanceId = "bridge-integration-test";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task host_rejects_a_foreign_http_origin_before_websocket_upgrade()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket client = new();
        client.Options.SetRequestHeader("Origin", "https://evil.example.invalid");

        try
        {
            await client.ConnectAsync(
                new Uri($"ws://127.0.0.1:{factory.Port}/ingest"),
                CancellationToken.None);

            Assert.Fail("A foreign HTTP Origin must be rejected before WebSocket acceptance.");
        }
        catch (WebSocketException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public async Task host_rejects_a_hello_without_the_configured_pairing_proof()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        await SendJson(ingest, new
        {
            type = "hello",
            gameId = ProtocolConstants.GameId,
            protocolVersion = ProtocolConstants.ProtocolVersion,
            schemaVersion = ProtocolConstants.SchemaVersion,
            channel = "ingest",
            origin = AllowedOrigin,
            bridgeInstanceId = BridgeInstanceId
        });

        JsonElement response = await ReceiveJson(ingest);
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("type").GetString(), Is.EqualTo("error"));
            Assert.That(response.GetProperty("code").GetString(), Is.EqualTo("PAIRING_REJECTED"));
        });
    }

    [Test]
    public async Task ingest_route_rejects_a_hello_declared_for_the_render_channel()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        await SendJson(ingest, new
        {
            type = "hello",
            gameId = ProtocolConstants.GameId,
            protocolVersion = ProtocolConstants.ProtocolVersion,
            schemaVersion = ProtocolConstants.SchemaVersion,
            channel = "render",
            origin = AllowedOrigin,
            pairingProof = PairingToken,
            bridgeInstanceId = BridgeInstanceId
        });

        JsonElement response = await ReceiveJson(ingest);
        Assert.Multiple(() =>
        {
            Assert.That(response.GetProperty("type").GetString(), Is.EqualTo("error"));
            Assert.That(response.GetProperty("code").GetString(), Is.EqualTo("CHANNEL_MISMATCH"));
        });
    }

    [Test]
    public async Task renderer_marker_waits_for_matching_hidden_receipt_and_a_current_authoritative_snapshot()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        JsonElement ingestWelcome = await SendHelloAndReceiveWelcome(ingest, "ingest");
        string sessionId = ingestWelcome.GetProperty("sessionId").GetString()!;

        using ClientWebSocket render = await ConnectAsync(factory, "/render");
        JsonElement renderWelcome = await SendHelloAndReceiveWelcome(render, "render");
        Assert.That(renderWelcome.GetProperty("sessionId").GetString(), Is.EqualTo(sessionId));

        JsonElement hideAll = await ReceiveJson(render);
        Assert.That(hideAll.GetProperty("type").GetString(), Is.EqualTo("hideAll"));

        await SendJson(render, CreateReceipt(hideAll, "hidden", sessionId));

        Task<JsonElement> pendingRenderCommand = ReceiveJson(render);
        Task firstCompleted = await Task.WhenAny(pendingRenderCommand, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.That(
            firstCompleted,
            Is.Not.EqualTo(pendingRenderCommand),
            "A Hidden receipt alone must never make a marker visible.");

        await SendJson(ingest, new
        {
            type = "stateSnapshot",
            sessionId,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });

        JsonElement showMarker = await pendingRenderCommand;
        Assert.Multiple(() =>
        {
            Assert.That(showMarker.GetProperty("type").GetString(), Is.EqualTo("showMarker"));
            Assert.That(showMarker.GetProperty("sessionId").GetString(), Is.EqualTo(sessionId));
            Assert.That(showMarker.GetProperty("renderLeaseId").GetString(),
                Is.EqualTo(hideAll.GetProperty("renderLeaseId").GetString()));
        });
    }

    [Test]
    public async Task foreign_receipt_identity_cannot_advance_the_active_render_lease()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        await SendHelloAndReceiveWelcome(ingest, "ingest");

        using ClientWebSocket render = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(render, "render");
        JsonElement hideAll = await ReceiveJson(render);

        await SendJson(render, CreateReceipt(hideAll, "hidden", "foreign-session"));

        JsonElement error = await ReceiveJson(render);
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("type").GetString(), Is.EqualTo("error"));
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("RECEIPT_IDENTITY_REJECTED"));
        });
    }

    [Test]
    public async Task receipt_with_a_wrong_connection_epoch_cannot_advance_the_active_render_lease()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        JsonElement ingestWelcome = await SendHelloAndReceiveWelcome(ingest, "ingest");
        string sessionId = ingestWelcome.GetProperty("sessionId").GetString()!;

        using ClientWebSocket render = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(render, "render");
        JsonElement hideAll = await ReceiveJson(render);

        await SendJson(render, new
        {
            type = "receipt",
            receiptType = "hidden",
            runtimeInstanceId = hideAll.GetProperty("runtimeInstanceId").GetString(),
            connectionEpoch = hideAll.GetProperty("connectionEpoch").GetInt64() + 1,
            commandSequence = hideAll.GetProperty("commandSequence").GetInt64(),
            sessionId,
            renderLeaseId = hideAll.GetProperty("renderLeaseId").GetString(),
            commandId = hideAll.GetProperty("commandId").GetString()
        });

        JsonElement error = await ReceiveJson(render);
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("type").GetString(), Is.EqualTo("error"));
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("RECEIPT_IDENTITY_REJECTED"));
        });
    }

    [Test]
    public async Task replacing_a_render_connection_sends_hideall_to_the_former_lease_before_it_can_be_stale()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        await SendHelloAndReceiveWelcome(ingest, "ingest");

        using ClientWebSocket formerRender = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(formerRender, "render");
        JsonElement formerInitialHide = await ReceiveJson(formerRender);
        Assert.That(formerInitialHide.GetProperty("type").GetString(), Is.EqualTo("hideAll"));

        Task<JsonElement> formerHideAfterReplacement = ReceiveJson(formerRender);

        using ClientWebSocket replacementRender = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(replacementRender, "render");
        JsonElement replacementInitialHide = await ReceiveJson(replacementRender);
        Assert.That(replacementInitialHide.GetProperty("type").GetString(), Is.EqualTo("hideAll"));

        Task completed = await Task.WhenAny(
            formerHideAfterReplacement,
            Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.That(
            completed,
            Is.EqualTo(formerHideAfterReplacement),
            "The former renderer must receive HideAll before the old lease is discarded.");

        JsonElement formerHide = await formerHideAfterReplacement;
        Assert.Multiple(() =>
        {
            Assert.That(formerHide.GetProperty("type").GetString(), Is.EqualTo("hideAll"));
            Assert.That(formerHide.GetProperty("renderLeaseId").GetString(),
                Is.EqualTo(formerInitialHide.GetProperty("renderLeaseId").GetString()));
        });
    }

    [Test]
    public async Task a_shown_marker_is_hidden_when_the_ingest_session_disconnects()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        JsonElement ingestWelcome = await SendHelloAndReceiveWelcome(ingest, "ingest");
        string sessionId = ingestWelcome.GetProperty("sessionId").GetString()!;

        using ClientWebSocket render = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(render, "render");
        JsonElement initialHide = await ReceiveJson(render);

        await SendJson(render, CreateReceipt(initialHide, "hidden", sessionId));
        Task<JsonElement> pendingShow = ReceiveJson(render);
        await SendJson(ingest, new
        {
            type = "stateSnapshot",
            sessionId,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });
        JsonElement showMarker = await pendingShow;
        Assert.That(showMarker.GetProperty("type").GetString(), Is.EqualTo("showMarker"));

        await SendJson(render, CreateReceipt(showMarker, "shown", sessionId));

        Task<JsonElement> hideAfterDisconnect = ReceiveJson(render);
        await ingest.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);

        Task completed = await Task.WhenAny(hideAfterDisconnect, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.That(
            completed,
            Is.EqualTo(hideAfterDisconnect),
            "A shown marker must be hidden immediately when its ingest session disconnects.");

        JsonElement hideAll = await hideAfterDisconnect;
        Assert.Multiple(() =>
        {
            Assert.That(hideAll.GetProperty("type").GetString(), Is.EqualTo("hideAll"));
            Assert.That(hideAll.GetProperty("renderLeaseId").GetString(),
                Is.EqualTo(showMarker.GetProperty("renderLeaseId").GetString()));
        });
    }

    [Test]
    public async Task a_sequence_gap_hides_a_shown_marker_and_requires_a_later_snapshot_to_recover()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        JsonElement ingestWelcome = await SendHelloAndReceiveWelcome(ingest, "ingest");
        string sessionId = ingestWelcome.GetProperty("sessionId").GetString()!;

        using ClientWebSocket render = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(render, "render");
        JsonElement initialHide = await ReceiveJson(render);
        await SendJson(render, CreateReceipt(initialHide, "hidden", sessionId));

        Task<JsonElement> pendingShow = ReceiveJson(render);
        await SendJson(ingest, new
        {
            type = "stateSnapshot",
            sessionId,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });
        JsonElement showMarker = await pendingShow;
        await SendJson(render, CreateReceipt(showMarker, "shown", sessionId));

        Task<JsonElement> pendingResync = ReceiveJson(ingest);
        Task<JsonElement> pendingHide = ReceiveJson(render);
        await SendJson(ingest, new
        {
            type = "stateSnapshot",
            sessionId,
            sequence = 3,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });

        Task resyncCompleted = await Task.WhenAny(pendingResync, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Task hideCompleted = await Task.WhenAny(pendingHide, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.That(resyncCompleted, Is.EqualTo(pendingResync));
        Assert.That(hideCompleted, Is.EqualTo(pendingHide));

        JsonElement resync = await pendingResync;
        JsonElement hideAll = await pendingHide;
        Assert.Multiple(() =>
        {
            Assert.That(resync.GetProperty("type").GetString(), Is.EqualTo("resyncRequired"));
            Assert.That(hideAll.GetProperty("type").GetString(), Is.EqualTo("hideAll"));
        });
    }

    [Test]
    public async Task host_rejects_an_oversized_text_frame_before_json_parsing()
    {
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        using ClientWebSocket ingest = await ConnectAsync(factory, "/ingest");
        await SendHelloAndReceiveWelcome(ingest, "ingest");

        string oversizedJson = "{\"type\":\"stateSnapshot\",\"padding\":\"" +
            new string('x', ProtocolConstants.MaximumTextFrameBytes) + "\"}";
        byte[] payload = Encoding.UTF8.GetBytes(oversizedJson);
        Assert.That(payload.Length, Is.GreaterThan(ProtocolConstants.MaximumTextFrameBytes));

        await ingest.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        JsonElement error = await ReceiveJson(ingest);
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("type").GetString(), Is.EqualTo("error"));
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("FRAME_OVERSIZED"));
        });
    }

    [Test]
    public async Task new_ingest_session_does_not_inherit_old_session_mailbox_state()
    {
        // C2 regression: a new ingest session must get a fresh session-scoped
        // mailbox. Old session envelopes must not occupy capacity or be drained
        // into the new runtime, and no show must be produced from old data.
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        // Session-A: connect ingest + render, show marker, then disconnect.
        using ClientWebSocket ingestA = await ConnectAsync(factory, "/ingest");
        JsonElement welcomeA = await SendHelloAndReceiveWelcome(ingestA, "ingest");
        string sessionA = welcomeA.GetProperty("sessionId").GetString()!;

        using ClientWebSocket renderA = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(renderA, "render");
        JsonElement hideAllA = await ReceiveJson(renderA);
        await SendJson(renderA, CreateReceipt(hideAllA, "hidden", sessionA));

        await SendJson(ingestA, new
        {
            type = "stateSnapshot",
            sessionId = sessionA,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });
        JsonElement showMarkerA = await ReceiveJson(renderA);
        Assert.That(showMarkerA.GetProperty("sessionId").GetString(), Is.EqualTo(sessionA));
        await SendJson(renderA, CreateReceipt(showMarkerA, "shown", sessionA));

        // Disconnect session-A — render-A receives hideAll from DetachIngestAsync.
        await ingestA.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
        JsonElement hideOnDisconnect = await ReceiveJson(renderA);
        Assert.That(hideOnDisconnect.GetProperty("type").GetString(), Is.EqualTo("hideAll"));

        // Give the server time to process render-A disconnect cleanup.
        await renderA.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        // Session-B: connect ingest (new session, fresh session-scoped mailbox).
        using ClientWebSocket ingestB = await ConnectAsync(factory, "/ingest");
        JsonElement welcomeB = await SendHelloAndReceiveWelcome(ingestB, "ingest");
        string sessionB = welcomeB.GetProperty("sessionId").GetString()!;
        Assert.That(sessionB, Is.Not.EqualTo(sessionA));

        // Connect render-B.
        using ClientWebSocket renderB = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(renderB, "render");
        JsonElement hideAllB = await ReceiveJson(renderB);
        await SendJson(renderB, CreateReceipt(hideAllB, "hidden", sessionB));

        // Session-B sends an authoritative snapshot.
        await SendJson(ingestB, new
        {
            type = "stateSnapshot",
            sessionId = sessionB,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });

        // The showMarker must reference session-B, proving the new runtime
        // was not polluted by session-A's mailbox state.
        JsonElement showMarkerB = await ReceiveJson(renderB);
        Assert.Multiple(() =>
        {
            Assert.That(showMarkerB.GetProperty("type").GetString(), Is.EqualTo("showMarker"));
            Assert.That(showMarkerB.GetProperty("sessionId").GetString(), Is.EqualTo(sessionB));
        });
    }

    [Test]
    public async Task stale_snapshot_from_a_former_ingest_connection_does_not_trigger_show()
    {
        // C2 regression: after a new ingest session attaches, a stale
        // snapshot from the old connection must not produce a show for
        // the new session. The old connection's session-scoped mailbox
        // and the runtime session check together must isolate the new
        // runtime from the stale envelope.
        await using PocHostFactory factory = CreateFactory();
        await factory.StartAsync();

        // Session-A: connect ingest + render, show marker.
        using ClientWebSocket ingestA = await ConnectAsync(factory, "/ingest");
        JsonElement welcomeA = await SendHelloAndReceiveWelcome(ingestA, "ingest");
        string sessionA = welcomeA.GetProperty("sessionId").GetString()!;

        using ClientWebSocket renderA = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(renderA, "render");
        JsonElement hideAllA = await ReceiveJson(renderA);
        await SendJson(renderA, CreateReceipt(hideAllA, "hidden", sessionA));

        await SendJson(ingestA, new
        {
            type = "stateSnapshot",
            sessionId = sessionA,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });
        JsonElement showMarkerA = await ReceiveJson(renderA);
        await SendJson(renderA, CreateReceipt(showMarkerA, "shown", sessionA));

        // Session-B attaches while ingestA is still open. AttachIngestAsync
        // rebinds the runtime to sessionB and issues hideAll to renderA.
        using ClientWebSocket ingestB = await ConnectAsync(factory, "/ingest");
        JsonElement welcomeB = await SendHelloAndReceiveWelcome(ingestB, "ingest");
        string sessionB = welcomeB.GetProperty("sessionId").GetString()!;
        Assert.That(sessionB, Is.Not.EqualTo(sessionA));

        // renderA receives the rebind hideAll, then is closed. The old
        // render connection is no longer the active render after rebind.
        JsonElement hideOnRebind = await ReceiveJson(renderA);
        Assert.That(hideOnRebind.GetProperty("type").GetString(), Is.EqualTo("hideAll"));
        await renderA.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        // Connect renderB for Session-B.
        using ClientWebSocket renderB = await ConnectAsync(factory, "/render");
        await SendHelloAndReceiveWelcome(renderB, "render");
        JsonElement hideAllB = await ReceiveJson(renderB);
        await SendJson(renderB, CreateReceipt(hideAllB, "hidden", sessionB));

        // Session-A (old ingest connection, still open) sends a stale
        // authoritative snapshot. It must not trigger a show for sessionB.
        await SendJson(ingestA, new
        {
            type = "stateSnapshot",
            sessionId = sessionA,
            sequence = 2,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });

        // No showMarker must arrive on renderB — the stale snapshot belongs
        // to a foreign session and must not advance the new runtime. The
        // pending receive is reused for Session-B's own show below so that
        // we never start a second concurrent ReceiveAsync on the same socket.
        Task<JsonElement> nextRenderCommand = ReceiveJson(renderB);
        Task completed = await Task.WhenAny(nextRenderCommand, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.That(completed, Is.Not.EqualTo(nextRenderCommand),
            "A stale snapshot from the old ingest connection must not trigger a show for the new session.");

        // Session-B sends its own authoritative snapshot — this should work.
        await SendJson(ingestB, new
        {
            type = "stateSnapshot",
            sessionId = sessionB,
            sequence = 1,
            matchObserved = true,
            roundObserved = true,
            isAuthoritativeSnapshot = true
        });

        // The pending nextRenderCommand now receives Session-B's showMarker.
        JsonElement showMarkerB = await nextRenderCommand;
        Assert.Multiple(() =>
        {
            Assert.That(showMarkerB.GetProperty("type").GetString(), Is.EqualTo("showMarker"));
            Assert.That(showMarkerB.GetProperty("sessionId").GetString(), Is.EqualTo(sessionB));
        });
    }

    private static PocHostFactory CreateFactory() => new(
        new PocHostOptions(
            Port: 0,
            AllowedOrigin: AllowedOrigin,
            PairingToken: PairingToken),
        FakeStorageFileSystem.ValidRoot());

    private static async Task<ClientWebSocket> ConnectAsync(PocHostFactory factory, string route)
    {
        ClientWebSocket client = new();
        client.Options.SetRequestHeader("Origin", AllowedOrigin);
        await client.ConnectAsync(
            new Uri($"ws://127.0.0.1:{factory.Port}{route}"),
            CancellationToken.None);
        return client;
    }

    private static async Task<JsonElement> SendHelloAndReceiveWelcome(ClientWebSocket ws, string channel)
    {
        await SendJson(ws, new
        {
            type = "hello",
            gameId = ProtocolConstants.GameId,
            protocolVersion = ProtocolConstants.ProtocolVersion,
            schemaVersion = ProtocolConstants.SchemaVersion,
            channel,
            origin = AllowedOrigin,
            pairingProof = PairingToken,
            bridgeInstanceId = BridgeInstanceId
        });

        JsonElement welcome = await ReceiveJson(ws);
        Assert.That(welcome.GetProperty("type").GetString(), Is.EqualTo("welcome"));
        return welcome;
    }

    private static object CreateReceipt(
        JsonElement command,
        string receiptType,
        string sessionId) => new
    {
        type = "receipt",
        receiptType,
        runtimeInstanceId = command.GetProperty("runtimeInstanceId").GetString(),
        connectionEpoch = command.GetProperty("connectionEpoch").GetInt64(),
        commandSequence = command.GetProperty("commandSequence").GetInt64(),
        sessionId,
        renderLeaseId = command.GetProperty("renderLeaseId").GetString(),
        commandId = command.GetProperty("commandId").GetString()
    };

    private static async Task SendJson(ClientWebSocket ws, object message)
    {
        string json = JsonSerializer.Serialize(message, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveJson(
        ClientWebSocket ws,
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[8192];
        int totalRead = 0;

        while (true)
        {
            ValueWebSocketReceiveResult result = await ws.ReceiveAsync(
                buffer.AsMemory(totalRead),
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            }

            totalRead += result.Count;
            if (result.EndOfMessage)
            {
                break;
            }

            if (totalRead >= buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
        }

        using JsonDocument doc = JsonDocument.Parse(buffer.AsMemory(0, totalRead));
        return doc.RootElement.Clone();
    }
}
