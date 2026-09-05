/** Small DOM helpers shared across the room UI. */

/** Escapes text for safe interpolation into an innerHTML template. */
export function escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
}

/** Formats a timestamp as a short local time, e.g. "14:05". */
export function formatTime(timestamp) {
    return new Date(timestamp).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

export const byId = (id) => document.getElementById(id);
