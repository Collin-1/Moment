/**
 * Voice activity detection over WebAudio.
 *
 * Two separate signals come out of this, and the split matters:
 *
 *  - **Is this person speaking** is detected locally and broadcast over the hub. Local
 *    analysis sees the signal before encoding, so it is accurate regardless of packet loss;
 *    it respects mute by construction, since the detector is disabled while muted; and every
 *    client ends up agreeing on who the active speaker is. If each client analysed remote
 *    streams instead, the centred stage would show different people to different people.
 *  - **How loud, right now** is measured per client from each remote stream, because a
 *    waveform wants ~30 updates a second and broadcasting a float that often, per
 *    participant, through a free-tier instance is real traffic for something that should be
 *    frame-accurate against the audio actually being heard.
 */

let context = null;

/**
 * One shared AudioContext, created lazily.
 *
 * It starts suspended under the browser's autoplay policy and can only be resumed from a user
 * gesture — which is why this is called from the "join the call" tap rather than at load.
 */
export function audioContext() {
    if (!context) {
        context = new (window.AudioContext || window.webkitAudioContext)();
    }
    if (context.state === "suspended") {
        context.resume().catch(() => {});
    }
    return context;
}

export class SpeakingDetector {
    /**
     * @param {MediaStream} stream
     * @param {{
     *   onSpeakingChange?: (speaking: boolean) => void,
     *   onThresholdDb?: number,
     *   offThresholdDb?: number,
     *   attackFrames?: number,
     *   releaseMs?: number,
     * }} options
     */
    constructor(stream, options = {}) {
        this.onSpeakingChange = options.onSpeakingChange;
        this.onThresholdDb = options.onThresholdDb ?? -50;
        this.offThresholdDb = options.offThresholdDb ?? -55;
        this.attackFrames = options.attackFrames ?? 2;
        this.releaseMs = options.releaseMs ?? 600;

        this.enabled = true;
        this.speaking = false;
        this.level = 0;

        this._envelope = -100;
        this._aboveFrames = 0;
        this._belowSince = null;

        const ctx = audioContext();
        this._analyser = ctx.createAnalyser();
        this._analyser.fftSize = 512;
        this._analyser.smoothingTimeConstant = 0.6;
        this._buffer = new Uint8Array(this._analyser.fftSize);

        this._source = ctx.createMediaStreamSource(stream);
        // Deliberately not connected to ctx.destination: an analyser works as a terminal
        // node, and connecting it would play your own microphone back at you.
        this._source.connect(this._analyser);

        // The boolean runs on an interval, not requestAnimationFrame. rAF stops entirely in a
        // background tab, which would freeze the speaking flag as `true` for anyone who
        // backgrounded the page mid-word.
        this._timer = setInterval(() => this._tick(), 100);
    }

    _tick() {
        if (!this.enabled) return;

        this._analyser.getByteTimeDomainData(this._buffer);

        // RMS over the time domain. getByteFrequencyData is the tempting alternative and the
        // wrong one: it is already log-scaled and bin-weighted, so a fan or a hum in one bin
        // reads as speech.
        let sum = 0;
        for (let i = 0; i < this._buffer.length; i++) {
            const sample = (this._buffer[i] - 128) / 128;
            sum += sample * sample;
        }
        const rms = Math.sqrt(sum / this._buffer.length);
        const db = 20 * Math.log10(rms + 1e-6);

        // Asymmetric smoothing: quick to notice speech, slow to let go of it.
        const k = db > this._envelope ? 0.5 : 0.85;
        this._envelope = this._envelope * k + db * (1 - k);

        this.level = Math.min(1, Math.max(0, (this._envelope + 60) / 45));

        this._evaluate();
    }

    _evaluate() {
        const now = Date.now();

        if (this._envelope > this.onThresholdDb) {
            this._belowSince = null;
            this._aboveFrames += 1;
            // A couple of consecutive frames, so a keyboard click or a door does not register.
            if (!this.speaking && this._aboveFrames >= this.attackFrames) {
                this._set(true);
            }
            return;
        }

        this._aboveFrames = 0;

        if (this._envelope < this.offThresholdDb) {
            // The release window bridges the gaps between words, which is what stops the
            // "speaking now" label flickering mid-sentence.
            this._belowSince ??= now;
            if (this.speaking && now - this._belowSince >= this.releaseMs) {
                this._set(false);
            }
        }
    }

    _set(speaking) {
        if (this.speaking === speaking) return;
        this.speaking = speaking;
        this.onSpeakingChange?.(speaking);
    }

    /** Muting disables detection outright, so a muted person can never render as speaking. */
    setEnabled(enabled) {
        this.enabled = enabled;
        if (!enabled) {
            this.level = 0;
            this._envelope = -100;
            this._aboveFrames = 0;
            this._set(false);
        }
    }

    stop() {
        clearInterval(this._timer);
        try { this._source.disconnect(); } catch { /* already torn down */ }
        this._set(false);
    }
}

/**
 * Measures a remote stream's level for its waveform.
 *
 * Note the media element requirement: in Chrome a MediaStreamAudioSourceNode built from a
 * *remote* stream produces silence unless that stream is also attached to a playing media
 * element. The per-peer <audio> element the call already creates satisfies this; without it
 * every remote waveform sits flat and it looks like a threshold bug.
 */
export class LevelMeter {
    constructor(stream) {
        const ctx = audioContext();
        this._analyser = ctx.createAnalyser();
        this._analyser.fftSize = 256;
        this._analyser.smoothingTimeConstant = 0.7;
        this._buffer = new Uint8Array(this._analyser.fftSize);
        this._source = ctx.createMediaStreamSource(stream);
        this._source.connect(this._analyser);
        this.level = 0;
    }

    sample() {
        this._analyser.getByteTimeDomainData(this._buffer);
        let sum = 0;
        for (let i = 0; i < this._buffer.length; i++) {
            const sample = (this._buffer[i] - 128) / 128;
            sum += sample * sample;
        }
        const db = 20 * Math.log10(Math.sqrt(sum / this._buffer.length) + 1e-6);
        this.level = Math.min(1, Math.max(0, (db + 60) / 45));
        return this.level;
    }

    stop() {
        try { this._source.disconnect(); } catch { /* already torn down */ }
    }
}
