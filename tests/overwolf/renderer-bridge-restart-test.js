'use strict';

// Behavioral regression test for C1: Bridge restart must hide old marker and
// reject delayed show from the old Bridge instance.
//
// Scenario:
//   1. Bridge-A sends hideAll + showMarker → marker visible
//   2. Bridge-B (restart) sends hideAll → marker hidden
//   3. Old Bridge-A sends delayed showMarker → marker NOT revived
//   4. Bridge-B sends showMarker → marker visible (rebind succeeded)
//
// This test loads render-identity.js and renderer.js in a sandboxed
// environment with mocked Overwolf APIs and DOM. It is a real behavioral
// test, not a static source check.

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const overwolfDir = path.join(__dirname, '..', '..', 'overwolf', 'tft-companion-poc');

let failures = 0;
let checks = 0;

function check(name, fn) {
    checks++;
    try {
        fn();
        console.log(`  [PASS] ${name}`);
    } catch (err) {
        console.log(`  [FAIL] ${name}: ${err.message}`);
        failures++;
    }
}

function createEnvironment() {
    const marker = {
        classList: {
            _set: new Set(),
            add(cls) { this._set.add(cls); },
            remove(cls) { this._set.delete(cls); },
            contains(cls) { return this._set.has(cls); }
        },
        isConnected: true,
        getClientRects() { return [{ width: 8, height: 8 }]; }
    };

    let mouseGrabSuccess = true;
    let delayMouseGrab = false;
    let pendingMouseGrabCallback = null;
    let messageListener = null;
    let bridgeMessageRouter = null;
    const sentMessages = [];
    const rafQueue = [];

    const sandbox = {
        console,
        setTimeout: (fn) => fn(),
        crypto: { randomUUID: () => 'test-uuid-' + Math.random().toString(36).slice(2) },
        requestAnimationFrame: (fn) => { rafQueue.push(fn); },
        Set: Set,
        overwolf: {
            windows: {
                getCurrentWindow: (cb) => cb({ window: { id: 'renderer-win' } }),
                setMouseGrab: (winId, grab, cb) => {
                    if (delayMouseGrab) {
                        pendingMouseGrabCallback = cb;
                    } else {
                        cb({ success: mouseGrabSuccess });
                    }
                },
                obtainDeclaredWindow: (name, cb) => cb({ window: { id: name + '-win' } }),
                sendMessage: (winId, msg, cb) => {
                    sentMessages.push({ winId, msg });
                    if (bridgeMessageRouter && winId === 'tft-companion-poc-bridge-win') {
                        bridgeMessageRouter(msg);
                    }
                    if (cb) cb();
                },
                onMessageReceived: {
                    addListener: (cb) => { messageListener = cb; }
                }
            }
        },
        document: {
            getElementById: (id) => id === 'poc-marker' ? marker : null
        },
        window: {},
        WebSocket: function() {}
    };

    sandbox.window.TftCompanionRenderIdentity = undefined;
    sandbox.globalThis = sandbox;

    return {
        sandbox,
        marker,
        getMessageListener: () => messageListener,
        sentMessages,
        setBridgeMessageRouter: (router) => { bridgeMessageRouter = router; },
        flushRaf: () => {
            while (rafQueue.length > 0) rafQueue.shift()();
        },
        setMouseGrabSuccess: (v) => { mouseGrabSuccess = v; },
        setDelayMouseGrab: (v) => { delayMouseGrab = v; },
        flushMouseGrab: (result) => {
            if (pendingMouseGrabCallback) {
                const cb = pendingMouseGrabCallback;
                pendingMouseGrabCallback = null;
                cb(result);
            }
        }
    };
}

function loadScript(env, filename) {
    const code = fs.readFileSync(path.join(overwolfDir, filename), 'utf8');
    vm.createContext(env.sandbox);
    vm.runInContext(code, env.sandbox, { filename });
}

function deliverCommand(env, command) {
    const listener = env.getMessageListener();
    if (!listener) throw new Error('No message listener registered');
    listener({
        message: {
            source: 'tft-companion-poc-bridge',
            payload: command
        }
    });
}

function flushRaf(env) {
    env.flushRaf();
}

function sendCommand(env, command) {
    deliverCommand(env, command);
    flushRaf(env);
}

function isMarkerVisible(env) {
    return env.marker.classList.contains('visible');
}

function rendererReceiptCount(env) {
    return env.sentMessages.filter((entry) =>
        entry.msg && entry.msg.payload && entry.msg.payload.type === 'rendererReceipt').length;
}

function findReceipts(env, commandId, receiptType) {
    return env.sentMessages.filter((entry) =>
        entry.msg && entry.msg.payload &&
        entry.msg.payload.type === 'rendererReceipt' &&
        entry.msg.payload.commandId === commandId &&
        entry.msg.payload.receiptType === receiptType);
}

function makeCommand(type, bridgeInstanceId, generation, opts) {
    opts = opts || {};
    return {
        type: type,
        bridgeInstanceId: bridgeInstanceId,
        bridgeLifecycleEpoch: opts.bridgeLifecycleEpoch || 1,
        bridgeRenderGeneration: generation,
        runtimeInstanceId: opts.runtimeInstanceId || 'rt-1',
        connectionEpoch: opts.connectionEpoch || 1,
        commandSequence: opts.commandSequence || 1,
        sessionId: opts.sessionId || 'session-1',
        renderLeaseId: opts.renderLeaseId || 'lease-1',
        commandId: opts.commandId || 'cmd-' + Math.random().toString(36).slice(2)
    };
}

function makeLifecycleHide(bridgeInstanceId, bridgeLifecycleEpoch) {
    return {
        type: 'bridgeLifecycleHide',
        bridgeInstanceId: bridgeInstanceId,
        bridgeLifecycleEpoch: bridgeLifecycleEpoch
    };
}

function createHostUnavailableBridgeEnvironment(rendererEnv, options) {
    options = options || {};
    const webSockets = [];
    const pendingRendererWindowCallbacks = [];
    const sentRendererMessages = [];
    let bridgeMessageListener = null;

    function NeverOpenWebSocket(url) {
        this.url = url;
        this.readyState = NeverOpenWebSocket.CONNECTING;
        webSockets.push(this);
    }
    NeverOpenWebSocket.CONNECTING = 0;
    NeverOpenWebSocket.OPEN = 1;
    NeverOpenWebSocket.prototype.close = function () {
        this.readyState = 3;
        if (typeof this.onclose === 'function') {
            this.onclose();
        }
    };
    NeverOpenWebSocket.prototype.send = function () {};

    const sandbox = {
        console,
        crypto: { randomUUID: () => options.bridgeInstanceId || 'bridge-instance-bbb' },
        atob: (value) => Buffer.from(value, 'base64').toString('binary'),
        btoa: (value) => Buffer.from(value, 'binary').toString('base64'),
        setTimeout: () => 0,
        WebSocket: NeverOpenWebSocket,
        window: {
            TftCompanionPocDevSettings: {
                host: 'ws://127.0.0.1:32173',
                allowedOrigin: 'overwolf-extension://tft-companion-poc',
                pairingToken: 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                requiredFeatures: ['live_client_data'],
                matchObservedPath: 'gameData.match',
                roundObservedPath: 'gameData.round'
            }
        },
        overwolf: {
            windows: {
                obtainDeclaredWindow: (name, cb) => {
                    const deliver = () => cb({ window: { id: name + '-win' } });
                    if (options.delayRendererWindowCallbacks === true) {
                        pendingRendererWindowCallbacks.push(deliver);
                    } else {
                        deliver();
                    }
                },
                sendMessage: (windowId, message, cb) => {
                    if (windowId === 'renderer-win') {
                        sentRendererMessages.push(message);
                        const listener = rendererEnv.getMessageListener();
                        if (listener) {
                            listener({ message: message });
                        }
                    }
                    if (cb) cb();
                },
                onMessageReceived: {
                    addListener: (callback) => { bridgeMessageListener = callback; }
                }
            },
            games: {
                events: {
                    setRequiredFeatures: (_features, cb) => cb({ success: false }),
                    onNewEvents: { addListener: () => {} },
                    onInfoUpdates2: { addListener: () => {} },
                    getInfo: () => {}
                }
            }
        }
    };
    sandbox.globalThis = sandbox;

    return {
        sandbox,
        webSockets,
        sentRendererMessages,
        deliverBridgeWindowMessage: (message) => {
            if (bridgeMessageListener) {
                bridgeMessageListener({ message: message });
            }
        },
        flushRendererWindowCallbacks: () => {
            while (pendingRendererWindowCallbacks.length > 0) {
                pendingRendererWindowCallbacks.shift()();
            }
        }
    };
}

// --- Test 1: Bridge restart hides old marker and rejects delayed show ---

function testBridgeRestart() {
    console.log('\nTest: Bridge restart hides old marker and rejects delayed show');

    const env = createEnvironment();

    // Load render-identity.js first (sets window.TftCompanionRenderIdentity)
    loadScript(env, 'render-identity.js');

    // Load renderer.js (registers message listener, enforces passthrough)
    loadScript(env, 'renderer.js');

    // Flush passthrough callbacks (getCurrentWindow -> setMouseGrab)
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';
    const BRIDGE_B = 'bridge-instance-bbb';

    // Step 1: Bridge-A sends initial hideAll
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1 }));
    check('marker hidden after Bridge-A initial hideAll', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Step 2: Bridge-A sends showMarker
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2 }));
    check('marker visible after Bridge-A showMarker', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    // Step 3: Bridge restarts — Bridge-B sends hideAll (generation resets to 1)
    sendCommand(env, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 1 }));
    check('marker hidden after Bridge-B hideAll (restart)', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Step 4: Old Bridge-A sends delayed showMarker
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 3 }));
    check('marker NOT visible after old Bridge-A delayed showMarker', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Step 5: Old Bridge-A sends delayed hideAll (must not revive or cause issues)
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 4 }));
    check('marker still hidden after old Bridge-A delayed hideAll', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Step 6: Bridge-B sends showMarker (rebind should succeed)
    sendCommand(env, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 2 }));
    check('marker visible after Bridge-B showMarker (rebind)', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });
}

// --- Test 2: Quarantine — different bridge cannot show without hideAll rebind ---

function testQuarantineNoShowWithoutRebind() {
    console.log('\nTest: Different bridgeInstanceId cannot show without hideAll rebind');

    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';
    const BRIDGE_C = 'bridge-instance-ccc';

    // Bridge-A establishes itself and hides
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1 }));
    check('marker hidden after Bridge-A hideAll', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Bridge-C tries showMarker directly (without hideAll rebind) — must be rejected
    sendCommand(env, makeCommand('showMarker', BRIDGE_C, 1, { commandSequence: 1 }));
    check('marker NOT visible after Bridge-C showMarker without rebind', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Bridge-C sends hideAll (rebind) then showMarker — should now succeed
    sendCommand(env, makeCommand('hideAll', BRIDGE_C, 1, { commandSequence: 2 }));
    check('marker hidden after Bridge-C hideAll (rebind)', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    sendCommand(env, makeCommand('showMarker', BRIDGE_C, 1, { commandSequence: 3 }));
    check('marker visible after Bridge-C showMarker (post-rebind)', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    // Old Bridge-A tries showMarker after being quarantined — must be rejected
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 5 }));
    check('marker still visible (unchanged) after quarantined Bridge-A showMarker', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    // Old Bridge-A tries hideAll after being quarantined — must be rejected
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 6 }));
    check('marker still visible (unchanged) after quarantined Bridge-A hideAll', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });
}

// --- Test 3: Same bridge normal flow still works ---

function testSameBridgeNormalFlow() {
    console.log('\nTest: Same bridge normal hide/show flow still works');

    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';

    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1 }));
    check('marker hidden after initial hideAll', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2 }));
    check('marker visible after showMarker', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 3 }));
    check('marker hidden after second hideAll', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
}

// --- Test 4: Same Bridge must reject stale or foreign-context commands ---

function testSameBridgeRejectsStaleAndForeignContextCommands() {
    console.log('\nTest: Same Bridge rejects stale and foreign-context commands');

    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';

    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'hide-1' }));
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'show-2' }));
    check('marker visible after the current same-Bridge show', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    const receiptsBeforeRejectedHide = env.sentMessages.length;
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, {
        runtimeInstanceId: 'rt-old',
        connectionEpoch: 99,
        sessionId: 'session-old',
        renderLeaseId: 'lease-old',
        commandSequence: 3,
        commandId: 'foreign-hide-3'
    }));
    check('foreign-context same-Bridge hide does not take control', () => {
        assert.strictEqual(isMarkerVisible(env), true);
        assert.strictEqual(env.sentMessages.length, receiptsBeforeRejectedHide);
    });

    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 3, commandId: 'hide-3' }));
    check('newer same-context hide still hides the marker', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    const receiptsBeforeStaleShow = env.sentMessages.length;
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'show-2-delayed' }));
    check('delayed same-Bridge show cannot revive the marker', () => {
        assert.strictEqual(isMarkerVisible(env), false);
        assert.strictEqual(env.sentMessages.length, receiptsBeforeStaleShow);
    });

    const receiptsBeforeEqualHide = env.sentMessages.length;
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 3, commandId: 'hide-3-repeat' }));
    check('equal-sequence same-Bridge hide cannot replace the current command', () => {
        assert.strictEqual(isMarkerVisible(env), false);
        assert.strictEqual(env.sentMessages.length, receiptsBeforeEqualHide);
    });
}

// --- Test 5: Actual Bridge startup hides even when the Host never opens ---

function testBridgeStartupHidesOldMarkerWithoutHostConnection() {
    console.log('\nTest: Bridge startup hides old marker without a Host connection');

    const rendererEnv = createEnvironment();
    loadScript(rendererEnv, 'render-identity.js');
    loadScript(rendererEnv, 'renderer.js');
    rendererEnv.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'a-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'a-show-2' }));
    check('old marker is visible before the new Bridge starts', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });

    const receiptsBeforeStartup = rendererReceiptCount(rendererEnv);
    const bridgeEnv = createHostUnavailableBridgeEnvironment(rendererEnv);
    rendererEnv.setBridgeMessageRouter(bridgeEnv.deliverBridgeWindowMessage);
    loadScript(bridgeEnv, 'background-bridge.js');

    check('new Bridge starts an ingest socket that never opens', () => {
        assert.strictEqual(bridgeEnv.webSockets.length, 1);
        assert.strictEqual(bridgeEnv.webSockets[0].readyState, 0);
    });
    check('new Bridge startup immediately hides the old marker without Host welcome', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });
    check('renderer lifecycle hide emits no synthetic Host receipt', () => {
        assert.strictEqual(rendererReceiptCount(rendererEnv), receiptsBeforeStartup);
    });

    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 3, commandId: 'a-show-3-delayed' }));
    check('old Bridge cannot revive the marker after lifecycle takeover', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });
}

// --- Test 6: A newer lifecycle epoch hides without a Host receipt ---

function testLifecycleHideIsOneWayAndInvalidatesTheOldContext() {
    console.log('\nTest: Lifecycle hide is one-way and invalidates the old context');

    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'hide-1' }));
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'show-2' }));
    check('marker is visible before same-Bridge lifecycle hide', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    const receiptsBeforeLifecycleHide = env.sentMessages.length;
    sendCommand(env, makeLifecycleHide(BRIDGE_A, 2));
    check('newer lifecycle hide clears the marker without a receipt', () => {
        assert.strictEqual(isMarkerVisible(env), false);
        assert.strictEqual(env.sentMessages.length, receiptsBeforeLifecycleHide);
    });

    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 3, commandId: 'old-lifecycle-show', bridgeLifecycleEpoch: 1 }));
    check('an old lifecycle command cannot revive the marker', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
}

// --- Test 7: A delayed old Bridge lifecycle cannot permanently evict a newer Bridge ---

function testDelayedOldBridgeLifecycleDoesNotPermanentlyQuarantineCurrentBridge() {
    console.log('\nTest: Delayed old Bridge lifecycle does not permanently quarantine the current Bridge');

    const rendererEnv = createEnvironment();
    loadScript(rendererEnv, 'render-identity.js');
    loadScript(rendererEnv, 'renderer.js');
    rendererEnv.flushRaf();

    const BRIDGE_A = 'bridgeaaa';
    const BRIDGE_B = 'bridgebbb';
    const BRIDGE_C = 'bridgeccc';

    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'a-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'a-show-2' }));
    check('Bridge-A marker is visible before the delayed lifecycle race', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });

    const delayedBridgeC = createHostUnavailableBridgeEnvironment(rendererEnv, {
        bridgeInstanceId: BRIDGE_C,
        delayRendererWindowCallbacks: true
    });
    loadScript(delayedBridgeC, 'background-bridge.js');

    const bridgeB = createHostUnavailableBridgeEnvironment(rendererEnv, {
        bridgeInstanceId: BRIDGE_B
    });
    rendererEnv.setBridgeMessageRouter(bridgeB.deliverBridgeWindowMessage);
    loadScript(bridgeB, 'background-bridge.js');
    check('Bridge-B acknowledges the Renderer lifecycle probe through the actual Bridge listener', () => {
        assert.ok(bridgeB.sentRendererMessages.some((message) =>
            message && message.payload && message.payload.type === 'bridgeLifecycleProbeAck'));
    });
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 1, commandId: 'b-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 2, commandId: 'b-show-2' }));
    check('Bridge-B becomes the visible current Bridge before C callback delivery', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });

    delayedBridgeC.flushRendererWindowCallbacks();
    check('late old Bridge-C lifecycle still fail-closed hides the marker', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });
    check('current Bridge-B acknowledges the delayed Bridge-C lifecycle probe', () => {
        const ackCount = bridgeB.sentRendererMessages.filter((message) =>
            message && message.payload && message.payload.type === 'bridgeLifecycleProbeAck').length;
        assert.strictEqual(ackCount, 2);
    });
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 3, commandId: 'b-hide-3' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 4, commandId: 'b-show-4' }));
    check('a newer valid Bridge-B hide/show can recover instead of being permanently quarantined', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });
}

// --- Test 8: An unacknowledged foreign candidate cannot use Host commands to commit ---

function testForeignCandidateCannotCommitBeforeMatchingProbeAck() {
    console.log('\nTest: Foreign candidate cannot commit before a matching probe ack');

    const rendererEnv = createEnvironment();
    loadScript(rendererEnv, 'render-identity.js');
    loadScript(rendererEnv, 'renderer.js');
    rendererEnv.flushRaf();

    const BRIDGE_B = 'bridgebbb';
    const BRIDGE_C = 'bridgeccc';

    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 1, commandId: 'b-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 2, commandId: 'b-show-2' }));
    check('Bridge-B is visible before an unacknowledged foreign candidate arrives', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });

    const delayedBridgeC = createHostUnavailableBridgeEnvironment(rendererEnv, {
        bridgeInstanceId: BRIDGE_C,
        delayRendererWindowCallbacks: true
    });
    loadScript(delayedBridgeC, 'background-bridge.js');
    rendererEnv.setBridgeMessageRouter(null);
    delayedBridgeC.flushRendererWindowCallbacks();
    check('unacknowledged Bridge-C lifecycle hides the marker', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });

    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_C, 1, { commandSequence: 1, commandId: 'c-hide-before-ack' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_C, 1, { commandSequence: 2, commandId: 'c-show-before-ack' }));
    check('foreign Host hide/show cannot commit or display before a matching probe ack', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });
}

// --- Test 9: An active Bridge lifecycle cannot erase an unacknowledged foreign candidate ---

function testActiveLifecycleCannotClearForeignCandidateBeforeProbeAck() {
    console.log('\nTest: Active lifecycle cannot clear a foreign candidate before probe ack');

    const rendererEnv = createEnvironment();
    loadScript(rendererEnv, 'render-identity.js');
    loadScript(rendererEnv, 'renderer.js');
    rendererEnv.flushRaf();

    const BRIDGE_B = 'bridgebbb';
    const BRIDGE_C = 'bridgeccc';

    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 1, commandId: 'b-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 2, commandId: 'b-show-2' }));

    const delayedBridgeC = createHostUnavailableBridgeEnvironment(rendererEnv, {
        bridgeInstanceId: BRIDGE_C,
        delayRendererWindowCallbacks: true
    });
    loadScript(delayedBridgeC, 'background-bridge.js');
    rendererEnv.setBridgeMessageRouter(null);
    delayedBridgeC.flushRendererWindowCallbacks();
    check('foreign candidate leaves the marker safely hidden while its probe is unanswered', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });

    sendCommand(rendererEnv, makeLifecycleHide(BRIDGE_B, 2));
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, {
        bridgeLifecycleEpoch: 2,
        commandSequence: 1,
        commandId: 'b-hide-epoch-2'
    }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, {
        bridgeLifecycleEpoch: 2,
        commandSequence: 2,
        commandId: 'b-show-epoch-2'
    }));
    check('active Bridge lifecycle and Host commands remain blocked until the foreign candidate is resolved', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });
}

// --- Test 10: Unflushed showMarker is superseded by a later hideAll (RAF coalescing) ---

function testUnflushedShowIsSupersededByLaterHide() {
    console.log('\nTest: Unflushed showMarker is superseded by a later hideAll');
    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';

    // Establish context with a flushed hideAll.
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'hide-1' }));

    // Deliver showMarker WITHOUT flushing — marker becomes visible
    // immediately (class added in showThenReceipt), but the shown-receipt
    // RAF is still pending.
    deliverCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'show-2' }));

    // Deliver a newer hideAll WITHOUT flushing — establishHostContext hides
    // the marker and bumps renderStateEpoch, invalidating the pending
    // show-2 RAF.
    deliverCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 3, commandId: 'hide-3' }));

    // Now flush all pending RAF callbacks.
    flushRaf(env);

    check('marker stays hidden after RAF flush', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
    check('no shown receipt for the superseded show-2', () => {
        assert.strictEqual(findReceipts(env, 'show-2', 'shown').length, 0);
    });
    check('hide-3 produces exactly one hidden receipt', () => {
        assert.strictEqual(findReceipts(env, 'hide-3', 'hidden').length, 1);
    });
}

// --- Test 11: A foreign lifecycle candidate invalidates a pending shown-receipt RAF ---

function testForeignCandidateInvalidatesPendingShownReceiptRaf() {
    console.log('\nTest: Foreign lifecycle candidate invalidates pending shown-receipt RAF');
    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';
    const BRIDGE_C = 'bridge-instance-ccc';

    // Establish context and make marker visible.
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'hide-1' }));
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'show-2' }));

    // Deliver another showMarker WITHOUT flushing — its shown-receipt RAF
    // is pending.
    deliverCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 3, commandId: 'show-3' }));

    // A foreign lifecycle candidate appears (delivered without flushing).
    // beginProvisionalLifecycleCandidate hides the marker and bumps
    // renderStateEpoch, invalidating the pending show-3 RAF.
    deliverCommand(env, makeLifecycleHide(BRIDGE_C, 1));

    // Flush all pending RAF callbacks.
    flushRaf(env);

    check('marker hidden after foreign candidate', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
    check('no shown receipt for the old show-3', () => {
        assert.strictEqual(findReceipts(env, 'show-3', 'shown').length, 0);
    });
    check('candidate undecided period does not auto-show', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
}

// --- Test 12: Pending show does not revive marker after delayed mouse passthrough ---

function testPendingShowDoesNotReviveAfterDelayedPassthrough() {
    console.log('\nTest: Pending show does not revive marker after delayed passthrough');

    // Extend the VM mock: setMouseGrab callback is captured, not invoked.
    const env = createEnvironment();
    env.setDelayMouseGrab(true);
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    // flushRaf does NOT invoke the captured mouse-grab callback.

    const BRIDGE_A = 'bridge-instance-aaa';

    // mousePassthroughConfirmed is false. A hideAll still establishes context
    // and sends a hidden receipt (hidden receipts don't require passthrough).
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { commandSequence: 1, commandId: 'hide-1' }));

    // A showMarker is accepted by identity but enters pending show because
    // mousePassthroughConfirmed is false. Marker must NOT become visible.
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { commandSequence: 2, commandId: 'show-2' }));
    check('marker not visible while passthrough is pending', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // A same-Bridge higher lifecycle hide invalidates the pending show
    // (activateLifecycle clears pendingShowCommand and bumps renderStateEpoch).
    sendCommand(env, makeLifecycleHide(BRIDGE_A, 2));

    // Now the delayed mouse-grab callback succeeds.
    env.flushMouseGrab({ success: true });
    flushRaf(env);

    check('marker still hidden after delayed passthrough succeeds', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
    check('pending show does not revive marker', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });
    check('no shown receipt for show-2', () => {
        assert.strictEqual(findReceipts(env, 'show-2', 'shown').length, 0);
    });
}

// --- Test 13: Same Bridge higher bridgeRenderGeneration rebinds via hideAll only ---

function testHigherBridgeRenderGenerationRebindsViaHideAllOnly() {
    console.log('\nTest: Same Bridge higher bridgeRenderGeneration rebinds via hideAll only');

    const env = createEnvironment();
    loadScript(env, 'render-identity.js');
    loadScript(env, 'renderer.js');
    env.flushRaf();

    const BRIDGE_A = 'bridge-instance-aaa';
    const ctxX = { runtimeInstanceId: 'rt-1', connectionEpoch: 1, sessionId: 'session-1', renderLeaseId: 'lease-1' };
    const ctxY = { runtimeInstanceId: 'rt-2', connectionEpoch: 2, sessionId: 'session-2', renderLeaseId: 'lease-2' };

    // Generation 1 / context X: hideAll seq=1, showMarker seq=2 → visible.
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 1, { ...ctxX, commandSequence: 1, commandId: 'x-hide-1' }));
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { ...ctxX, commandSequence: 2, commandId: 'x-show-2' }));
    check('marker visible after generation-1 showMarker', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    // Generation 2 / context Y: showMarker seq=1 must be rejected
    // (higher generation showMarker is not hideAll → rejected).
    const receiptsBeforeGen2Show = env.sentMessages.length;
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 2, { ...ctxY, commandSequence: 1, commandId: 'y-show-1' }));
    check('generation-2 showMarker rejected, marker state unchanged', () => {
        assert.strictEqual(isMarkerVisible(env), true);
        assert.strictEqual(env.sentMessages.length, receiptsBeforeGen2Show);
    });

    // Generation 2 / context Y: hideAll seq=1 must be accepted.
    sendCommand(env, makeCommand('hideAll', BRIDGE_A, 2, { ...ctxY, commandSequence: 1, commandId: 'y-hide-1' }));
    check('generation-2 hideAll accepted, marker hidden', () => {
        assert.strictEqual(isMarkerVisible(env), false);
    });

    // Generation 2 / context Y: showMarker seq=2 → visible.
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 2, { ...ctxY, commandSequence: 2, commandId: 'y-show-2' }));
    check('marker visible after generation-2 showMarker', () => {
        assert.strictEqual(isMarkerVisible(env), true);
    });

    // Verify: generation-1 delayed RAF / receipt cannot display or send
    // a receipt under the generation-2 context. Deliver a generation-1
    // showMarker (stale generation) — it must be rejected by the
    // bridgeRenderGeneration < currentBridgeRenderGeneration guard.
    const receiptsBeforeStaleGen1 = env.sentMessages.length;
    sendCommand(env, makeCommand('showMarker', BRIDGE_A, 1, { ...ctxX, commandSequence: 3, commandId: 'x-show-3-stale' }));
    check('generation-1 stale showMarker cannot display or send receipt', () => {
        assert.strictEqual(isMarkerVisible(env), true);
        assert.strictEqual(env.sentMessages.length, receiptsBeforeStaleGen1);
    });

    // Also verify a generation-1 delayed RAF is invalidated by renderStateEpoch.
    // Re-establish generation-1 context, queue a showMarker RAF without
    // flushing, then rebind to generation-2 and flush.
    const env2 = createEnvironment();
    loadScript(env2, 'render-identity.js');
    loadScript(env2, 'renderer.js');
    env2.flushRaf();

    sendCommand(env2, makeCommand('hideAll', BRIDGE_A, 1, { ...ctxX, commandSequence: 1, commandId: 'x-hide-1' }));
    // showMarker without flush — marker visible immediately, RAF pending.
    deliverCommand(env2, makeCommand('showMarker', BRIDGE_A, 1, { ...ctxX, commandSequence: 2, commandId: 'x-show-2' }));
    // Rebind to generation-2 without flush — hideMarker, renderStateEpoch bumped.
    deliverCommand(env2, makeCommand('hideAll', BRIDGE_A, 2, { ...ctxY, commandSequence: 1, commandId: 'y-hide-1' }));
    flushRaf(env2);

    check('generation-1 delayed RAF produces no shown receipt under generation-2', () => {
        assert.strictEqual(findReceipts(env2, 'x-show-2', 'shown').length, 0);
    });
    check('marker hidden under generation-2 after RAF flush', () => {
        assert.strictEqual(isMarkerVisible(env2), false);
    });
}

// --- Test 14: Matching lifecycle probe ack commits foreign candidate C and restores display; old Bridge B stays quarantined ---

function testMatchingProbeAckCommitsForeignCandidateAndRestoresDisplay() {
    console.log('\nTest: Matching probe ack commits foreign candidate C and restores display; old Bridge B stays quarantined');

    const rendererEnv = createEnvironment();
    loadScript(rendererEnv, 'render-identity.js');
    loadScript(rendererEnv, 'renderer.js');
    rendererEnv.flushRaf();

    const BRIDGE_B = 'bridgebbb';
    const BRIDGE_C = 'bridgeccc';

    // Step 1: Establish Bridge-B (visible).
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 1, commandId: 'b-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 2, commandId: 'b-show-2' }));
    check('Bridge-B is visible before Bridge-C lifecycle takeover', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });

    // Step 2: Build Bridge-C's Host-unavailable VM. Route the Renderer's
    // outgoing bridge-window messages to Bridge-C's REAL listener so that
    // when the Renderer emits rendererLifecycleProbe, Bridge-C's actual
    // startRendererInboundMessageListener receives it and returns a
    // matching bridgeLifecycleProbeAck (ack.bridgeInstanceId === C).
    const bridgeC = createHostUnavailableBridgeEnvironment(rendererEnv, {
        bridgeInstanceId: BRIDGE_C
    });
    rendererEnv.setBridgeMessageRouter(bridgeC.deliverBridgeWindowMessage);
    loadScript(bridgeC, 'background-bridge.js');

    // Bridge-C's startup sendRendererLifecycleHide is delivered through the
    // real sendMessage path; the Renderer begins a provisional candidate
    // and emits a rendererLifecycleProbe targeted at the bridge window.
    // The router forwards that probe to Bridge-C's real listener, which
    // returns a matching bridgeLifecycleProbeAck. The Renderer commits C.
    check('Bridge-C real listener returned a matching bridgeLifecycleProbeAck with bridgeInstanceId === C', () => {
        const probes = rendererEnv.sentMessages.filter((entry) =>
            entry && entry.msg && entry.msg.source === 'renderer' &&
            entry.msg.payload &&
            entry.msg.payload.type === 'rendererLifecycleProbe' &&
            entry.msg.payload.candidateBridgeInstanceId === BRIDGE_C);
        const acks = bridgeC.sentRendererMessages.filter((message) =>
            message && message.payload &&
            message.payload.type === 'bridgeLifecycleProbeAck' &&
            message.payload.bridgeInstanceId === BRIDGE_C);
        assert.strictEqual(probes.length, 1);
        assert.strictEqual(acks.length, 1);
        assert.strictEqual(acks[0].payload.probeId, probes[0].msg.payload.probeId);
    });
    check('marker is hidden after Bridge-C candidate is committed', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
    });

    // Step 4: After matching ack, C's normal Host commands restore display.
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_C, 1, { commandSequence: 1, commandId: 'c-hide-1' }));
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_C, 1, { commandSequence: 2, commandId: 'c-show-2' }));
    check('Bridge-C showMarker restores visibility after matching ack', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
    });
    check('Bridge-C shown receipt is emitted for c-show-2', () => {
        assert.strictEqual(findReceipts(rendererEnv, 'c-show-2', 'shown').length, 1);
    });

    // Step 5: Safety regression — old Bridge-B cannot reclaim display.
    const receiptsBeforeBDelayed = rendererEnv.sentMessages.length;
    sendCommand(rendererEnv, makeCommand('showMarker', BRIDGE_B, 1, { commandSequence: 99, commandId: 'b-show-delayed' }));
    check('delayed Bridge-B showMarker cannot reclaim display after C commit', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
        // B is quarantined: no new receipt emitted for the delayed B command.
        assert.strictEqual(rendererEnv.sentMessages.length, receiptsBeforeBDelayed);
        assert.strictEqual(findReceipts(rendererEnv, 'b-show-delayed', 'shown').length, 0);
    });

    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_B, 1, { commandSequence: 100, commandId: 'b-hide-delayed' }));
    check('delayed Bridge-B hideAll cannot reclaim display after C commit', () => {
        // Marker must remain visible (C's state unchanged); B is quarantined.
        assert.strictEqual(isMarkerVisible(rendererEnv), true);
        assert.strictEqual(rendererEnv.sentMessages.length, receiptsBeforeBDelayed);
        assert.strictEqual(findReceipts(rendererEnv, 'b-hide-delayed', 'hidden').length, 0);
    });

    // Confirm C remains the active bridge: a newer C hide still works.
    sendCommand(rendererEnv, makeCommand('hideAll', BRIDGE_C, 1, { commandSequence: 3, commandId: 'c-hide-3' }));
    check('Bridge-C remains the active bridge after B quarantine (C hide still works)', () => {
        assert.strictEqual(isMarkerVisible(rendererEnv), false);
        assert.strictEqual(findReceipts(rendererEnv, 'c-hide-3', 'hidden').length, 1);
    });
}

// --- Run all tests ---

testBridgeRestart();
testQuarantineNoShowWithoutRebind();
testSameBridgeNormalFlow();
testSameBridgeRejectsStaleAndForeignContextCommands();
testBridgeStartupHidesOldMarkerWithoutHostConnection();
testLifecycleHideIsOneWayAndInvalidatesTheOldContext();
testDelayedOldBridgeLifecycleDoesNotPermanentlyQuarantineCurrentBridge();
testForeignCandidateCannotCommitBeforeMatchingProbeAck();
testActiveLifecycleCannotClearForeignCandidateBeforeProbeAck();
testUnflushedShowIsSupersededByLaterHide();
testForeignCandidateInvalidatesPendingShownReceiptRaf();
testPendingShowDoesNotReviveAfterDelayedPassthrough();
testHigherBridgeRenderGenerationRebindsViaHideAllOnly();
testMatchingProbeAckCommitsForeignCandidateAndRestoresDisplay();

console.log(`\n${checks - failures}/${checks} checks passed.`);
process.exit(failures > 0 ? 1 : 0);
