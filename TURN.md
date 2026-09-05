# Enabling voice and video calls across restrictive networks

Moment's calls are peer-to-peer. Two browsers usually find each other using STUN alone, which
costs nothing and is configured out of the box. But STUN cannot help when both peers sit behind
a **symmetric NAT** or a firewall that blocks peer-to-peer UDP — most corporate networks, some
mobile carriers, and a meaningful slice of home routers. In those cases the media has to be
relayed through a **TURN** server.

Roughly 10-20% of real-world call attempts need a relay. Without one they fail *silently*: ICE
negotiates, gets nowhere, and the call simply never starts.

**This is the single biggest gap between "calls work on my machine" and "calls work for users."**

## What the app does today

- Ships with public STUN and no relay (`Rtc:Turn:Provider = None`).
- Tells the user plainly when a peer cannot be reached and no relay is configured, instead of
  leaving them staring at a call that never connects.
- Reads relay configuration from the environment, so enabling one needs **no code change**.

## Enabling a relay

Set these environment variables on the host. On Render they are already declared in
`render.yaml` with `sync: false`, so the values are entered in the dashboard and never
committed.

```
Rtc__Turn__Provider=StaticSecret
Rtc__Turn__Urls__0=turn:turn.example.com:3478?transport=udp
Rtc__Turn__Urls__1=turn:turn.example.com:3478?transport=tcp
Rtc__Turn__Urls__2=turns:turn.example.com:5349?transport=tcp
Rtc__Turn__StaticAuthSecret=<the shared secret from your TURN server>
Rtc__Turn__CredentialTtlSeconds=3600
```

**Include the `turns:` URL.** Networks that block UDP *and* plain TCP TURN will usually still
allow TLS on 443/5349, and that is precisely the case that decides whether the app works from
inside an office.

### Why `StaticSecret`

`StaticSecret` is coturn's `use-auth-secret` scheme (the "TURN REST API"). The server holds one
long-lived secret and mints a short-lived credential per participant:

```
username   = <unix-expiry>:<participant-id>
credential = base64(HMAC-SHA1(username, shared-secret))
```

The secret itself never reaches a browser, each credential expires within the hour, and coturn
validates the HMAC without needing any shared state. `Provider=Static` is also supported for
servers configured with a fixed username and password, but it hands every participant the same
long-lived credential and should be a last resort.

Credentials are served from `GET /api/ice/{roomCode}`, which is gated on the room session — so
only somebody already admitted to a room can mint credentials against your relay bandwidth —
and marked `no-store` so they are never written to disk.

### Providers

Any standards-compliant TURN server works. Self-hosted [coturn](https://github.com/coturn/coturn)
is the cheapest option; Twilio, Metered and Cloudflare all sell hosted relays. Relay bandwidth
is the cost driver, since relayed media flows through the server rather than between peers.

## Verifying it actually works

STUN masks a broken relay on every network you would normally test from, so a call succeeding
proves nothing about your TURN configuration. Append `?forceRelay=1` to the room URL:

```
https://your-app.example.com/Room/Index?roomCode=ABC123&forceRelay=1
```

That sets `iceTransportPolicy: "relay"`, so the call can **only** connect through TURN. If it
connects, your relay works. If it does not, it was never working and STUN was covering for it.
