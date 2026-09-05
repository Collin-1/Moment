import { byId } from "moment/dom";

/** Renders the countdown, escalating its styling as the room nears expiry. */
export function updateTimer(secondsRemaining) {
    const display = byId("timerDisplay");
    const hours = Math.floor(secondsRemaining / 3600);
    const minutes = Math.floor((secondsRemaining % 3600) / 60);
    const seconds = secondsRemaining % 60;

    display.textContent = hours >= 1 ? `${hours}h ${minutes}m` : `${minutes}m ${seconds}s`;

    display.className = "timer-display";
    if (secondsRemaining <= 300) {
        display.className += " timer-critical";
    } else if (secondsRemaining <= 600) {
        display.className += " timer-warning";
    }
}
