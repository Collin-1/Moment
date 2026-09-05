import { byId } from "moment/dom";

let hideTimer;

/** Shows a transient message. Set as text, never as markup. */
export function showNotification(message) {
    const notification = byId("notification");
    notification.textContent = message;
    notification.classList.add("show");
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => notification.classList.remove("show"), 5000);
}

export function setReconnecting(active) {
    byId("reconnectingBanner").classList.toggle("show", active);
}
