---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [002, 003]
---

# 006 — Fix runaway memory growth during normal use

## Goal

`Hookline.App` grows memory far beyond what its design should require during completely ordinary use — not an edge case, not hours of use, just "open it, play a track, open the trim window." This directly threatens the app's entire premise (`plans/000-roadmap.md`: "leave the app running"). This spec is find-and-fix, prioritized above any further feature work.

## Severity / why this jumps the queue

Phase 1 (specs 001–004) is otherwise complete and reviewed. This was caught during hands-on testing after 004 shipped, not by the existing automated test suite — none of the 55 passing tests caught it, which itself is worth noting in "What shipped" (was it a gap in coverage, or does the leak only manifest against real WASAPI capture in a way a synthetic/simulated test can't reach?).

## Reviewer's findings (2026-07-27) — starting point, not gospel

- Fresh launch: ~150MB working set. Opening the trim window with an empty buffer: ~194MB. Both normal.
- After roughly 20-40 seconds of a track actually playing (real WASAPI process-loopback capture active, trim window open showing a populated waveform): working set observed at 600-720MB in one run, and separately reached **1.35GB** after a few minutes of more general use (drag/preview/export/catalog) in an earlier session.
- A batch of 30 rapid synthetic drag gestures on an already-populated waveform changed memory by only about -12MB (i.e., dragging itself, once data exists, does not appear to be the driver) — this rules out `WaveformControl`'s per-render peak-scan as the primary suspect, though it hasn't been rigorously eliminated (the drag test happened concurrent with the owner's own real interaction on the same running instance, which confounds a clean before/after reading — see "Open questions").
- Read `RollingAudioBuffer.cs` in full: eviction logic (`EvictOldestBytes`) looks correctly bounded to the configured window regardless of track-instance count — didn't find an obvious leak there by inspection.
- Read `AudioPreviewPlayer.cs` in full: `Play()` calls `StopCore` first, and `StopCore` disposes `WaveOutEvent`/`RawSourceWaveStream`/`MemoryStream` and unsubscribes its event handler every cycle — didn't find an obvious leak there either by inspection.
- None of this rules out: the live WASAPI process-loopback capture path specifically (as opposed to the synthetic/simulated capture used in spec 002's "ten-hour" test) doing something different in practice — e.g. retaining native buffers, over-allocating per packet, or a chunking pattern that behaves differently against real packet sizes/timing than the simulated test exercises. That mismatch (passing synthetic test, leaking against real capture) is the most likely single lead.

## Codex handoff

- This needs actual profiling, not more black-box guessing from outside the process. Use `dotnet-counters`, `dotnet-gcdump`/`dotnet-dump`, or equivalent to see managed-heap size vs. working set (rules managed vs. native/unmanaged growth in or out) and to see what's actually accumulating.
- Reproduce with the real process-loopback capture path specifically (not just the existing simulated/synthetic unit test), since that's the one path not yet proven clean.
- Once found: fix it, then add a regression test that would have caught it. If the existing "ten-hour simulated" test in `Hookline.Audio.Tests` doesn't exercise whatever the real leak turns out to be, say so explicitly in "What shipped" and explain what new coverage closes that gap — don't just patch the symptom.
- If the leak turns out to be in native/COM interop around `ActivateAudioInterfaceAsync`/process-loopback activation (undisposed COM objects, buffers not released after each packet, etc.), treat that as the prime suspect given it's the one part of the pipeline that's genuinely fragile per `docs/CONVENTIONS.md`'s own caveat about that corner of the API.

## Edge cases

- Confirm whether the growth is roughly proportional to elapsed capture time (a true per-second/per-packet leak) or front-loaded into one or two large one-time allocations that then plateau (the reviewer's measurements above are more consistent with the latter, but weren't clean enough to be certain — see open questions).
- Confirm behavior across a track change / replay (spec 001/002's instance-segmentation) — does starting a new track instance make things better (old instance's chunks age out and evict normally) or worse (new per-instance state stacking on top of old, never released)?
- Confirm the trim window's own lifecycle isn't part of it: does memory look different with the trim window never opened at all (capture running headless, as in the spec 002 debug harness) versus with it opened? This isolates App-layer (WPF/waveform/catalog) causes from Audio-layer (capture/buffer) causes.

## Acceptance criteria

- [x] Root cause identified and documented (not just "reduced the symptom").
- [x] A fresh launch, left playing/idle for a sustained period (at minimum the equivalent of one full 5-minute buffer window, ideally longer), stays within a small, stated, reasonable bound of its cold-start baseline — put an actual number on this once the fix is in, so future regressions have something concrete to check against.
- [x] A regression test exists that fails without the fix and passes with it. If real WASAPI capture can't be exercised in a CI-style test, the test should cover whatever the actual root cause turns out to be as precisely as possible, and the gap should be named explicitly rather than left implicit.
- [x] Confirm via the app's own tray/status surface (or a debug console, whichever is faster to verify with) that behavior is sane both with and without the trim window open.

## Open questions

- The reviewer's own testing was confounded by testing on the same live instance the owner was also actively using at the same time (track changes and window state shifted mid-measurement in ways not attributable to the synthetic test alone). Whoever picks this up should get a clean, single-actor reproduction rather than trusting the exact numbers above — they're evidence that something is very wrong, not a precise characterization of the growth curve.

## Follow-up ideas

## What shipped

- Root cause: real WASAPI packets are timestamped onto an independently refreshed SMTC playback timeline. Normal clock/scheduling skew made adjacent 10 ms PCM packets appear about 0.15 ms apart. `RollingAudioBuffer.Query` therefore returned thousands of false excluded ranges. The trim waveform retained a filled rectangle plus roughly two dozen hatch-line drawing primitives for every microscopic gap, producing a large WPF/native allocation as soon as the window rendered. A clean pre-fix profile reproduced 5,681 false gaps and an immediate jump from about 206 MB to 561.2 MB working set; the managed heap was only about 87.8 MB, which isolated the excess to rendering rather than the bounded PCM buffer.
- `RollingAudioBuffer` now keeps the raw observed packet clock separately and snaps adjacent packets onto a continuous normalized timeline when their cadence differs from the PCM duration by at most 5 ms. Real discontinuities and seeks remain gaps. Partial buffer eviction advances both clocks. `WaveformControl` also refuses to retain excluded-range geometry narrower than 0.75 display pixel as a defense in depth.
- The new `RealPacketClockJitterDoesNotFragmentTheTimeline` regression feeds 6,000 real-cadence 44.1 kHz stereo packets with the measured 0.1507 ms per-packet clock skew. It produced 177 fragmented ranges under the first naive correction and thousands without normalization; the shipped implementation produces one range and no gaps. The previous long synthetic test used perfectly contiguous timestamps, so it could validate byte eviction but could never trigger this App-layer rendering failure. CI still does not activate real WASAPI or render a live WPF window; the new test models the measured boundary condition directly.
- Sustained real-process-loopback validation ran for more than ten minutes, past a complete five-minute rolling window. Cold start was 154.6 MB working set / 83.7 MB private. At full-window age the closed trim state was 288.2 MB / 199.2 MB, and a newly-rendered trim window was 328.3 MB / 248.8 MB. A 30-second runtime-counter sample with the window open ranged from 338.9 to 341.7 MB working set while the bounded managed heap ranged from 108.5 to 116.3 MB; a heap snapshot contained only 23 `AudioTimeRange` objects. The stated regression guardrail is therefore 375 MB working set, or no more than 225 MB above this cold-start baseline, with a full buffer and trim window open.
- The app's own hotkey/tray lifecycle was used to close and reopen the trim surface during the run. Capture remained responsive both headless and with the window open. The diagnostic instance was stopped afterward.

## Review notes (reviewer, 2026-07-27)

- Rebuilt and retested independently: 0 warnings/errors, 64/64 passing across all three test projects.
- Read the root-cause explanation against the actual diff and it holds up: `RollingAudioBuffer` now tracks a separate `ObservedPlaybackStart` (raw SMTC-reported time) alongside the normalized `PlaybackStart`/`PlaybackEnd`, and snaps a new packet onto the previous one's exact end time when the observed gap is within 5ms of the packet's own duration — exactly the SMTC-clock-vs-WASAPI-packet-clock skew described. `WaveformControl` separately refuses to draw excluded-range geometry under 0.75px as defense in depth.
- The new `RealPacketClockJitterDoesNotFragmentTheTimeline` test reproduces the measured 0.1507ms/packet drift across 6,000 packets and asserts a single included range with zero excluded ranges — a precise, targeted regression test, not a vague "add more coverage" gesture.
- This confirms the user's own real-world signal (a subsequent ~35 minute live run reported as working normally) rather than contradicting it.

Clean. Flipping to DONE.
