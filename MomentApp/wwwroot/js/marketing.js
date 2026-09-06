/**
 * Marketing page behaviour: reveal sections as they scroll into view.
 *
 * Two earlier approaches failed in ways worth recording:
 *
 *  - Pure CSS animations ran at page load, so everything below the fold had already
 *    finished animating by the time you scrolled to it.
 *  - IntersectionObserver missed elements entirely on a fast scroll. If a section goes
 *    from below the viewport to above it between two frames, `isIntersecting` is false
 *    both times and no callback ever fires — leaving the section invisible until the
 *    reader happened to scroll back up.
 *
 * A sweep against the viewport is deterministic: it cannot miss an element regardless of
 * how the scroll position got there, including anchor jumps and restored positions.
 */

const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

if (!prefersReducedMotion) {
    // Opts the page into "hidden until revealed". Set from script deliberately: if this file
    // never runs, the class is never added and the content simply stays visible rather than
    // the whole page below the hero silently blanking.
    document.documentElement.classList.add("js-reveal");

    /** Elements still waiting to be revealed. */
    let pending = [...document.querySelectorAll(".fade-in")];
    let queued = false;

    function sweep() {
        queued = false;
        const trigger = window.innerHeight * 0.88;

        pending = pending.filter((el) => {
            if (el.getBoundingClientRect().top > trigger) return true;
            el.classList.add("visible");
            return false;
        });

        if (pending.length === 0) {
            window.removeEventListener("scroll", schedule);
            window.removeEventListener("resize", schedule);
        }
    }

    // Coalesce to one measurement per frame; reading layout on every scroll event would
    // force a reflow far more often than the display can use.
    function schedule() {
        if (queued) return;
        queued = true;
        requestAnimationFrame(sweep);
    }

    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule, { passive: true });
    schedule();
}

/* ------------------------------------------------------------------ hero sky */

const clips = [...document.querySelectorAll(".sky-clip")];

if (clips.length === 2) {
    if (prefersReducedMotion) {
        // CSS has already hidden them; this stops the decoder as well, which is the part
        // that costs battery.
        clips.forEach((clip) => clip.pause());
    } else {
        /**
         * The clip's last frame does not match its first — SSIM 0.82 across the seam
         * against 0.96 between adjacent frames — so `loop` cuts visibly once every pass.
         * Two copies of the same file, crossfaded over the last second, hide it.
         *
         * `loop` is set on the first element in the markup rather than here, so a failure
         * to load this module leaves a looping sky (cut and all) instead of a single
         * frozen frame. Taking it off is this code claiming the handover.
         */
        const FADE_S = 1.2;
        let active = 0;
        let handing = false;

        clips[0].removeAttribute("loop");

        function handoff() {
            if (handing) return;
            handing = true;

            const next = clips[1 - active];
            next.currentTime = 0;
            next.play().catch(() => {});
            next.classList.add("is-on");
            clips[active].classList.remove("is-on");
            active = 1 - active;

            setTimeout(() => { handing = false; }, FADE_S * 1000);
        }

        for (const clip of clips) {
            // timeupdate fires about four times a second, which is coarse — but it only has
            // to start the fade, and the fade itself is a CSS transition.
            clip.addEventListener("timeupdate", () => {
                if (clip !== clips[active] || !clip.duration) return;
                if (clip.currentTime >= clip.duration - FADE_S) handoff();
            });

            // Whatever has finished fading out has no reason to keep decoding.
            clip.addEventListener("ended", () => {
                clip.pause();
                clip.currentTime = 0;
            });
        }

        // A background video that has scrolled away is pure battery cost. Unlike the reveal
        // sweep above, a missed callback here is harmless: the worst case is that the sky
        // keeps playing off-screen, not that content stays invisible.
        const field = document.querySelector(".skyfield");
        if (field && "IntersectionObserver" in window) {
            const watcher = new IntersectionObserver(([entry]) => {
                if (entry.isIntersecting) clips[active].play().catch(() => {});
                else clips.forEach((clip) => clip.pause());
            });
            watcher.observe(field);
        }
    }
}
