// Unit tests for the C1 bridgeInstanceId fail-closed rebind policy.
//
// These tests verify the identity contract implemented in
// render-identity.js: a different bridgeInstanceId must enter quarantine
// via hideAll, and must never be allowed to show without a prior rebind.
const assert = require('assert');
const test = require('node:test');
const identity = require('../../overwolf/tft-companion-poc/render-identity.js');

const bridgeA = Object.freeze({
    bridgeInstanceId: 'bridge-a',
    runtimeInstanceId: 'runtime-1',
    connectionEpoch: 8,
    sessionId: 'session-a',
    renderLeaseId: 'lease-a',
    commandSequence: 4,
    bridgeRenderGeneration: 1,
    bridgeLifecycleEpoch: 1,
    commandId: 'command-4'
});

const bridgeB = Object.freeze({
    ...bridgeA,
    bridgeInstanceId: 'bridge-b',
    connectionEpoch: 9,
    sessionId: 'session-b',
    renderLeaseId: 'lease-b',
    commandSequence: 1,
    bridgeRenderGeneration: 1,
    bridgeLifecycleEpoch: 1,
    commandId: 'command-b-1'
});

test('canAcceptHide returns true for the first command (initial identity)', () => {
    assert.equal(identity.canAcceptHide(null, bridgeA), true);
});

test('canAcceptHide accepts a strictly newer command from the same complete render context', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.canAcceptHide(current, {
        ...bridgeA,
        commandSequence: 5,
        commandId: 'command-5'
    }), true);
});

test('canAcceptHide rejects stale or equal sequences from the same bridge', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.canAcceptHide(current, { ...bridgeA, commandSequence: 4, commandId: 'command-4-repeat' }), false);
    assert.equal(identity.canAcceptHide(current, { ...bridgeA, commandSequence: 3, commandId: 'command-3' }), false);
});

// Table-driven identity field isolation tests.
// Each variant mutates exactly ONE identity field while keeping
// commandSequence=6 (> current 5) and commandId='cmd-6' (≠ current 'cmd-5'),
// so the only reason for rejection is the single field mismatch.
const identityBase = Object.freeze({
    bridgeInstanceId: 'bridge-a',
    runtimeInstanceId: 'runtime-1',
    connectionEpoch: 8,
    sessionId: 'session-a',
    renderLeaseId: 'lease-a',
    commandSequence: 5,
    bridgeRenderGeneration: 1,
    bridgeLifecycleEpoch: 1,
    commandId: 'cmd-5'
});

const singleFieldMutations = [
    { field: 'runtimeInstanceId', value: 'runtime-other' },
    { field: 'connectionEpoch', value: 99 },
    { field: 'sessionId', value: 'session-other' },
    { field: 'renderLeaseId', value: 'lease-other' },
    { field: 'bridgeRenderGeneration', value: 2 },
    { field: 'bridgeLifecycleEpoch', value: 0 },
    { field: 'bridgeLifecycleEpoch', value: 2 }
];

for (const mutation of singleFieldMutations) {
    test(`canAcceptHide rejects when only ${mutation.field}=${mutation.value} differs`, () => {
        const current = identity.apply(identityBase);
        const command = { ...identityBase, [mutation.field]: mutation.value, commandSequence: 6, commandId: 'cmd-6' };
        assert.equal(identity.canAcceptHide(current, command), false);
    });
}

test('canAcceptHide returns false when bridgeInstanceId differs (rebind handled elsewhere)', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.canAcceptHide(current, bridgeB), false);
});

test('canAcceptShow returns false when there is no current identity', () => {
    assert.equal(identity.canAcceptShow(null, bridgeA), false);
});

test('canAcceptShow accepts a strictly newer command from the same complete render context', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.canAcceptShow(current, {
        ...bridgeA,
        commandSequence: 5,
        commandId: 'command-5'
    }), true);
});

test('canAcceptShow rejects stale or equal sequences from the same bridge', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.canAcceptShow(current, { ...bridgeA, commandSequence: 4, commandId: 'command-4-repeat' }), false);
    assert.equal(identity.canAcceptShow(current, { ...bridgeA, commandSequence: 3, commandId: 'command-3' }), false);
});

for (const mutation of singleFieldMutations) {
    test(`canAcceptShow rejects when only ${mutation.field}=${mutation.value} differs`, () => {
        const current = identity.apply(identityBase);
        const command = { ...identityBase, [mutation.field]: mutation.value, commandSequence: 6, commandId: 'cmd-6' };
        assert.equal(identity.canAcceptShow(current, command), false);
    });
}

test('canAcceptHide rejects when commandId is reused despite a higher sequence', () => {
    const current = identity.apply(identityBase);
    const command = { ...identityBase, commandSequence: 6, commandId: 'cmd-5' };
    assert.equal(identity.canAcceptHide(current, command), false);
});

test('canAcceptShow rejects when commandId is reused despite a higher sequence', () => {
    const current = identity.apply(identityBase);
    const command = { ...identityBase, commandSequence: 6, commandId: 'cmd-5' };
    assert.equal(identity.canAcceptShow(current, command), false);
});

test('canAcceptHide accepts same complete context with strictly higher sequence and different commandId', () => {
    const current = identity.apply(identityBase);
    const command = { ...identityBase, commandSequence: 6, commandId: 'cmd-6' };
    assert.equal(identity.canAcceptHide(current, command), true);
});

test('canAcceptShow accepts same complete context with strictly higher sequence and different commandId', () => {
    const current = identity.apply(identityBase);
    const command = { ...identityBase, commandSequence: 6, commandId: 'cmd-6' };
    assert.equal(identity.canAcceptShow(current, command), true);
});

test('same-Bridge commands cannot cross a lifecycle epoch', () => {
    const current = identity.apply(bridgeA);
    const fromAnOlderLifecycle = {
        ...bridgeA,
        bridgeLifecycleEpoch: 0,
        commandSequence: 5,
        commandId: 'command-old-lifecycle'
    };
    const fromANewerLifecycle = {
        ...bridgeA,
        bridgeLifecycleEpoch: 2,
        commandSequence: 5,
        commandId: 'command-new-lifecycle'
    };

    assert.equal(identity.canAcceptHide(current, fromAnOlderLifecycle), false);
    assert.equal(identity.canAcceptShow(current, fromAnOlderLifecycle), false);
    assert.equal(identity.canAcceptHide(current, fromANewerLifecycle), false);
    assert.equal(identity.canAcceptShow(current, fromANewerLifecycle), false);
});

test('canAcceptShow returns false when bridgeInstanceId differs (must rebind first)', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.canAcceptShow(current, bridgeB), false);
});

test('isRebind returns false when there is no current identity', () => {
    assert.equal(identity.isRebind(null, bridgeA), false);
});

test('isRebind returns false when bridgeInstanceId matches', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.isRebind(current, { ...bridgeA, commandSequence: 99 }), false);
});

test('isRebind returns true when bridgeInstanceId differs', () => {
    const current = identity.apply(bridgeA);
    assert.equal(identity.isRebind(current, bridgeB), true);
});

test('apply produces an identity capturing all identity fields', () => {
    const id = identity.apply(bridgeA);
    assert.equal(id.bridgeInstanceId, 'bridge-a');
    assert.equal(id.runtimeInstanceId, 'runtime-1');
    assert.equal(id.connectionEpoch, 8);
    assert.equal(id.sessionId, 'session-a');
    assert.equal(id.renderLeaseId, 'lease-a');
    assert.equal(id.commandSequence, 4);
    assert.equal(id.bridgeRenderGeneration, 1);
    assert.equal(id.bridgeLifecycleEpoch, 1);
    assert.equal(id.commandId, 'command-4');
});
