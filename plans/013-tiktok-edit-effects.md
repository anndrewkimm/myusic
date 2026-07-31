---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [009, 010]
---

# 013 — TikTok-edit effects: reverb, 8D auto-pan, and one-click edit presets

## Goal

Get Hookline's exported clips to the "edit audio" sound that's everywhere on TikTok — slowed + reverb, nightcore/"sped up," 8D audio — by adding the one genuinely missing piece (reverb) plus one new spatial effect (8D-style auto-pan), and wrapping both together with the existing speed control (spec 009) into one-click presets, the same "most people just tap a preset" pattern spec 010 already proved out for the EQ.

**This is less new work than it sounds like.** "Sped up"/nightcore edits are just faster playback with pitch rising along with it — spec 009's speed control already does exactly that (pitch-coupled resampling, 0.5x-2x range already spans a typical nightcore range). The two real gaps are **reverb** (already flagged as a deferred follow-up in spec 009) and a **continuous stereo auto-pan** ("8D audio" isn't literally spatial/binaural audio — see Research grounding — it's regular stereo panning automated in a slow circular motion, usually paired with reverb for depth). Both are simple, fast DSP that runs instantly on ordinary hardware, consistent with how 009/010 already feel — this is explicitly not another spec-011-weight-class feature.

## Research grounding (2026-07-28)

- **"8D audio" is not real spatial/binaural audio** — it's a stereo track with automated panning (engineers move the pan position in a circular/figure-eight path) combined with reverb "to make it seem like the sounds are moving around your head," and it's explicitly headphone-dependent since it relies on no crosstalk between ears. [Epidemic Sound — What is 8D audio, and how does it work?](https://www.epidemicsound.com/blog/what-is-8d-audio/), [Medium — 8D Audio: What is it and how does it work?](https://chizaraibeakanma.medium.com/8d-audio-what-is-it-and-how-does-it-work-826b5274fed)
- **"Slowed + reverb"** is a remix style descended from chopped-and-screwed/lo-fi that slows a track down and adds reverb for a "dreamy, almost underwater" sound. A commonly cited "classic" preset sets speed to **×0.80** with a moderate reverb amount; more extreme "aesthetic vibe" variants go as low as ×0.65 with heavier reverb. [Oreate AI — The Slowed and Reverb Effect: How TikTok Is Reshaping Audio Trends](https://www.oreateai.com/blog/the-slowed-and-reverb-effect-how-tiktok-is-reshaping-audio-trends/fe692ca930f69dcb1f94532931ab8a1e), [SoundTools — TikTok Slowed + Reverb Maker](https://soundtools.io/slowed-reverb-tiktok/)
- Neither trend has one universally "correct" parameter set — online tools vary in their exact defaults. This spec's preset values are reasonable, research-informed approximations (grounded in the ×0.80 "classic slowed + reverb" figure above), not a claim of matching any specific tool exactly — same posture spec 010 already took with its EQ preset curves.

## Codex handoff

- **Reverb**: check whether NAudio already ships something usable before building one; if not, a Schroeder/Freeverb-style algorithm (parallel comb filters + series allpass filters — the well-documented, public-domain Jezar Freeverb design) is the standard lightweight approach and doesn't require a new heavyweight package. Internal room-size/decay character is fixed and tuned toward the "spacious, dreamy" character the research above describes — only wet/dry amount is user-facing, matching 009's one-knob-per-effect restraint (bass boost got one knob, not a full compressor).
- **Stereo rotation ("8D")**: a sine-LFO-driven pan position, continuous and sample-smooth (no stepped/zipper-noise pan changes), one rotation-rate control. Runs across the *entire final assembled clip length* (after loop/extend, not before) so a looped clip rotates continuously through its full length rather than restarting the rotation phase at every loop repetition.
- **Processing order**: slice selection → speed change (009) → EQ cascade (010) → reverb (new) → loop/extend (009) → stereo rotation (new) → existing edge-fade-and-encode (unchanged). Reverb sits before loop/extend specifically so 009's existing click-free loop-crossfade already smooths over the reverb tail at the seam, rather than needing a second bespoke solution.
- **Extend the existing neutral fast-path** (`ClipEffectSettings.IsNeutral`) to cover the two new fields (reverb amount = 0, rotation rate = 0) exactly as spec 010 extended it for the EQ curve — leaving everything at default must keep returning the same object reference, not just equivalent bytes, per the guarantee both 009 and 010 already established structurally.
- **Presets set three controls at once**: the existing Speed slider plus the two new Reverb and Rotation controls. Manually adjusting *any* of those three after choosing a preset flips the UI to "Custom," reusing 010's exact preset/Custom interaction pattern rather than inventing a new one.
- Composes with spec 011's stem remix automatically, the same way spec 010's EQ already does — the remix feeds into this same processor, no special-casing needed anywhere in the export path.
- Reuse the existing ~5-minute effects sanity cap already established across specs 009/010/011 rather than introducing a new one. Confirm reverb/rotation processing stays effectively instant at that duration — this spec is explicitly in the "instant effects" weight class, not spec 011's background-job weight class; if the reverb algorithm can't stay comfortably within live-preview budget for up to 5 minutes of audio, flag that back rather than silently degrading preview responsiveness.

## Resolved implementation decisions

- **Reverb**: single wet/amount control, 0-100%, default 0 (off/dry). No exposed room-size, decay-time, or damping controls — one knob, matching what was actually asked for.
- **Stereo rotation (8D)**: single rotation-rate control, default 0 (off, normal stereo). UI copy notes the effect is designed for headphone listening, since that's inherent to how the effect actually works (no crosstalk between ears), not an app limitation.
- **Three built-in one-click presets**, each setting Speed + Reverb + Rotation together:
  - **Slowed + Reverb** — speed ≈0.80x, moderate-to-heavy reverb, no rotation.
  - **Sped Up** (nightcore-style) — speed ≈1.25-1.5x, reverb off, no rotation. This preset is a convenience wrapper around spec 009's existing speed control — confirm at implementation time whether it needs any new DSP at all (it likely doesn't).
  - **8D Audio** — rotation on at a slow, continuous rate, speed unchanged (1.0x), light reverb (per the research above, real 8D relies on reverb alongside panning for the "surrounding" illusion, not panning alone).
- **No preset selected is the default state** (exactly today's dry/neutral export), consistent with 009/010 defaulting to off/neutral.
- **Selecting a different preset fully replaces the prior Speed/Reverb/Rotation values** — no partial merge, same as 010's preset-switching behavior.

## User story

I've trimmed the clip I want to post. I tap "Slowed + Reverb" and it instantly gets that dreamy, underwater TikTok sound — I don't have to know what reverb even is. Or I tap "8D Audio" and, on headphones, it sounds like the song is circling around my head. Or I tap "Sped Up" for the nightcore thing. If I want to fine-tune from there, I can nudge the reverb or rotation manually and it just shows "Custom." Either way I hit Export and get the same tagged MP3 as always.

## Edge cases

- All new controls at default with no preset selected — export is byte-identical to today's (pre-spec-013) output, no regression.
- Reverb's natural decay tail must not be abruptly cut by the existing edge fade-out or by a loop seam — verify the current fade duration still sounds graceful with reverb active; extend it if the existing constant was tuned for dry audio only.
- 100% reverb wet amount — must not clip/overflow; same sample-clamping discipline every prior effect in this app already follows.
- Fastest and slowest rotation rate settings — pan changes must stay smooth (sample-interpolated), never a stepped/zippering artifact.
- Selecting a preset, then manually adjusting speed, reverb, or rotation — correctly shows "Custom," doesn't keep claiming to be the old preset.
- Switching presets after manual adjustments — full replace across all three controls, no partial-merge confusion.
- Composes correctly with existing EQ (010) and loop/extend (009) in the stated order, and with spec 011's stem remix when stems have been isolated — no special-casing, no interference.
- A selection already at the existing ~5-minute effects cap — new effects don't expand it further.
- Preview reflects all current settings (including active preset/Custom state) live, same as existing effects already do.

## Acceptance criteria

- [x] A reverb effect (single 0-100% wet/amount control, default off) is available, audibly adds spaciousness, and never crashes or overflows at 100%.
- [x] A stereo "8D" rotation effect (single rotation-rate control, default off) is available, audibly rotates the sound between channels using smooth, non-zippering pan interpolation, and runs continuously across the full final clip length rather than resetting per loop repetition.
- [x] At least three one-click presets exist — "Slowed + Reverb," "Sped Up," and "8D Audio" — each setting Speed/Reverb/Rotation together to a distinct, recognizable combination.
- [x] Manually adjusting Speed, Reverb, or Rotation after choosing a preset correctly switches the UI to a "Custom" state.
- [x] All controls left at default (no preset selected) produce a byte-identical export to today's pre-spec-013 behavior.
- [x] Reverb composes correctly with the existing loop/extend crossfade and edge fade-out — no abrupt cut of the reverb tail.
- [x] Extreme settings (100% reverb, fastest/slowest rotation, combined with existing extreme speed/EQ/loop settings) export successfully without crashing, overflow, or audible zipper noise.
- [x] Composes correctly with spec 011's stem remix with no special-casing in the export path.
- [x] Preview reflects all current settings, including active preset/Custom state, live.
- [x] Non-UI DSP logic (the reverb algorithm, pan-rotation math, preset values, Custom-state detection) is covered by unit tests independent of the UI thread.

## Open questions

- Resolved during implementation: the reverb uses a fixed four-comb/two-allpass Schroeder topology with 0.82 feedback, 0.25 damping, stereo delay spread, and a two-second decay tail. Its filter network runs at a bounded internal rate with averaged input and linearly interpolated full-rate output to keep five-minute previews responsive.
- Resolved during implementation: rotation is adjustable from 0.05-0.25 Hz (20-4 seconds per cycle); the 8D preset uses 0.1 Hz (10 seconds per cycle).
- Resolved during implementation: "Sped Up" is purely a one-click wrapper around the existing pitch-coupled speed DSP; no duplicate DSP path was added.

## Follow-up ideas

- Independent pitch-shift as an extra fine-tune on top of "Slowed + Reverb" (some tools drop pitch a few semitones independently of speed) — spec 009 already flagged independent pitch-shift as meaningfully harder DSP and deliberately out of scope; this spec doesn't change that.
- A distinct delay/echo effect (as opposed to reverb) — spec 009's original follow-up list bundled "Reverb/echo" together; this spec resolves reverb only, echo/delay as its own repeat-based effect remains a separate, still-deferred idea.
- User-savable custom edit-style presets beyond the three built in here — mirrors spec 010's own deferred "user-savable custom presets" idea.
- A broader creative-effects rack: high-pass/low-pass filters, chorus, flanger, phaser, distortion/saturation, bit-crushing/lo-fi, tape or vinyl character, stereo widening, reverse, stutter/beat-repeat, and section-level effect automation. These should be prioritized and specified rather than added as an unstructured wall of controls.
- Expand EQ/mastering presets beyond today's Bass Boost with distinct goals such as Sub Bass, Punch, Vocal Clarity, Bright/Air, Warm, Lo-fi, Club, and Car. Any stronger bass/loudness presets should pair gain compensation with a limiter so "more impact" does not mean clipping.
- A guided "Remix Styles" system, including a Hardstyle recipe. A credible Pop → Hardstyle conversion is not an EQ/effects preset: it needs BPM/beat/key analysis, tempo mapping, stem-aware arrangement, hardstyle kick/bass/drum material, builds/drops/transitions, and final limiting. Decide in a separate spec whether the new musical material comes from bundled/licensed assets, user-supplied samples, or a generative service; each choice has different quality, offline, licensing, and cost tradeoffs.
- Smaller deterministic style recipes can ship before full genre conversion — for example Nightcore, Chopped & Screwed, Lo-fi/Vinyl, Club Boost, Dreamy, and Phone/Radio — by composing the existing speed/EQ/reverb/rotation controls with future pitch, filter, delay, saturation, and dynamics effects.
- Add audition-safe A/B comparison, output-gain matching, and an overload/limiter indicator before the effects catalog grows. Presets otherwise tend to sound "better" only because they are louder, and stacked effects can clip.

## What shipped

- Added a clamped 0-100% wet Schroeder reverb with a natural two-second tail. NAudio 2.3.0 exposes no suitable reverb effect in its shipped API/docs, so this uses the lightweight no-new-dependency fallback anticipated by the spec.
- Added sample-smooth full-clip stereo rotation from 0.05-0.25 Hz. The LFO advances once across the final loop-assembled result, progressively routes both stereo channels toward the moving side, normalizes the combined channel, and clamps final PCM samples.
- Added Slowed + Reverb (`0.80x`, 55% wet, rotation off), Sped Up (`1.35x`, dry, rotation off), and 8D Audio (`1.00x`, 20% wet, 0.1 Hz) presets. Switching presets replaces all three values; any manual Speed/Reverb/Rotation change moves the visible state to Custom.
- Extended the trim UI with the preset row and live controls, including visible headphone guidance for 8D. Preview and export continue to consume the exact same processed snapshot.
- Preserved the neutral same-object fast path and the stated processing order: stem remix → speed → EQ → reverb → loop/crossfade → rotation → existing exporter edge fade/encode. No stem-specific export path was added.
- Added independent tests for preset values and Custom transitions, full-wet stereo decay/clamping, reverb-before-loop ordering, loop-phase continuity, slowest/fastest rotation smoothness, five-minute preview performance, extreme combined MP3 export, stem composition, and preview/export parity.
- Closed the exporter edge-fade review gap with a direct regression test: a full-scale, full-wet impulse produces a real reverb tail, naturally decays to at most 0.1% of its peak before the exporter's final 15 ms fade, preserves every preceding sample, and ends at zero without an audible truncation. The existing fade duration and neutral export behavior remain unchanged.
- Validation: all 133 tests pass in Debug and Release (24 NowPlaying, 50 Audio, 59 App); solution formatting and `git diff --check` are clean. The five-minute stereo reverb sanity test measured about 1.14 seconds in Debug on this machine.

## Review notes

9 of 10 acceptance criteria verified directly against code/tests (not just this section's narrative) and hold up: reverb clamping and topology at `SchroederReverb.cs`, rotation continuity across the full assembled clip (no per-loop phase reset), the three presets' exact values, Custom-state transitions, and the reference-identical neutral fast path all check out.

**Gap**: the "Reverb composes correctly with the existing loop/extend crossfade and edge fade-out — no abrupt cut of the reverb tail" criterion, and this spec's own edge case ("verify the current fade duration still sounds graceful with reverb active; extend it if the existing constant was tuned for dry audio only"), were never actually checked. `Mp3ClipExporter.cs` has zero diff — `FadeDuration` is still the pre-existing 15ms — and no test exercises the exporter-level edge fade against a full-wet reverb tail. Ordering (reverb-before-loop-assembly) is verified and correct, but that's a different claim than "the fade doesn't audibly truncate the tail." Given the comb feedback (0.82) implies roughly a 1s RT60 well inside the 2s tail, this will likely turn out fine — but it needs an actual check (either a test asserting the tail isn't truncated by the 15ms fade, or a widened fade constant with reasoning), not an assumption written up as resolved.

Everything else here is solid; this is a small, mechanical fix-or-verify, not a design problem. Please close this specific gap and flip back to REVIEW.

**Resolution (2026-07-29)**: added an exporter-level regression test using the full-wet reverb impulse response. It verifies that the two-second tail contains real signal, has naturally fallen to at most 0.1% of peak by the final 15 ms fade, remains byte-identical before that fade, and reaches zero smoothly at the edge. Debug and isolated Release runs both pass all 133 tests. No fade widening was needed, so neutral exports retain the established 15 ms behavior.

**Re-reviewed 2026-07-29**: confirmed `FullWetReverbDecaysBeforeExporterEdgeFade` (`Mp3ClipExporterTests.cs:8`) is a genuine regression test, not a tautology — it independently checks the tail has real signal past the source clip, decays below peak/10 before the fade window, stays near-silent (≤peak/1000) within the fade region, and leaves everything before that byte-identical. Independently ran `dotnet test` on Hookline.Audio.Tests in Release: 50/50 pass. Gap closed. All 10 acceptance criteria now verified. Status: DONE.
