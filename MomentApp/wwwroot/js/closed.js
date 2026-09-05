/**
 * Renders the summary of a room that has just ended.
 *
 * The figures come from sessionStorage, written by the room page as it closed. They are never
 * sent to the server: the point of this screen is that the room is gone, and asking the server
 * to describe a room it has deliberately forgotten would defeat that.
 *
 * A direct visit, a reload, or a new tab therefore shows no summary at all, which is correct.
 */
const KEY = "moment:lastRoom";

let summary = null;
try {
    const raw = sessionStorage.getItem(KEY);
    summary = raw ? JSON.parse(raw) : null;
    sessionStorage.removeItem(KEY); // shown once, then forgotten like everything else
} catch {
    // Storage can be unavailable (private mode, blocked site data). No summary is fine.
}

if (summary) {
    const panel = document.getElementById("roomSummary");
    for (const [field, value] of Object.entries(summary)) {
        const cell = panel?.querySelector(`[data-summary="${field}"]`);
        if (cell) cell.textContent = value;
    }
    if (panel) panel.hidden = false;
}
