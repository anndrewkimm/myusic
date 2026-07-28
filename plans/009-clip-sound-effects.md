---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [003]
---

# 009 — Clip sound effects: speed, bass boost, loop-extend

## Goal

Add lightweight, "song-edit" style effects to a trim selection before export — the kind of thing people mean by a TikTok-style edit: sped up or slowed down, bass boosted, or a favorite bit looped to last longer. This is deliberately **not** stem separation (isolating/removing individual instruments) — that was considered and explicitly deferred (see `plans/000-roadmap.md` Phase 3) as a much heavier, slower, ML-dependent feature that doesn't match what was actually being asked for. Everything here is simple, fast digital signal processing that runs instantly on ordinary hardware, consistent with how the rest of Hookline already feels.

## Codex handoff

- Apply the same adapter pattern spec 008 used: process the sliced selection into a new plain PCM buffer, then feed that through the *existing*, unmodified `AudioPreviewPlayer`/`Mp3ClipExporter` pipeline. If you find yourself changing those two classes' core behavior (as opposed to what's handed to them), reconsider the approach.
- All three effects are independent and composable (any combination, including none). Order of operations: slice selection → speed change → bass boost → loop/extend → existing edge-fade-and-encode step (unchanged). Fades must wrap the *whole assembled result* once, not each loop repetition.
- Leaving every effect at its default/off value must produce byte-for-byte the same export as spec 003 already does today — this spec adds capability, it must not regress the existing golden path.

## Resolved implementation decisions

- **Speed change** is implemented as simple resampling (the classic "play it back at a different rate" trick) — pitch shifts naturally along with speed, exactly like real "sped up"/"slowed down" edits. This is **not** independent pitch-shifting (changing pitch without changing speed) — that's a meaningfully harder DSP problem (phase vocoder / granular synthesis) and is explicitly out of scope here (see Follow-up ideas).
- **Bass boost** is a single low-shelf filter (NAudio already ships a usable biquad filter — `NAudio.Dsp.BiQuadFilter` — no new package dependency needed) with one boost-amount control. No multi-band EQ, no separate treble/mid controls — just the one "make the low end louder" knob that matches what was actually asked for.
- **Loop/extend** seamlessly repeats the (speed/bass-adjusted) selected audio to reach a requested length — either a repeat count or an approximate target duration, implementer's call on which control feels more natural. The existing single fade-in/fade-out wraps the whole final assembled buffer once; loop boundaries themselves must be seamless (no click/gap between repetitions).
- **All three controls live directly in the existing trim window** (spec 003), not a separate editor window — a small effects row near Preview/Export. All default to off/neutral; touching none of them changes nothing about today's export.
- **Preview always reflects current effect settings** and recomputes live as sliders move — these are cheap operations on at most a few minutes of audio, no explicit "apply" button needed, consistent with the rest of the app's instant-feedback feel.
- **A stated sanity cap on total exported/looped duration** (implementer's call on the exact number, but something like 15 minutes is a reasonable ceiling) so an extreme loop-count request can't produce an unreasonably large export or excessive processing time.

## User story

I've trimmed the part I like. Now I want it to hit different — maybe slow it down a little, maybe boost the bass, maybe just make that one bar loop for twice as long before it plays out. I drag a couple of sliders, hear the change instantly in Preview, and hit Export exactly like before — same file, same tags, same collision-safe naming, just processed the way I wanted first.

## Edge cases

- Extreme speed values (very slow or very fast) still export successfully — sound as expected at the extreme, never crash.
- Looping a very short selection toward a long target duration is bounded by the stated sanity cap rather than growing unbounded.
- Heavy bass boost on already-loud audio can sound distorted (expected, same as any real bass-boost tool) but must never crash or overflow — existing sample-clamping discipline (already used in the edge-fade code) applies here too.
- Changing the trim selection (dragging edges) after setting effects — effects reapply to whatever the current selection is, same as today's live-preview behavior for selection changes.
- A selection that overlaps an excluded/ad-flagged region — that warning is computed independently of these effects and must keep working exactly as spec 003 already handles it.
- All effects left at default — export is unchanged from today, byte for byte.

## Acceptance criteria

- [x] An effects row exists in the trim window: speed, bass boost, and loop/extend controls, all optional and off/neutral by default.
- [x] Leaving all three at default produces an export identical in behavior to spec 003's existing export (no regression).
- [x] Adjusting speed audibly changes both pitch and duration in preview and in the exported file.
- [x] Adjusting bass boost audibly boosts low end without crashing or byte-overflowing.
- [x] Loop/extend seamlessly repeats the selection to the requested length with exactly one fade-in and one fade-out around the whole result, no per-repetition clicks or gaps.
- [x] Preview reflects all current effect settings before the user commits to Export.
- [x] Extreme parameter values (very slow/fast speed, max bass boost, long loop targets) all export successfully, bounded by a stated sanity cap on total duration.
- [x] Non-UI effect-processing logic (resampling for speed, the bass-boost filter, loop assembly) is covered by unit tests independent of the UI thread.

## Open questions

- Resolved during implementation: loop/extend uses a 1–64 repeat-count control. Effects may expand a selection up to 5 minutes; a source selection already longer than 5 minutes remains intact but cannot be expanded further.

## Follow-up ideas

- Independent pitch-shift (change pitch without changing speed) — meaningfully harder DSP than what's built here (needs a phase-vocoder or similar approach), deliberately deferred.
- Reverb/echo.
- Real ML-based stem separation (isolating vocals/drums/bass/other) — already flagged in `plans/000-roadmap.md` Phase 3 as a much heavier, separate undertaking; revisit only if there's real demand for it specifically, distinct from the song-edit effects built here.

## What shipped

- Added a composable PCM effects adapter that applies pitch-coupled speed resampling, a clamped NAudio low-shelf bass boost, and click-resistant loop assembly in the specified order.
- Added neutral-by-default Speed, Bass boost, and Loop controls to the existing trim window. An active preview restarts with freshly processed audio whenever a control changes, and Export receives the same processed snapshot.
- Loop joins use a short crossfade while the existing exporter remains unchanged and applies its edge fade once around the complete assembled result.
- Added independent DSP tests for neutral passthrough, speed, bass response and channel isolation, loop joins, effect order, duration bounding, live preview refresh, preview/export parity, and an extreme-settings MP3 export.
- Validation: all 85 tests pass in Debug and in an isolated Release build. No known acceptance-criteria gaps.

## Review notes (reviewer, 2026-07-27)

- Rebuilt and retested independently: 0 warnings/errors, 85/85 passing (24 NowPlaying.Tests + 29 Audio.Tests + 32 App.Tests).
- Read `ClipEffectsProcessor.cs` in full: the neutral-defaults guarantee is airtight — `Process` returns the exact same `source` object reference (not a re-encoded copy) whenever `settings.IsNeutral`, so "no regression when untouched" isn't just tested, it's structurally guaranteed. Speed change is straightforward linear-interpolation resampling (pitch-coupled, as specified). Bass boost uses NAudio's own `BiQuadFilter.LowShelf` with output clamped through the existing `WriteSample` clamp path, so extreme boost can't overflow. Loop assembly cross-fades a short (5ms) window between repetitions specifically to avoid the click/gap the spec called out. All three are bounded by both a duration cap (5 minutes) and an `Array.MaxLength`-derived hard ceiling, so no combination of extreme settings can blow up memory.
- Read `TrimViewModel.cs`'s integration: preview and export both call the same `CreateSelectionSnapshot`, which runs the slice through `ClipEffectsProcessor.Process` — there's structurally no way for preview and export to disagree, since they're the same code path.
- Confirmed slider bounds match the processor's own declared min/max constants (speed 0.5-2x, bass 0-18dB, loop 1-64), so the UI can't hand the processor an out-of-range value that would throw.

Clean. Flipping to DONE.
