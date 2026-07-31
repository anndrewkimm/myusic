---
status: REVIEW
touches: [Hookline.App, Hookline.Audio]
depends_on: [003, 004, 008, 009]
---

# 019 — Mix two clips into one track

## Goal

Let someone combine two separately-captured/imported clips into a single
output, instead of Hookline only ever handling one continuous source at a
time. Owner's own framing: "I could cut this same part of the song and then
combine it with this song" — a mashup/layering feature, not just sequential
editing of one track.

## User story

I've got two clips I like — say, a moment from one song and a moment from
another. I want to combine them into one track instead of exporting two
separate MP3s and mixing them myself in some other tool.

## Grounding constraint (why this is necessarily post-hoc, not live)

Spotify plays one track at a time, and Hookline's loopback capture (spec
002) is tied to a single now-playing track's boundaries — there is no live
"record two songs playing at once" mode, because that's not a thing Spotify
itself can do. So this feature is necessarily: pick two clips that already
exist (already-exported catalog clips, a freshly-trimmed selection, or an
imported file), then combine them in a second pass. Worth saying explicitly
so nobody expects a live dual-capture mode — that's not what this is.

## Scope for this spec, and what comes after (owner decision, resolved 2026-07-31)

There are three materially different features hiding under "mix songs
together," at very different levels of effort and risk. Owner chose to
sequence more than one rather than pick just a single one outright:

- **A — Simple layer/overlay.** Pick two clips, play them on top of each
  other (each with its own independent volume), no tempo or pitch matching.
  Straightforward PCM mixing on top of infrastructure that already exists
  (both sources already normalize to the same 44.1kHz/16-bit/stereo format
  via the existing import/capture pipeline). Output length is whichever
  policy gets chosen below. This is buildable now, cleanly, on top of
  existing plumbing.
- **B — Stem-swap mashup.** Pick two clips, run spec 011's existing stem
  separation on each, then let the user pick which stems come from which
  source — e.g. vocals from clip A, everything else from clip B. Musically
  more satisfying than a raw overlay (won't just sound like two songs
  playing over each other), and reuses spec 011's Demucs pipeline entirely
  unchanged rather than building new DSP. Works best when the two source
  clips already happen to share a similar tempo/key — Hookline wouldn't be
  correcting for a mismatch, just leaving the result as musical or
  dissonant as the user's own clip choice makes it.
- **C — Beat-matched DJ-style mixing.** Detect BPM/beat grid on both clips,
  time-stretch one or both to match tempo, align beats, crossfade between
  them. This is what "mixing" means in a DJ-software sense, but it's a
  meaningfully bigger and riskier undertaking than A or B: real-time-quality
  tempo detection and time-stretching is a hard problem on its own (naive
  time-stretching audibly degrades audio — warbling/artifacts — well before
  reaching DJ-software quality), plus a beat-grid UI. This is
  backlog/maybe-never territory unless the owner specifically wants to
  commit real effort to it now, not a natural next step from A/B.

**Resolved sequencing:** this spec (019) builds **A only** — that's the
scope everything below (decisions/edge cases/acceptance criteria) describes
and what Codex should implement. **B is intentionally not part of this
spec** — it's queued as a distinct future spec once 019 has shipped and had
some real use, since it's mostly gluing spec 011's already-built stem
separation onto a two-source picker rather than new DSP, and specs stay
small and independently reviewable per this repo's own convention rather
than growing one spec to cover both. **C stays in Phase 3 backlog** in
`plans/000-roadmap.md` — it's a different scale of project (real tempo
detection and time-stretch quality work) and isn't being sequenced next,
just not ruled out forever.

## Resolved implementation decisions

- **Entry point: a new "Mix..." tray menu entry**, alongside the existing
  "Import audio file...", "Import from URL...", and "Open clip library"
  entries. Opens a dedicated two-source picker screen rather than being
  bolted onto the existing single-source trim window — two independent
  sources is a genuinely different mental model than one continuous
  selection, and forcing it into the trim window would mean redesigning a
  screen that already works well for its existing job.
- **Source selection**: both clips come from the same picker — either an
  already-exported catalog clip (spec 004) or a fresh trim/import, reusing
  existing selection UI rather than inventing a new file-picker. Picking the
  same source for both slots is allowed, not blocked — it's a harmless
  input (a cheap way to double/thicken a single clip), and blocking it would
  just be a special case for no real benefit.
- **Independent volume per source**, exposed as two plain sliders/percentage
  controls (same style as spec 011's per-stem volume) — "how loud is each
  layer relative to the other" is the one control this feature cannot ship
  without.
- **Output duration: the longer of the two selected clips.** The shorter
  clip loops to fill that length, reusing spec 009's existing seamless
  loop/crossfade primitive unchanged rather than building new looping logic.
  This is the most content-preserving default — nothing from either clip
  gets silently cut short to match the other. Anyone who wants a shorter
  result can already trim either source down first, in the same picker,
  before mixing — no separate "match to shorter" toggle needed for that.
- **Export reuses `Mp3ClipExporter`/tagging unchanged** — a mixed result is
  still just an `ImportedAudioFile`-shaped buffer by the time it reaches
  export, same discipline every prior import-adjacent spec (008/017/018)
  has held to. Title/artist tags: implementer's call on a sensible combined
  default (e.g. "Track A / Track B"), editable the same way any other
  clip's tags already are before export.
- **Interaction with spec 015 (per-segment effects) and spec 011 (stem
  isolation): out of scope for this spec.** A mixed two-source result is
  treated as its own new source afterward — open it back up from the
  catalog to segment-split or stem-isolate it further, same as any other
  clip. Not building combined mix+segment or mix+stem UI in this pass.

## Edge cases

- Two sources with very different loudness — covered by the independent
  per-source volume control above, not an automatic loudness-matching
  feature (that's a further-out idea, not part of this spec).
- Mixing a clip with itself (same source picked twice) — explicitly
  allowed, not an error state.
- The shorter clip's loop-to-match-length seam must be click-free, same
  seamless-loop guarantee spec 009 already established — this spec doesn't
  reinvent that, just reuses it.
- Combined output exceeding the existing 5-minute effects-extension cap
  (spec 009's existing ceiling) — same clear-message handling as today,
  not a new cap invented for this spec.
- One or both sources still mid-processing (e.g. a stem-isolation job
  running on a clip selected as a mix source) — same gating an in-progress
  slow operation already uses elsewhere; a source can't be picked into a
  mix while it's still busy.
- Picking two sources of very different original loudness/format (mono vs
  stereo, different sample rates) — both already normalize to the same
  44.1kHz/16-bit/stereo shape through the existing import/capture pipeline
  before either ever reaches this feature, so no new normalization logic
  is needed here.

## Acceptance criteria

- [ ] A "Mix..." tray entry opens a dedicated two-source picker, each slot
      fillable from the clip catalog or a fresh trim/import.
- [ ] Each source has its own independent volume control, applied before
      the two are summed into the mixed output.
- [ ] Output duration equals the longer selected source; the shorter source
      loops seamlessly (no click/gap at the seam) to fill that length.
- [ ] Mixing a clip with itself is allowed and produces a correct result,
      not an error.
- [ ] The mixed result exports through the existing `Mp3ClipExporter`
      unchanged, with sensible default tags the user can edit before
      export, same as any other clip.
- [ ] A mixed clip lands in the catalog like any other export and can be
      reopened afterward for further editing (segment-split, stem-isolate,
      etc.) as its own independent clip.
- [ ] All edge cases above are handled explicitly, not silently ignored.

## What shipped

- Added a dedicated `Mix two clips...` tray action and window. Each source
  can be loaded from the existing clip catalog or through the existing
  local audio import picker; already-decoded sources are cached within the
  window so selecting the same file again does not repeat the decode.
- Added independent 0-150% source volume controls, editable combined title
  and artist tags, and export through the existing cataloging
  `IClipExporter` path.
- Added the UI-agnostic `TwoSourceAudioMixer`: the longer source sets the
  exact output duration, the shorter source uses spec 009's existing
  crossfade loop primitive, sample sums clamp safely, same-source mixing is
  supported, cancellation is observed, and the existing five-minute
  effects cap produces a clear error.
- Mixed exports carry a reserved synthetic source identity; catalog re-trim
  recognizes that identity and decodes the saved MP3. This makes a mixed
  export reopen as its own independent editable source rather than
  incorrectly depending on either input's live buffer, without changing
  expired-buffer behavior for ordinary captures.
- Added mixer, view-model, same-source, tag-editing, limit, and WPF window
  initialization coverage. Full Debug and Release verification each pass
  179/179 tests with 0 warnings.

No deviations or known gaps within the acceptance criteria.

## Follow-up ideas

- The owner would prefer `Ctrl+Alt+H` to open a compact Hookline launcher
  window instead of a cursor-positioned tray menu. That is a separate UX
  decision because it changes every top-level action, not just mixing.
- Profile and plan the reported effect-control/preview latency separately,
  including stem-model warmup and clearer in-progress feedback for stem
  isolation. Stem separation itself is intentionally outside this spec.
