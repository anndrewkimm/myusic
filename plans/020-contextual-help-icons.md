---
status: READY
touches: [Hookline.App]
depends_on: [003, 009, 010, 011, 012, 013]
---

# 020 — Contextual help icons for effect controls

## Goal

Someone new to Hookline opens the trim window and sees a working set of
controls (Speed/Reverb/8D/Loop, a 10-band equalizer, four-to-six stem
volume sliders) with no explanation of what any of them do beyond their
label. Add a small, optional "what does this do" affordance next to each
control — not a tutorial, not a wizard, nothing that gets in the way of
someone who already knows what they're doing.

## Explicit non-goal

**This is not about simplifying or hiding controls.** Every slider, preset,
and checkbox that exists today stays exactly where it is, fully exposed,
with its current default behavior unchanged. This spec only adds an
optional, ignorable source of explanation next to what's already there.

## Resolved implementation decisions

- **A small (i) info icon next to each control's existing label.** Hovering
  it shows a one-to-two-sentence plain-English explanation in a standard
  WPF tooltip. Not a new panel, not a modal, not a first-run tutorial —
  purely inline and optional.
- **Also keyboard/screen-reader accessible, not hover-only.** The icon is
  focusable (reachable via Tab) and shows the same tooltip content on
  keyboard focus, plus sets `AutomationProperties.HelpText` to the same
  string. This is a small addition on top of the hover behavior, not a
  separate accessibility effort — same content, two more ways to reach it.
- **Purely additive**: default layout, default values, and existing keyboard
  shortcuts (spec 003's nudge/arrow behavior, etc.) are all unchanged. If
  implementing this would require moving or resizing any existing control
  to make room, that's a signal the icon placement was wrong, not a reason
  to touch the control itself.
- **Two controls already carry a different `AutomationProperties.HelpText`**
  today — the 8D rotation slider (`AppStrings.HeadphonesHint`) and the stem
  Band-view avatars (`AppStrings.StemBandHint`). "Purely additive" above
  means *visible layout/behavior* is unchanged; these two controls'
  `HelpText` value specifically is intended to be replaced by this spec's
  copy (screen readers only get one `HelpText` string per element, so this
  isn't optional), not left as a second, conflicting value alongside it.
- **Forward-compatible with spec 015** (per-segment effects): since spec 015
  reuses the exact same effect controls scoped to a segment instead of the
  whole selection, these help icons apply automatically to the same
  controls in a segment context with no special-casing needed — this is a
  control-level addition, not a selection-scope-level one.

## Copy (exact strings, so Codex isn't guessing at tone/content)

- **Speed** — "Changes playback speed without changing pitch. 1.00× is
  normal speed."
- **Reverb** — "Adds a sense of space, like the track is playing in a
  larger room. Off means dry, unprocessed audio."
- **8D rotation** — "Slowly pans the sound left-to-right in a circular
  motion for a 'moving around your head' effect. Best heard on
  headphones."
- **Loop** — "Repeats the selected audio to reach a longer length, instead
  of playing it once and stopping."
- **Equalizer (section-level)** — "Boosts or cuts specific frequency
  ranges — drag a band up to emphasize that pitch range, down to reduce
  it. Presets below are quick starting points; you can still fine-tune
  any band by hand afterward."
- **Equalizer character presets** (Flat/Bass boost/Treble boost/Vocal/
  Bright/Mellow) — "One-click starting points for the equalizer above.
  Picking one still lets you adjust individual bands afterward."
- **Edit style presets** (Slowed + Reverb/Sped Up/8D Audio) — "One-click
  combinations of the Speed/Reverb/8D controls below, tuned to a specific
  style. You can still adjust each one individually afterward."
- **Stem isolation (section-level)** — "Splits the track into separate
  parts — vocals, bass, drums, and everything else — so you can raise,
  lower, or mute each one independently."
- **Each stem volume slider** (Vocals/Bass/Drums/Other) — "0% removes this
  part entirely. 100% is its original volume. Above 100% makes it louder
  than the original mix."
- **6-stem checkbox** ("add Guitar + Piano") — "Also tries to separate
  Guitar and Piano into their own controls. Experimental — lower
  separation quality than the standard four parts."

Not an EQ-band-by-band tooltip (31/62/125.../16k): the frequency number is
already printed under each slider, and the section-level explanation above
covers what dragging a band does. A per-band tooltip would just repeat
"boosts/cuts around [frequency]" ten times with no new information.

## Edge cases

- Rapid mouse movement across several icons in a row shouldn't cause
  tooltip flicker or a perceptible delay — rely on the standard OS/WPF
  tooltip show/hide timing rather than a custom timer.
- Tooltip text must not get clipped or force horizontal scrolling in the
  trim window at its normal size — keep each string within the lengths
  above (WPF `ToolTip` wraps by default, but verify at implementation
  time against the window's actual width).
- The icon itself must not be reachable in a way that steals accidental
  clicks from the adjacent slider/checkbox it's labeling — small, clearly
  separate hit target.

## Acceptance criteria

- [ ] Every control listed in "Copy" above has an adjacent (i) icon showing
      that exact (or lightly edited for final UI fit) explanation on hover.
- [ ] Each icon is keyboard-focusable (Tab reaches it) and shows the same
      tooltip content on focus.
- [ ] Each icon sets `AutomationProperties.HelpText` to the same content
      for screen readers.
- [ ] No existing control's default visibility, layout, spacing, or
      behavior changes as a result of adding the icons.
- [ ] Icons appear correctly in both the Sliders and Band view (spec 012)
      stem-isolation layouts, not just one.

## Open questions

None blocking. Flipped straight to `READY` — the exact copy above can still
be tightened in review if the tone doesn't land, but there's no fork here
worth holding up implementation for.
