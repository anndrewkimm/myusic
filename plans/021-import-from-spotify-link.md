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
- **Metadata source: the official Web API's Client Credentials flow is the
  primary and only path — not a fallback.** This reverses this spec's
  original draft, which proposed oEmbed-by-default for a zero-setup
  experience. Checked directly against a live request during planning
  (`open.spotify.com/oembed?url=...` for a real track) rather than assumed:
  the response contains **no artist field at all** — `title` is the bare
  track name only (e.g. `"title": "Shape of You"`, nothing else
  identifying the artist, not even inside the embed HTML). Searching
  YouTube on a bare title with no artist produces meaningfully worse,
  more ambiguous matches — exactly the failure mode this spec's mandatory
  confirmation screen exists to catch, but a bad search means the *right*
  video may not even appear in the candidates to confirm. Since this
  spec's entire premise is correctly identifying the right recording,
  accuracy wins over zero-setup convenience here.
  The user provides their own free Spotify Developer Client ID/Secret once
  via a settings entry (app-only Client Credentials auth, no Spotify login,
  ~2-minute one-time registration at developer.spotify.com), stored locally
  under `%LOCALAPPDATA%\Hookline\` — **never a credential bundled or
  committed in the repo**, since this repo may be published to GitHub (per
  `docs/CONVENTIONS.md`) and a shared bundled secret would be both a leak
  risk and a shared rate-limit target across every install. This also gets
  exact track duration (not available from oEmbed either), which is the
  strongest signal for ranking YouTube candidates. If no credentials are
  configured yet, the dialog says so plainly and points at the one-time
  setup step rather than silently degrading to a worse title-only search —
  no silent fallback substitution, same discipline as the WASAPI-loopback
  fallback already documented in `docs/CONVENTIONS.md`.
- **YouTube search + match ranking** reuses spec 018's YoutubeExplode 6.6.0
  (confirmed as what 018 actually shipped with — no fallback to yt-dlp was
  needed there, so there's no ambiguity to resolve here either) rather than
  adding a second library. YoutubeExplode exposes a search API
  (`SearchClient`); verify its current signature against the installed
  version at implementation time, same "don't assume, check" discipline
  spec 018 already used for the rest of the library's surface. Rank
  candidates by title/artist text similarity, and by closeness to the
  Spotify track's exact duration (available from the Web API metadata this
  spec now requires) — a strong signal, since a real official upload's
  length closely matches the track length while a remix/lyric-compilation/
  full-album video typically won't.
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
- No Spotify Client ID/Secret configured yet — clear message pointing at
  the one-time settings setup, distinct from any other error; never a
  silent degrade to a worse title-only lookup.
- Web API is down, rate-limited, or returns an unexpected shape — clear
  error, never an unhandled exception reaching the UI thread, per
  `docs/CONVENTIONS.md`'s background-operation rule.
- Invalid or revoked Client ID/Secret — clear, specific error pointing at
  the credential, distinct from a track-not-found error, so the user knows
  what to fix.
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
- [ ] Missing or invalid/revoked Spotify Client ID/Secret each produce a
      distinct, specific error (not conflated with "track not found"), and
      no credential is ever bundled or committed in the repo.
- [ ] New Spotify-link-parsing and match-ranking logic lives in
      `Hookline.Audio`, is UI-agnostic, and has unit test coverage
      independent of real network access (fake/stubbed metadata and search
      results), matching the existing test discipline from spec 018.
- [ ] No changes to spec 018's own download/progress/cancel/error logic —
      this spec only supplies it a resolved video URL.

## Sequencing note

This spec hands its resolved video URL directly to spec 018's fetch
pipeline. Spec 018 shipped and is `DONE` as of 2026-07-31 (including a
second-pass fix that changed how the trim window is invoked — see its
"Review notes" — none of which affects this spec, since this hands off to
`UrlAudioImportService`/the fetch pipeline itself, not to how that pipeline
gets triggered). No remaining sequencing blocker; this is genuinely ready
for Codex to pick up whenever it's next in line.
