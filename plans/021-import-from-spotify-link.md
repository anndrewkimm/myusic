---
status: READY
touches: [Hookline.App, Hookline.Audio]
depends_on: [018]
---

# 021 — Import audio by pasting a Spotify link

## Goal

The owner's music discovery happens entirely inside Spotify, not YouTube —
but spec 018's URL-import only accepts a direct video URL. This spec lets
the same "Import from URL..." flow also accept a Spotify track link: resolve
its public metadata, find the matching video, confirm it with the user, then
download through spec 018's existing fetch pipeline unchanged.

## User story

I find a song I want a clip of by browsing Spotify, not YouTube — that's
just where I actually listen. Today, getting it into Hookline via spec 018
means leaving Hookline, searching for the song on YouTube myself, copying
that URL, and pasting it in. I want to paste the Spotify link I already have
instead, and have Hookline do the "find it on YouTube" step for me.

## Why this is legitimate and where the real line is

Spotify's own API/links **never** expose full track audio — only metadata
(title, artist, album) and, for some tracks, a 30-second preview. That's a
licensing boundary enforced by Spotify itself, not a gap this spec works
around. **This spec never talks to Spotify for anything except public
metadata.** The actual audio fetch is spec 018's existing YouTube-URL flow,
completely unchanged, triggered by a video URL this spec resolves *for* the
user instead of the user finding it themselves. See `docs/CONVENTIONS.md`
for the full framing — read it before touching this spec, same as spec 018
required.

Because the match is search-based (there is no official Spotify-track-to-
YouTube-video mapping), **the user must see and confirm the specific
resolved video before anything downloads** — same confirm screen spec 018
already built for its own flow, just reached via a Spotify link instead of
a pasted video URL. This isn't optional politeness; it's the actual
correctness guard against downloading the wrong recording (a cover, a
remix, a lyric video with different content, etc.).

## Resolved implementation decisions

- **One shared entry point, not a second dialog.** Extend spec 018's
  existing "Import from URL..." dialog to detect a Spotify track link
  (`open.spotify.com/track/...`, including an optional locale segment like
  `/intl-en/track/...`, and the `spotify:track:{id}` URI form) versus a
  direct video URL, and branch accordingly. One thing for the user to
  remember ("paste a link"), not two menu entries to choose between for a
  similar action. When a Spotify link is detected, the dialog shows an
  explicit intermediate "Looking up this track on YouTube..." state before
  reaching the same title/thumbnail/duration confirmation screen spec 018
  already has for a direct video URL — the extra step is visible, not
  hidden, consistent with this app's existing "warn/show, never silently
  auto-act" discipline (e.g. spec 003's excluded-region warning).
- **Spotify link parsing accepts only single-track links.** Strip any query
  string (e.g. `?si=...` share-tracking params) and any locale path segment
  before extracting the track ID. Any other Spotify link type — album,
  playlist, artist, episode, show, or a user profile — is explicitly
  rejected with a message asking for a single track link, mirroring spec
  018's own playlist/channel rejection for YouTube links. A Spotify local
  file (added to the user's own library from disk) does not have a
  resolvable `open.spotify.com` web link at all, so it naturally falls out
  as "not a valid track link" rather than needing special-case handling.
- **Metadata source: Spotify's public oEmbed endpoint first** (no
  authentication, no setup step — `open.spotify.com/oembed?url=...`),
  giving the "paste a link and it just works" experience by default.
  **Verify its current response shape at implementation time** — if it
  reliably separates artist from track title, use it as-is; if it doesn't
  (or drops artist entirely), fall back to the official Web API's Client
  Credentials flow (app-only auth, no user login, free to register) for
  richer, more reliable metadata including exact duration. If that fallback
  path is needed, the user provides their own free Spotify Developer Client
  ID/Secret once via a settings entry, stored locally under
  `%LOCALAPPDATA%\Hookline\` — **never a credential bundled or committed in
  the repo**, since this repo may be published to GitHub (per
  `docs/CONVENTIONS.md`) and a shared bundled secret would be both a
  leak risk and a shared rate-limit target across every install. Whichever
  path is actually active must be clear from the code (no silent fallback
  substitution), same discipline as the WASAPI-loopback fallback already
  documented in `docs/CONVENTIONS.md`.
- **YouTube search + match ranking** reuses whatever extraction library
  spec 018 ends up using (its own search capability if it has one, or an
  equivalent search call) rather than adding a second library. Rank
  candidates by title/artist text similarity, and by closeness to the
  Spotify track's duration when duration is available from the metadata
  source in use — a strong signal, since a real official upload's length
  closely matches the track length while a remix/lyric-compilation/full-
  album video typically won't.
- **Confirmation shows more than one candidate when the match is
  ambiguous.** If the top-ranked result isn't a clear, confident best match
  (implementer's call on the exact confidence threshold — verify against
  real search results at implementation time), show a short list (e.g. top
  3-5) with title/thumbnail/duration for each, rather than forcing a single
  blind guess. If exactly one clear best match exists, still show it
  through the same single-result confirm screen spec 018 already has — no
  new UI needed for the common case, just a list instead of a single card
  for the ambiguous one.
- **From the moment a match is confirmed, this is spec 018's flow, byte-
  for-byte.** No new download code, no new progress/cancel UI, no new
  sanity-cap logic — the resolved video URL is handed to spec 018's
  existing pipeline exactly as if the user had pasted it directly.

## Edge cases

- Malformed or non-Spotify-track link pasted into the URL field — clear
  error, no crash; falls through to spec 018's existing "not a valid video
  URL" handling if it isn't recognized as a Spotify link at all.
- Album/playlist/artist/episode/show/user-profile Spotify link — explicit
  rejection asking for a single track link, not silently resolving to the
  first track in a playlist or similar.
- Track removed, region-locked, or otherwise unavailable on Spotify's own
  side — metadata lookup itself fails with a clear, specific error distinct
  from "no YouTube match found."
- No YouTube match found at all for a resolved track — clear error that
  suggests falling back to manually pasting a YouTube link in the same
  dialog, rather than a dead end.
- Multiple plausible YouTube matches (covers, remixes, live versions, lyric
  videos) — user is shown a short list to pick from, never a silent
  best-guess auto-download.
- oEmbed (or the Web API fallback) is down, rate-limited, or returns an
  unexpected shape — clear error, never an unhandled exception reaching the
  UI thread, per `docs/CONVENTIONS.md`'s background-operation rule.
- Web API fallback path active with an invalid or revoked Client ID/Secret
  — clear, specific error pointing at the credential, distinct from a
  track-not-found error, so the user knows what to fix.
- Web API Client Credentials token expiring mid-session — transparent
  refresh; never a user-visible error under normal use.
- Everything downstream of a confirmed match — download progress/cancel,
  network failure mid-download, video longer than the existing sanity cap,
  extraction library breakage — is spec 018's existing edge-case handling,
  unchanged. Not re-specified here to avoid two sources of truth drifting
  apart; see `plans/018-import-from-youtube-url.md`.

## Acceptance criteria

- [ ] Pasting a valid `open.spotify.com/track/...` link (with or without a
      locale segment or `?si=...` query string) or a `spotify:track:{id}`
      URI into the existing "Import from URL..." dialog is recognized as a
      Spotify link, distinct from a direct video URL.
- [ ] The dialog shows a visible "looking up this track" state, then a
      confirm screen (single match, or a short list when ambiguous) with
      title/thumbnail/duration, before any download begins.
- [ ] Confirming a match downloads through spec 018's existing fetch
      pipeline with no new/duplicated download logic.
- [ ] Album/playlist/artist/episode/show/user-profile Spotify links are
      rejected with a clear message asking for a single track link.
- [ ] A track with no resolvable metadata, or no YouTube match, produces a
      clear, specific, non-crashing error — distinct messages for each
      failure point (bad link, unavailable track, no match found, lookup
      service error).
- [ ] If the Web API fallback path is used, invalid/revoked credentials
      produce an error distinct from "track not found," and no Spotify
      Client ID/Secret is ever bundled or committed in the repo.
- [ ] New Spotify-link-parsing and match-ranking logic lives in
      `Hookline.Audio`, is UI-agnostic, and has unit test coverage
      independent of real network access (fake/stubbed metadata and search
      results), matching the existing test discipline from spec 018.
- [ ] No changes to spec 018's own download/progress/cancel/error logic —
      this spec only supplies it a resolved video URL.

## Sequencing note

This spec hands its resolved video URL directly to spec 018's fetch
pipeline, so it cannot actually be implemented before spec 018 is `DONE` —
marked `READY` here so it's queued and fully specified, but Codex should
finish 018 first (consistent with `CLAUDE.md`'s "only one spec
`IN_PROGRESS` at a time" rule; the `depends_on` field above reflects the
real ordering constraint, not just documentation).
