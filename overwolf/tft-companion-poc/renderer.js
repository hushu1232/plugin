// TFT Companion v0.0.1 Renderer: fixed commands only, no Host socket.

(function () {
    'use strict';

    const BRIDGE_WINDOW_NAME = 'tft-companion-poc-bridge';
    const RENDERER_WINDOW_NAME = 'renderer';
    const identity = typeof window !== 'undefined' ? window.TftCompanionRenderIdentity : null;
    let mousePassthroughConfirmed = false;
    let currentIdentity = null;
    let pendingShowCommand = null;
    let pendingShowStateEpoch = 0;
    let currentBridgeRenderGeneration = 0;
    let activeBridgeInstanceId = null;
    let activeBridgeLifecycleEpoch = 0;
    let provisionalLifecycleCandidate = null;
    let lifecycleProbeSequence = 0;
    let renderStateEpoch = 0;
    const quarantinedBridgeInstanceIds = new Set();

    start();

    function start() {
        hideMarker();
        enforcePassthrough();

        if (!identity || typeof overwolf === 'undefined' ||
            !overwolf.windows ||
            !overwolf.windows.onMessageReceived ||
            !overwolf.windows.onMessageReceived.addListener) {
            return;
        }

        overwolf.windows.onMessageReceived.addListener(function (event) {
            const envelope = event && event.message ? event.message : event;
            const command = envelope && envelope.source === BRIDGE_WINDOW_NAME ? envelope.payload : null;
            if (isValidLifecycleProbeAck(command)) {
                handleLifecycleProbeAck(command);
                return;
            }
            if (isValidLifecycleHide(command)) {
                handleLifecycleHide(command);
                return;
            }
            if (!isValidHostCommand(command)) {
                return;
            }

            handleHostCommand(command);
        });
    }

    function isValidHostCommand(command) {
        return command &&
            (command.type === 'hideAll' || command.type === 'showMarker') &&
            typeof command.bridgeInstanceId === 'string' && command.bridgeInstanceId.length > 0 &&
            Number.isInteger(command.bridgeLifecycleEpoch) && command.bridgeLifecycleEpoch > 0 &&
            typeof command.runtimeInstanceId === 'string' && command.runtimeInstanceId.length > 0 &&
            Number.isInteger(command.connectionEpoch) && command.connectionEpoch > 0 &&
            Number.isInteger(command.commandSequence) && command.commandSequence > 0 &&
            Number.isInteger(command.bridgeRenderGeneration) && command.bridgeRenderGeneration > 0 &&
            typeof command.sessionId === 'string' && command.sessionId.length > 0 &&
            typeof command.renderLeaseId === 'string' && command.renderLeaseId.length > 0 &&
            typeof command.commandId === 'string' && command.commandId.length > 0;
    }

    function isValidLifecycleHide(command) {
        return command &&
            command.type === 'bridgeLifecycleHide' &&
            typeof command.bridgeInstanceId === 'string' && command.bridgeInstanceId.length > 0 &&
            Number.isInteger(command.bridgeLifecycleEpoch) && command.bridgeLifecycleEpoch > 0 &&
            !Object.prototype.hasOwnProperty.call(command, 'runtimeInstanceId') &&
            !Object.prototype.hasOwnProperty.call(command, 'connectionEpoch') &&
            !Object.prototype.hasOwnProperty.call(command, 'sessionId') &&
            !Object.prototype.hasOwnProperty.call(command, 'renderLeaseId') &&
            !Object.prototype.hasOwnProperty.call(command, 'commandSequence') &&
            !Object.prototype.hasOwnProperty.call(command, 'commandId') &&
            !Object.prototype.hasOwnProperty.call(command, 'bridgeRenderGeneration');
    }

    function isValidLifecycleProbeAck(command) {
        return command &&
            command.type === 'bridgeLifecycleProbeAck' &&
            typeof command.probeId === 'string' && command.probeId.length > 0 &&
            typeof command.bridgeInstanceId === 'string' && command.bridgeInstanceId.length > 0;
    }

    function handleLifecycleHide(command) {
        if (quarantinedBridgeInstanceIds.has(command.bridgeInstanceId)) {
            return;
        }

        if (provisionalLifecycleCandidate) {
            if (command.bridgeInstanceId === provisionalLifecycleCandidate.bridgeInstanceId &&
                command.bridgeLifecycleEpoch > provisionalLifecycleCandidate.bridgeLifecycleEpoch) {
                beginProvisionalLifecycleCandidate(command);
            } else if (command.bridgeInstanceId !== activeBridgeInstanceId) {
                beginProvisionalLifecycleCandidate(command);
            }
            return;
        }

        if (!activeBridgeInstanceId) {
            activateLifecycle(command);
            return;
        }

        if (activeBridgeInstanceId === command.bridgeInstanceId) {
            if (command.bridgeLifecycleEpoch <= activeBridgeLifecycleEpoch) {
                return;
            }
            activateLifecycle(command);
            return;
        }

        beginProvisionalLifecycleCandidate(command);
    }

    function activateLifecycle(command) {
        activeBridgeInstanceId = command.bridgeInstanceId;
        activeBridgeLifecycleEpoch = command.bridgeLifecycleEpoch;
        currentIdentity = null;
        currentBridgeRenderGeneration = 0;
        provisionalLifecycleCandidate = null;
        pendingShowCommand = null;
        pendingShowStateEpoch = 0;
        renderStateEpoch += 1;
        hideMarker();
    }

    function beginProvisionalLifecycleCandidate(command) {
        if (provisionalLifecycleCandidate &&
            provisionalLifecycleCandidate.bridgeInstanceId === command.bridgeInstanceId &&
            command.bridgeLifecycleEpoch <= provisionalLifecycleCandidate.bridgeLifecycleEpoch) {
            return;
        }

        provisionalLifecycleCandidate = {
            bridgeInstanceId: command.bridgeInstanceId,
            bridgeLifecycleEpoch: command.bridgeLifecycleEpoch,
            probeId: String(++lifecycleProbeSequence)
        };
        pendingShowCommand = null;
        pendingShowStateEpoch = 0;
        renderStateEpoch += 1;
        hideMarker();
        sendLifecycleProbe(provisionalLifecycleCandidate);
    }

    function sendLifecycleProbe(candidate) {
        if (typeof overwolf === 'undefined' ||
            !overwolf.windows ||
            !overwolf.windows.obtainDeclaredWindow ||
            !overwolf.windows.sendMessage) {
            return;
        }

        overwolf.windows.obtainDeclaredWindow(BRIDGE_WINDOW_NAME, function (result) {
            if (!isCurrentLifecycleCandidate(candidate) ||
                !result || !result.window || !result.window.id) {
                return;
            }

            overwolf.windows.sendMessage(result.window.id, {
                source: RENDERER_WINDOW_NAME,
                payload: {
                    type: 'rendererLifecycleProbe',
                    probeId: candidate.probeId,
                    candidateBridgeInstanceId: candidate.bridgeInstanceId,
                    candidateBridgeLifecycleEpoch: candidate.bridgeLifecycleEpoch
                }
            }, function () {});
        });
    }

    function isCurrentLifecycleCandidate(candidate) {
        return Boolean(provisionalLifecycleCandidate) &&
            provisionalLifecycleCandidate.probeId === candidate.probeId &&
            provisionalLifecycleCandidate.bridgeInstanceId === candidate.bridgeInstanceId &&
            provisionalLifecycleCandidate.bridgeLifecycleEpoch === candidate.bridgeLifecycleEpoch;
    }

    function handleLifecycleProbeAck(ack) {
        const candidate = provisionalLifecycleCandidate;
        if (!candidate || ack.probeId !== candidate.probeId) {
            return;
        }

        if (ack.bridgeInstanceId === candidate.bridgeInstanceId) {
            commitLifecycleCandidate(candidate);
            return;
        }

        if (ack.bridgeInstanceId === activeBridgeInstanceId) {
            provisionalLifecycleCandidate = null;
            pendingShowCommand = null;
            pendingShowStateEpoch = 0;
            hideMarker();
        }
    }

    function commitLifecycleCandidate(candidate) {
        if (activeBridgeInstanceId && activeBridgeInstanceId !== candidate.bridgeInstanceId) {
            quarantinedBridgeInstanceIds.add(activeBridgeInstanceId);
        }
        activeBridgeInstanceId = candidate.bridgeInstanceId;
        activeBridgeLifecycleEpoch = candidate.bridgeLifecycleEpoch;
        currentIdentity = null;
        currentBridgeRenderGeneration = 0;
        provisionalLifecycleCandidate = null;
        pendingShowCommand = null;
        pendingShowStateEpoch = 0;
        renderStateEpoch += 1;
        hideMarker();
    }

    function handleHostCommand(command) {
        if (quarantinedBridgeInstanceIds.has(command.bridgeInstanceId)) {
            return;
        }

        if (provisionalLifecycleCandidate) {
            return;
        }

        if (!activeBridgeInstanceId) {
            if (command.type === 'hideAll') {
                establishHostContext(command);
            }
            return;
        }

        if (activeBridgeInstanceId !== command.bridgeInstanceId) {
            if (command.type !== 'hideAll') {
                return;
            }
            quarantinedBridgeInstanceIds.add(activeBridgeInstanceId);
            establishHostContext(command);
            return;
        }

        if (command.bridgeLifecycleEpoch < activeBridgeLifecycleEpoch) {
            return;
        }

        if (command.bridgeLifecycleEpoch > activeBridgeLifecycleEpoch) {
            if (command.type === 'hideAll') {
                establishHostContext(command);
            }
            return;
        }

        if (!currentIdentity) {
            if (command.type === 'hideAll') {
                establishHostContext(command);
            }
            return;
        }

        if (command.bridgeRenderGeneration < currentBridgeRenderGeneration) {
            return;
        }

        if (command.bridgeRenderGeneration > currentBridgeRenderGeneration) {
            if (command.type === 'hideAll') {
                establishHostContext(command);
            }
            return;
        }

        if (command.type === 'hideAll' && identity.canAcceptHide(currentIdentity, command)) {
            establishHostContext(command);
        } else if (command.type === 'showMarker' && identity.canAcceptShow(currentIdentity, command)) {
            currentIdentity = identity.apply(command);
            renderStateEpoch += 1;
            if (mousePassthroughConfirmed) {
                showThenReceipt(command, renderStateEpoch);
            } else {
                pendingShowCommand = command;
                pendingShowStateEpoch = renderStateEpoch;
            }
        }
    }

    function establishHostContext(command) {
        activeBridgeInstanceId = command.bridgeInstanceId;
        activeBridgeLifecycleEpoch = command.bridgeLifecycleEpoch;
        currentBridgeRenderGeneration = command.bridgeRenderGeneration;
        currentIdentity = identity.apply(command);
        pendingShowCommand = null;
        pendingShowStateEpoch = 0;
        renderStateEpoch += 1;
        hideThenReceipt(command, renderStateEpoch);
    }

    function enforcePassthrough() {
        if (typeof overwolf === 'undefined' ||
            !overwolf.windows ||
            !overwolf.windows.getCurrentWindow ||
            !overwolf.windows.setMouseGrab) {
            pendingShowCommand = null;
            pendingShowStateEpoch = 0;
            renderStateEpoch += 1;
            hideMarker();
            return;
        }

        overwolf.windows.getCurrentWindow(function (currentWindow) {
            if (!currentWindow || !currentWindow.window || !currentWindow.window.id) {
                pendingShowCommand = null;
                pendingShowStateEpoch = 0;
                renderStateEpoch += 1;
                hideMarker();
                return;
            }

            overwolf.windows.setMouseGrab(currentWindow.window.id, false, function (result) {
                mousePassthroughConfirmed = Boolean(result && (result.success === true || result.status === 'success'));
                if (!mousePassthroughConfirmed) {
                    pendingShowCommand = null;
                    pendingShowStateEpoch = 0;
                    renderStateEpoch += 1;
                    hideMarker();
                    return;
                }

                showPendingMarkerIfCurrent();
            });
        });
    }

    function showPendingMarkerIfCurrent() {
        const command = pendingShowCommand;
        const expectedStateEpoch = pendingShowStateEpoch;
        pendingShowCommand = null;
        pendingShowStateEpoch = 0;
        if (command && matchesCurrentIdentity(command, expectedStateEpoch) && mousePassthroughConfirmed) {
            showThenReceipt(command, expectedStateEpoch);
        }
    }

    function showThenReceipt(command, expectedStateEpoch) {
        const marker = document.getElementById('poc-marker');
        if (!marker || !mousePassthroughConfirmed) {
            return;
        }

        marker.classList.add('visible');
        requestAnimationFrame(function () {
            if (matchesCurrentIdentity(command, expectedStateEpoch) && mousePassthroughConfirmed &&
                marker.isConnected && marker.getClientRects().length > 0) {
                sendReceipt(command, 'shown', expectedStateEpoch);
            }
        });
    }

    function hideThenReceipt(command, expectedStateEpoch) {
        hideMarker();
        requestAnimationFrame(function () {
            if (matchesCurrentIdentity(command, expectedStateEpoch)) {
                sendReceipt(command, 'hidden', expectedStateEpoch);
            }
        });
    }

    function matchesCurrentIdentity(command, expectedStateEpoch) {
        return !provisionalLifecycleCandidate &&
            expectedStateEpoch === renderStateEpoch &&
            Boolean(currentIdentity) &&
            activeBridgeInstanceId === command.bridgeInstanceId &&
            activeBridgeLifecycleEpoch === command.bridgeLifecycleEpoch &&
            currentIdentity.bridgeInstanceId === command.bridgeInstanceId &&
            currentIdentity.runtimeInstanceId === command.runtimeInstanceId &&
            currentIdentity.connectionEpoch === command.connectionEpoch &&
            currentIdentity.sessionId === command.sessionId &&
            currentIdentity.renderLeaseId === command.renderLeaseId &&
            currentIdentity.commandSequence === command.commandSequence &&
            currentBridgeRenderGeneration === command.bridgeRenderGeneration &&
            currentIdentity.bridgeLifecycleEpoch === command.bridgeLifecycleEpoch &&
            currentIdentity.commandId === command.commandId;
    }

    function hideMarker() {
        const marker = document.getElementById('poc-marker');
        if (marker) {
            marker.classList.remove('visible');
        }
    }

    function sendReceipt(command, receiptType, expectedStateEpoch) {
        if (typeof overwolf === 'undefined' ||
            !overwolf.windows ||
            !overwolf.windows.obtainDeclaredWindow ||
            !overwolf.windows.sendMessage) {
            return;
        }

        overwolf.windows.obtainDeclaredWindow(BRIDGE_WINDOW_NAME, function (result) {
            if (!matchesCurrentIdentity(command, expectedStateEpoch) ||
                (receiptType === 'shown' && !mousePassthroughConfirmed)) {
                return;
            }
            if (!result || !result.window || !result.window.id) {
                return;
            }

            overwolf.windows.sendMessage(result.window.id, {
                source: RENDERER_WINDOW_NAME,
                payload: {
                    type: 'rendererReceipt',
                    receiptType: receiptType,
                    runtimeInstanceId: command.runtimeInstanceId,
                    connectionEpoch: command.connectionEpoch,
                    commandSequence: command.commandSequence,
                    sessionId: command.sessionId,
                    renderLeaseId: command.renderLeaseId,
                    commandId: command.commandId
                }
            }, function () {});
        });
    }
})();
