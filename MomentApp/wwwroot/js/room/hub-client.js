import { config } from "moment/config";

/**
 * The single SignalR connection for the room: presence, chat, voting and WebRTC signalling.
 *
 * There used to be two connections (one per hub), which raced: a chat message could arrive
 * before the room state it belonged after, and system messages sent by the room hub were
 * never displayed because the client only listened for them on the chat connection.
 */
export const connection = new signalR.HubConnectionBuilder()
    .withUrl(config.hubUrl)
    .withAutomaticReconnect()
    .build();

export const on = (event, handler) => connection.on(event, handler);

const invoke = (method, ...args) => connection.invoke(method, config.roomId, ...args);

export const hub = {
    joinRoom: () => invoke("JoinRoom"),
    leaveRoom: () => invoke("LeaveRoom"),
    updateActivity: () => invoke("UpdateActivity"),

    sendMessage: (content) => invoke("SendMessage", content),
    startTyping: () => invoke("StartTyping"),
    stopTyping: () => invoke("StopTyping"),

    initiateVote: () => invoke("InitiateVote"),
    castVote: (yes) => invoke("CastVote", yes),

    joinVoice: () => invoke("JoinVoice"),
    joinVideo: () => invoke("JoinVideo"),
    leaveVoice: () => invoke("LeaveVoice"),
    setVideoEnabled: (enabled) => invoke("SetVideoEnabled", enabled),
    setMuted: (muted) => invoke("SetMuted", muted),
    setSpeaking: (speaking) => invoke("SetSpeaking", speaking),
    rejoinCall: (isVideo, isMuted) => invoke("RejoinCall", isVideo, isMuted),
    sendSignal: (toParticipantId, type, payload) =>
        invoke("SendVoiceSignal", toParticipantId, type, JSON.stringify(payload)),
};

export const isConnected = () =>
    connection.state === signalR.HubConnectionState.Connected;

export async function start() {
    await connection.start();
    await hub.joinRoom();
}
