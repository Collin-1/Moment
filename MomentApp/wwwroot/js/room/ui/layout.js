import { byId } from "moment/dom";
import { state } from "moment/state";
import { remountMessages, scrollToBottom } from "moment/ui/chat";
import { updateStage } from "moment/ui/video-stage";

/**
 * Surface switching, bottom sheets and the panel tabs.
 *
 * Chat, voice and video are one page with three layouts, switched by `data-mode` on <body>.
 * That matters more than it looks: moving from chat to a call must not rebuild the page,
 * because the peer connections and the hub connection live in it.
 */

const MODES = ["chat", "voice", "video"];

export function currentMode() {
    return document.body.dataset.mode || "chat";
}

export function setMode(mode) {
    if (!MODES.includes(mode) || mode === currentMode()) return;

    document.body.dataset.mode = mode;

    // The message list lives in the stage for chat and in the side panel for video, so the
    // rendered history moves across rather than being re-rendered into a second copy.
    remountMessages();
    updateStage();
    scrollToBottom();
}

/* ------------------------------------------------------------------- sheets */

let openSheet = null;

function closeSheet() {
    if (!openSheet) return;
    openSheet.removeAttribute("data-open");
    byId("sheetScrim")?.removeAttribute("data-open");
    byId("rosterToggle")?.setAttribute("aria-expanded", "false");
    openSheet = null;
}

function toggleSheet(el, toggleButton) {
    if (openSheet === el) {
        closeSheet();
        return;
    }
    closeSheet();
    el.setAttribute("data-open", "");
    byId("sheetScrim")?.setAttribute("data-open", "");
    toggleButton?.setAttribute("aria-expanded", "true");
    openSheet = el;
}

/* -------------------------------------------------------------- panel tabs */

function selectTab(which) {
    const tabs = { messages: byId("tabMessages"), participants: byId("tabParticipants") };
    const panes = { messages: byId("paneMessages"), participants: byId("paneParticipants") };

    for (const key of Object.keys(tabs)) {
        tabs[key]?.setAttribute("aria-selected", String(key === which));
        panes[key]?.classList.toggle("active", key === which);
    }

    if (which === "messages") scrollToBottom();
}

/* ------------------------------------------------------------------- wiring */

export function initLayout({ onModeChange } = {}) {
    byId("rosterToggle")?.addEventListener("click", (event) => {
        // On a phone the roster is a bottom sheet; on a desktop it is always on screen and
        // the button is hidden, so this only ever fires in the small layout.
        const panel = currentMode() === "video" ? byId("roomPanel") : byId("sidebar");
        if (panel) toggleSheet(panel, event.currentTarget);
    });

    byId("sheetScrim")?.addEventListener("click", closeSheet);

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") closeSheet();
    });

    byId("tabMessages")?.addEventListener("click", () => selectTab("messages"));
    byId("tabParticipants")?.addEventListener("click", () => selectTab("participants"));

    // The chat button swaps between the call surface and the message list. In video mode the
    // messages are already beside the call, so it opens the panel instead.
    byId("chatToggleBtn")?.addEventListener("click", () => {
        if (currentMode() === "video") {
            selectTab("messages");
            if (window.matchMedia("(max-width: 900px)").matches) {
                toggleSheet(byId("roomPanel"), null);
            }
            return;
        }

        const next = currentMode() === "chat"
            ? (state.isVideoCall ? "video" : "voice")
            : "chat";
        setMode(next);
        onModeChange?.(next);
    });

    // Leaving the small layout must not strand an open sheet off-screen.
    window.matchMedia("(min-width: 901px)").addEventListener("change", closeSheet);
}
