import { byId } from "moment/dom";
import { state, selfId, selfName } from "moment/state";

/**
 * The voice-call stage: one large avatar for whoever is speaking, with expanding rings and a
 * waveform. This is what a call looks like when nobody has a camera on.
 */

const initial = (name) => (name || "?").trim().charAt(0).toUpperCase() || "?";

export function renderSpeaker() {
    const empty = byId("callEmpty");
    const speaker = byId("activeSpeaker");
    if (!empty || !speaker) return;

    if (!state.isInVoice) {
        empty.hidden = false;
        speaker.hidden = true;
        document.body.dataset.speaking = "false";
        return;
    }

    empty.hidden = true;
    speaker.hidden = false;

    // Falls back to self so the stage is never blank in a silent room — a call with nobody
    // talking should still show who is in it.
    const id = state.activeSpeakerId ?? selfId;
    const name = id === selfId ? selfName : state.participantNames.get(id) || "Participant";
    const speaking = state.speakingParticipantIds.has(id);

    speaker.style.setProperty(
        "--participant-color",
        state.participantColors.get(id) || "#00ff4d",
    );
    byId("speakerAvatar").textContent = initial(name);
    byId("speakerName").textContent = id === selfId ? `${name} (You)` : name;
    byId("speakerStatus").textContent = speaking
        ? "speaking now"
        : state.mutedParticipantIds.has(id) ? "muted" : "in the call";

    document.body.dataset.speaking = String(speaking);
}

/** Drives the large stage waveform from the active speaker's level. */
export function setSpeakerLevel(level) {
    byId("speakerWave")?.style.setProperty("--level", level.toFixed(2));
}
