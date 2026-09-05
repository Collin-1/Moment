import { byId } from "moment/dom";
import { state, selfId, selfName } from "moment/state";

/**
 * Video tiles: a filmstrip of everyone in the call, plus one large tile for whoever currently
 * holds the stage.
 *
 * A tile always exists for a participant in the call, camera on or not. With the camera off it
 * shows their coloured initial rather than a black rectangle, matching the design's
 * cameras-off frame. The `data-live` attribute on the video element is what CSS uses to decide
 * which of the two to show, and it is driven by the track's own mute/unmute events.
 */

/** participantId -> filmstrip tile */
const tiles = new Map();

function initial(name) {
    return (name || "?").trim().charAt(0).toUpperCase() || "?";
}

function buildTile(participantId) {
    const tile = document.createElement("div");
    tile.className = "tile";
    tile.dataset.participantId = participantId;

    const name = participantId === selfId
        ? selfName
        : state.participantNames.get(participantId) || "Participant";

    tile.innerHTML = `
        <video autoplay playsinline${participantId === selfId ? " muted" : ""}></video>
        <div class="tile-avatar"><span></span></div>
        <div class="tile-label"></div>
    `;

    tile.style.setProperty(
        "--participant-color",
        state.participantColors.get(participantId) || "#4492ca",
    );
    tile.querySelector(".tile-avatar span").textContent = initial(name);
    tile.querySelector(".tile-label").textContent =
        participantId === selfId ? `${name} (You)` : name;

    byId("filmstrip").appendChild(tile);
    tiles.set(participantId, tile);
    return tile;
}

export function ensureTile(participantId) {
    return tiles.get(participantId) ?? buildTile(participantId);
}

/**
 * Attaches a stream to a participant's tile and follows the track's liveness.
 *
 * A received video track starts muted and unmutes once frames actually arrive; driving the
 * avatar fallback off those events means a peer with their camera off shows their initial
 * without any signalling of our own.
 */
export function attachRemoteVideo(participantId, stream) {
    const tile = ensureTile(participantId);
    const video = tile.querySelector("video");
    if (video.srcObject !== stream) video.srcObject = stream;

    const track = stream.getVideoTracks()[0];
    if (!track) return;

    const sync = () => {
        video.toggleAttribute("data-live", !track.muted && track.readyState === "live");
        updateStage();
    };

    track.addEventListener("unmute", sync);
    track.addEventListener("mute", sync);
    track.addEventListener("ended", sync);
    sync();
}

export function attachLocalVideo(stream) {
    const tile = ensureTile(selfId);
    const video = tile.querySelector("video");
    video.srcObject = stream;               // muted at creation, or you hear yourself delayed
    video.toggleAttribute("data-live", Boolean(stream?.getVideoTracks().length));
    updateStage();
}

export function removeRemoteVideo(participantId) {
    tiles.get(participantId)?.remove();
    tiles.delete(participantId);
    updateStage();
}

export function removeLocalVideo() {
    const tile = tiles.get(selfId);
    if (!tile) return;
    const video = tile.querySelector("video");
    video.srcObject = null;
    video.removeAttribute("data-live");
    updateStage();
}

export function clearTiles() {
    tiles.forEach((tile) => tile.remove());
    tiles.clear();
    updateStage();
}

export function hasLocalVideo() {
    return tiles.get(selfId)?.querySelector("video")?.hasAttribute("data-live") ?? false;
}

/**
 * Promotes one participant to the large tile.
 *
 * Priority is screen share, then the active speaker, then whoever spoke last — a share is
 * always the thing people need to see, and stickiness stops the stage flipping on a one-word
 * interjection.
 */
export function updateStage() {
    const stage = byId("stageTile");
    if (!stage) return;

    const video = playing => playing;
    const candidate = state.stageParticipantId
        ?? state.activeSpeakerId
        ?? [...state.videoParticipantIds][0]
        ?? null;

    if (!candidate || document.body.dataset.mode !== "video") {
        stage.hidden = true;
        return;
    }

    const source = tiles.get(candidate)?.querySelector("video");
    let stageVideo = stage.querySelector("video");

    if (!stageVideo) {
        stageVideo = document.createElement("video");
        stageVideo.autoplay = true;
        stageVideo.playsInline = true;
        stageVideo.muted = true;   // audio already plays through the peer audio elements
        stage.prepend(stageVideo);
    }

    stageVideo.srcObject = source?.srcObject ?? null;
    stageVideo.toggleAttribute("data-live", Boolean(source?.hasAttribute("data-live")));
    stage.toggleAttribute("data-share", state.stageParticipantId === candidate && state.isScreenSharing);

    const name = candidate === selfId
        ? `${selfName} (You)`
        : state.participantNames.get(candidate) || "Participant";

    stage.style.setProperty(
        "--participant-color",
        state.participantColors.get(candidate) || "#4492ca",
    );
    byId("stageTileInitial").textContent = initial(name);
    byId("stageTileLabel").textContent = name;
    stage.hidden = false;
}

/** Kept for callers that only want a refresh of visibility. */
export const updateVideoStage = updateStage;
