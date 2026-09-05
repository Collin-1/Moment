import { config } from "moment/config";

/**
 * Fetches ICE servers (including short-lived TURN credentials) for this room.
 *
 * Credentials are fetched rather than embedded in the page so they can be refreshed on a long
 * call and are never written into cacheable markup. The result is cached until shortly before
 * it expires, and a failure degrades to public STUN rather than blocking the call — a call
 * that might work is strictly better than one that definitely will not.
 */

const REFRESH_MARGIN_MS = 5 * 60 * 1000;

const FALLBACK = {
    iceServers: [{ urls: "stun:stun.l.google.com:19302" }],
    hasTurn: false,
};

let cached = null;

/** True once we know a relay is configured; used for honest failure messaging. */
export let relayAvailable = false;

export async function getIceConfig() {
    if (cached && Date.now() < cached.expiresAtMs - REFRESH_MARGIN_MS) {
        return cached.value;
    }

    let payload = FALLBACK;
    let expiresAtMs = Date.now() + 60_000;

    try {
        const response = await fetch(`/api/ice/${encodeURIComponent(config.roomId)}`, {
            cache: "no-store",
            credentials: "same-origin",
        });

        if (response.ok) {
            const dto = await response.json();
            payload = { iceServers: dto.iceServers, hasTurn: dto.hasTurn };
            expiresAtMs = dto.expiresAtUnix * 1000;
        } else {
            console.warn("ICE config request failed:", response.status);
        }
    } catch (err) {
        console.warn("ICE config unreachable, falling back to STUN:", err);
    }

    relayAvailable = payload.hasTurn === true;

    // ?forceRelay=1 pins traffic to TURN. Without it you cannot tell a working relay from a
    // broken one, because STUN quietly succeeds on every network you would normally test from.
    const forceRelay = new URLSearchParams(location.search).has("forceRelay");

    cached = {
        expiresAtMs,
        value: {
            iceServers: payload.iceServers,
            iceTransportPolicy: forceRelay ? "relay" : "all",
            bundlePolicy: "max-bundle",
            iceCandidatePoolSize: 2,
        },
    };

    return cached.value;
}

/**
 * The most recently fetched configuration, without a network round-trip.
 *
 * Peer creation must stay synchronous: two signals for the same peer arriving together would
 * otherwise both await and both build a connection. Callers prime the cache with
 * {@link getIceConfig} before joining a call, and read it through here afterwards.
 */
export function currentIceConfig() {
    return cached?.value ?? {
        iceServers: FALLBACK.iceServers,
        iceTransportPolicy: "all",
        bundlePolicy: "max-bundle",
        iceCandidatePoolSize: 2,
    };
}
