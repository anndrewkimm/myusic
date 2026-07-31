---
status: DRAFT
touches: [Hookline.App, Hookline.Audio]
depends_on: [003, 009, 010, 011, 012, 013]
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

## Open questions

These need resolving before this can move to `READY` — they're real design
forks, not implementation details:

- **Stitching at segment boundaries**: hard cut, or a short crossfade (e.g.
  matching spec 013's existing 15ms export fade) to avoid audible clicks at
  every split? Recommend defaulting to a short crossfade, consistent with the
  app's existing fade convention, unless there's a reason to want hard cuts.
- **Segments whose duration changes under their own effects**: Speed and Loop
  already change a whole selection's exported length today (the app's own UI
  says "Effects can extend a clip to 5 minutes"). If segment 2 is looped or
  sped up, does its contribution to the final export simply grow/shrink to
  match (total export length = sum of each segment's own post-effect length),
  or are Speed/Loop excluded from per-segment scope specifically to keep the
  overall timeline predictable? Recommended default is the former (each
  segment renders to whatever length its own settings produce, lengths sum),
  but this changes what "the timeline" visually means once export runs long
  or short of the original selection, so it's worth confirming.
- **Stem isolation cost across segments**: stem separation is already
  described in-app as slow ("usually several seconds or longer"). If two
  segments both use stem-derived effects (e.g. vocal removal in segment 2,
  a different stem balance in segment 3), does separation run once over the
  union of the selection (today's existing behavior, reused per segment) or
  independently per segment? Recommend: separate once over the whole
  selection as today, and let each segment simply pick which rendering
  (original mix, or the shared separated stems at that segment's own volume
  settings) applies to its own sample range — avoids paying the slow
  separation cost more than once per export.
- **Live preview fidelity**: today's preview plays the live effect chain in
  real time. A multi-segment preview likely needs to pre-render a stitched
  buffer whenever any segment's settings change (same work export will do)
  rather than switching effect chains on the fly mid-playback. Confirm this
  tradeoff (a debounce/re-render delay after each tweak) is acceptable versus
  today's instant live preview. `plans/016-continuous-effect-preview.md`
  (position-preserving resume on effect change, single-segment) is a
  reasonable building block here — reusing its resume-by-fraction mechanism
  after a re-stitch is worth considering instead of designing this from
  scratch, though stitched multi-segment re-render is still a bigger problem
  than what 016 solves.
- **Minimum segment length** — needs a floor (e.g. a fraction of a second) so
  a dragged split can't create a degenerate, inaudible sliver.

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

## Acceptance criteria

To be finalized once the open questions above are resolved. First pass,
subject to revision:

- [ ] A user can add, drag, and remove split points on the waveform within
      their existing trim selection.
- [ ] Each segment created by splits exposes the same effect controls
      (EQ/presets, stem isolation + volumes, sound effects, edit presets) as
      today's whole-selection flow, independently per segment.
- [ ] Exporting a multi-segment selection produces one continuous file with
      each segment's own effects applied, stitched per the resolved
      boundary-handling decision above.
- [ ] A selection with no splits is functionally and byte-identical to
      today's existing single-chain export.
- [ ] Preview and export are byte-identical to each other across every
      segment boundary.
- [ ] All edge cases above are handled explicitly, not silently ignored.
