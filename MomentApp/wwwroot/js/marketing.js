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
