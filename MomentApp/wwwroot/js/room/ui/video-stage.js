import { byId } from "moment/dom";
import { state, selfId, selfName } from "moment/state";

/**
 * Video tiles: one equal cell per person in the call.
 *
 * There used to be two views of the same call — a filmstrip of thumbnails and a separate large
 * tile for whoever held the stage — which put the active speaker on screen twice, and showed
 * you two copies of yourself when you were alone. One grid, everybody the same size.
 *
 * A tile always exists for a participant in the call, camera on or not. With the camera off it
 * shows their coloured initial rather than a black rectangle, matching the design's
 * cameras-off frame. The `data-live` attribute on the video element is what CSS uses to decide
 * which of the two to show, and it is driven by the track's own mute/unmute events.
 */

/** participantId -> tile */
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

    byId("videoGrid").appendChild(tile);
    tiles.set(participantId, tile);
    updateStage();
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
 * How many columns a given number of tiles should sit in.
 *
 * The square root, rounded up, which is what produces the arrangements people expect: one on
 * its own, two side by side, three or four as a square, and so on. CSS cannot compute this —
 * `auto-fit` with a minimum width leaves a stranded trailing row at exactly the counts a call
 * of this size actually has.
 */
function columnsFor(count) {
    if (count <= 1) return 1;
    const wanted = Math.ceil(Math.sqrt(count));

    // Two across is as far as a phone can go before faces become thumbnails.
    return window.matchMedia("(max-width: 900px)").matches ? Math.min(wanted, 2) : wanted;
}

/**
 * Lays the tiles out, centring a short last row.
 *
 * The grid is built at twice the column count with every tile spanning two, which is what
 * makes a half-cell offset expressible: three people in two columns puts the third across the
 * middle rather than hard left with a hole beside it.
 */
function layOutGrid(grid, count) {
    const cols = columnsFor(count);
    grid.style.gridTemplateColumns = `repeat(${cols * 2}, minmax(0, 1fr))`;

    const remainder = count % cols;
    const firstOfLastRow = remainder === 0 ? -1 : count - remainder;
    const offset = remainder === 0 ? 0 : cols - remainder;

    // Both halves, not just the start: setting grid-column-start alone replaces the `span 2`
    // that the stylesheet's shorthand put there, and the tile collapses to a single track.
    [...tiles.values()].forEach((tile, index) => {
        tile.style.gridColumn = index === firstOfLastRow ? `${1 + offset} / span 2` : "";
    });

    return cols;
}

/**
 * Lays the grid out and marks who is speaking.
 *
 * Named for what it used to do — promote somebody to a large tile — and kept under that name
 * because a dozen callers ask for a refresh after changing something. There is no large tile
 * any more: the arrangement is the layout, and the active speaker is a ring.
 */
export function updateStage() {
    const grid = byId("videoGrid");
    if (!grid) return;

    const cols = layOutGrid(grid, tiles.size);
    grid.dataset.count = String(tiles.size);
    grid.dataset.cols = String(cols);

    // Only our own share is known here — a remote one arrives as an ordinary video track, with
    // nothing to distinguish it — so this marks the one tile we can be sure about.
    const sharing = state.isScreenSharing ? selfId : null;

    for (const [participantId, tile] of tiles) {
        tile.classList.toggle(
            "speaking",
            state.speakingParticipantIds.has(participantId)
                && !state.mutedParticipantIds.has(participantId),
        );
        tile.toggleAttribute("data-share", participantId === sharing);

        // A name can arrive after the tile does, when somebody joins the call before the
        // roster entry that names them.
        const label = tile.querySelector(".tile-label");
        const avatar = tile.querySelector(".tile-avatar span");
        const name = participantId === selfId
            ? selfName
            : state.participantNames.get(participantId) || "Participant";

        if (label) label.textContent = participantId === selfId ? `${name} (You)` : name;
        if (avatar) avatar.textContent = initial(name);

        const colour = state.participantColors.get(participantId);
        if (colour) tile.style.setProperty("--participant-color", colour);
    }
}

/** Kept for callers that only want a refresh of visibility. */
export const updateVideoStage = updateStage;
