import { byId, escapeHtml } from "moment/dom";
import { hub } from "moment/hub";

export const initiateVote = () => hub.initiateVote().catch((e) => console.error("initiateVote", e));
export const castVote = (yes) => hub.castVote(yes).catch((e) => console.error("castVote", e));

export function updateVotePanel(voteStatus) {
    const panel = byId("votePanel");

    // A null status means no vote is running — either none was started, or one lapsed without
    // passing. Put the panel away and offer the button again, otherwise the room is left
    // looking like a vote is still open.
    if (!voteStatus) {
        panel.style.display = "none";
        panel.innerHTML = "";
        byId("voteBtn").style.display = "";
        return;
    }

    const percentage = Math.round(voteStatus.yesPercentage);
    const rows = Object.entries(voteStatus.participantVotes)
        .map(([name, vote]) => {
            const icon = vote === true ? "&#9989;" : vote === false ? "&#10060;" : "&#9203;";
            return `${icon} ${escapeHtml(name)}<br>`;
        })
        .join("");

    panel.innerHTML = `
        <h3>&#128499;&#65039; Vote to Close Room</h3>
        <div class="vote-status">
            ${voteStatus.yesVotes} of ${voteStatus.totalParticipants} voted Yes (${percentage}%)
            &mdash; ${voteStatus.requiredVotes} needed
        </div>
        <div class="vote-status">${rows}</div>
        ${voteStatus.hasPassed
            ? '<div class="vote-passed">&#9989; Vote Passed!</div>'
            : `<div class="vote-buttons">
                   <button class="vote-yes" data-vote="yes">&#9989; Yes</button>
                   <button class="vote-no" data-vote="no">&#10060; No</button>
               </div>`}
    `;

    panel.querySelector('[data-vote="yes"]')?.addEventListener("click", () => castVote(true));
    panel.querySelector('[data-vote="no"]')?.addEventListener("click", () => castVote(false));

    panel.style.display = "block";
    byId("voteBtn").style.display = "none";
}
