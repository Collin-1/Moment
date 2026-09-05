import { config } from "moment/config";
import { byId } from "moment/dom";
import { state, selfId } from "moment/state";
import { connection, hub, on, start, isConnected } from "moment/hub";
import { media } from "moment/rtc/media";
import { getIceConfig } from "moment/rtc/ice";
import {
    connectTo, handleSignal, close as closePeer, closeAll,
    replaceVideoTrack, reconcilePeers, peerStates, stats, setPeerFailedHandler,
} from "moment/rtc/peers";
import { showNotification, setReconnecting } from "moment/ui/toast";
import { updateTimer } from "moment/ui/timer";
import { addMessage, showTyping, hideTyping, scrollToBottom, initChatView } from "moment/ui/chat";
import {
    addParticipant, removeParticipant, updateParticipantCount,
    updateParticipantStatus, updateMediaIndicators, updateAllMediaIndicators,
} from "moment/ui/participants";
import { updateVotePanel, initiateVote } from "moment/ui/vote";
import { refreshCallUi, updateVoiceUi, updateMuteUi, updateCameraUi, callStatusText } from "moment/ui/call-controls";
import { removeRemoteVideo, updateVideoStage } from "moment/ui/video-stage";

const HEARTBEAT_MS = 30000;

/* ------------------------------------------------------------------ call actions */

async function joinCall({ video }) {
    if (state.isInVoice) return;

    if (!media.supported()) {
        showNotification((video ? "Video" : "Voice") + " calling is not supported in this browser.");
        return;
    }

    try {
        state.isVideoCall = video;
        await media.acquire({ video });

        // Fetched before joining so peer creation can read it synchronously — two signals for
        // the same peer arriving together must not race to build two connections.
        await getIceConfig();

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
    state.voiceParticipantIds.delete(selfId);
    state.videoParticipantIds.clear();

    closeAll();
    media.release();

    refreshCallUi();
    updateVideoStage();
    updateAllMediaIndicators();
}

function toggleMute() {
    const muted = media.toggleMute();
    updateMuteUi();

    // Other participants cannot reliably detect this for themselves — a receiver's view of a
    // muted track is inconsistent across browsers and lags by seconds — so it is broadcast.
    applyMediaState(selfId, { isMuted: muted });
    hub.setMuted(muted).catch((err) => console.error("setMuted", err));
}

/** Applies a participant's media state to the roster and its indicators. */
function applyMediaState(participantId, { isVideoOn, isMuted }) {
    if (isMuted === true) state.mutedParticipantIds.add(participantId);
    if (isMuted === false) state.mutedParticipantIds.delete(participantId);
    if (isVideoOn === true) state.videoParticipantIds.add(participantId);
    if (isVideoOn === false) state.videoParticipantIds.delete(participantId);
    updateMediaIndicators(participantId);
}

async function toggleCamera() {
    if (!state.isInVoice) return;
    try {
        if (state.isCameraOn) {
            media.stopCamera();
            await replaceVideoTrack(null);
            await hub.setVideoEnabled(false);
        } else {
            const track = await media.startCamera();
            state.isVideoCall = true;
            await replaceVideoTrack(track);
            await hub.setVideoEnabled(true);
        }
        updateCameraUi();
        updateVideoStage();
    } catch (err) {
        console.error("Camera toggle error:", err);
        showNotification("Failed to toggle camera.");
    }
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
        navigator.share({ title: "Join my Moment chat", text: "Join my chat room!", url });
    } else {
        navigator.clipboard.writeText(url);
        showNotification("Link copied to clipboard!");
    }
}

/* ------------------------------------------------------------------ hub handlers */

function registerHandlers() {
    // A failed connection with no relay configured is the classic symptom of a restrictive
    // NAT. Saying so beats a call that silently never starts.
    setPeerFailedHandler((peerId, hasRelay) => {
        const name = state.participantNames.get(peerId) || "A participant";
        showNotification(hasRelay
            ? name + " could not be reached. Retrying..."
            : name + " could not be reached — your networks cannot connect directly. A TURN relay is not configured.");
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
        showNotification(participant.displayName + " joined the room");
    });

    on("UserLeft", (participantId, displayName) => {
        removeParticipant(participantId);
        updateParticipantCount();
        showNotification(displayName + " left the room");
    });

    on("ParticipantStatusChanged", updateParticipantStatus);

    on("MediaStateChanged", (participantId, mediaState) => {
        applyMediaState(participantId, {
            isVideoOn: mediaState.isVideoOn,
            isMuted: mediaState.isMuted,
        });
        updateVideoStage();
    });
    on("TimerUpdate", updateTimer);

    on("ExpiryWarning", (minutes) => {
        showNotification("This room will expire in " + minutes + " minute" + (minutes > 1 ? "s" : "") + ".");
    });

    on("RoomClosed", () => {
        cleanupCall();
        window.location.href = config.closedUrl;
    });

    on("VoteStarted", (initiator) => showNotification(initiator + " started a vote to close the room"));
    on("VoteUpdated", updateVotePanel);
    on("VotePassed", () => showNotification("Vote passed. The room will close in 5 minutes."));

    on("VoiceJoined", async (payload) => {
        state.isInVoice = true;
        state.isVideoCall = payload.isVideo === true;

        state.voiceParticipantIds.clear();
        state.voiceParticipantIds.add(selfId);
        (payload.participants || []).forEach((p) => state.voiceParticipantIds.add(p.id));

        state.videoParticipantIds.clear();
        if (state.isVideoCall) state.videoParticipantIds.add(selfId);
        (payload.videoParticipants || []).forEach((p) => state.videoParticipantIds.add(p.id));

        // Announce our own starting mute state so late joiners are not shown as unmuted.
        hub.setMuted(state.isMuted).catch(() => {});

        refreshCallUi();
        updateAllMediaIndicators();

        // Opening the connection creates the transceivers, which fires negotiationneeded and
        // sends the offer. There is no separate "make an offer" step.
        for (const peer of payload.participants || []) {
            connectTo(peer.id);
        }
    });

    on("VoiceParticipantJoined", (participant) => {
        state.voiceParticipantIds.add(participant.id);
        updateVoiceUi(callStatusText());
        updateMediaIndicators(participant.id);
    });

    on("VideoParticipantJoined", (participant) => {
        if (participant.displayName) {
            state.participantNames.set(participant.id, participant.displayName);
        }
        state.videoParticipantIds.add(participant.id);
        updateVideoStage();
        updateMediaIndicators(participant.id);
        if (participant.id !== selfId && participant.displayName) {
            showNotification(participant.displayName + " started video");
        }
    });

    on("VoiceParticipantLeft", (participantId, displayName) => {
        state.voiceParticipantIds.delete(participantId);
        state.mutedParticipantIds.delete(participantId);
        closePeer(participantId);
        updateVoiceUi(callStatusText());
        updateMediaIndicators(participantId);
        if (displayName && participantId !== selfId) {
            showNotification(displayName + " left the call");
        }
    });

    on("VideoParticipantLeft", (participantId, displayName) => {
        state.videoParticipantIds.delete(participantId);
        removeRemoteVideo(participantId);
        updateVideoStage();
        updateMediaIndicators(participantId);
        if (displayName && participantId !== selfId) {
            showNotification(displayName + " stopped video");
        }
    });

    on("VoiceSignal", async (signal) => {
        if (!state.isInVoice) return;
        await handleSignal(signal);
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
            if (p.isMuted) state.mutedParticipantIds.add(p.id);
            else state.mutedParticipantIds.delete(p.id);
        });

        if (roomState.voiceParticipants) {
            state.voiceParticipantIds.clear();
            roomState.voiceParticipants.forEach((p) => state.voiceParticipantIds.add(p.id));
        }

        if (roomState.videoParticipants) {
            state.videoParticipantIds.clear();
            roomState.videoParticipants.forEach((p) => state.videoParticipantIds.add(p.id));
        }

        updateVoiceUi(callStatusText());
        updateVideoStage();
        updateAllMediaIndicators();
    });
}

/* ------------------------------------------------------------------ wiring */

function bindControls() {
    byId("joinVoiceBtn").addEventListener("click", () => joinCall({ video: false }));
    byId("joinVideoBtn").addEventListener("click", () => joinCall({ video: true }));
    byId("leaveVoiceBtn").addEventListener("click", leaveCall);
    byId("muteVoiceBtn").addEventListener("click", toggleMute);
    byId("cameraToggleBtn").addEventListener("click", toggleCamera);
    byId("voteBtn").addEventListener("click", initiateVote);
    byId("leaveRoomBtn").addEventListener("click", leaveRoom);
    byId("shareBtn").addEventListener("click", shareRoom);
}

async function boot() {
    initChatView();
    bindControls();
    registerHandlers();

    refreshCallUi();
    updateVideoStage();
    updateAllMediaIndicators();

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

// A small read-only surface for debugging a live call and for the browser test harness.
// Exposes state only; it cannot drive the call.
window.__moment = {
    selfId,
    connected: isConnected,
    // Drops the transport so the client exercises its own reconnect path. Used by the test
    // harness to simulate a network blip without touching the media connections.
    forceReconnect: () => connection.stop().then(() => connection.start()),
    peerStates,
    stats,
    callState: () => ({
        inVoice: state.isInVoice,
        videoCall: state.isVideoCall,
        muted: state.isMuted,
        cameraOn: state.isCameraOn,
        sharing: state.isScreenSharing,
        voicePeers: [...state.voiceParticipantIds],
        videoPeers: [...state.videoParticipantIds],
    }),
};

boot();
