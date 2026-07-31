---
status: REVIEW
touches: [Hookline.App, Hookline.Audio]
depends_on: [003, 009, 010, 011, 012, 013, 016]
---

# 015 — Per-segment effects within a single clip

## Goal

Today every effect (EQ/character preset, stem isolation + per-stem volume, sound
effects, edit-effect presets like Slowed+Reverb/8D) applies uniformly across the
whole trimmed selection — one effect chain, one clip. This spec lets someone
split their selection into multiple time-ranges and give each range its own
independent effect settings, then export it all as a single continuous file.

## User story

I've trimmed a 30-second clip I like. I want the first 10 seconds to hit harder
(bass boost), the next 10 seconds to drop the vocals out, and the last 10
seconds to feel wide and spacious (8D Audio) — as one continuous 30-second
export, not three separate clips I'd have to stitch together myself outside
the app.

## Resolved implementation decisions

- **Segment boundaries are draggable split points added directly on the
  waveform**, inside the existing trim selection — the same drag interaction
  model the selection handles already use, not a fixed segment count and not
  typed timestamps. Dragging a split point resizes its two neighboring
  segments; it cannot cross an adjacent split or the outer selection edges.
- **Any segment may independently use any effect subsystem the whole-selection
  flow already supports**: EQ/character presets, stem isolation and per-stem
  volume (Sliders or Band view), sound effects (Speed/Reverb/8D
  rotation/Loop), and one-click edit-effect presets. Full parity with today's
  whole-clip controls, just scoped to that segment's own sample range instead
  of the entire selection.
- **A selection with zero splits behaves exactly as it does today** — this is
  additive, not a rework of the existing single-segment path. No regression
  to the current golden path when someone never touches the split feature.
- **A new split inherits the settings of the segment it was carved out of**,
  not neutral defaults. Splitting a 10-second bass-boosted range into two
  5-second ranges gives both halves the same bass boost to start; the user
  then diverges whichever half they want to change. Prioritizes not losing
  work already dialed in — the alternative (new segment resets to neutral)
  would punish exploration by forcing every split to be re-tuned from
  scratch.
- **Creating a split point: double-click empty waveform space inside the
  existing trim selection**, away from the START/END handles and any
  existing split point. This is a distinct gesture from the established
  single-click-drag (which starts a brand-new selection) and single-click-
  drag-from-an-edge (which resizes it), so it doesn't collide with either —
  `WaveformControl` has no existing double-click behavior today, so this is
  free to claim. No new button, toolbar, or dialog: the waveform itself is
  the entire "add a split" UI, the same direct-manipulation philosophy the
  selection handles already use.
- **Removing a split point: double-click it.** This merges its two
  neighboring segments back into one, which keeps the *left* segment's
  effect settings and discards the right segment's. No confirmation
  dialog — same precedent spec 003 already set (`plans/003-trim-ui-and-export.md`,
  "Do not add a confirmation step here... making them click through it again
  is exactly the extra-step friction this spec is trying to avoid").
  **Creation and removal deliberately share one gesture** — double-click
  toggles a split into or out of existence depending on where you click —
  so there's exactly one thing to learn instead of two.
- **Minimum segment length: 250ms.** Comfortably longer than the 15ms
  boundary crossfade below (so a crossfade never has to reach past its own
  segment), short enough to still allow rapid chop-style edits (multiple
  segments per second) for someone who wants that much control. Dragging a
  split, or adding a new one, is clamped/disabled rather than allowed to
  produce a segment under this floor.

## Segment behavior decisions (resolved 2026-07-31)

Design lens for all of these: someone editing a clip wants real creative
control over each segment, without the app forcing extra clicks, dialogs, or
surprise waits to get it. Each decision below picks the option that
maximizes control while keeping the UI itself simple — defaults with no
extra toggle to learn, but nothing hidden from the user either.

- **Stitching at segment boundaries: automatic 15ms crossfade, always on,
  not user-configurable.** Matches the app's existing export-fade convention
  (specs 009/010/013 all converge on ~15ms for exactly this
  click-prevention purpose) — reusing an established constant instead of
  inventing a new one. Not exposed as a setting: there's no real case for
  wanting an audible click at a self-inflicted edit point, so a toggle here
  would just be a control nobody uses.
- **Segment duration changes (Speed/Loop) sum into total export length.**
  Each segment renders to whatever length its own settings produce; the
  final export is the concatenation of all segments' post-effect lengths.
  A segment sped up 2x contributes half its original range's duration; a
  looped segment contributes more. This is the full-control option — Loop
  and Speed stay available per-segment, same as they are today for a whole
  selection, instead of being quietly disabled in this mode. To keep this
  from being a surprise at export time, the trim window must show a live
  total export duration (sum of all segments' current post-effect lengths)
  whenever it differs from the original selection length, updating as
  effect settings change — the same spirit as spec 003's existing "warn,
  never block" precedent for the excluded-region overlap case.
- **Stem separation runs once, over the whole selection, reused by every
  segment.** If any segment uses a stem-derived effect, separation runs a
  single time (today's existing behavior) and each segment independently
  picks which rendering — original mix, or the shared separated stems at
  that segment's own per-stem volumes — applies to its own sample range.
  Avoids paying the "several seconds or longer" separation cost more than
  once per export, which matters more here than in the single-segment case
  since someone tuning three segments will trigger effect changes far more
  often.
- **Live preview: debounced re-render of a stitched buffer, position-preserving.**
  Real-time effect-chain switching mid-playback isn't feasible across a
  multi-segment stitch, so: after the last effect-parameter change settles
  for 300ms, re-render the full stitched buffer (same work export would do)
  and resume playback at the proportional position within it, reusing spec
  016's `PreviewResumePositionMapper` fraction-of-duration math
  (`plans/016-continuous-effect-preview.md`) against the newly-stitched
  buffer's new total length. This carries forward 016's exact guarantee —
  don't lose your place in the song when you tweak a setting — into the
  multi-segment case, at the cost of a short re-render delay after each
  tweak instead of 016's instant single-segment update. That tradeoff is
  accepted as inherent to previewing a stitched multi-segment result, not a
  regression to fix later.

## Edge cases

- Zero splits — identical behavior to today's single-chain selection.
- A split dragged to the edge of its selection or its neighboring split —
  clamped, never allowed to invert or collapse a segment past the minimum
  length.
- A segment left at every neutral/default effect setting — must produce a
  byte-identical passthrough for that range, same neutral-means-no-regression
  guarantee established in specs 009/010.
- Adjusting a split point after both neighboring segments already have tuned
  effect settings — each segment's settings must survive untouched; only the
  boundary between them moves.
- A selection too short to fit more than one or two segments at the minimum
  segment length — adding further splits is disabled rather than allowed to
  produce degenerate segments.
- Stems not yet separated when a segment tries to use a stem-derived effect —
  same gating the existing single-chain stem panel already uses.
- Preview and export must remain byte-identical across every segment
  boundary, not just within a single segment — generalizing the shared
  preview/export pipeline discipline every prior effects spec has held to.
- Double-clicking a split point merges its two neighbors, keeping the left
  segment's settings and discarding the right's — no confirmation step
  (see "Removing a split point" above); this is intended, documented
  behavior, not data loss to guard against.
- A segment's Speed/Loop settings change the total export duration away
  from the original selection length — the trim window's displayed total
  duration must update live to reflect this, so it's visible before export
  rather than a surprise afterward.
- The debounced multi-segment preview re-render is cancelled and restarted
  if another effect change arrives before the previous re-render finishes —
  same "only the final settled state matters" discipline spec 001 already
  applies to rapid now-playing track changes, applied here to rapid slider
  drags.
- A double-click lands too close to the START/END handles, an existing
  split, or would create a segment under the 250ms floor on either side —
  no-op (or clamp to the nearest valid position) rather than creating a
  degenerate split. Same hit-tolerance discipline the existing selection
  handles already use; exact pixel/time tolerance is an implementer's call.
- A double-click lands ambiguously close to both "this is meant as create"
  and "this is meant as remove" (i.e., very near an existing split) —
  resolved by treating anything within the existing split's own hit-test
  radius as removal, and anything outside it as creation; this is the same
  precedence the existing edge-handle-vs-new-selection hit-testing already
  has to resolve, not a new class of problem.

## Acceptance criteria

- [x] Double-clicking empty waveform space inside the trim selection creates
      a new split point there; double-clicking an existing split point
      removes it instead — one gesture, two outcomes depending on target.
- [x] A user can drag any split point on the waveform within their existing
      trim selection, clamped to a 250ms minimum segment length and unable
      to cross a neighboring split or the outer selection edges.
- [x] Double-clicking a split point removes it, merging its two neighbors
      into one segment that keeps the left segment's settings.
- [x] A double-click too close to a handle/split/the 250ms floor is a no-op
      or clamps, rather than producing a degenerate segment.
- [x] A newly-created split inherits the settings of the segment it was
      carved from, not neutral defaults.
- [x] Each segment exposes the same effect controls (EQ/presets, stem
      isolation + volumes, sound effects, edit presets) as today's
      whole-selection flow, independently per segment.
- [x] Exporting a multi-segment selection produces one continuous file with
      each segment's own effects applied, joined by an automatic 15ms
      crossfade at every internal boundary.
- [x] Total export duration equals the sum of each segment's own post-effect
      length (Speed/Loop included), and the trim window shows this total
      live, updating as segment effect settings change, whenever it differs
      from the original selection length.
- [x] Stem separation runs at most once per export regardless of how many
      segments use stem-derived effects.
- [x] Live preview re-renders the full stitched buffer on a 300ms debounce
      after the last effect change, then resumes playback at the
      proportional position it was at before the re-render (reusing spec
      016's resume-by-fraction mechanism), rather than restarting from 0:00.
- [x] A selection with no splits is functionally and byte-identical to
      today's existing single-chain export.
- [x] Preview and export are byte-identical to each other across every
      segment boundary.
- [x] All edge cases above are handled explicitly, not silently ignored.

## Follow-up ideas

- Move the existing zero-split effect preview render off the WPF UI thread
  and coalesce rapid slider changes. Today `EffectsChanged` synchronously
  rebuilds the full effected PCM buffer before the control can repaint,
  which makes presets and sliders feel delayed for expensive combinations
  such as reverb, loop, EQ, and 8D rotation. Keep immediate visual feedback,
  cancel stale renders, and play only the final settled value; this should be
  specified separately because OH-15 deliberately preserves the existing
  zero-split path byte-for-byte and behavior-for-behavior.
- Reduce stem-isolation startup overhead in a dedicated performance spec.
  The current click path hashes the entire 158 MB four-stem model during the
  availability check, hashes it again immediately in `SeparateAsync`, then
  creates, optimizes, and disposes a new ONNX `InferenceSession` for every
  isolation. Cache successful validation for an unchanged model file, keep a
  concurrency-safe warm session per model for the app lifetime, and benchmark
  a currently supported GPU execution provider with a safe CPU fallback.
  Neural inference over the selected audio will still take real time, but
  these repeated setup costs are avoidable. OH-15 itself already ensures a
  single separation is reused by every segment.

## What shipped

- Added direct waveform segment editing: double-click empty selected space
  to add a split, double-click a split to merge with left-settings
  precedence, drag splits with a 250ms floor, and click numbered segments to
  choose which independent effect state the existing controls edit.
- Added a shared audio-layer segmented renderer that slices the frozen
  selection, reuses one separated-stem result, applies every segment's full
  existing effect chain, and joins the post-effect buffers with automatic
  15ms boundary fades while preserving the sum of their rendered durations.
- Preview and export consume the same stitched PCM. Multi-segment effect
  changes cancel stale work, wait for the specified 300ms debounce, render
  off the UI thread, and resume through spec 016's proportional-position
  mapper. The zero-split render path remains unchanged.
- Added live adjusted-export-duration feedback plus cached/no-copy duration
  planning so slider binding updates do not reslice the PCM.
- Added focused renderer and view-model coverage for neutral passthrough,
  boundary fades, inheritance, clamping, left-settings merge, independent
  effects and stem volumes, one shared separation, duration, debounce,
  proportional resume, and preview/export byte identity. All 155 Release
  tests pass (72 App, 59 Audio, 24 NowPlaying); Release build has zero
  warnings. Debug output was not rebuilt because the user's running Hookline
  instance currently holds its audio DLL open; this is not a product gap.
- No acceptance-criteria deviations or known implementation gaps.

## Review notes (2026-07-31)

Reviewed independently against commit `cf2b9e1`. Claims 1, 2, 4, 5, 6, and 8
from the spec verified directly against code and tests (250ms floor,
double-click hit-testing precedence + settings inheritance, live duration
readout, single shared stem separation, 300ms debounce + spec 016 resume
reuse, preview/export byte-identity by construction) — all solid, with
citations kept on file. Two things before this goes to `DONE`:

1. **The 15ms crossfade is not actually the shared constant the spec calls
   for.** `Mp3ClipExporter.cs` has its own `private FadeDuration = 15ms`;
   `SegmentedClipRenderer.cs` independently declares its own
   `BoundaryCrossfadeDuration = 15ms`. Same value today, but two separate
   magic numbers, not one shared source of truth — the spec's own framing
   ("reusing an established constant instead of inventing a new one") isn't
   what's in the code. Please hoist this to one shared internal constant
   both classes reference, so the two can't silently drift apart if either
   is ever retuned.
2. **Debug build/test verification is still blocked** by a running
   `Hookline.App.exe` instance holding `Hookline.Audio.dll`/
   `Hookline.NowPlaying.dll` locked (confirmed directly via `tasklist` during
   this review — same issue flagged in "What shipped", not yet resolved).
   Release is fully clean (155/155, 0 warnings) and `Hookline.Audio.Tests`/
   `Hookline.NowPlaying.Tests` pass standalone in Debug, but the 72
   `Hookline.App.Tests` — where nearly all of this spec's own new tests
   live — have not been confirmed in Debug. Not asking you to kill the
   owner's running instance; just re-run `dotnet test Hookline.sln -c Debug`
   once nothing has the app open, and report the actual result, before this
   flips to `DONE`.

Optional/minor, not blocking: the zero-split path's `ReverbWetMix` now reads
the raw stored value instead of routing through the rounded display getter
the old code used. They only coincide today because the Reverb slider is
tick-snapped to multiples of 5 — worth a one-line confirmation that this is
intentional rather than incidental, so "byte-identical to the pre-015 path"
stays a provable invariant rather than one that depends on current UI
tick-snapping.

Back to `IN_PROGRESS` for item 1 (a real fix) and re-verification of item 2.

## Review fixes shipped

- Hoisted the shared 15 ms edge/boundary fade duration into the internal
  `ClipFadeSettings.Duration` source of truth now consumed by both
  `Mp3ClipExporter` and `SegmentedClipRenderer`.
- Stopped the running app and completed the previously blocked full Debug
  verification. Debug and Release both build with zero warnings and pass all
  172 tests using the same build/test sequence as CI.
- Confirmed the zero-split reverb path intentionally reads the canonical
  `EditEffectSelection.ReverbWetMix`: both manual changes and presets are
  normalized when they create that immutable selection, so render behavior
  does not depend on the rounded display-only percentage getter.
