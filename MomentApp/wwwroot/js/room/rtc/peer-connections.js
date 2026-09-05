import { state, selfId } from "moment/state";
import { currentIceConfig, relayAvailable } from "moment/rtc/ice";
import { hub } from "moment/hub";
import { media } from "moment/rtc/media";
import { attachRemoteVideo, removeRemoteVideo, updateVideoStage } from "moment/ui/video-stage";
import { byId } from "moment/dom";

/**
 * Full-mesh WebRTC: one RTCPeerConnection per remote participant, signalled over the hub.
 *
 * Implements the perfect-negotiation pattern, which is what makes simultaneous changes on both
 * sides safe. Three design choices carry most of the weight:
 *
 *  - **Both transceivers are created up front, in a fixed order, on both sides.** That means a
 *    participant who joined audio-only still has a video m-line and can receive video when
 *    somebody turns their camera on. It also means every later change is an in-place direction
 *    or track swap rather than an m-line append — and simultaneous m-line appends are the most
 *    glare-prone thing two peers can do.
 *  - **`onnegotiationneeded` is the only place an offer is created.** Initial connect, camera
 *    toggle, screen share and ICE restart all funnel through one path, so there is one
 *    ordering to reason about instead of four.
 *  - **A `disconnected` connection is given time to heal rather than torn down.** That state is
 *    routine on a Wi-Fi roam or a cellular handover and usually recovers within seconds;
 *    closing on it converts a blip into a dropped call.
 */

/** How long to let a `disconnected` peer recover before forcing an ICE restart. */
const DISCONNECT_GRACE_MS = 6000;
/** How long to let a `failed` peer recover after a restart before rebuilding it. */
const FAILED_REBUILD_MS = 12000;

/** @type {Map<string, PeerRecord>} */
const peers = new Map();

/** Notified when a peer connection gives up, so the UI can say something truthful. */
let onPeerFailed = null;
export const setPeerFailedHandler = (fn) => { onPeerFailed = fn; };
const remoteAudio = new Map();

/**
 * @typedef {object} PeerRecord
 * @property {string} id
 * @property {RTCPeerConnection} pc
 * @property {boolean} polite
 * @property {boolean} makingOffer
 * @property {boolean} ignoreOffer
 * @property {boolean} settingRemoteAnswer
 * @property {RTCIceCandidateInit[]} pendingCandidates
 * @property {RTCRtpTransceiver} audio
 * @property {RTCRtpTransceiver} video
 * @property {ReturnType<typeof setTimeout> | null} recoveryTimer
 */

function create(remoteId) {
    const pc = new RTCPeerConnection(currentIceConfig());

    /** @type {PeerRecord} */
    const peer = {
        id: remoteId,
        pc,
        // Deterministic and always opposite on the two sides, so no coordination message is
        // needed to agree who yields in a collision.
        polite: selfId < remoteId,
        makingOffer: false,
        ignoreOffer: false,
        settingRemoteAnswer: false,
        pendingCandidates: [],
        audio: pc.addTransceiver("audio", { direction: "sendrecv" }),
        video: pc.addTransceiver("video", {
            direction: media.hasVideo() ? "sendrecv" : "recvonly",
        }),
        recoveryTimer: null,
    };

    if (media.stream) {
        const audioTrack = media.stream.getAudioTracks()[0];
        const videoTrack = media.stream.getVideoTracks()[0];
        if (audioTrack) peer.audio.sender.replaceTrack(audioTrack);
        if (videoTrack) peer.video.sender.replaceTrack(videoTrack);
    }

    pc.onnegotiationneeded = async () => {
        try {
            peer.makingOffer = true;
            // Implicit setLocalDescription: the browser picks offer or answer as appropriate.
            await pc.setLocalDescription();
            await hub.sendSignal(remoteId, "description", pc.localDescription);
        } catch (err) {
            console.error("negotiationneeded", remoteId, err);
        } finally {
            peer.makingOffer = false;
        }
    };

    pc.onicecandidate = ({ candidate }) => {
        if (candidate) {
            hub.sendSignal(remoteId, "candidate", candidate).catch(() => {});
        }
    };

    pc.ontrack = ({ track, streams }) => {
        // `replaceTrack` on a pre-created transceiver does not associate the track with a
        // MediaStream, so `streams` arrives empty — unlike `addTrack`, which carries the
        // stream across. Wrapping the bare track keeps both paths working.
        const stream = streams[0] ?? new MediaStream([track]);

        if (track.kind === "audio") {
            attachRemoteAudio(remoteId, stream);
            return;
        }

        // A received video track starts muted and unmutes once frames actually arrive. Driving
        // the tile off those events means a peer with their camera off shows no tile at all,
        // rather than an empty black rectangle, and turning the camera back on restores it
        // without any signalling of our own.
        const show = () => attachRemoteVideo(remoteId, stream);
        const hide = () => {
            removeRemoteVideo(remoteId);
            updateVideoStage();
        };

        track.addEventListener("unmute", show);
        track.addEventListener("mute", hide);
        track.addEventListener("ended", hide);

        if (!track.muted) show();
    };

    pc.oniceconnectionstatechange = () => {
        if (pc.iceConnectionState === "failed") {
            pc.restartIce();
        }
    };

    pc.onconnectionstatechange = () => {
        clearTimeout(peer.recoveryTimer);

        if (pc.connectionState === "disconnected") {
            peer.recoveryTimer = setTimeout(() => {
                if (pc.connectionState !== "connected") pc.restartIce();
            }, DISCONNECT_GRACE_MS);
        } else if (pc.connectionState === "failed") {
            onPeerFailed?.(remoteId, relayAvailable);
            peer.recoveryTimer = setTimeout(() => {
                if (pc.connectionState === "failed") {
                    close(remoteId);
                    connectTo(remoteId);
                }
            }, FAILED_REBUILD_MS);
        } else if (pc.connectionState === "closed") {
            close(remoteId);
        }
    };

    peers.set(remoteId, peer);
    return peer;
}

const ensure = (remoteId) => peers.get(remoteId) ?? create(remoteId);

/**
 * Opens a connection to a peer. Idempotent: creating the transceivers fires
 * `negotiationneeded`, which is what actually sends the offer.
 */
export function connectTo(remoteId) {
    if (remoteId === selfId) return;
    ensure(remoteId);
}

export async function handleSignal(signal) {
    const remoteId = signal.fromParticipantId;
    const peer = ensure(remoteId);
    const { pc } = peer;

    try {
        if (signal.type === "description") {
            const description = JSON.parse(signal.data);

            const readyForOffer = !peer.makingOffer
                && (pc.signalingState === "stable" || peer.settingRemoteAnswer);
            const offerCollision = description.type === "offer" && !readyForOffer;

            peer.ignoreOffer = !peer.polite && offerCollision;
            if (peer.ignoreOffer) return;

            // Tracks the window where an answer is in flight, so a description arriving during
            // it is not mistaken for a collision.
            peer.settingRemoteAnswer = description.type === "answer";
            await pc.setRemoteDescription(description);
            peer.settingRemoteAnswer = false;

            await flushCandidates(peer);

            if (description.type === "offer") {
                await pc.setLocalDescription();
                await hub.sendSignal(remoteId, "description", pc.localDescription);
            }
            return;
        }

        if (signal.type === "candidate") {
            const candidate = JSON.parse(signal.data);
            if (!candidate) return;

            // A candidate can arrive before the description it belongs to: the offerer starts
            // trickling as soon as setLocalDescription resolves. Queue rather than throw.
            if (!pc.remoteDescription) {
                peer.pendingCandidates.push(candidate);
                return;
            }

            try {
                await pc.addIceCandidate(candidate);
            } catch (err) {
                // Candidates for an offer we deliberately ignored are expected to fail.
                if (!peer.ignoreOffer) throw err;
            }
        }
    } catch (err) {
        console.error("handleSignal", remoteId, signal.type, err);
    }
}

async function flushCandidates(peer) {
    const queued = peer.pendingCandidates.splice(0);
    for (const candidate of queued) {
        try {
            await peer.pc.addIceCandidate(candidate);
        } catch (err) {
            console.warn("stale ICE candidate discarded", err);
        }
    }
}

/**
 * Swaps the outgoing video track on every peer.
 *
 * `replaceTrack` does not renegotiate when the sender is already sending, which is what makes
 * a camera swap or a screen-share handover seamless. Changing direction does renegotiate —
 * exactly once per peer, absorbed by perfect negotiation.
 */
export async function replaceVideoTrack(track) {
    for (const peer of peers.values()) {
        await peer.video.sender.replaceTrack(track);

        const wanted = track ? "sendrecv" : "recvonly";
        if (peer.video.direction !== wanted) {
            peer.video.direction = wanted;
        }

        if (track) applyEncoding(peer, track.contentHint);
    }
    updateVideoStage();
}

export async function replaceAudioTrack(track) {
    for (const peer of peers.values()) {
        await peer.audio.sender.replaceTrack(track);
    }
}

/**
 * Screen content needs sharpness over smoothness; camera content needs the opposite. Without
 * this, a shared screen at camera-tuned settings produces unreadable text.
 */
function applyEncoding(peer, contentHint) {
    try {
        const params = peer.video.sender.getParameters();
        params.encodings = params.encodings?.length ? params.encodings : [{}];
        params.degradationPreference =
            contentHint === "detail" ? "maintain-resolution" : "maintain-framerate";
        params.encodings[0].maxBitrate = contentHint === "detail" ? 1_500_000 : 600_000;
        peer.video.sender.setParameters(params).catch(() => {});
    } catch {
        // Encoding hints are an optimisation; never let them break a call.
    }
}

function attachRemoteAudio(remoteId, stream) {
    if (remoteAudio.has(remoteId)) {
        remoteAudio.get(remoteId).srcObject = stream;
        return;
    }

    const audio = document.createElement("audio");
    audio.autoplay = true;
    audio.playsInline = true;
    audio.srcObject = stream;
    audio.dataset.participantId = remoteId;
    byId("voiceAudioContainer").appendChild(audio);
    remoteAudio.set(remoteId, audio);
}

export function close(remoteId) {
    const peer = peers.get(remoteId);
    if (peer) {
        clearTimeout(peer.recoveryTimer);
        peer.pc.onnegotiationneeded = null;
        peer.pc.onicecandidate = null;
        peer.pc.ontrack = null;
        peer.pc.onconnectionstatechange = null;
        peer.pc.oniceconnectionstatechange = null;
        peer.pc.close();
        peers.delete(remoteId);
    }

    const audio = remoteAudio.get(remoteId);
    if (audio) {
        audio.srcObject = null;
        audio.remove();
        remoteAudio.delete(remoteId);
    }

    removeRemoteVideo(remoteId);
}

export function closeAll() {
    [...peers.keys()].forEach(close);
    peers.clear();
}

/**
 * Brings the mesh in line with the roster the server just gave us, after a reconnect.
 *
 * Peers whose media never dropped are left strictly alone — media flows peer-to-peer and
 * survives a signalling outage, so rebuilding a healthy connection would cause the very
 * interruption the reconnect is meant to avoid.
 */
export async function reconcilePeers(roster) {
    const wanted = new Map((roster || []).map((p) => [p.id, p]));

    for (const id of [...peers.keys()]) {
        if (!wanted.has(id)) close(id);
    }

    for (const [id, entry] of wanted) {
        if (id === selfId) continue;

        const peer = peers.get(id);
        if (!peer) {
            if (entry.shouldOffer !== false) connectTo(id);
            continue;
        }

        if (["connected", "connecting", "new"].includes(peer.pc.connectionState)) continue;
        if (entry.shouldOffer !== false) peer.pc.restartIce();
    }
}

/* ------------------------------------------------------------------ diagnostics */

/** Connection state per peer. Used by the test harness and useful when debugging a call. */
export function peerStates() {
    return [...peers.values()].map((p) => ({
        id: p.id,
        state: p.pc.connectionState,
        ice: p.pc.iceConnectionState,
        signaling: p.pc.signalingState,
        polite: p.polite,
        videoDirection: p.video.direction,
    }));
}

/** Total inbound bytes, which is the honest answer to "is media actually flowing". */
export async function stats() {
    let inboundAudio = 0;
    let inboundVideo = 0;

    for (const peer of peers.values()) {
        const report = await peer.pc.getStats();
        report.forEach((s) => {
            if (s.type !== "inbound-rtp") return;
            if (s.kind === "audio") inboundAudio += s.bytesReceived || 0;
            if (s.kind === "video") inboundVideo += s.bytesReceived || 0;
        });
    }

    return { inboundAudio, inboundVideo, peers: peers.size };
}
