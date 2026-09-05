import { byId } from "moment/dom";
import { state, selfId } from "moment/state";

const findRow = (participantId) =>
    document.querySelector(`[data-participant-id="${CSS.escape(participantId)}"]`);

const statusClass = (status) =>
    status === 0 ? "dot-online" : status === 1 ? "dot-away" : "dot-offline";

export function addParticipant(participant) {
    state.participantNames.set(participant.id, participant.displayName);

    if (findRow(participant.id)) {
        return;
    }

    const row = document.createElement("div");
    row.className = "participant";
    row.setAttribute("data-participant-id", participant.id);
    row.innerHTML = `
        <div class="participant-dot ${statusClass(participant.status)}"></div>
        <div class="participant-color"></div>
        <div class="participant-name"></div>
        <div class="participant-media">
            <span class="media-indicator media-voice" title="In voice">&#127897;</span>
            <span class="media-indicator media-video" title="Video on">&#128247;</span>
        </div>
    `;

    // Set as properties rather than interpolated into the markup: a colour interpolated into
    // a style attribute can close the attribute and inject a handler into every other
    // participant's page. setProperty silently rejects an invalid value, which is what we want.
    row.querySelector(".participant-color")
        .style.setProperty("background-color", participant.colorHex);
    row.querySelector(".participant-name")
        .textContent = participant.displayName + (participant.id === selfId ? " (You)" : "");

    byId("participantsList").appendChild(row);
    updateMediaIndicators(participant.id);
}

export function removeParticipant(participantId) {
    findRow(participantId)?.remove();
    state.participantNames.delete(participantId);
}

export function updateParticipantCount() {
    byId("participantCount").textContent = byId("participantsList").children.length;
}

export function updateParticipantStatus(participantId, status) {
    const dot = findRow(participantId)?.querySelector(".participant-dot");
    if (dot) {
        dot.className = "participant-dot " + statusClass(status);
    }
}

export function updateMediaIndicators(participantId) {
    const row = findRow(participantId);
    if (!row) return;

    const mic = row.querySelector(".media-voice");
    if (mic) {
        const inCall = state.voiceParticipantIds.has(participantId);
        const muted = state.mutedParticipantIds.has(participantId);
        mic.classList.toggle("active", inCall && !muted);
        mic.classList.toggle("muted", inCall && muted);
        mic.title = !inCall ? "Not in call" : muted ? "Muted" : "In voice";
    }
    row.querySelector(".media-video")
        ?.classList.toggle("active", state.videoParticipantIds.has(participantId));
}

export function updateAllMediaIndicators() {
    document.querySelectorAll("[data-participant-id]").forEach((el) => {
        const id = el.getAttribute("data-participant-id");
        if (id) updateMediaIndicators(id);
    });
}
