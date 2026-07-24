---
status: DRAFT
touches: [Hookline.Audio]
depends_on: [001]
---

# 002 — Loopback capture + rolling buffer

> Status is DRAFT, not READY — flip to READY once 001 is DONE and reviewed, since this spec assumes 001's `TrackChanged`/state events exist and behave as specced. If 001's review surfaces changes to that interface, update this spec before handing it to Codex.

## Goal

Continuously capture Spotify's audio output into an in-memory rolling buffer, without the user ever pressing "record." By the time the user realizes "I like this part," the audio is already captured and sitting in the buffer, ready to be trimmed by spec 003.

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

- [ ] Process-specific (or documented fallback) loopback capture is running continuously while the app is open.
- [ ] Rolling buffer holds the configured window and correctly evicts older audio.
- [ ] Buffer queries are correctly segmented by track instance — verified by manually skipping tracks and confirming no bleed-over in a dumped WAV.
- [ ] Ad-flagged and paused regions are excluded/marked per spec 001's signals.
- [ ] Debug WAV-dump command exists and produces audibly-correct output.
- [ ] Sustained-run memory-bound test exists (even a simple long-running integration test) proving the buffer doesn't grow unbounded.
- [ ] Output-device-change behavior is at minimum detectable (doesn't fail silently), documented in "What shipped" even if full auto-recovery isn't achieved in this spec.

## Open questions

## Follow-up ideas
