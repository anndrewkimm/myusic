---
status: DONE
touches: [Hookline.NowPlaying]
---

# 001 — Now-playing watcher

## Goal

A background service that always knows what track is currently playing in the Spotify desktop app, and raises an event the moment that changes. This is the foundation everything else (buffering, tagging, catalog) depends on — get it right and boring before anything audio-related is built on top of it.

## User story

As the user, I never interact with this directly — it's invisible plumbing. Its correctness shows up later as "the exported clip has the right artist/title" and "the buffer never mixes two songs together."

## Functional requirements

- Poll (or subscribe to, if the API supports push) `GlobalSystemMediaTransportControlsSessionManager` for sessions belonging to the Spotify desktop app specifically (there may be multiple media sessions active on the system — e.g. a browser tab also playing something — this watcher must only care about Spotify's session).
- On track change, raise a `TrackChanged` event with: title, artist, album, track duration (if available), album art (if available, as a bitmap/stream), and a monotonic "track instance id" (so two plays of the same song are distinguishable — needed later so the buffer doesn't assume "same title = same audio").
- Expose current playback state (playing/paused) — needed later to pause capture during pauses.
- Must be resilient to Spotify not running at all (no session found) and to Spotify starting/stopping while the watcher is alive — should pick up a new session appearing without requiring the app to restart.
- Design the public interface (`INowPlayingWatcher` or similar) so it is not Spotify-specific in its shape, even though the only implementation in Phase 1 targets Spotify — Phase 3 may add other sources. Don't build those other sources now, just don't paint the interface into a Spotify-only corner.

## UX details

None directly — this is a headless service. For this spec's own testability, ship a minimal debug console/window that just prints track-change events live (title — artist, timestamp) so the spec can be verified by eye: open Spotify, skip through a few tracks, watch the log update correctly and promptly (target: under ~1 second of perceived lag from the track actually changing to the event firing).

## Edge cases

- Spotify is closed when the app starts — watcher should not crash, should just report "nothing playing," and should pick up Spotify's session the moment it opens.
- User pauses — should this fire a distinct "paused" state, not a "track changed to nothing"? Yes: pause/resume is a state change, not a track change. Track identity is unchanged.
- User seeks within the same track — not a track change.
- User replays the same track from the start (loop, or manually restarting it) — is this a new "track instance"? Yes — treat it as a new instance id even though title/artist are identical, so a later buffer/capture doesn't accidentally straddle "end of first play" and "start of replay" as one continuous chunk.
- Ads on Spotify Free — these typically surface as a distinct "track" in the media session (often with generic/missing metadata, sometimes literally titled "Advertisement"). Detect this if the metadata makes it detectable, and raise a distinguishable flag (`IsLikelyAd` or similar, best-effort) — later specs (buffering, capture) will use this to pause/exclude capture during ads. If Spotify's metadata doesn't reliably expose this, note that clearly in "What shipped" rather than guessing — don't invent unreliable heuristics that create false positives on real songs with short/odd titles.
- Multiple media sessions on the system (e.g., a YouTube video also open in a browser) — must filter to the Spotify process specifically, not "whatever session the OS reports as active," since the active session can be ambiguous with multiple apps playing.
- Rapid track skipping (user spamming next/next/next) — should not queue up a flood of stale events; only the final settled track should matter downstream. Debounce if needed, but don't drop the very last real change.

## Acceptance criteria

- [x] `INowPlayingWatcher` (or equivalent) interface exists in `Hookline.NowPlaying`, Spotify implementation provided.
- [ ] `TrackChanged` event fires with correct title/artist/album/art within ~1s of an actual change in manual testing.
- [x] Playback state (playing/paused) is separately observable and doesn't fire spurious track-change events on pause/resume/seek.
- [x] Each distinct play (including replays of the same song) gets a unique track-instance id.
- [x] Works correctly across: Spotify not running at startup → opened later; Spotify closed while app is running; multiple media sessions present on the system.
- [x] Debug viewer (console or minimal window) exists and demonstrates the above live.
- [x] Unit tests for anything not requiring an actual live Spotify session (e.g., filtering logic, debounce logic) live in `Hookline.NowPlaying.Tests`.

## Open questions

(Codex: fill in here if anything below needs a decision before/during implementation.)

## Follow-up ideas

## What shipped

- Added the .NET 8 solution with a UI-agnostic `Hookline.NowPlaying` library,
  a `Hookline.NowPlaying.Debug` console viewer, and
  `Hookline.NowPlaying.Tests`.
- Added a source-agnostic `INowPlayingWatcher` contract and a Spotify SMTC
  implementation. It filters exact known Spotify desktop/Store app IDs,
  follows session arrival/removal, observes playback separately, debounces
  metadata/timeline changes for 250 ms, and assigns monotonic instance IDs.
- Same-song replay detection uses a timeline rollback to the first two seconds
  after at least five seconds of progress. Backward seeks that do not return to
  the start remain the same instance; seeking to the start is treated as the
  spec's manual restart case.
- Ad detection is deliberately conservative: only an explicit
  `Advertisement` title is flagged. Missing metadata and other ambiguous ad
  labels remain unflagged to avoid false positives.
- Verification: warning-free Debug and Release builds, formatting/whitespace
  checks, and 23 passing tests covering source filtering, source lifecycle,
  pause/resume/seek behavior, replay IDs, debounce behavior, rapid skipping,
  and explicit ad labeling.
- Live smoke test on 2026-07-23 found the running Spotify desktop session,
  reported its paused state, and loaded the current title, artist, album,
  2:10 duration, and 122,399-byte album art about 0.37 seconds after viewer
  startup with no stderr output. I did not alter the owner's current playback,
  so the remaining unchecked criterion is the reviewer's manual next/next
  latency check in the live viewer.
