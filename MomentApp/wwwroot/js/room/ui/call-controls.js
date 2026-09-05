import { byId } from "moment/dom";
import { state } from "moment/state";

export function updateVoiceUi(statusText) {
    byId("voiceCount").textContent = state.voiceParticipantIds.size;
    byId("voiceStatus").textContent = statusText;

    byId("joinVoiceBtn").disabled = state.isInVoice;
    byId("joinVideoBtn").disabled = state.isInVoice;
    byId("leaveVoiceBtn").disabled = !state.isInVoice;
    byId("muteVoiceBtn").disabled = !state.isInVoice;
    byId("cameraToggleBtn").disabled = !state.isInVoice;
}

export function updateMuteUi() {
    byId("muteVoiceBtn").textContent = state.isMuted ? "Unmute" : "Mute";
}

export function updateCameraUi() {
    byId("cameraToggleBtn").textContent = state.isCameraOn ? "Camera On" : "Camera Off";
}

/** Status line matching the current call mode. */
export const callStatusText = () =>
    state.isInVoice ? (state.isVideoCall ? "Video call" : "Voice call") : "Not connected";

export function refreshCallUi() {
    updateVoiceUi(callStatusText());
    updateMuteUi();
    updateCameraUi();
}
