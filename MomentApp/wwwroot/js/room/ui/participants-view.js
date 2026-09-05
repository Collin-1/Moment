import { byId } from "moment/dom";
import { state, selfId } from "moment/state";

/**
 * The participant roster, rendered into both the sidebar (chat and voice) and the right-hand
 * panel (video). Both mount points show the same list, so the roster is rendered twice from
 * one source rather than kept in two places that can disagree.
 */

const MOUNTS = ["participantsList", "paneParticipants"];

const initial = (name) => (name || "?").trim().charAt(0).toUpperCase() || "?";

const statusClass = (status) =>
    status === 0 ? "online" : status === 1 ? "away" : "offline";

function rowsFor(participantId) {
    return document.querySelectorAll(
        `[data-participant-id="${CSS.escape(participantId)}"]`,
    );
}

function buildRow(participant) {
    const row = document.createElement("div");
    row.className = `participant ${statusClass(participant.status)}`;
    row.dataset.participantId = participant.id;

    row.innerHTML = `
        <span class="avatar"></span>
        <span class="participant-name"></span>
        <span class="participant-state">
            <span class="muted-icon" title="Muted" aria-label="Muted">
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none"
                     stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                    <path d="M4 4l16 16M9 5a3 3 0 0 1 6 0v5m-6 1a3 3 0 0 0 4.5 2.6"/>
                    <path d="M5 11a7 7 0 0 0 10.5 6M19 11a7 7 0 0 1-.4 2.3"/>
                </svg>
            </span>
            <span class="wave" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i><i></i></span>
        </span>
    `;

    // Colour and name are set as properties, never interpolated into the template: a colour
    // placed into a style attribute can close it and inject a handler into every other
    // participant's page.
    row.style.setProperty("--participant-color", participant.colorHex);
    row.querySelector(".avatar").textContent = initial(participant.displayName);
    row.querySelector(".participant-name").textContent =
        participant.displayName + (participant.id === selfId ? " (You)" : "");

    return row;
}

export function addParticipant(participant) {
    state.participantNames.set(participant.id, participant.displayName);
    state.participantColors.set(participant.id, participant.colorHex);

    for (const mountId of MOUNTS) {
        const mount = byId(mountId);
        if (!mount) continue;
        if (mount.querySelector(`[data-participant-id="${CSS.escape(participant.id)}"]`)) continue;
        mount.appendChild(buildRow(participant));
    }

    updateMediaIndicators(participant.id);
}

export function removeParticipant(participantId) {
    rowsFor(participantId).forEach((row) => row.remove());
    state.participantNames.delete(participantId);
    state.participantColors.delete(participantId);
    state.mutedParticipantIds.delete(participantId);
    state.speakingParticipantIds.delete(participantId);
}

export function updateParticipantCount() {
    const count = byId("participantsList")?.children.length ?? 0;
    const el = byId("participantCount");
    const present = byId("presentCount");
    if (el) el.textContent = count;
    if (present) present.textContent = count;
}

export function updateParticipantStatus(participantId, status) {
    rowsFor(participantId).forEach((row) => {
        row.classList.remove("online", "away", "offline");
        row.classList.add(statusClass(status));
    });
}

export function updateMediaIndicators(participantId) {
    const inCall = state.voiceParticipantIds.has(participantId);
    const muted = state.mutedParticipantIds.has(participantId);
    const speaking = state.speakingParticipantIds.has(participantId);

    rowsFor(participantId).forEach((row) => {
        row.classList.toggle("in-call", inCall);
        row.classList.toggle("muted", inCall && muted);
        row.classList.toggle("speaking", inCall && speaking && !muted);
    });
}

export function updateAllMediaIndicators() {
    const seen = new Set();
    document.querySelectorAll("[data-participant-id]").forEach((el) => {
        const id = el.dataset.participantId;
        if (id && !seen.has(id)) {
            seen.add(id);
            updateMediaIndicators(id);
        }
    });
}

/**
 * Drives one participant's waveform bars.
 *
 * Writes a CSS custom property rather than painting: the bars are transformed on the GPU, so
 * this is one style write per participant per frame instead of a canvas repaint each.
 */
export function setLevel(participantId, level) {
    rowsFor(participantId).forEach((row) => {
        row.style.setProperty("--level", level.toFixed(2));
    });
}
