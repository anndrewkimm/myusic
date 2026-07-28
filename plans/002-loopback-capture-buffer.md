---
status: DONE
touches: [Hookline.Audio]
depends_on: [001]
---

# 002 — Loopback capture + rolling buffer

> This spec is now ready for implementation. It assumes spec 001's watcher contract is stable and available, and it should remain focused on the capture/buffer pipeline rather than UI work.

## Goal

Continuously capture Spotify's audio output into an in-memory rolling buffer, without the user ever pressing "record." By the time the user realizes "I like this part," the audio is already captured and sitting in the buffer, ready to be trimmed by spec 003.

## Codex handoff

- Keep this implementation headless and UI-free; the goal is to prove the capture/buffer pipeline end to end.
- Reuse the existing now-playing watcher contract from spec 001 rather than inventing a new Spotify-specific path.
- Prioritize process-loopback capture, track-instance segmentation, pause/ad exclusion, and a debug WAV dump over any UI work.
- Treat memory-bounded behavior and capture-stall detection as explicit quality requirements, not nice-to-haves.

## Resolved implementation decisions

These are the decisions that remove the earlier ambiguity and make the plan implementable without guessing:

- The watcher contract must be extended so the audio layer can anchor its buffer to the same timeline the user hears. That means the watcher should expose the current playback position and the source process identity for the selected media session.
- The primary capture path is process-loopback. If the target process cannot be identified or capture cannot be started, the system must fall back to full-system loopback and report that fallback explicitly in its status. It must not silently switch modes.
- Buffer segmentation must be driven by track-instance identity and playback state. A new track or a replay should start a new segment; pause and ad regions must be marked or excluded so the query API never returns mixed content across songs or non-song spans.
- The query API must be deterministic and snapshot-based while a buffer dump is in flight. It must never return partial or corrupted audio if a track change happens mid-query.
- The debug WAV export is a required proof step and should exist before any UI work begins.

## Implementation plan

1. Extend the watcher contract to expose the current playback position and the selected source process identity.
2. Add a headless audio-capture service in the audio layer with explicit status states: running, stalled, failed, fallback-loopback, and stopped.
3. Implement a bounded rolling buffer that stores audio by track segment and evicts old data once the configured window is exceeded.
4. Add track-segmentation logic that uses the watcher’s track-instance ID, playback position, and playback-state transitions.
5. Add pause/ad exclusion rules and make them visible through the buffer/query API.
6. Add a debug command that writes the current track’s recent audio to a WAV file and verify the result manually by ear.
7. Add tests for memory bounds, segment boundaries, replay handling, and fallback status reporting.

## Non-negotiables

- No UI work in this spec.
- No silent fallback from process-loopback to full-system loopback.
- No silent data loss if capture stalls or the output device changes.
- No unbounded memory growth.
- No cross-track bleed in query results.

## User story

As the user, I open the app once, leave it running, and go about listening to Spotify normally. I never think about "recording" as a concept — the app is just always a little bit behind real-time, holding on to what already played.

## Functional requirements

- Capture Spotify's audio output specifically, not the full system mix (see `docs/CONVENTIONS.md` for the process-loopback-vs-full-system-mix tradeoff and fallback plan — this spec must implement the process-specific approach first, and only fall back to full-system loopback if that proves genuinely unworkable, with the fallback clearly called out in "What shipped").
- Maintain a rolling buffer of the last N minutes of audio (configurable; default suggestion: 5 minutes — long enough to catch "wait, go back" moments, short enough not to waste memory). Older audio is discarded as new audio comes in.
- Use spec 001's track-instance-id and playback-state events to segment the buffer by track: querying "give me the buffer for the current track" must never return audio from a previous song, even if the previous song was still within the last N minutes.
- Pause capture (or at least mark the captured region as excluded) when spec 001 reports the track is likely an ad, and when playback is paused.
- Expose a simple query API: "give me the raw audio for [track instance id], optionally from time X to time Y within that track's elapsed playback time" — this is what spec 003's trim UI will call.

## UX details

None directly — headless. For this spec's own verification: a debug command that dumps "the last 30 seconds of the current track's buffer" to a WAV file on demand, so correctness can be checked by ear (does the WAV actually contain the right 30 seconds, cleanly, with no other track's audio bleeding in?).

## Edge cases

- Track changes while a buffer dump/query is in flight — don't corrupt or return partial garbage; either serve from a snapshot or block briefly, implementer's call, but must not crash or return silently-wrong audio.
- App just started, current track has been playing for longer than the buffer window — querying "from the start of the track" should clearly indicate only the last N minutes are available, not silently return less than requested with no signal.
- Very short buffer requests (sub-second) and very long ones (the full N-minute window) both need to work.
- System audio output device changes mid-capture (user unplugs headphones, switches to speakers) — capture should recover gracefully, not silently go dead. If it can't recover instantly, it should be detectable (a "capture is stalled" state) rather than failing silently — this matters a lot, since silent data loss here means the user thinks a clip was captured when it wasn't.
- Memory bounds: buffer must actually stay bounded at the configured length under sustained multi-hour use — this needs an explicit test, not just "seems fine in a 5-minute manual check."
- What happens if two different pieces of audio are legitimately close together in time but both flagged non-ad, non-paused, same track-instance (e.g. a brief silent gap within a song) — should not be misread as a track boundary.

## Acceptance criteria

- [x] Process-specific (or documented fallback) loopback capture is running continuously while the app is open.
- [x] Rolling buffer holds the configured window and correctly evicts older audio.
- [x] Buffer queries are correctly segmented by track instance — verified by manually skipping tracks and confirming no bleed-over in a dumped WAV.
- [x] Ad-flagged and paused regions are excluded/marked per spec 001's signals.
- [x] Debug WAV-dump command exists and produces audibly-correct output.
- [x] Sustained-run memory-bound test exists (even a simple long-running integration test) proving the buffer doesn't grow unbounded.
- [x] Output-device-change behavior is at minimum detectable (doesn't fail silently), documented in "What shipped" even if full auto-recovery isn't achieved in this spec.

## Open questions

None. The resolved implementation decisions above authorized extending the
watcher with both a timestamped playback timeline and the selected source
process identity.

## Follow-up ideas

None identified during implementation.

## What shipped

- Extended `INowPlayingWatcher` with a timestamped playback-position snapshot
  and selected-source identity. The production resolver selects the root of the
  active Spotify process tree so process-loopback includes Spotify's child
  processes.
- Added a headless `Hookline.Audio` service with process-specific WASAPI
  loopback as the primary backend. It uses
  `ActivateAudioInterfaceAsync`/process-loopback activation and captures the
  target process tree as 44.1 kHz, 16-bit stereo PCM. Activation is gated on
  Microsoft's currently documented minimum Windows build 20348; older systems
  take the reported fallback path.
- Added an explicit NAudio 2.3.0 full-system-loopback fallback. The service
  reports `FallbackLoopback` plus the reason instead of silently changing
  capture scope.
- Added the configurable rolling buffer (five minutes by default), strict
  byte-bound eviction, track-instance segmentation, paused/ad exclusion,
  playback-time range queries, truncation/gap signals, and immutable query
  snapshots.
- Added running, fallback, stalled, failed, and stopped status reporting.
  Unexpected backend stops trigger a visible stalled state and an automatic
  restart attempt. While playback is active, a lack of capture packets also
  transitions to `Stalled`, so an output-device disruption cannot fail
  silently. Process-loopback itself is independent of the physical output
  endpoint; the NAudio fallback reports its stop event through the same recovery
  path.
- Added the `Hookline.Audio.Debug` console harness. Its `d [path]` command
  writes the current track's most recent 30 seconds to PCM WAV.
- Added ten audio tests, including a simulated ten-hour/360,000-chunk sustained
  run, strict memory bounds, cross-track isolation, mid-track truncation,
  sub-second queries, stable snapshots, pause/ad exclusion, replay
  segmentation, fallback status, stall/restart behavior, and WAV structure.
  The watcher suite also gained coverage for the new source/timeline contract.

Verification completed:

- `dotnet format Hookline.sln --no-restore --verify-no-changes`
- `dotnet build Hookline.sln -c Release --no-restore`: zero warnings/errors
- `dotnet test Hookline.sln -c Release --no-build`: 34/34 passing
- Live Release smoke test selected `ProcessLoopback`, found the Spotify process
  and track, and shut down cleanly.
- Live debug export produced 4.2 seconds of valid RIFF PCM: stereo, 44.1 kHz,
  16-bit, 737,352 audio bytes with nonzero sample data.

Known review gaps (resolved 2026-07-27, see below):

- The exported WAV could not be judged by ear in the automated environment.
- The no-bleed behavior is covered by deterministic track-instance tests, but
  the requested human workflow of manually skipping Spotify tracks and
  listening to the resulting WAV remains for review. No playback controls were
  invoked during implementation.

## Review notes (2026-07-27)

Reviewer ran both debug consoles live against a real Spotify session:

- Status consistently reported `Running (ProcessLoopback)`, never fell back
  to full-system loopback.
- Paused regions correctly excluded: a dump attempt while paused failed with
  "the current track has no buffered audio" rather than returning silence or
  stale audio.
- Dumped two real clips: the first 21.4s of one track, and 19.4s of the very
  next track (a genuine track change) immediately after. Owner confirmed by
  ear both were clean and correctly matched to their respective songs, with
  no bleed from the prior track — the duration of the second dump (19.4s)
  matched almost exactly the elapsed time since that track had started,
  consistent with the segment boundary resetting cleanly at the track change.
  Also exercised a same-song replay (new instance, same title) immediately
  before the genuine track change, adding a harder boundary case than a
  simple skip.
- Both remaining acceptance boxes and the "known review gaps" above are
  considered resolved. Spec accepted as DONE.
