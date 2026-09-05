import { config } from "moment/config";

/**
 * Shared client state for the room.
 *
 * Deliberately a single mutable object rather than module-level `let`s scattered across the
 * UI modules: the call state is read by chat, the roster, the video tiles, the speaker stage
 * and the control bar, and having exactly one place it lives is what stops those views
 * disagreeing about who is in the call.
 */
export const state = {
    /** Participant ids currently in the call (audio). */
    voiceParticipantIds: new Set(),
    /** Participant ids currently sending video. */
    videoParticipantIds: new Set(),
    /** Participant ids whose microphone is muted. */
    mutedParticipantIds: new Set(),
    /** Participant ids currently detected as speaking. */
    speakingParticipantIds: new Set(),

    /** id -> display name, for labelling tiles and the speaker stage. */
    participantNames: new Map(),
    /** id -> colour, so avatars, tiles and bubbles are tinted consistently. */
    participantColors: new Map(),

    /** Who is speaking right now, for the call stage. */
    activeSpeakerId: null,
    /** Who holds the large video tile, when it is not simply the active speaker. */
    stageParticipantId: null,

    isInVoice: false,
    isVideoCall: false,
    isMuted: false,
    isCameraOn: false,
    isScreenSharing: false,
    isTyping: false,
};

export const selfId = config.participantId;
export const selfName = config.displayName;
export const selfColor = config.colorHex;

// Seed our own identity so tiles and avatars are tinted before RoomState arrives.
state.participantNames.set(selfId, selfName);
state.participantColors.set(selfId, selfColor);
