// Static safety check for the v0.0.1 Overwolf PoC.
// This test intentionally uses no package dependency. It checks only the
// committed bundle; dev-settings.local.js is ignored and must never be read.

const assert = require('assert');
const fs = require('fs');
const path = require('path');

const repositoryRoot = path.join(__dirname, '..', '..');
const overwolfDir = path.join(repositoryRoot, 'overwolf', 'tft-companion-poc');
const manifest = readJson('manifest.json');
const packageJson = readJson('package.json');
const bridgeHtml = readText('background-bridge.html');
const bridgeJs = readText('background-bridge.js');
const rendererHtml = readText('renderer.html');
const rendererJs = readText('renderer.js');
const rendererCss = readText('renderer.css');
const renderIdentityJs = readText('render-identity.js');
const exampleSettings = readText('dev-settings.example.js');
const gitignore = fs.readFileSync(path.join(repositoryRoot, '.gitignore'), 'utf8');

let failures = 0;
const checks = [];

function readText(fileName) {
    return fs.readFileSync(path.join(overwolfDir, fileName), 'utf8');
}

function readJson(fileName) {
    return JSON.parse(readText(fileName));
}

function check(name, fn) {
    try {
        fn();
        checks.push({ name, passed: true });
    } catch (error) {
        failures++;
        checks.push({ name, passed: false, message: error.message });
    }
}

function assertNoForbiddenTokens(source, fileName) {
    const forbidden = [
        'fetch(',
        'XMLHttpRequest',
        'EventSource',
        'sendBeacon',
        'localStorage',
        'sessionStorage',
        'indexedDB',
        'overwolf.io',
        'SendInput',
        'sendKey',
        'sendMouse',
        'simulateInput',
        'MemoryRead',
        'PacketCapture',
        'adb',
        'http://',
        'https://'
    ];

    for (const token of forbidden) {
        assert.ok(!source.includes(token), `${fileName} contains forbidden token: ${token}`);
    }
}

check('manifest targets TFT and declares a transparent click-through renderer', () => {
    assert.strictEqual(manifest.game_id, 21570, 'game_id must be the TFT GEP ID');
    assert.strictEqual(manifest.windows.renderer.transparent, true, 'renderer must be transparent');
    assert.strictEqual(manifest.windows.renderer.clickthrough, true, 'renderer must request click-through');
    assert.strictEqual(manifest.windows.renderer.topmost, true, 'renderer must stay topmost');
});

check('manifest requests no input, memory, injection, capture or remote permissions', () => {
    const allManifest = JSON.stringify(manifest.permissions || {}).toLowerCase();
    for (const token of ['keyboardinput', 'mouseinput', 'memory', 'inject', 'capture', 'microphone', 'remote']) {
        assert.ok(!allManifest.includes(token), `manifest must not contain ${token}`);
    }
});

check('a tracked non-secret example exists and the real local settings remain ignored', () => {
    assert.ok(exampleSettings.includes('TftCompanionPocDevSettings'), 'example must expose the declared settings object');
    assert.ok(exampleSettings.includes('ws://127.0.0.1:32173'), 'example must pin the fixed loopback host');
    assert.ok(gitignore.includes('/overwolf/tft-companion-poc/dev-settings.local.js'), 'local JavaScript settings must be ignored');
    assert.ok(gitignore.includes('/overwolf/tft-companion-poc/dev-settings.local.json'), 'local JSON settings must be ignored');
});

check('background page loads the ignored local settings before the Bridge', () => {
    const settingsIndex = bridgeHtml.indexOf('dev-settings.local.js');
    const bridgeIndex = bridgeHtml.lastIndexOf('background-bridge.js');
    assert.ok(settingsIndex >= 0, 'background page must reference dev-settings.local.js');
    assert.ok(bridgeIndex > settingsIndex, 'Bridge must run after local settings are available');
});

check('Bridge is the only Host peer and owns both physical loopback channels', () => {
    assert.ok(bridgeJs.includes('/ingest'), 'Bridge must own /ingest');
    assert.ok(bridgeJs.includes('/render'), 'Bridge must own /render');
    assert.ok(bridgeJs.includes('new WebSocket'), 'Bridge must create loopback WebSockets');
    assert.ok(bridgeJs.includes('pairingProof'), 'Bridge must send the pairing proof in Hello body');
    assert.ok(bridgeJs.includes('bridgeInstanceId'), 'Bridge must bind both channels to one bridge instance');
    assert.ok(bridgeJs.includes('connectionEpoch'), 'Bridge must validate the render connection epoch');
    assert.ok(bridgeJs.includes('commandSequence'), 'Bridge must reject out-of-order render commands');
    assert.ok(bridgeJs.includes('ws://127.0.0.1:32173'), 'Bridge must validate the exact fixed loopback endpoint');
    assert.ok(!bridgeJs.includes('?pairing'), 'pairing material must not appear in a URL query');
    assert.ok(!bridgeJs.includes('pairingToken='), 'pairing material must not appear in a URL query');
});

check('Bridge fails closed without exact local configuration and only emits semantic snapshots', () => {
    for (const name of ['host', 'allowedOrigin', 'pairingToken', 'requiredFeatures', 'matchObservedPath', 'roundObservedPath']) {
        assert.ok(bridgeJs.includes(name), `Bridge must validate ${name}`);
    }
    assert.ok(bridgeJs.includes('stateSnapshot'), 'Bridge must send a typed stateSnapshot');
    assert.ok(bridgeJs.includes('isAuthoritativeSnapshot'), 'snapshot must declare its authority');
    assert.ok(bridgeJs.includes('setRequiredFeatures'), 'Bridge must register a configured GEP feature allowlist');
    assert.ok(bridgeJs.includes('onNewEvents'), 'Bridge must observe GEP events');
    assert.ok(bridgeJs.includes('onInfoUpdates2'), 'Bridge must observe GEP info updates');
    assert.ok(bridgeJs.includes('getInfo'), 'Bridge must request an authoritative snapshot only when needed');
    assert.ok(!bridgeJs.includes('JSON.stringify(info)'), 'Bridge must not serialize raw GEP info');
});

check('Renderer never connects directly to Host and only handles fixed declarative render commands', () => {
    assert.ok(!rendererJs.includes('new WebSocket'), 'Renderer must not create a Host WebSocket');
    assert.ok(!rendererJs.includes('ws://'), 'Renderer must not contain a WebSocket URL');
    assert.ok(rendererJs.includes('hideAll'), 'Renderer must accept HideAll');
    assert.ok(rendererJs.includes('showMarker'), 'Renderer must accept ShowMarker');
    assert.ok(rendererJs.includes('bridgeLifecycleHide'), 'Renderer must handle a one-way Bridge lifecycle hide');
    assert.ok(rendererJs.includes('commandId'), 'Renderer must echo a command identity in receipts');
    assert.ok(rendererJs.includes('renderLeaseId'), 'Renderer must verify a render lease identity');
    assert.ok(rendererJs.includes('runtimeInstanceId'), 'Renderer must track a Host runtime identity');
    assert.ok(rendererJs.includes('connectionEpoch'), 'Renderer must reject stale connection epochs');
    assert.ok(rendererJs.includes('commandSequence'), 'Renderer must reject stale command sequences');
    assert.ok(rendererJs.includes('setMouseGrab'), 'Renderer must request window-level mouse passthrough');
    assert.ok(rendererCss.includes('pointer-events: none'), 'Renderer CSS must be click-through as defence in depth');
    assert.ok(rendererHtml.includes('render-identity.js'), 'Renderer HTML must load the tested identity guard');
    assert.ok(rendererHtml.includes('renderer.js'), 'Renderer HTML must load its local renderer script');
});

check('Bridge and Renderer contain no fallback transport, persistence, game control or external network', () => {
    assertNoForbiddenTokens(bridgeJs, 'background-bridge.js');
    assertNoForbiddenTokens(rendererJs, 'renderer.js');
    assertNoForbiddenTokens(renderIdentityJs, 'render-identity.js');
    assertNoForbiddenTokens(bridgeHtml, 'background-bridge.html');
    assertNoForbiddenTokens(rendererHtml, 'renderer.html');
});

check('Renderer contains no coaching or business-logic vocabulary', () => {
    const lower = rendererJs.toLowerCase();
    for (const word of ['advice', 'strategy', 'positioning', 'synergy', 'winrate']) {
        assert.ok(!lower.includes(word), `renderer must not contain business keyword: ${word}`);
    }
    for (const pattern of [/\bcomp\b/, /\btier\b/, /\bhex\b/]) {
        assert.ok(!pattern.test(lower), `renderer must not contain business keyword: ${pattern}`);
    }
});

check('the package test script resolves every repository boundary and Renderer regression suite from the bundle directory', () => {
    const script = packageJson.scripts.test;
    assert.ok(script.includes('node --test'), 'npm test must use the Node test runner');
    assert.ok(script.includes('../../tests/overwolf/static-boundary-check.js'), 'npm test must include the static boundary suite');
    assert.ok(script.includes('../../tests/overwolf/render-identity.test.js'), 'npm test must include the identity behaviour suite');
    assert.ok(script.includes('../../tests/overwolf/pairing-token-canonical.test.js'), 'npm test must include canonical pairing-token coverage');
    assert.ok(script.includes('../../tests/overwolf/renderer-bridge-restart-test.js'), 'npm test must include the Bridge restart behaviour suite');
});

console.log('Static boundary check results:');
for (const checkResult of checks) {
    const status = checkResult.passed ? 'PASS' : 'FAIL';
    console.log(`  [${status}] ${checkResult.name}`);
    if (!checkResult.passed) {
        console.log(`          ${checkResult.message}`);
    }
}

console.log(`\n${checks.length - failures}/${checks.length} checks passed.`);
process.exit(failures === 0 ? 0 : 1);
