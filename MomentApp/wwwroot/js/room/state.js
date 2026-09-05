import { config } from "moment/config";

/**
 * Shared client state for the room.
 *
 * Deliberately a single mutable object rather than module-level `let`s scattered across the
 * UI modules: the call state is read by chat, participants, video and the control bar, and
 * having exactly one place it lives is what keeps those views from disagreeing.
 */
export const state = {
    /** Participant ids currently in the call (audio). */
    voiceParticipantIds: new Set(),
    /** Participant ids currently sending video. */
    videoParticipantIds: new Set(),
    /** Participant ids whose microphone is muted. */
    mutedParticipantIds: new Set(),
    /** id -> display name, for labelling video tiles. */
    participantNames: new Map(),

    isInVoice: false,
    isVideoCall: false,
    isMuted: false,
    isCameraOn: false,
    isScreenSharing: false,
    isTyping: false,
};

export const selfId = config.participantId;
export const selfName = config.displayName;
