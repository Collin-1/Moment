import { config } from "moment/config";
import { byId } from "moment/dom";
import { state, selfId } from "moment/state";
import { connection, hub, on, start, isConnected } from "moment/hub";
import { media } from "moment/rtc/media";
import { getIceConfig } from "moment/rtc/ice";
import {
    connectTo, handleSignal, close as closePeer, closeAll,
    replaceVideoTrack, reconcilePeers, remoteAudioStream, audioPeerIds,
    peerStates, stats, setPeerFailedHandler,
} from "moment/rtc/peers";
import { SpeakingDetector, LevelMeter, audioContext } from "moment/audio/speaking";
import { showNotification, setReconnecting } from "moment/ui/toast";
import { updateTimer, initTimer } from "moment/ui/timer";
import { addMessage, showTyping, hideTyping, scrollToBottom, initChatView } from "moment/ui/chat";
import {
    addParticipant, removeParticipant, updateParticipantCount,
    updateParticipantStatus, updateMediaIndicators, updateAllMediaIndicators, setLevel,
} from "moment/ui/participants";
import { updateVotePanel, initiateVote } from "moment/ui/vote";
import { refreshCallUi, callStatusText, updateVoiceUi } from "moment/ui/call-controls";
import {
    attachLocalVideo, removeRemoteVideo, removeLocalVideo,
    ensureTile, clearTiles, updateStage,
} from "moment/ui/video-stage";
import { renderSpeaker, setSpeakerLevel } from "moment/ui/call-view";
import { initLayout, setMode, currentMode } from "moment/ui/layout";

const HEARTBEAT_MS = 30000;
const SPEAKING_THROTTLE_MS = 300;

/** Local voice activity detector, live only while in a call. */
let detector = null;
/** peerId -> LevelMeter over their inbound audio. */
const meters = new Map();
let levelFrame = null;
let lastSpeakingSent = 0;
let pendingSpeaking = null;
let speakingTimer = null;

const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/* ------------------------------------------------------------ speaking + levels */

function markSpeaking(participantId, speaking) {
    if (speaking) state.speakingParticipantIds.add(participantId);
    else state.speakingParticipantIds.delete(participantId);

    // The stage follows the most recent speaker, and only moves while somebody is actually
    // talking, so it does not reshuffle every time a sentence ends.
    if (speaking) state.activeSpeakerId = participantId;

    updateMediaIndicators(participantId);
    renderSpeaker();
}

/** Sends our own speaking state on transitions only, rate-limited, trailing value flushed. */
function reportSpeaking(speaking) {
    pendingSpeaking = speaking;
    const wait = Math.max(0, SPEAKING_THROTTLE_MS - (Date.now() - lastSpeakingSent));

    const flush = () => {
        speakingTimer = null;
        if (pendingSpeaking === null) return;
        lastSpeakingSent = Date.now();
        const value = pendingSpeaking;
        pendingSpeaking = null;
        hub.setSpeaking(value).catch(() => {});
    };

    if (wait === 0) {
        flush();
        return;
    }
    if (!speakingTimer) speakingTimer = setTimeout(flush, wait);
}

/**
 * Drives every waveform from one loop.
 *
 * requestAnimationFrame is right here — unlike the speaking boolean — because a hidden tab
 * genuinely should stop animating waveforms, and one loop for the page beats one per
 * participant.
 */
function startLevelLoop() {
    if (levelFrame || reduceMotion) return;

    const tick = () => {
        if (detector) setLevel(selfId, detector.enabled ? detector.level : 0);

        for (const [peerId, meter] of meters) {
            setLevel(peerId, meter.sample());
        }

        const active = state.activeSpeakerId;
        if (active) {
            const level = active === selfId
                ? (detector?.level ?? 0)
                : (meters.get(active)?.level ?? 0);
            setSpeakerLevel(level);
        }

        levelFrame = requestAnimationFrame(tick);
    };

    levelFrame = requestAnimationFrame(tick);
}

function stopLevelLoop() {
    if (levelFrame) cancelAnimationFrame(levelFrame);
    levelFrame = null;
    meters.forEach((meter) => meter.stop());
    meters.clear();
}

/** Attaches a level meter to each peer whose inbound audio has arrived. */
function syncMeters() {
    for (const peerId of audioPeerIds()) {
        if (meters.has(peerId)) continue;
        const stream = remoteAudioStream(peerId);
        if (!stream) continue;
        try {
            meters.set(peerId, new LevelMeter(stream));
        } catch (err) {
            console.warn("level meter unavailable for", peerId, err);
        }
    }

    for (const peerId of [...meters.keys()]) {
        if (!state.voiceParticipantIds.has(peerId)) {
            meters.get(peerId).stop();
            meters.delete(peerId);
        }
    }
}

/* ------------------------------------------------------------------ call actions */

async function joinCall({ video }) {
    if (state.isInVoice) return;

    if (!media.supported()) {
        showNotification((video ? "Video" : "Voice") + " calling is not supported in this browser.");
        return;
    }

    try {
        state.isVideoCall = video;

        // Resumed here because this always runs from a tap: an AudioContext starts suspended
        // under the autoplay policy and can only be resumed from a user gesture.
        audioContext();

        await media.acquire({ video });
        await getIceConfig();

        detector = new SpeakingDetector(media.stream, {
            onSpeakingChange: (speaking) => {
                markSpeaking(selfId, speaking);
                reportSpeaking(speaking);
            },
        });
        startLevelLoop();

        ensureTile(selfId);
        if (video) attachLocalVideo(media.stream);

        refreshCallUi();
        await (video ? hub.joinVideo() : hub.joinVoice());
    } catch (err) {
        console.error("Call join error:", err);
        showNotification(video
            ? "Camera and microphone access are required to join the video call."
            : "Microphone access is required to join the call.");
        cleanupCall();
    }
}

async function leaveCall() {
    if (!state.isInVoice && !media.stream) return;
    try {
        await hub.leaveVoice();
    } catch (err) {
        console.error("Call leave error:", err);
    }
    cleanupCall();
}

function cleanupCall() {
    state.isInVoice = false;
    state.isVideoCall = false;
    state.activeSpeakerId = null;
    state.stageParticipantId = null;
    state.voiceParticipantIds.delete(selfId);
    state.videoParticipantIds.clear();
    state.speakingParticipantIds.clear();

    detector?.stop();
    detector = null;
    stopLevelLoop();

    closeAll();
    media.release();
    clearTiles();

    refreshCallUi();
    renderSpeaker();
    updateStage();
    updateAllMediaIndicators();
    document.body.dataset.speaking = "false";
}

function toggleMute() {
    const muted = media.toggleMute();

    // Disabling the detector while muted means a muted participant can never render as
    // speaking anywhere, rather than every view having to check both flags.
    detector?.setEnabled(!muted);
    if (muted) markSpeaking(selfId, false);

    refreshCallUi();
    applyMediaState(selfId, { isMuted: muted });
    hub.setMuted(muted).catch((err) => console.error("setMuted", err));
}

async function toggleCamera() {
    if (!state.isInVoice) return;
    try {
        if (state.isCameraOn) {
            media.stopCamera();
            await replaceVideoTrack(null);
            removeLocalVideo();
            await hub.setVideoEnabled(false);
        } else {
            const track = await media.startCamera();
            state.isVideoCall = true;
            await replaceVideoTrack(track);
            attachLocalVideo(media.stream);
            await hub.setVideoEnabled(true);
            if (currentMode() !== "video") setMode("video");
        }
        refreshCallUi();
        updateStage();
    } catch (err) {
        console.error("Camera toggle error:", err);
        showNotification("Failed to turn the camera on.");
    }
}

async function toggleScreenShare() {
    if (!state.isInVoice) return;

    try {
        if (state.isScreenSharing) {
            await stopSharing();
            return;
        }

        const track = await media.startScreenShare({ onEnded: () => stopSharing() });
        await replaceVideoTrack(track);
        state.stageParticipantId = selfId;
        attachLocalVideo(media.displayStream);
        await hub.setVideoEnabled(true);
        if (currentMode() !== "video") setMode("video");
        refreshCallUi();
        updateStage();
    } catch (err) {
        // A refused picker throws. That is a choice, not a failure worth shouting about.
        if (err?.name !== "NotAllowedError") {
            console.error("Screen share error:", err);
            showNotification("Could not start screen sharing.");
        }
        state.isScreenSharing = false;
        refreshCallUi();
    }
}

async function stopSharing() {
    const camera = media.stopScreenShare();
    await replaceVideoTrack(camera);
    state.stageParticipantId = null;

    if (camera) attachLocalVideo(media.stream);
    else removeLocalVideo();

    await hub.setVideoEnabled(Boolean(camera)).catch(() => {});
    refreshCallUi();
    updateStage();
}

async function leaveRoom() {
    if (!confirm("Leave this room permanently? You will not be able to rejoin.")) return;
    try {
        if (state.isInVoice) await leaveCall();
        await hub.leaveRoom();
        window.location.href = config.homeUrl;
    } catch (err) {
        console.error("Error leaving room:", err);
    }
}

function shareRoom() {
    const url = window.location.href;
    if (navigator.share) {
        navigator.share({ title: "Join my moment", text: "Join my room", url }).catch(() => {});
    } else {
        navigator.clipboard.writeText(url).then(
            () => showNotification("Link copied to clipboard"),
            () => showNotification("Could not copy the link"),
        );
    }
}

/** Records a small summary for the end-of-room screen. Never leaves the browser. */
function rememberSummary() {
    try {
        sessionStorage.setItem("moment:lastRoom", JSON.stringify({
            roomCode: config.roomId,
            duration: config.totalDuration,
            participants: `${byId("participantsList")?.children.length ?? 0} people`,
            messages: byId("messageCount")?.textContent ?? "0",
        }));
    } catch {
        // Storage can be unavailable. The summary is a nicety, not a requirement.
    }
}

function applyMediaState(participantId, { isVideoOn, isMuted }) {
    if (isMuted === true) state.mutedParticipantIds.add(participantId);
    if (isMuted === false) state.mutedParticipantIds.delete(participantId);
    if (isVideoOn === true) state.videoParticipantIds.add(participantId);
    if (isVideoOn === false) state.videoParticipantIds.delete(participantId);
    updateMediaIndicators(participantId);
    renderSpeaker();
}

/* ------------------------------------------------------------------ hub handlers */

function registerHandlers() {
    setPeerFailedHandler((peerId, hasRelay) => {
        const name = state.participantNames.get(peerId) || "A participant";
        showNotification(hasRelay
            ? `${name} could not be reached. Retrying…`
            : `${name} could not be reached — your networks cannot connect directly, and no relay is configured.`);
    });

    on("ReceiveMessage", addMessage);
    on("UserTyping", showTyping);
    on("UserStoppedTyping", hideTyping);
    on("Error", showNotification);

    // Deliberately no peer teardown here. Media flows directly between browsers and survives
    // a signalling outage, so a reconnect should be invisible to an ongoing call.
    connection.onreconnecting(() => setReconnecting(true));

    connection.onreconnected(async () => {
        try {
            await hub.joinRoom();

            if (state.isInVoice) {
                const roster = await hub.rejoinCall(state.isCameraOn, state.isMuted);
                await reconcilePeers(roster);
                (roster || []).forEach((peer) => {
                    state.voiceParticipantIds.add(peer.id);
                    applyMediaState(peer.id, { isVideoOn: peer.isVideoOn, isMuted: peer.isMuted });
                });
                refreshCallUi();
            }
        } catch (err) {
            console.error("Reconnect recovery failed:", err);
            showNotification("Reconnected, but the call could not be restored.");
        } finally {
            setReconnecting(false);
        }
    });

    connection.onclose(() => {
        setReconnecting(false);
        showNotification("Connection lost. Reload the page to rejoin.");
    });

    on("UserJoined", (participant) => {
        addParticipant(participant);
        updateParticipantCount();
        showNotification(`${participant.displayName} joined the room`);
    });

    on("UserLeft", (participantId, displayName) => {
        removeParticipant(participantId);
        updateParticipantCount();
        showNotification(`${displayName} left the room`);
    });

    on("ParticipantStatusChanged", updateParticipantStatus);
    on("TimerUpdate", updateTimer);

    on("MediaStateChanged", (participantId, mediaState) => {
        applyMediaState(participantId, {
            isVideoOn: mediaState.isVideoOn,
            isMuted: mediaState.isMuted,
        });
        updateStage();
    });

    on("SpeakingChanged", markSpeaking);

    on("ExpiryWarning", (minutes) => {
        showNotification(`This room ends in ${minutes} minute${minutes > 1 ? "s" : ""}.`);
    });

    on("RoomClosed", () => {
        rememberSummary();
        cleanupCall();
        window.location.href = config.closedUrl;
    });

    on("VoteStarted", (initiator) => showNotification(`${initiator} started a vote to end the room`));
    on("VoteUpdated", updateVotePanel);
    on("VotePassed", () => showNotification("Vote passed. The room ends in five minutes."));

    on("VoiceJoined", async (payload) => {
        state.isInVoice = true;
        state.isVideoCall = payload.isVideo === true;

        state.voiceParticipantIds.clear();
        state.voiceParticipantIds.add(selfId);
        (payload.participants || []).forEach((p) => state.voiceParticipantIds.add(p.id));

        state.videoParticipantIds.clear();
        if (state.isVideoCall) state.videoParticipantIds.add(selfId);
        (payload.videoParticipants || []).forEach((p) => state.videoParticipantIds.add(p.id));

        // Announce our starting mute state so late joiners are not shown as unmuted.
        hub.setMuted(state.isMuted).catch(() => {});

        refreshCallUi();
        renderSpeaker();
        updateAllMediaIndicators();
        setMode(state.isVideoCall ? "video" : "voice");

        // Opening the connection creates the transceivers, which fires negotiationneeded and
        // sends the offer. There is no separate "make an offer" step.
        for (const peer of payload.participants || []) {
            connectTo(peer.id);
            ensureTile(peer.id);
        }
        setTimeout(syncMeters, 1500);
    });

    on("VoiceParticipantJoined", (participant) => {
        state.voiceParticipantIds.add(participant.id);
        ensureTile(participant.id);
        updateVoiceUi(callStatusText());
        updateMediaIndicators(participant.id);
        renderSpeaker();
        setTimeout(syncMeters, 1500);
    });

    on("VideoParticipantJoined", (participant) => {
        if (participant.displayName) {
            state.participantNames.set(participant.id, participant.displayName);
        }
        state.videoParticipantIds.add(participant.id);
        updateMediaIndicators(participant.id);
        updateStage();
    });

    on("VoiceParticipantLeft", (participantId, displayName) => {
        state.voiceParticipantIds.delete(participantId);
        state.mutedParticipantIds.delete(participantId);
        state.speakingParticipantIds.delete(participantId);
        if (state.activeSpeakerId === participantId) state.activeSpeakerId = null;
        if (state.stageParticipantId === participantId) state.stageParticipantId = null;

        closePeer(participantId);
        removeRemoteVideo(participantId);
        meters.get(participantId)?.stop();
        meters.delete(participantId);

        updateVoiceUi(callStatusText());
        updateMediaIndicators(participantId);
        renderSpeaker();
        updateStage();

        if (displayName && participantId !== selfId) {
            showNotification(`${displayName} left the call`);
        }
    });

    on("VideoParticipantLeft", (participantId) => {
        state.videoParticipantIds.delete(participantId);
        updateMediaIndicators(participantId);
        updateStage();
    });

    on("VoiceSignal", async (signal) => {
        if (!state.isInVoice) return;
        await handleSignal(signal);
        syncMeters();
    });

    on("VoiceError", (message) => {
        showNotification(message || "Call error");
        cleanupCall();
    });

    on("RoomState", (roomState) => {
        (roomState.messages || []).forEach(addMessage);
        scrollToBottom();

        (roomState.participants || []).forEach((p) => {
            state.participantNames.set(p.id, p.displayName);
            state.participantColors.set(p.id, p.colorHex);
            if (p.isMuted) state.mutedParticipantIds.add(p.id);
            else state.mutedParticipantIds.delete(p.id);
            addParticipant(p);
        });

        if (roomState.voiceParticipants) {
            state.voiceParticipantIds.clear();
            roomState.voiceParticipants.forEach((p) => state.voiceParticipantIds.add(p.id));
        }

        if (roomState.videoParticipants) {
            state.videoParticipantIds.clear();
            roomState.videoParticipants.forEach((p) => state.videoParticipantIds.add(p.id));
        }

        updateParticipantCount();
        updateVotePanel(roomState.voteStatus);
        refreshCallUi();
        renderSpeaker();
        updateStage();
        updateAllMediaIndicators();
    });
}

/* ------------------------------------------------------------------------ wiring */

function bindControls() {
    byId("startCallBtn")?.addEventListener("click", () =>
        joinCall({ video: currentMode() === "video" }));
    byId("joinVoiceBtn")?.addEventListener("click", () => joinCall({ video: false }));
    byId("joinVideoBtn")?.addEventListener("click", () => joinCall({ video: true }));
    byId("leaveVoiceBtn")?.addEventListener("click", leaveCall);
    byId("muteVoiceBtn")?.addEventListener("click", toggleMute);
    byId("cameraToggleBtn")?.addEventListener("click", toggleCamera);
    byId("shareScreenBtn")?.addEventListener("click", toggleScreenShare);
    byId("voteBtn")?.addEventListener("click", initiateVote);
    byId("leaveRoomBtn")?.addEventListener("click", leaveRoom);
    byId("leaveRoomSheetBtn")?.addEventListener("click", leaveRoom);
    byId("shareBtn")?.addEventListener("click", shareRoom);
}

async function boot() {
    initChatView();
    initTimer();
    initLayout();
    bindControls();
    registerHandlers();

    refreshCallUi();
    renderSpeaker();
    updateStage();

    try {
        await start();
    } catch (err) {
        console.error("Error connecting:", err);
        showNotification("Connection error. Please refresh the page.");
    }

    setInterval(() => {
        if (isConnected()) hub.updateActivity().catch(() => {});
    }, HEARTBEAT_MS);
}

boot();

// A small read-only surface for debugging a live call and for the browser test harness.
// It exposes state only; it cannot drive the call.
window.__moment = {
    selfId,
    connected: isConnected,
    forceReconnect: () => connection.stop().then(() => connection.start()),
    peerStates,
    stats,
    mode: currentMode,
    callState: () => ({
        inVoice: state.isInVoice,
        videoCall: state.isVideoCall,
        muted: state.isMuted,
        cameraOn: state.isCameraOn,
        sharing: state.isScreenSharing,
        activeSpeaker: state.activeSpeakerId,
        speaking: [...state.speakingParticipantIds],
        voicePeers: [...state.voiceParticipantIds],
        videoPeers: [...state.videoParticipantIds],
    }),
};
