// TFT Companion v0.0.1 Renderer Identity Contract.
//
// Fail-closed rebind policy for bridgeInstanceId changes:
//
// When the Renderer receives a command whose bridgeInstanceId differs from
// the current identity's bridgeInstanceId, the following policy applies:
//
//   1. If the incoming bridgeInstanceId is in the quarantined set, the
//      command is silently dropped. A quarantined bridge can never rebind
//      or affect Renderer state.
//
//   2. If the incoming bridgeInstanceId is NOT quarantined:
//      a. A hideAll command REBINDS: the old bridgeInstanceId is added to
//         the quarantined set (it can never take over again), the marker
//         is hidden, the pending show is cleared, and the new
//         bridgeInstanceId becomes current.
//      b. A showMarker command is REJECTED. The new bridge must first
//         establish itself via a hideAll rebind before it can show.
//
//   3. The first command received by the Renderer must be a hideAll. This
//      establishes the initial bridgeInstanceId without quarantining any
//      prior bridge.
//
// This policy ensures that:
//   - A restarted Bridge can establish itself via hideAll (rebind).
//   - An old Bridge's delayed showMarker cannot revive the marker.
//   - An old Bridge's delayed hideAll cannot rebind back (it is
//     quarantined once the new Bridge takes over).
//   - bridgeRenderGeneration is only used for intra-bridge ordering,
//     never for cross-bridge lifecycle decisions.

(function () {
    'use strict';

    function canAcceptHide(currentIdentity, command) {
        if (!command || typeof command.bridgeInstanceId !== 'string' || command.bridgeInstanceId.length === 0) {
            return false;
        }
        // First command: hideAll establishes initial identity.
        if (!currentIdentity) {
            return true;
        }
        return isStrictlyNewerSameContextCommand(currentIdentity, command);
    }

    function canAcceptShow(currentIdentity, command) {
        if (!currentIdentity || typeof currentIdentity.bridgeInstanceId !== 'string') {
            return false;
        }
        if (!command || typeof command.bridgeInstanceId !== 'string' || command.bridgeInstanceId.length === 0) {
            return false;
        }
        return isStrictlyNewerSameContextCommand(currentIdentity, command);
    }

    function isRebind(currentIdentity, command) {
        if (!currentIdentity) {
            return false;
        }
        if (!command || typeof command.bridgeInstanceId !== 'string' || command.bridgeInstanceId.length === 0) {
            return false;
        }
        return currentIdentity.bridgeInstanceId !== command.bridgeInstanceId;
    }

    function isStrictlyNewerSameContextCommand(currentIdentity, command) {
        return currentIdentity.bridgeInstanceId === command.bridgeInstanceId &&
            currentIdentity.runtimeInstanceId === command.runtimeInstanceId &&
            currentIdentity.connectionEpoch === command.connectionEpoch &&
            currentIdentity.sessionId === command.sessionId &&
            currentIdentity.renderLeaseId === command.renderLeaseId &&
            currentIdentity.bridgeRenderGeneration === command.bridgeRenderGeneration &&
            currentIdentity.bridgeLifecycleEpoch === command.bridgeLifecycleEpoch &&
            Number.isInteger(command.commandSequence) &&
            command.commandSequence > currentIdentity.commandSequence &&
            typeof command.commandId === 'string' && command.commandId.length > 0 &&
            currentIdentity.commandId !== command.commandId;
    }

    function apply(command) {
        return {
            bridgeInstanceId: command.bridgeInstanceId,
            runtimeInstanceId: command.runtimeInstanceId,
            connectionEpoch: command.connectionEpoch,
            sessionId: command.sessionId,
            renderLeaseId: command.renderLeaseId,
            commandSequence: command.commandSequence,
            bridgeRenderGeneration: command.bridgeRenderGeneration,
            bridgeLifecycleEpoch: command.bridgeLifecycleEpoch,
            commandId: command.commandId
        };
    }

    var api = {
        canAcceptHide: canAcceptHide,
        canAcceptShow: canAcceptShow,
        isRebind: isRebind,
        apply: apply
    };

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = api;
    }

    if (typeof window !== 'undefined') {
        window.TftCompanionRenderIdentity = api;
    }
})();
