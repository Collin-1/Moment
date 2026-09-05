import { state } from "moment/state";
import { attachLocalVideo, removeLocalVideo } from "moment/ui/video-stage";

/**
 * Owns the local microphone, camera and screen-share tracks.
 *
 * Kept separate from the peer layer so "what hardware are we using" and "how do we negotiate
 * with peers" can change independently. This module never touches an RTCPeerConnection; it
 * hands tracks to the caller, which forwards them to the mesh.
 */
export const media = {
    /** @type {MediaStream | null} The local capture stream. */
    stream: null,

    /** Camera track parked while a screen share is in progress. */
    _parkedCamera: null,

    /** @type {MediaStream | null} */
    _displayStream: null,

    supported() {
        return Boolean(navigator.mediaDevices?.getUserMedia);
    },

    canShareScreen() {
        return Boolean(navigator.mediaDevices?.getDisplayMedia);
    },

    hasVideo() {
        return Boolean(this.stream?.getVideoTracks().some((t) => t.readyState === "live"));
    },

    videoTrack() {
        return this.stream?.getVideoTracks()[0] ?? null;
    },

    async acquire({ video }) {
        this.stream = await navigator.mediaDevices.getUserMedia({ audio: true, video });
        state.isMuted = false;
        state.isCameraOn = video;
        if (video) attachLocalVideo(this.stream);
        return this.stream;
    },

    /**
     * Mutes by disabling the track rather than stopping it. Stopping would drop the sender and
     * force a renegotiation with every peer just to toggle a microphone.
     */
    toggleMute() {
        if (!this.stream) return state.isMuted;
        state.isMuted = !state.isMuted;
        this.stream.getAudioTracks().forEach((t) => (t.enabled = !state.isMuted));
        return state.isMuted;
    },

    /** Returns a live camera track, acquiring one if the call started audio-only. */
    async startCamera() {
        if (!this.stream) {
            this.stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
        }

        let track = this.stream.getVideoTracks().find((t) => t.readyState === "live");
        if (!track) {
            const cameraStream = await navigator.mediaDevices.getUserMedia({ video: true });
            track = cameraStream.getVideoTracks()[0];
            if (track) this.stream.addTrack(track);
        }

        if (track) {
            track.enabled = true;
            track.contentHint = "motion";
        }

        state.isCameraOn = true;
        attachLocalVideo(this.stream);
        return track ?? null;
    },

    stopCamera() {
        this.stream?.getVideoTracks().forEach((track) => {
            track.stop();
            this.stream.removeTrack(track);
        });
        removeLocalVideo();
        state.isCameraOn = false;
    },

    /**
     * Starts a screen share and returns the track to hand to the mesh.
     *
     * The camera track is parked rather than stopped so that ending the share can restore it
     * without a second permission prompt.
     */
    async startScreenShare({ onEnded } = {}) {
        this._displayStream = await navigator.mediaDevices.getDisplayMedia({
            video: { frameRate: { ideal: 15, max: 30 } },
            audio: false,
        });

        const track = this._displayStream.getVideoTracks()[0];
        // Tells the encoder to favour sharpness over frame rate — text has to stay readable.
        track.contentHint = "detail";

        // Fires when the viewer uses the browser's own "Stop sharing" chrome, which is the
        // control most people actually reach for.
        track.addEventListener("ended", () => onEnded?.());

        this._parkedCamera = this.videoTrack();
        state.isScreenSharing = true;
        attachLocalVideo(this._displayStream);
        return track;
    },

    /** Ends the share and returns the camera track to restore, if there was one. */
    stopScreenShare() {
        this._displayStream?.getTracks().forEach((t) => t.stop());
        this._displayStream = null;
        state.isScreenSharing = false;

        const camera = this._parkedCamera?.readyState === "live" ? this._parkedCamera : null;
        this._parkedCamera = null;

        if (camera && this.stream) {
            attachLocalVideo(this.stream);
        } else {
            removeLocalVideo();
        }
        return camera;
    },

    release() {
        this.stream?.getTracks().forEach((t) => t.stop());
        this._displayStream?.getTracks().forEach((t) => t.stop());
        this.stream = null;
        this._displayStream = null;
        this._parkedCamera = null;
        removeLocalVideo();
        state.isMuted = false;
        state.isCameraOn = false;
        state.isScreenSharing = false;
    },
};
