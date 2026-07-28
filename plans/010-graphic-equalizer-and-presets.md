---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [009]
---

# 010 — Graphic equalizer with one-click presets

## Goal

Give the user real tone-shaping freedom on top of spec 009's speed/loop effects: a proper multi-band graphic equalizer, plus one-click presets (Bass Boost, Treble Boost, Vocal, etc.) that set the whole curve at once — the same idea as Spotify's or Sony Headphones Connect's equalizer, where most people just tap a preset and move on, but the individual bands are still there to fine-tune for anyone who wants to.

## Research grounding (reviewer, 2026-07-27)

- Standard graphic EQs use 10 bands at ISO center frequencies an octave apart: **31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz**, typically ±12dB per band. This is the conventional layout across consumer and pro audio software alike (iTunes' EQ among them). [Methodshop — iTunes Equalizer Settings](https://methodshop.com/best-itunes-equalizer-settings/), [Diamond Cut forum — 10-band graphic EQ](https://www.diamondcut.com/vforum/forum/general-discussion/general-audio/55908-10-band-graphic-equalizer)
- Sony's Headphones Connect app is a good real reference for consumer preset naming: **Bright, Excited, Mellow, Relaxed, Vocal, Treble Boost, Bass Boost, Speech**, plus **Heavy** (stronger bass) and **Clear** (sharper treble), alongside a manual/custom band editor. [Sony — Tuning sound with the Equalizer](https://www.sony.co.uk/electronics/support/articles/00286844), [Tom's Guide — WH-1000XM4 EQ](https://www.tomsguide.com/opinion/sonys-wh-1000xm4-headphones-are-great-heres-how-i-made-them-sound-even-better)
- Neither Sony nor Spotify publish the exact per-band dB values behind their preset names — those curves are proprietary tuning, not public numbers. This spec's presets are original, reasonable approximations of what each name implies (e.g., "Bass Boost" lifts the bottom two bands, "Vocal" lifts the 1-2kHz presence range while gently cutting sub-bass), not a claim of matching any specific product's exact curve.

## Codex handoff

- This **replaces** spec 009's single `BassBoostDecibels` knob rather than sitting alongside it — once a full EQ exists (with a "Bass Boost" preset covering the same job), keeping both would just be two overlapping ways to do the same thing. `SpeedMultiplier` and `LoopCount` are unaffected and stay exactly as they are.
- Verify `NAudio.Dsp.BiQuadFilter`'s peaking-EQ constructor (`PeakingEQ`) at implementation time — spec 009 already leaned on this class for its shelf filter, so the dependency is proven in this codebase; confirm the exact per-band cascade (one filter instance per band, applied in series per channel) behaves as expected before committing to it.
- Preserve spec 009's core guarantees: an all-zero/Flat EQ combined with neutral speed/loop must still produce a byte-identical export to having no effects at all (same `IsNeutral`-style fast-path), and preview/export must keep sharing the exact same processing call.

## Resolved implementation decisions

- **10 bands, ISO standard center frequencies** (31Hz-16kHz as listed above), each adjustable **-12dB to +12dB**, implemented as a cascade of peaking-EQ biquad filters per channel.
- **Presets set all 10 bands at once**: at minimum **Flat** (reset/all-zero), **Bass Boost**, **Treble Boost**, **Vocal**, **Bright**, **Mellow** — using Sony's naming as the reference set since it's a real, recognizable precedent. Exact curve values are this spec's own reasonable approximation (see "Research grounding") — not a claim of matching any specific product.
- **Selecting a preset sets the sliders; manually moving any individual band afterward switches the UI into a "Custom" state** (no preset shown as active) without resetting anything else — same behavior pattern as real EQ apps.
- **Collapsed by default.** The 10-band manual view is a lot of UI for a window whose whole premise (spec 003) is staying small and fast to dismiss — show just the preset buttons (plus a "Flat" reset) by default, with an expand action to reveal the individual band sliders for anyone who wants to fine-tune. Most users should never need to open it.
- **Not persisted across trim sessions** — resets to Flat every time a new trim window opens, same as spec 009's speed/loop controls. Consistent, and avoids a "why is my bass still boosted, I didn't touch anything" surprise.
- **Processing order**: slice selection → speed change → EQ cascade → loop/extend → existing edge-fade-and-encode (unchanged) — same position spec 009's bass boost occupied, since it's still fundamentally a tone-shaping step before any looping.

## User story

I've got my clip trimmed. I tap "Bass Boost" and it just sounds like I wanted — thumpy, warmer — without me knowing or caring what a biquad filter is. If I want to get particular, I can open the full equalizer and nudge individual bands myself, but I never have to.

## Edge cases

- All bands at 0dB (Flat, the default) — export identical to spec 009's neutral-effects export, no regression.
- Extreme boosts across many/all bands simultaneously — must never crash or overflow; same sample-clamping discipline as spec 009's bass-boost filter applies per band here too.
- Selecting a preset, then manually adjusting one band — correctly shows "Custom," doesn't silently keep claiming to be that preset.
- Selecting a different preset after manual adjustments — fully replaces the manual values, no partial-merge confusion.
- EQ combined with speed change and/or looping — all three compose correctly in the stated order; changing one doesn't reset the others.
- Preview reflects current EQ settings the same way it already does for speed/bass/loop today.

## Acceptance criteria

- [x] A 10-band graphic EQ (ISO standard frequencies, ±12dB per band) is available in the trim window, collapsed by default behind one-click presets.
- [x] At least Flat, Bass Boost, Treble Boost, Vocal, Bright, and Mellow presets exist and each sets a distinct, sensible curve across all 10 bands.
- [x] Manually adjusting any band after choosing a preset is possible and correctly reflects a "Custom" state rather than still showing the old preset as active.
- [x] Flat EQ (combined with neutral speed/loop) produces a byte-identical export to having no effects at all.
- [x] Extreme multi-band boosts export successfully without crashing or overflowing.
- [x] EQ composes correctly with spec 009's speed and loop effects in the stated processing order.
- [x] Preview reflects current EQ settings live, consistent with spec 009's existing preview behavior.
- [x] Non-UI logic (the filter cascade itself, each preset's curve values, custom-state detection) is covered by unit tests independent of the UI thread.

## Open questions

- Resolved during implementation: the exact baseline curves are documented in "What shipped" below.
- Whether to offer additional genre-style presets (Rock, Pop, Acoustic, Electronic, etc., as Spotify's own EQ does) beyond the Sony-style character presets listed here — nice-to-have, not required for this spec.

## Follow-up ideas

- User-savable custom presets (name and store your own curve for reuse across sessions).
- Genre-style presets (Rock/Pop/Acoustic/Electronic/Jazz) in addition to the character-style presets built here.

## What shipped

- Replaced the single bass shelf with an immutable 10-band curve at 31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, and 16k Hz. Each active band uses NAudio's peaking EQ with Q=1.4, cascaded independently per channel, with per-band clamping.
- Added a collapsed-by-default preset row and an expandable conventional vertical-slider EQ. Selecting a preset replaces all band values; moving a band changes the visible state to Custom without changing speed or loop.
- Chosen curves, in frequency order above:
  - Flat: `[0, 0, 0, 0, 0, 0, 0, 0, 0, 0]`
  - Bass Boost: `[+6, +6, +4, +2, 0, 0, 0, 0, 0, 0]`
  - Treble Boost: `[0, 0, 0, 0, 0, 0, +2, +4, +6, +6]`
  - Vocal: `[-4, -3, -2, 0, +2, +4, +5, +3, 0, -1]`
  - Bright: `[-1, 0, 0, 0, +1, +2, +3, +4, +5, +4]`
  - Mellow: `[+2, +2, +1, +1, 0, 0, -1, -3, -5, -6]`
- Preserved the neutral fast path and the shared preview/export processor. Processing order is slice, speed, EQ, loop, then the existing exporter fade/encode.
- Added independent coverage for every preset curve, band/range validation, Custom transitions, per-channel cascading and clamping, live preview refresh, composition, and an extreme all-band MP3 export.
- Validation: all 92 tests pass in Debug and in an isolated Release build. No known acceptance-criteria gaps.

## Review notes (reviewer, 2026-07-27)

- Rebuilt and retested independently: 0 warnings/errors, 92/92 passing (24 NowPlaying.Tests + 34 Audio.Tests + 34 App.Tests).
- Read `ClipEffectsProcessor.cs`'s `ApplyEqualizer`: only bands with nonzero gain get a cascaded `BiQuadFilter.PeakingEQ` instance (flat bands are skipped entirely, not run as no-op filters), and — a genuinely good detail — the signal is clamped to valid range *between each cascaded filter stage*, not just once at the end. That matters: an intermediate overflow partway through a multi-band cascade could otherwise skew a later filter's internal state in a way that a single end-of-chain clamp wouldn't catch.
- Confirmed `ClipEffectSettings.IsNeutral` now checks `EqualizerCurve.IsFlat` (an immutable, validated value type with real `Equals`/`GetHashCode`) in place of the old scalar bass-boost check — the neutral fast-path guarantee from spec 009 carries forward correctly.
- Cross-checked all five preset curves in `EqualizerPresetCatalog.cs` against the exact values documented in "What shipped" — they match precisely.

Clean. Flipping to DONE.
