import { byId, escapeHtml, formatTime } from "moment/dom";
import { state, selfId, selfName } from "moment/state";
import { hub } from "moment/hub";
import { showNotification } from "moment/ui/toast";

const SCROLL_SLACK_PX = 100;

const nearBottom = (el) =>
    el.scrollHeight - el.scrollTop - el.clientHeight < SCROLL_SLACK_PX;

export function addMessage(message) {
    const area = byId("messagesArea");
    const wasAtBottom = nearBottom(area);
    const row = document.createElement("div");

    if (message.type === 1) {
        row.className = "message message-system";
        row.innerHTML = `<div class="message-content">${escapeHtml(message.content)}</div>`;
    } else {
        const isOwn = message.senderId === selfId;
        row.className = `message ${isOwn ? "message-user" : "message-other"}`;

        // message.content is deliberately raw: the server HTML-encodes it and then turns URLs
        // into anchors, so it arrives as safe markup. Everything else is set as text or via
        // style.setProperty rather than interpolated — a colour interpolated into a style
        // attribute can close it and inject a handler into every other participant's page.
        row.innerHTML = `
            ${isOwn ? "" : `<div class="message-header">
                <div class="message-color"></div>
                <div class="message-name"></div>
            </div>`}
            <div class="message-content">${message.content}</div>
            <div class="message-time">${formatTime(message.timestamp)}</div>
        `;

        if (!isOwn) {
            const swatch = row.querySelector(".message-color");
            const name = row.querySelector(".message-name");
            swatch.style.setProperty("background-color", message.senderColor);
            name.style.setProperty("color", message.senderColor);
            name.textContent = message.senderName;
        }
    }

    area.appendChild(row);

    // Don't yank the view away from someone reading back through the history.
    if (wasAtBottom || message.senderId === selfId) {
        scrollToBottom();
    }
}

export function scrollToBottom() {
    const area = byId("messagesArea");
    area.scrollTop = area.scrollHeight;
}

export function showTyping(userName) {
    if (userName !== selfName) {
        byId("typingIndicator").textContent = `${userName} is typing...`;
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
    byId("messageForm").addEventListener("submit", submit);
    byId("scrollToBottomBtn").addEventListener("click", scrollToBottom);

    const area = byId("messagesArea");
    const scrollBtn = byId("scrollToBottomBtn");
    area.addEventListener("scroll", () => {
        scrollBtn.classList.toggle("show", !nearBottom(area));
    });

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
