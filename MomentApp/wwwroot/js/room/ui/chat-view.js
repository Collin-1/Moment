import { byId, escapeHtml, formatTime } from "moment/dom";
import { state, selfId, selfName } from "moment/state";
import { hub } from "moment/hub";
import { showNotification } from "moment/ui/toast";

/**
 * The message list and composer.
 *
 * The list is rendered into whichever mount is currently in use: the main stage in chat mode,
 * or the right-hand panel in video mode. Messages are moved rather than duplicated, so the
 * scroll position and the DOM stay singular.
 */

const SCROLL_SLACK_PX = 100;

let messageCount = 0;

/**
 * Ids already rendered.
 *
 * A joiner is sent the system message announcing their own arrival twice: once live, and once
 * inside the RoomState history, because the message is stored before the snapshot is taken.
 * Rather than reorder the server's send, the client simply refuses to draw a message twice.
 */
const rendered = new Set();

const nearBottom = (el) => el.scrollHeight - el.scrollTop - el.clientHeight < SCROLL_SLACK_PX;

/** The element messages are currently appended to. */
function activeList() {
    return document.body.dataset.mode === "video"
        ? byId("paneMessages")
        : byId("messagesArea");
}

export function addMessage(message) {
    const list = activeList();
    if (!list) return;
    if (message.id) {
        if (rendered.has(message.id)) return;
        rendered.add(message.id);
    }

    const wasAtBottom = nearBottom(list);
    const row = document.createElement("div");

    if (message.type === 1) {
        // System notices read as room events, so they are centred between rules rather than
        // styled as something a participant said.
        row.className = "message system";
        row.textContent = message.content;
    } else {
        const isOwn = message.senderId === selfId;
        row.className = `message ${isOwn ? "own" : ""}`.trim();

        // message.content is deliberately raw: the server HTML-encodes it and then turns URLs
        // into anchors, so it arrives as safe markup. Everything else is set as text or via
        // style.setProperty — a colour interpolated into a style attribute can close it and
        // inject a handler into every other participant's page.
        row.innerHTML = `
            <div class="message-row">
                ${isOwn ? "" : '<span class="avatar"></span>'}
                <div class="bubble-text">${message.content}</div>
            </div>
            <div class="message-meta"></div>
        `;

        if (!isOwn) {
            const avatar = row.querySelector(".avatar");
            avatar.textContent = (message.senderName || "?").charAt(0).toUpperCase();
            row.style.setProperty("--participant-color", message.senderColor);
            row.querySelector(".bubble-text").style.setProperty("color", message.senderColor);
        }

        row.querySelector(".message-meta").textContent =
            `${isOwn ? "You" : message.senderName} · ${formatTime(message.timestamp)}`;
    }

    list.appendChild(row);

    messageCount += 1;
    const counter = byId("messageCount");
    if (counter) counter.textContent = messageCount;

    // Don't yank the view away from somebody reading back through the history.
    if (wasAtBottom || message.senderId === selfId) scrollToBottom();
}

/** Moves the rendered history when the surface changes, rather than re-rendering it. */
export function remountMessages() {
    const target = activeList();
    const other = target?.id === "paneMessages" ? byId("messagesArea") : byId("paneMessages");
    if (!target || !other) return;

    while (other.firstChild) target.appendChild(other.firstChild);
    scrollToBottom();
}

/**
 * Moves the composer to wherever the messages currently are.
 *
 * Moved rather than duplicated, for the same reason the list is: two inputs would be two
 * values, two drafts and two places for focus to be, and whichever one you had typed into
 * would be the one that had just been hidden.
 */
export function remountComposer() {
    const mount = document.body.dataset.mode === "video"
        ? byId("panelComposer")
        : byId("dockComposer");
    const form = byId("messageForm");

    if (mount && form && form.parentElement !== mount) mount.appendChild(form);
}

/** Puts the cursor in the message box, wherever it currently lives. */
export function focusComposer() {
    byId("messageInput")?.focus();
}

export function scrollToBottom() {
    const list = activeList();
    if (list) list.scrollTop = list.scrollHeight;
}

export function showTyping(userName) {
    if (userName !== selfName) {
        byId("typingIndicator").textContent = `${userName} is typing…`;
    }
}

export function hideTyping() {
    byId("typingIndicator").textContent = "";
}

async function submit(event) {
    event.preventDefault();
    const input = byId("messageInput");
    const content = input.value.trim();
    if (!content) return;

    try {
        await hub.sendMessage(content);
        input.value = "";
        if (state.isTyping) {
            state.isTyping = false;
            await hub.stopTyping();
        }
    } catch (err) {
        console.error("Error sending message:", err);
        showNotification("Failed to send message");
    }
}

export function initChatView() {
    // A Video room opens straight into video mode, and setMode is a no-op for the mode you
    // are already in — so the first placement has to happen here rather than on a switch.
    remountComposer();

    byId("messageForm").addEventListener("submit", submit);

    let typingTimer;
    byId("messageInput").addEventListener("input", async () => {
        if (!state.isTyping) {
            state.isTyping = true;
            await hub.startTyping().catch(() => {});
        }
        clearTimeout(typingTimer);
        typingTimer = setTimeout(async () => {
            state.isTyping = false;
            await hub.stopTyping().catch(() => {});
        }, 1000);
    });
}

export { escapeHtml };
