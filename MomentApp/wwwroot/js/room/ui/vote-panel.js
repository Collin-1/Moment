import { byId } from "moment/dom";
import { hub } from "moment/hub";

export const initiateVote = () =>
    hub.initiateVote().catch((e) => console.error("initiateVote", e));

export const castVote = (yes) =>
    hub.castVote(yes).catch((e) => console.error("castVote", e));

const initial = (name) => (name || "?").trim().charAt(0).toUpperCase() || "?";

export function updateVotePanel(voteStatus) {
    const panel = byId("votePanel");
    const startButton = byId("voteBtn");
    if (!panel) return;

    // A null status means no vote is running — none was started, or one lapsed without
    // passing. Put the panel away and offer the button again, or the room is left looking
    // like a vote is still open.
    if (!voteStatus) {
        panel.hidden = true;
        panel.innerHTML = "";
        if (startButton) startButton.hidden = false;
        return;
    }

    const { yesVotes, totalParticipants, requiredVotes, hasPassed, participantVotes } = voteStatus;
    const progress = requiredVotes > 0 ? Math.min(100, (yesVotes / requiredVotes) * 100) : 0;

    panel.innerHTML = `
        <h3>Vote to end this room</h3>
        <div class="vote-avatars"></div>
        <div class="vote-progress"><i></i></div>
        <p class="vote-count"></p>
        ${hasPassed
            ? "<p>Vote passed. The room closes in five minutes.</p>"
            : `<div class="vote-actions">
                   <button type="button" data-vote="yes">Yes</button>
                   <button type="button" data-vote="no">No</button>
               </div>`}
    `;

    panel.querySelector(".vote-progress i").style.inlineSize = `${progress}%`;
    panel.querySelector(".vote-count").textContent =
        `${yesVotes} of ${totalParticipants} voted yes — ${requiredVotes} needed`;

    // Names come from other participants, so they are set as text, never interpolated.
    const avatars = panel.querySelector(".vote-avatars");
    for (const [name, vote] of Object.entries(participantVotes ?? {})) {
        const chip = document.createElement("span");
        chip.className = "avatar";
        chip.textContent = initial(name);
        chip.title = `${name}: ${vote === true ? "yes" : vote === false ? "no" : "not voted"}`;
        chip.style.opacity = vote === null || vote === undefined ? "0.4" : "1";
        avatars.appendChild(chip);
    }

    panel.querySelector('[data-vote="yes"]')?.addEventListener("click", () => castVote(true));
    panel.querySelector('[data-vote="no"]')?.addEventListener("click", () => castVote(false));

    panel.hidden = false;
    if (startButton) startButton.hidden = true;
}
