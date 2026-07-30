---
status: REVIEW
touches: [Hookline.App]
depends_on: [011]
---

# 012 — Playful "band view" for stem remixing

## Goal

Make spec 011's stem remixer (Vocals/Bass/Drums/Other, each a 0-150% volume control) approachable to someone who's never touched a mixer, by offering a second, optional way to set the exact same values: instead of four labeled percentage sliders, each active stem is represented as a simple character you pull forward to bring up in the mix or push back to fade out.

**Why this scope and not more:** the reference point for this idea (drag a character in to add a sound to a beat, à la Incredibox) is a *compositional* tool — you build up from silence out of a library of loops. Hookline is *subtractive* — the stems already exist in a song you captured; there's no library to browse and nothing to "add" that isn't already there. So this spec is deliberately **not** a sound-library/compose-from-scratch mode. It's a friendlier skin on a feature that already works, not a new instrument. That's a real, low-cost usability improvement (a beginner reads "pull the singer forward" faster than they parse a percentage slider labeled "Vocals: 100%") as long as it stays optional and doesn't slow down or clutter the existing golden path — same discipline spec 011 already used to keep stem isolation from leaking weight into the fast common trim/export flow, and the same discipline spec 010 used to keep the full EQ collapsed by default.

## Codex handoff

- This is a **view-layer feature only**. It touches `Hookline.App`; it must not touch `Hookline.Audio`. `StemVolumeViewModel` (from spec 011) is the single source of truth — Band view and the existing Sliders view are two renderings bound to the exact same collection of values, not two separate data models that need to stay in sync.
- No external art assets. Characters/avatars must be simple shapes drawn procedurally in WPF (`Path`/`DrawingContext`, the same custom-rendering approach `WaveformControl` already uses), styled distinctly per stem via color/silhouette — not sourced images. This avoids both a licensing question and an asset pipeline this repo doesn't have.
- Sliders view stays the default, unchanged experience. Band view is opt-in via a toggle, mirroring how spec 010 keeps the full EQ collapsed behind presets by default — this spec adds an alternative, it does not change default behavior for the existing user.

## Resolved implementation decisions

- **A "Sliders / Band" toggle** inside the existing stem panel from spec 011, shown only once stems have actually been separated (same gating spec 011's volume controls already use) — nothing new appears before that point.
- **Each active stem (4 in default mode, 6 if 6-stem mode is active) gets one avatar.** Dragging it along a single continuous axis maps monotonically to that stem's existing 0-150% gain — same range, same 100% "natural" default position, same 0% "muted" endpoint — as the Sliders view. This is a different view of `StemVolumeViewModel`'s existing property, not a new value.
- **Plain language only in Band view** — no "gain," "dB," or raw percentage as the primary label. A short word/phrase (e.g. quiet/normal/loud) or the plain stem name is fine; a numeric readout may exist as a secondary/small detail but must not be the primary way the value is communicated.
- **6-stem mode's existing experimental/lower-quality callout (from spec 011) must remain visible in Band view** when 6-stem mode is active — switching views must not hide a warning the user already saw in Sliders view.
- **Switching between the two views at any time preserves values exactly** — no reset, no rounding drift, since both are bound to the same underlying values.
- **No new export path.** Whatever values Band view leaves the stems at feed the exact same remix/export pipeline spec 011 already built, unchanged.

## User story

I've isolated the stems on a clip and I want to hear it with the vocals a bit quieter, but I don't really think in percentages or "gain." I flip to Band view and see four simple characters lined up. I drag the singer back a little and the bassist forward a little, hear it change live, and export it — same tagged MP3 as always. If I later want to be precise instead of playful, I can flip back to Sliders and see (and set) the exact same values as numbers.

## Edge cases

- 6-stem mode active — Band view scales to 6 avatars without broken layout, and the guitar/piano quality warning stays visible.
- Rapid back-to-back drags — live preview stays responsive; no worse than the existing Sliders view's own live-preview refresh behavior under fast slider movement.
- Switching views mid-adjustment — the value in progress carries over exactly, whichever view you land on.
- Narrow/small trim window — avatars remain distinguishable and usable at the same minimum window size the existing scroll-safe stem panel already supports, no overlap/clipping.
- Stems not yet separated — no Sliders/Band toggle is shown at all, same as today's gating on the Sliders view itself.
- Keyboard-only use — not required to be solved by making drag itself keyboard-operable; the existing Sliders view is the fully keyboard-accessible path to the same values, so no one is left stuck.

## Acceptance criteria

- [x] A toggle exists between the existing Sliders view (spec 011, unchanged, still the default) and a new Band view, both bound to the same underlying stem volume values.
- [x] Band view represents each currently-active stem as a simple, procedurally-drawn avatar (no external art assets) using plain, non-technical language as the primary label.
- [x] Dragging an avatar continuously adjusts that stem's volume across the same 0-150% range as the Sliders view, with the same 100% default position.
- [x] Switching between Sliders and Band view at any point preserves the exact current values — no reset, no rounding drift.
- [x] 6-stem mode's existing experimental/lower-quality callout remains visible in Band view when 6-stem mode is active.
- [x] Band view is opt-in; Sliders view remains what's shown by default — no change to today's default experience.
- [x] Live preview and export behave identically regardless of which view was used to set the values — no new special-casing in the export path.
- [x] The drag-position-to-gain mapping (and its inverse) is covered by unit tests independent of the UI thread and independent of any WPF rendering.

## Open questions

- Exact drag gesture/axis (forward-back vs. up-down vs. a track-with-character-glyph) — implementer's call, but it must be one continuous drag that maps monotonically to gain, not a click-to-cycle set of discrete states.
- Exact avatar shapes/colors per stem — implementer's call: simple, abstract, clearly distinguishable per stem, consistent with the app's existing dark theme (spec 003).
- Whether to show a small numeric/relative-word readout alongside each avatar — implementer's call, per the "plain language primary, numeric secondary at most" rule above.

- Resolved during implementation: Band view uses a vertical stage-depth lane. The back/top endpoint is 0% (quiet), the forward/bottom endpoint is 150% (loud), and a dashed position marks the natural 100% mix.
- Resolved during implementation: each stem uses a distinct color and small procedural silhouette/prop (microphone, bass, drum, abstract diamond, guitar, or keyboard), drawn entirely by `StemBandAvatarControl`.
- Resolved during implementation: the stem name is the primary label and a small Muted/Quiet/Natural/Loud readout is secondary. Band view deliberately shows no percentage; switching to Sliders remains the precision path.

## Follow-up ideas

- A sound-library/compose-from-scratch mode (the actual Incredibox model) — a fundamentally different, much bigger feature requiring a loop library and licensing considerations Hookline doesn't have today. Deliberately out of scope here; revisit only as its own, separately-justified idea.
- Short plain-language "what is this stem" tooltips per character (e.g. explaining what "Other" typically contains) — a real teaching-layer nice-to-have, deferred.
- Subtle audio-reactive animation (avatars bouncing with the beat) — pure polish, not required for the core interaction.
- A separate faster-than-real-time ingestion spec: the existing local MP3/WAV import already makes a complete owned file immediately trimmable, while Spotify's current Web API exposes metadata/control rather than downloadable full-track audio and explicitly prohibits facilitating Spotify-content downloads. A pasted Spotify link therefore cannot legitimately replace real-time output capture; investigate a clearer local-file-first workflow and link-assisted metadata/playback seeking without turning Hookline into a downloader.

## What shipped

- Added a gated Sliders/Band toggle to the completed-stems panel. Sliders remains the default; the toggle and both views remain hidden until separation succeeds.
- Added six distinct procedurally-rendered avatars with a continuous vertical 0-150% drag lane and a visible natural-position marker. The four- and six-stem layouts use the same scroll-safe uniform grid, and the experimental Guitar/Piano warning remains outside the view switch so it is never hidden.
- Both views bind directly to the same `StemVolumeViewModel` objects. View switching changes no audio state, and preview/export continue through the existing shared stem-remix pipeline with no Band-specific processing or export branch.
- Added a UI-thread-independent `StemBandPositionMapper` plus endpoint, clamping, monotonic mapping, and exact round-trip tests. Added view-model coverage proving exact values and object identities survive both view switches and that Band-selected preview/export use the same output.
- Live Release verification used the actual Ctrl+Alt+H window, a real 1.2-second buffered selection, and the installed six-stem model. All six avatars rendered without overlap at the existing 880x700/minimum-size scroll layout; the experimental warning stayed visible. Dragging Vocals to Loud produced 150% in Sliders and remained Loud after switching back to Band.
- Validation is clean in Debug and Release: 132 tests pass (24 NowPlaying, 49 Audio, 59 App), builds have zero warnings, `dotnet format --verify-no-changes` passes, and `git diff --check` is clean. No acceptance-criteria deviations or known gaps.
