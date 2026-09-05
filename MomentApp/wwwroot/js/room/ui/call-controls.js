import { byId } from "moment/dom";
import { state } from "moment/state";
import { media } from "moment/rtc/media";

/**
 * The floating control bar.
 *
 * Every button reflects its state through `aria-pressed` or an `off` class, so a control
 * reads correctly to a screen reader as well as visually — "muted" is a state, not just a
 * colour.
 */

function setToggle(id, { pressed, off, label, disabled } = {}) {
    const button = byId(id);
    if (!button) return;
    if (pressed !== undefined) button.setAttribute("aria-pressed", String(pressed));
    if (off !== undefined) button.classList.toggle("off", off);
    if (label !== undefined) button.setAttribute("aria-label", label);
    if (disabled !== undefined) button.disabled = disabled;
}

export const callStatusText = () =>
    state.isInVoice ? (state.isVideoCall ? "Video call" : "Voice call") : "Not connected";

export function refreshCallUi() {
    const inCall = state.isInVoice;

    setToggle("muteVoiceBtn", {
        disabled: !inCall,
        off: inCall && state.isMuted,
        pressed: inCall && !state.isMuted,
        label: state.isMuted ? "Unmute microphone" : "Mute microphone",
    });

    setToggle("cameraToggleBtn", {
        disabled: !inCall,
        off: inCall && !state.isCameraOn,
        pressed: inCall && state.isCameraOn,
        label: state.isCameraOn ? "Turn camera off" : "Turn camera on",
    });

    setToggle("shareScreenBtn", {
        // Screen capture is unavailable on most mobile browsers. A control that cannot work
        // should not look like it might.
        disabled: !inCall || !media.canShareScreen(),
        pressed: state.isScreenSharing,
        label: state.isScreenSharing ? "Stop sharing your screen" : "Share your screen",
    });

    setToggle("leaveVoiceBtn", { disabled: !inCall });

    const startCall = byId("startCallBtn");
    if (startCall) startCall.hidden = inCall;

    // Legacy status nodes: the hub handlers still write through these names.
    const count = byId("voiceCount");
    const status = byId("voiceStatus");
    if (count) count.textContent = state.voiceParticipantIds.size;
    if (status) status.textContent = callStatusText();
}

export const updateVoiceUi = refreshCallUi;
export const updateMuteUi = refreshCallUi;
export const updateCameraUi = refreshCallUi;
