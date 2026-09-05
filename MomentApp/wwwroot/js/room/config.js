/**
 * Server-rendered configuration for this room.
 *
 * Read from a `<script type="application/json">` island rather than interpolated into
 * JavaScript. Razor HTML-encodes `@Model` expressions, so a display name containing an
 * apostrophe used to arrive inside a JS string literal as `&#x27;` and render as the raw
 * entity. Serialising to JSON with the default encoder also escapes `<` and `>`, which makes
 * a `</script>` inside a user-supplied name inert.
 */
const el = document.getElementById("moment-config");
if (!el) {
    throw new Error("moment-config island is missing; the room cannot start.");
}

export const config = Object.freeze(JSON.parse(el.textContent));
