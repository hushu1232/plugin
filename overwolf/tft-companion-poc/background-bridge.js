// TFT Companion v0.0.1 Background Bridge.
// It owns both Host sockets and only forwards configured semantic booleans.

(function () {
    'use strict';

    const EXACT_HOST = 'ws://127.0.0.1:32173';
    const GAME_ID = 21570;
    const PROTOCOL_VERSION = 1;
    const SCHEMA_VERSION = 1;
    const MAX_RECONNECT_ATTEMPTS = 5;
    const BRIDGE_WINDOW_NAME = 'tft-companion-poc-bridge';
    const RENDERER_WINDOW_NAME = 'renderer';
    const settings = typeof window !== 'undefined' ? window.TftCompanionPocDevSettings : null;

    // Export for Node.js testing before the early-return guard so the
    // canonical pairing-token validator can be unit-tested independently.
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = {
            isCanonicalPairingToken: isCanonicalPairingToken,
            isValidSettings: isValidSettings
        };
    }

    const bridgeInstanceId = createBridgeInstanceId();
    let ingestSocket = null;
    let renderSocket = null;
    let ingestSocketGeneration = 0;
    let renderSocketGeneration = 0;
    let sessionId = null;
    let renderContext = null;
    let reconnectAttempts = 0;
    let snapshotSequence = 0;
    let gepReady = false;
    let lastRenderIdentity = null;
    let rendererDispatchGeneration = 0;
    let rendererLifecycleDispatchGeneration = 0;
    let infoRequestGeneration = 0;
    let bridgeRenderGeneration = 0;
    let bridgeLifecycleEpoch = 0;

    // The Renderer can probe the currently declared Bridge window before it
    // commits a foreign lifecycle takeover. Register first so the startup
    // lifecycle hide cannot race ahead of the local response listener.
    startRendererInboundMessageListener();

    // A Bridge restart must fail closed even before a Host connection can be
    // established. This payload is renderer-only and never produces a Host
    // receipt.
    sendRendererLifecycleHide();

    if (!isValidSettings(settings)) {
        return;
    }

    startGepBridge();
    connectIngest();

    function isValidSettings(candidate) {
        return Boolean(candidate) &&
            candidate.host === EXACT_HOST &&
            typeof candidate.allowedOrigin === 'string' && candidate.allowedOrigin.length > 0 &&
            typeof candidate.pairingToken === 'string' && isCanonicalPairingToken(candidate.pairingToken) &&
            Array.isArray(candidate.requiredFeatures) && candidate.requiredFeatures.length > 0 &&
            candidate.requiredFeatures.every((feature) => typeof feature === 'string' && feature.length > 0) &&
            isSafePath(candidate.matchObservedPath) &&
            isSafePath(candidate.roundObservedPath);
    }

    // I1: Canonical base64url pairing token validation.
    // Decodes the token, re-encodes it, and requires the re-encoded value
    // to match the input verbatim. This rejects tokens whose last character
    // has non-zero padding bits (e.g. 42 'A' + 'B') even though they have
    // the correct length, charset, and decode to the same 32 bytes.
    function isCanonicalPairingToken(token) {
        if (typeof token !== 'string' || token.length !== 43 || !/^[A-Za-z0-9_-]+$/.test(token)) {
            return false;
        }
        var base64 = token.replace(/-/g, '+').replace(/_/g, '/');
        var pad = base64.length % 4;
        if (pad > 0) {
            base64 += '===='.slice(0, 4 - pad);
        }
        try {
            var binary = atob(base64);
            var reencoded = btoa(binary)
                .replace(/=/g, '')
                .replace(/\+/g, '-')
                .replace(/\//g, '_');
            return reencoded === token;
        } catch (e) {
            return false;
        }
    }

    function isSafePath(pathValue) {
        return typeof pathValue === 'string' &&
            pathValue.length > 0 &&
            pathValue.length <= 128 &&
            pathValue.split('.').every((segment) => /^[A-Za-z0-9_]+$/.test(segment));
    }

    function createBridgeInstanceId() {
        if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID().replace(/-/g, '');
        }

        return String(Date.now()) + String(Math.random()).slice(2);
    }

    function connectIngest() {
        if (reconnectAttempts >= MAX_RECONNECT_ATTEMPTS) {
            return;
        }

        const socket = new WebSocket(settings.host + '/ingest');
        const generation = ++ingestSocketGeneration;
        ingestSocket = socket;
        socket.onopen = function () {
            if (!isCurrentIngestSocket(socket, generation)) {
                closeSocket(socket);
                return;
            }
            reconnectAttempts = 0;
            sendHello(socket, 'ingest');
        };
        socket.onmessage = function (event) {
            if (!isCurrentIngestSocket(socket, generation)) {
                return;
            }
            const message = parseMessage(event.data);
            if (!message) {
                return;
            }

            if (message.type === 'welcome' && message.channel === 'ingest' && typeof message.sessionId === 'string') {
                if (sessionId !== message.sessionId) {
                    snapshotSequence = 0;
                    infoRequestGeneration += 1;
                }
                closeRenderLink();
                sessionId = message.sessionId;
                connectRender();
                if (message.resyncRequired === true) {
                    requestAuthoritativeInfo();
                }
            } else if (message.type === 'resyncRequired') {
                requestAuthoritativeInfo();
            }
        };
        socket.onerror = function () {};
        socket.onclose = function () {
            if (!isCurrentIngestSocket(socket, generation)) {
                return;
            }
            infoRequestGeneration += 1;
            sessionId = null;
            closeRenderLink();
            scheduleReconnect(connectIngest);
        };
    }

    function connectRender() {
        if (!sessionId || reconnectAttempts >= MAX_RECONNECT_ATTEMPTS) {
            return;
        }

        if (renderSocket &&
            (renderSocket.readyState === WebSocket.CONNECTING || renderSocket.readyState === WebSocket.OPEN)) {
            return;
        }

        const socket = new WebSocket(settings.host + '/render');
        const generation = ++renderSocketGeneration;
        renderSocket = socket;
        socket.onopen = function () {
            if (!isCurrentRenderSocket(socket, generation)) {
                closeSocket(socket);
                return;
            }
            sendHello(socket, 'render');
        };
        socket.onmessage = function (event) {
            if (!isCurrentRenderSocket(socket, generation)) {
                return;
            }
            const message = parseMessage(event.data);
            if (!message) {
                return;
            }

            if (message.type === 'welcome' && message.channel === 'render') {
                if (!isValidRenderWelcome(message) || message.sessionId !== sessionId) {
                    closeSocket(socket);
                    return;
                }

                renderContext = {
                    runtimeInstanceId: message.runtimeInstanceId,
                    connectionEpoch: message.connectionEpoch,
                    sessionId: message.sessionId,
                    renderLeaseId: message.renderLeaseId,
                    lastCommandSequence: 0
                };
                bridgeRenderGeneration += 1;
                infoRequestGeneration += 1;
                // The Host requires a snapshot created after this lease.
                requestAuthoritativeInfo();
                return;
            }

            if (!isRenderCommand(message) || !matchesRenderContext(message)) {
                return;
            }

            renderContext.lastCommandSequence = message.commandSequence;
            lastRenderIdentity = copyRenderIdentity(message);
            sendRendererCommand(message);
        };
        socket.onerror = function () {};
        socket.onclose = function () {
            if (!isCurrentRenderSocket(socket, generation)) {
                return;
            }

            renderSocket = null;
            renderSocketGeneration += 1;
            renderContext = null;
            bridgeRenderGeneration += 1;
            infoRequestGeneration += 1;
            sendRendererLifecycleHide();
            scheduleReconnect(connectRender);
        };
    }

    function closeRenderLink() {
        const socket = renderSocket;
        renderSocket = null;
        renderSocketGeneration += 1;
        renderContext = null;
        bridgeRenderGeneration += 1;
        infoRequestGeneration += 1;
        sendRendererLifecycleHide();
        closeSocket(socket);
    }

    function closeSocket(socket) {
        if (socket &&
            (socket.readyState === WebSocket.CONNECTING || socket.readyState === WebSocket.OPEN)) {
            socket.close();
        }
    }

    function isCurrentIngestSocket(socket, generation) {
        return ingestSocket === socket && ingestSocketGeneration === generation;
    }

    function isCurrentRenderSocket(socket, generation) {
        return renderSocket === socket && renderSocketGeneration === generation;
    }

    function scheduleReconnect(connect) {
        reconnectAttempts += 1;
        if (reconnectAttempts <= MAX_RECONNECT_ATTEMPTS) {
            setTimeout(connect, 2000);
        }
    }

    function sendHello(socket, channel) {
        sendJson(socket, {
            type: 'hello',
            gameId: GAME_ID,
            protocolVersion: PROTOCOL_VERSION,
            schemaVersion: SCHEMA_VERSION,
            channel: channel,
            origin: settings.allowedOrigin,
            pairingProof: settings.pairingToken,
            bridgeInstanceId: bridgeInstanceId
        });
    }

    function sendJson(socket, message) {
        if (socket && socket.readyState === WebSocket.OPEN) {
            socket.send(JSON.stringify(message));
        }
    }

    function parseMessage(text) {
        try {
            const message = JSON.parse(text);
            return message && typeof message === 'object' ? message : null;
        } catch (_) {
            return null;
        }
    }

    function isValidRenderWelcome(message) {
        return typeof message.runtimeInstanceId === 'string' && message.runtimeInstanceId.length > 0 &&
            Number.isInteger(message.connectionEpoch) && message.connectionEpoch > 0 &&
            typeof message.renderLeaseId === 'string' && message.renderLeaseId.length > 0;
    }

    function isRenderCommand(message) {
        return (message.type === 'hideAll' || message.type === 'showMarker') &&
            typeof message.runtimeInstanceId === 'string' && message.runtimeInstanceId.length > 0 &&
            Number.isInteger(message.connectionEpoch) && message.connectionEpoch > 0 &&
            Number.isInteger(message.commandSequence) && message.commandSequence > 0 &&
            typeof message.sessionId === 'string' &&
            typeof message.renderLeaseId === 'string' &&
            typeof message.commandId === 'string';
    }

    function matchesRenderContext(message) {
        return Boolean(renderContext) &&
            message.runtimeInstanceId === renderContext.runtimeInstanceId &&
            message.connectionEpoch === renderContext.connectionEpoch &&
            message.sessionId === renderContext.sessionId &&
            message.renderLeaseId === renderContext.renderLeaseId &&
            message.commandSequence > renderContext.lastCommandSequence;
    }

    function copyRenderIdentity(message) {
        return {
            runtimeInstanceId: message.runtimeInstanceId,
            connectionEpoch: message.connectionEpoch,
            commandSequence: message.commandSequence,
            sessionId: message.sessionId,
            renderLeaseId: message.renderLeaseId,
            commandId: message.commandId
        };
    }

    function startGepBridge() {
        if (typeof overwolf === 'undefined' ||
            !overwolf.games ||
            !overwolf.games.events ||
            !overwolf.games.events.setRequiredFeatures ||
            !overwolf.games.events.onNewEvents ||
            !overwolf.games.events.onInfoUpdates2 ||
            !overwolf.games.events.getInfo) {
            return;
        }

        overwolf.games.events.setRequiredFeatures(settings.requiredFeatures, function (result) {
            gepReady = isSuccessfulCallback(result);
            if (gepReady) {
                requestAuthoritativeInfo();
            }
        });

        overwolf.games.events.onNewEvents.addListener(function () {
            // Event payloads are intentionally ignored.
        });

        overwolf.games.events.onInfoUpdates2.addListener(function () {
            // This callback is only a compatibility observation. Host-visible
            // state is emitted exclusively by an accepted getInfo response.
        });
    }

    function requestAuthoritativeInfo() {
        if (!sessionId || !gepReady ||
            typeof overwolf === 'undefined' ||
            !overwolf.games ||
            !overwolf.games.events ||
            !overwolf.games.events.getInfo) {
            return;
        }

        const socket = ingestSocket;
        const socketGeneration = ingestSocketGeneration;
        const capturedSessionId = sessionId;
        const capturedRenderContext = copyCurrentRenderContext();
        const requestGeneration = ++infoRequestGeneration;

        overwolf.games.events.getInfo(function (result) {
            if (requestGeneration !== infoRequestGeneration ||
                !isCurrentIngestSocket(socket, socketGeneration) ||
                sessionId !== capturedSessionId ||
                !sameRenderContext(capturedRenderContext)) {
                return;
            }
            if (!isSuccessfulCallback(result)) {
                return;
            }

            const semanticRoot = result.info && typeof result.info === 'object' ? result.info : result;
            publishSnapshot(semanticRoot, socket, capturedSessionId);
        });
    }

    function isSuccessfulCallback(result) {
        return Boolean(result) && typeof result === 'object' &&
            (result.success === true || result.status === 'success');
    }

    function publishSnapshot(info, socket, snapshotSessionId) {
        if (!snapshotSessionId || !gepReady || !info || typeof info !== 'object') {
            return;
        }

        sendJson(socket, {
            type: 'stateSnapshot',
            sessionId: snapshotSessionId,
            sequence: ++snapshotSequence,
            matchObserved: readConfiguredPresence(info, settings.matchObservedPath),
            roundObserved: readConfiguredPresence(info, settings.roundObservedPath),
            isAuthoritativeSnapshot: true
        });
    }

    function copyCurrentRenderContext() {
        if (!renderContext) {
            return null;
        }

        return {
            runtimeInstanceId: renderContext.runtimeInstanceId,
            connectionEpoch: renderContext.connectionEpoch,
            sessionId: renderContext.sessionId,
            renderLeaseId: renderContext.renderLeaseId
        };
    }

    function sameRenderContext(captured) {
        if (!captured) {
            return renderContext === null;
        }

        return Boolean(renderContext) &&
            renderContext.runtimeInstanceId === captured.runtimeInstanceId &&
            renderContext.connectionEpoch === captured.connectionEpoch &&
            renderContext.sessionId === captured.sessionId &&
            renderContext.renderLeaseId === captured.renderLeaseId;
    }

    function readConfiguredPresence(root, pathValue) {
        let current = root;
        for (const segment of pathValue.split('.')) {
            if (!current || typeof current !== 'object' || !Object.prototype.hasOwnProperty.call(current, segment)) {
                return false;
            }
            current = current[segment];
        }

        return current !== null && current !== undefined;
    }

    function sendRendererCommand(command) {
        const dispatchGeneration = ++rendererDispatchGeneration;
        const renderGeneration = bridgeRenderGeneration;
        const rendererCommand = Object.assign({}, command, {
            bridgeInstanceId: bridgeInstanceId,
            bridgeRenderGeneration: renderGeneration,
            bridgeLifecycleEpoch: bridgeLifecycleEpoch
        });

        sendRendererPayload(rendererCommand, function () {
            return dispatchGeneration === rendererDispatchGeneration;
        });
    }

    function sendRendererLifecycleHide() {
        const lifecycleEpoch = ++bridgeLifecycleEpoch;
        // Invalidate delayed normal commands and receipts from the lifecycle
        // that has just ended. Lifecycle delivery has its own generation so a
        // later normal command cannot cancel the fail-closed hide.
        rendererDispatchGeneration += 1;
        lastRenderIdentity = null;
        const dispatchGeneration = ++rendererLifecycleDispatchGeneration;

        sendRendererPayload({
            type: 'bridgeLifecycleHide',
            bridgeInstanceId: bridgeInstanceId,
            bridgeLifecycleEpoch: lifecycleEpoch
        }, function () {
            return dispatchGeneration === rendererLifecycleDispatchGeneration;
        });
    }

    function sendRendererPayload(payload, isCurrentDispatch) {
        if (typeof overwolf === 'undefined' ||
            !overwolf.windows ||
            !overwolf.windows.obtainDeclaredWindow ||
            !overwolf.windows.sendMessage) {
            return;
        }

        overwolf.windows.obtainDeclaredWindow(RENDERER_WINDOW_NAME, function (result) {
            if (!isCurrentDispatch()) {
                return;
            }
            if (!result || !result.window || !result.window.id) {
                return;
            }

            overwolf.windows.sendMessage(result.window.id, {
                source: BRIDGE_WINDOW_NAME,
                payload: payload
            }, function () {});
        });
    }

    function isRendererLifecycleProbe(message) {
        return message &&
            message.type === 'rendererLifecycleProbe' &&
            typeof message.probeId === 'string' && message.probeId.length > 0 &&
            typeof message.candidateBridgeInstanceId === 'string' && message.candidateBridgeInstanceId.length > 0 &&
            Number.isInteger(message.candidateBridgeLifecycleEpoch) && message.candidateBridgeLifecycleEpoch > 0;
    }

    function sendRendererProbeAck(probeId) {
        sendRendererPayload({
            type: 'bridgeLifecycleProbeAck',
            probeId: probeId,
            bridgeInstanceId: bridgeInstanceId
        }, function () {
            return true;
        });
    }

    function startRendererInboundMessageListener() {
        if (typeof overwolf === 'undefined' ||
            !overwolf.windows ||
            !overwolf.windows.onMessageReceived ||
            !overwolf.windows.onMessageReceived.addListener) {
            return;
        }

        overwolf.windows.onMessageReceived.addListener(function (event) {
            const envelope = event && event.message ? event.message : event;
            const message = envelope && envelope.source === RENDERER_WINDOW_NAME ? envelope.payload : null;
            if (isRendererLifecycleProbe(message)) {
                sendRendererProbeAck(message.probeId);
                return;
            }

            const receipt = message;
            if (!receipt ||
                receipt.type !== 'rendererReceipt' ||
                (receipt.receiptType !== 'hidden' && receipt.receiptType !== 'shown') ||
                !lastRenderIdentity ||
                receipt.runtimeInstanceId !== lastRenderIdentity.runtimeInstanceId ||
                receipt.connectionEpoch !== lastRenderIdentity.connectionEpoch ||
                receipt.commandSequence !== lastRenderIdentity.commandSequence ||
                receipt.sessionId !== lastRenderIdentity.sessionId ||
                receipt.renderLeaseId !== lastRenderIdentity.renderLeaseId ||
                receipt.commandId !== lastRenderIdentity.commandId) {
                return;
            }

            sendJson(renderSocket, {
                type: 'receipt',
                receiptType: receipt.receiptType,
                runtimeInstanceId: receipt.runtimeInstanceId,
                connectionEpoch: receipt.connectionEpoch,
                commandSequence: receipt.commandSequence,
                sessionId: receipt.sessionId,
                renderLeaseId: receipt.renderLeaseId,
                commandId: receipt.commandId
            });
        });
    }
})();
