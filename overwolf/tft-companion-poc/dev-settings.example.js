// Copy this file to dev-settings.local.js and replace every placeholder with
// target-machine values verified against the current official Overwolf/GEP docs.
// This example is intentionally non-working and contains no secret.
window.TftCompanionPocDevSettings = Object.freeze({
    host: 'ws://127.0.0.1:32173',
    allowedOrigin: 'copy-the-exact-loaded-overwolf-origin-here',
    pairingToken: 'copy-a-43-character-base64url-pairing-token-here',
    requiredFeatures: ['copy-a-currently-documented-gep-feature-here'],
    matchObservedPath: 'copy.current.documented.match.path',
    roundObservedPath: 'copy.current.documented.round.path'
});
