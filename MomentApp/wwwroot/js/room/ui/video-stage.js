import { byId } from "moment/dom";
import { state, selfName } from "moment/state";

/** participantId -> tile element */
const remoteTiles = new Map();
let localTile = null;

function makeTile(stream, label, { muted = false, badge = null } = {}) {
    const tile = document.createElement("div");
    tile.className = "video-tile";

    const video = document.createElement("video");
    video.autoplay = true;
    video.playsInline = true;   // without this iOS forces the video fullscreen
    video.muted = muted;
    video.srcObject = stream;
    tile.appendChild(video);

    const caption = document.createElement("div");
    caption.className = "video-label";
    caption.textContent = label;
    tile.appendChild(caption);

    if (badge) {
        const el = document.createElement("div");
        el.className = "video-local-badge";
        el.textContent = badge;
        tile.appendChild(el);
    }

    return tile;
}

export function attachRemoteVideo(participantId, stream) {
    const existing = remoteTiles.get(participantId);
    if (existing) {
        existing.querySelector("video").srcObject = stream;
        updateVideoStage();
        return;
    }

    const tile = makeTile(stream, state.participantNames.get(participantId) || "Participant");
    tile.dataset.participantId = participantId;
    byId("videoGrid").appendChild(tile);
    remoteTiles.set(participantId, tile);
    updateVideoStage();
}

export function attachLocalVideo(stream) {
    const grid = byId("videoGrid");
    if (!grid) return;

    if (localTile) {
        localTile.querySelector("video").srcObject = stream;
    } else {
        // Muted, or you hear yourself with a delay.
        localTile = makeTile(stream, selfName, { muted: true, badge: "You" });
        grid.appendChild(localTile);
    }
    updateVideoStage();
}

export function removeRemoteVideo(participantId) {
    remoteTiles.get(participantId)?.remove();
    remoteTiles.delete(participantId);
}

export function removeLocalVideo() {
    localTile?.remove();
    localTile = null;
}

export function hasLocalVideo() {
    return localTile !== null;
}

export function updateVideoStage() {
    const stage = byId("videoStage");
    if (!stage) return;
    const showing = state.isVideoCall
        || state.videoParticipantIds.size > 0
        || localTile !== null
        || remoteTiles.size > 0;
    stage.classList.toggle("active", showing);
}
