import { byId } from "moment/dom";
import { config } from "moment/config";

/**
 * Two clocks: the countdown to the room's end, and the elapsed time since it opened.
 *
 * The server broadcasts the remaining seconds every ten seconds. This ticks locally in
 * between so the display moves every second, and each broadcast re-anchors it against drift.
 */

let remaining = null;
let started = Date.now();

const pad = (n) => String(n).padStart(2, "0");

function formatRemaining(seconds) {
    if (seconds <= 0) return "0s";
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    if (h >= 1) return `${h}h ${m}m`;
    if (m >= 1) return `${m}m ${pad(s)}s`;
    return `${s}s`;
}

function formatElapsed(ms) {
    const total = Math.max(0, Math.floor(ms / 1000));
    return `${pad(Math.floor(total / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
}

function paint() {
    const elapsed = byId("elapsed");
    if (elapsed) elapsed.textContent = formatElapsed(Date.now() - started);

    if (remaining === null) return;

    const text = formatRemaining(remaining);
    const countdown = byId("timerDisplay");
    const callCountdown = byId("callTimerDisplay");
    if (countdown) countdown.textContent = text;
    if (callCountdown) callCountdown.textContent = text;

    // Escalates as the room nears its end. The visible urgency is the point of the timer.
    const card = byId("timerCard");
    if (card) {
        card.classList.toggle("critical", remaining <= 300);
        card.classList.toggle("warning", remaining > 300 && remaining <= 600);
    }
}

/** Re-anchors the countdown from the server, correcting any local drift. */
export function updateTimer(secondsRemaining) {
    remaining = secondsRemaining;
    paint();
}

export function initTimer() {
    const expiresAt = Date.parse(config.expiresAt);
    if (!Number.isNaN(expiresAt)) {
        remaining = Math.max(0, Math.round((expiresAt - Date.now()) / 1000));
    }

    started = Date.now();
    paint();

    setInterval(() => {
        if (remaining !== null && remaining > 0) remaining -= 1;
        paint();
    }, 1000);
}
