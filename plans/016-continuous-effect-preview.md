---
status: DONE
touches: [Hookline.App]
depends_on: [003, 009, 010, 011, 012, 013]
---

# 016 — Preview keeps playing through effect changes instead of restarting

## Goal

Today, changing any effect setting while a preview is playing — an EQ band,
a character preset, a stem volume, a sound effect, an edit-effect preset —
stops playback and restarts it from the very beginning of the selection.
This spec makes preview keep playing through the change, continuing from
the equivalent position in the newly-processed audio instead of jumping
back to 0:00.

## User story

I'm previewing a clip about 10 seconds in and I nudge the bass slider. I
want to keep hearing the song from where I already was — with the new bass
setting applied — not get thrown back to the start every time I touch a
control.

## Root cause (confirmed in code)

- `TrimViewModel.EffectsChanged` (`TrimViewModel.cs:1148`) already detects
  that preview was playing (`_previewPlayer.IsPlaying`), stops it, and
  restarts it — but `StartPreview()` always calls
  `_previewPlayer.Play(selection)` with no notion of "resume from where you
  were."
- `AudioPreviewPlayer.Play` (`AudioPreviewPlayer.cs:40`) has no seek/offset
  parameter at all: it always builds a fresh `WaveOutEvent` +
  `RawSourceWaveStream` over the new processed buffer and calls
  `RaisePosition(TimeSpan.Zero)` unconditionally.
- The raw playback offset within the currently-playing processed buffer is
  computed internally in `OnTimerTick` (via `_output.GetPosition()`) but is
  only ever exposed after being remapped into *timeline* space
  (`AudioSnapshotSlicer.MapAudioOffsetToTimeline`, surfaced as
  `TrimViewModel.Playhead`). Nothing currently exposes the raw
  fraction-through-the-buffer that a resume needs.

## Resolved implementation decisions

- **`IAudioPreviewPlayer` gains a way to resume at an arbitrary point** —
  e.g. an optional `TimeSpan resumeAt` parameter on `Play`, seeking the
  underlying stream/`WaveOutEvent` to that offset before calling `Play()`,
  instead of always starting at sample 0.
- **Position carries over by proportional (fractional) position through the
  buffer, not absolute wall-clock time.** Effects like Speed and Loop/extend
  change the processed buffer's total duration, so `resumeAt` must be
  computed as `(currentRawPosition / oldSnapshot.Duration) * newSnapshot.Duration`,
  not as a literal `TimeSpan` carried unchanged. For a uniform tempo change
  (Speed), this correctly lands on the same musical moment; for
  Loop/extend it lands on a well-defined position even though it may fall
  in a different loop repetition than before (see edge cases — that's
  acceptable, not a bug).
- **`IAudioPreviewPlayer` needs to expose the current raw position within
  its own buffer** (not the timeline-mapped `Playhead`) so
  `TrimViewModel.EffectsChanged` can compute the fraction above before
  tearing down the old player. Cheapest option: expose the same raw
  `audioPosition` value `OnTimerTick` already computes, before it gets
  remapped to timeline space.
- **`TrimViewModel.EffectsChanged`**: when `restartPreview` is true, capture
  the outgoing player's raw fraction *before* calling `StopPreview()`, build
  the new processed selection as today, then call the new resume-aware
  `Play(selection, resumeAt)` instead of the plain `Play(selection)`.
- **Starting preview from a fully stopped state is unchanged** — pressing
  Preview after Stop (or on first use) still starts at 0, exactly like
  today. Only an in-flight effect change while already playing gets the new
  behavior.

## Edge cases

- Effect changed while preview is stopped/paused — unaffected, still starts
  at 0 exactly as today.
- Effect changed at or past the very end of the old buffer (preview about
  to finish naturally) — resumes at/near the end of the new buffer; if that
  immediately exceeds the new buffer's length, clamp to its last valid
  sample rather than seeking out of range.
- An effect change that shrinks total duration a lot (e.g. a large
  speed-up) such that the fractional offset would land past the new
  buffer's end — clamp to the last valid sample, same as above.
- Loop/extend changes causing the resumed fractional offset to fall in a
  different loop repetition than where playback actually was — acceptable
  and expected, not treated as a defect.
- Rapid, repeated effect changes (e.g. dragging a slider continuously) —
  each restart must fully dispose the previous `WaveOutEvent`/stream before
  the next one opens (the existing `StopCore` teardown already guarantees
  this); no leaked native audio handles across a burst of changes.
- An effect change that makes the selection produce empty/silent audio —
  falls back to existing empty-selection handling (`StatusMessage` set,
  preview not started), not a crash.

## Acceptance criteria

- [ ] Changing any effect setting (EQ band/character preset, stem volume,
      sound effect, edit-effect preset) while preview is actively playing
      keeps audio playing continuously, at the equivalent position in the
      newly-processed buffer — never restarted from 0:00.
- [ ] The equivalent position is computed proportionally against the new
      (possibly different-duration) processed buffer, correctly handling
      Speed/Loop-driven duration changes per the resolved decision above.
- [ ] Starting preview from a stopped state is unchanged — still begins at
      position 0.
- [ ] No leaked NAudio output devices/handles across rapid consecutive
      effect changes during playback.
- [ ] The fractional position-mapping math has unit test coverage
      independent of the UI thread, per `docs/CONVENTIONS.md`'s
      testability rule.

## Notes for other specs

- `plans/015-segmented-clip-effects.md` (still `DRAFT`) separately flags
  "Live preview fidelity" as an open question for per-segment previews.
  This spec doesn't resolve that question (015 is about stitching multiple
  segments' previews together, a bigger problem), but the resume-by-fraction
  mechanism here is a reasonable building block for whatever 015 eventually
  decides — worth a glance when 015 gets picked back up.

## What shipped

- Added resume-aware preview playback with an exposed raw audio position.
  Replacement processed buffers seek to the same proportional position,
  including after Speed or Loop changes alter the total duration.
- Resume seeks are sample-frame aligned and clamp to the final valid frame.
  A resumed player's raw position includes its initial seek offset, so
  repeated effect changes continue mapping from the correct position.
- Existing output, stream, and timer state is fully torn down before each
  replacement player opens. Failed player initialization now also tears
  down partial native/audio resources before surfacing the error.
- Starting Preview from a stopped state still passes a zero offset.
- Added UI-thread-independent fractional mapping tests plus view-model
  coverage for duration-changing resume behavior and the stopped-state
  path. The full Debug suite passes: 140 tests (24 NowPlaying, 50 Audio,
  66 App); solution formatting and `git diff --check` are clean.
- No scope deviations. Known verification gap: automated tests do not open
  a physical `WaveOutEvent` device, so perceived live continuity and native
  handle behavior during a real slider-drag burst remain for live review.

## Review notes (reviewer, 2026-07-30)

- Rebuilt Release independently (0 warnings, 0 errors) and ran the full
  suite: 140/140 pass (24 NowPlaying, 50 Audio, 66 App), matching the
  "What shipped" count exactly.
- Read the actual diff, not just the spec's own summary. Root cause matches
  what I'd found reading the code before writing this spec:
  `AudioPreviewPlayer.Play` now takes an optional `resumeAt`, seeks the
  `RawSourceWaveStream` to a block-aligned, end-clamped byte offset before
  playing, and tracks `_playbackStartOffset` so `CurrentAudioPosition`
  correctly reports `_playbackStartOffset + _output.GetPosition()` rather
  than resetting relative to the seek point.
- `PreviewResumePositionMapper.Map` does the fraction math in `decimal`
  (avoiding float drift), clamps the source position to the old duration
  first, and guards all three zero/negative cases (no prior position, no
  prior duration, no new duration) back to `TimeSpan.Zero` — matches the
  spec's resolved decision exactly, and the 6-case theory test
  (`PreviewResumePositionMapperTests`) covers normal fraction, over-duration
  clamp, and every zero-guard branch.
- Hand-verified the core scenario by tracing the numbers: a 1000ms preview
  at playhead 400ms, then Speed set to 2x (buffer duration halves to
  500ms) — `ChangingAnEffectResumesAtTheProportionalPosition` asserts the
  resume lands at exactly 200ms (400/1000 × 500), which is the correct
  "same musical moment" result the spec called for. A second test confirms
  starting Preview from a stopped state still resumes at 0.
- All 6 edge cases from the spec are handled: stopped-state start (tested),
  end-of-buffer / large-speedup clamping (covered by the mapper's
  over-duration case plus `GetPlaybackOffset`'s separate last-frame clamp,
  so a resume can never seek past the buffer even if the duration math
  alone wouldn't have caught it), Loop's different-repetition landing spot
  (accepted by design, nothing to fix), rapid repeated changes (unchanged
  `StopCore` teardown-before-reopen invariant, not weakened by this diff),
  and empty-selection effect changes (unchanged early-return path in
  `StartPreview`, untouched by this diff).
- Two unrelated modified files in the working tree (`plans/012`,
  `plans/013`) predate this diff entirely (present in git status before
  this spec was even drafted) — confirmed not part of Codex's actual
  change here. No scope creep: the diff touches exactly
  `AudioPreviewPlayer.cs`, `IAudioPreviewPlayer.cs`, `TrimViewModel.cs`, the
  new `PreviewResumePositionMapper.cs`, and their tests.
- One honest, non-blocking gap remains, same as Codex flagged: no
  automated test opens a real `WaveOutEvent`, so perceived audio continuity
  during an actual slider-drag (not just the position math) is worth a real
  listen next time the app is run, same category of residual note as spec
  003's foreground-window caveat.

Clean. Flipping to `DONE`.
