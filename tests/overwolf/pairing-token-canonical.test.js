// I1: Canonical pairing token validation for the Bridge settings.
//
// Verifies that the Bridge's isCanonicalPairingToken rejects non-canonical
// base64url values (42 'A' + 'B') and that isValidSettings rejects a
// settings object carrying such a token.
const assert = require('assert');
const test = require('node:test');
const bridge = require('../../overwolf/tft-companion-poc/background-bridge.js');

const CANONICAL = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; // 43 A's
const NON_CANONICAL = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB'; // 42 A's + B

test('isCanonicalPairingToken accepts a canonical 43-char base64url value', () => {
    assert.equal(bridge.isCanonicalPairingToken(CANONICAL), true);
});

test('isCanonicalPairingToken rejects 42 A + B (non-canonical padding bits)', () => {
    assert.equal(bridge.isCanonicalPairingToken(NON_CANONICAL), false);
});

test('isCanonicalPairingToken rejects wrong length', () => {
    assert.equal(bridge.isCanonicalPairingToken('short'), false);
    assert.equal(bridge.isCanonicalPairingToken(CANONICAL + 'x'), false);
});

test('isCanonicalPairingToken rejects invalid characters', () => {
    assert.equal(bridge.isCanonicalPairingToken('!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!'), false);
});

test('isValidSettings rejects a settings object with a non-canonical pairing token', () => {
    const validBase = {
        host: 'ws://127.0.0.1:32173',
        allowedOrigin: 'overwolf-tool://tft-companion-poc',
        pairingToken: CANONICAL,
        requiredFeatures: ['matchState'],
        matchObservedPath: 'game.matchObserved',
        roundObservedPath: 'game.roundObserved'
    };

    // Sanity: canonical token is accepted.
    assert.equal(bridge.isValidSettings(validBase), true);

    // Non-canonical token must be rejected.
    const nonCanonicalSettings = { ...validBase, pairingToken: NON_CANONICAL };
    assert.equal(bridge.isValidSettings(nonCanonicalSettings), false);
});
