/**
 * Small behaviours for the entry screens. Everything here is an enhancement — the forms
 * submit and validate correctly without it.
 */

/* Copy-to-clipboard buttons, marked with data-copy="#targetSelector". */
document.querySelectorAll("[data-copy]").forEach((button) => {
    button.addEventListener("click", async () => {
        const target = document.querySelector(button.dataset.copy);
        if (!target) return;

        const original = button.textContent;
        try {
            await navigator.clipboard.writeText(target.value);
        } catch {
            // Clipboard access can be refused (permissions, insecure origin). Selecting the
            // text at least leaves the reader one keystroke from copying it themselves.
            target.select();
            button.textContent = "Press Ctrl+C";
            setTimeout(() => (button.textContent = original), 2500);
            return;
        }

        button.textContent = "Copied";
        setTimeout(() => (button.textContent = original), 2000);
    });
});

/* Room codes are generated uppercase; matching case by hand is needless friction. */
document.querySelectorAll("[data-uppercase]").forEach((input) => {
    input.addEventListener("input", () => {
        const start = input.selectionStart;
        input.value = input.value.toUpperCase();
        input.setSelectionRange(start, start); // preserve the caret across the rewrite
    });
});
