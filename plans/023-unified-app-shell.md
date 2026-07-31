---
status: DRAFT
touches: [Hookline.App]
depends_on: [003, 004, 014, 018, 019, 022]
---

# 023 — One Hookline workspace instead of five separate windows

## Goal

Direct owner feedback (captured in `plans/019-mix-two-clips.md`'s "Open
questions" section after trying the shipped Mix feature) rejected two
things about how Hookline is put together today, not just how mixing
works specifically:

1. `Ctrl+Alt+H` opens a tray action menu that spawns a separate,
   independent window per action (capture, import, mix, library) —
   the owner wants one persistent application shell with views/tabs
   instead, that preserves work as you navigate between them.
2. Mixing two clips only exposes volume control per source, not the
   full editing toolset (EQ, effects, stems) independently per source —
   which the owner correctly pointed out contradicts the spirit of every
   other part of the app.

Item 2 is really a symptom of item 1: there's no shared "editor" concept
today, so Mix had to be built as its own separate, thinner window instead
of reusing the real editor. This spec addresses the root cause.

## Why this is a bigger decision than any single feature spec

This is not a Mix-window fix. It touches window-management code shared by
every entry point in the app. Current state, checked directly in
`src/Hookline.App/App.xaml.cs` before writing this spec (not assumed):
Hookline manages **five separate window concerns independently** —
`_trimWindowSlot`, `_catalogWindowSlot`, `_urlImportWindowSlot`,
`_mixWindowSlot` (each a `ManagedWindowSlot<T>`, spec 014's fix), plus
`_importWindowSlots` (a `Dictionary<long, ManagedWindowSlot<TrimWindow>>`,
one per imported track) and `ClipRetrimLauncher`'s own separate
per-catalog-entry window dictionary. A real shell means consolidating
this into one coherent navigation/session model — a genuine
rearchitecture of how the app manages windows, not a UI skin change.
Sizing this honestly up front so it gets planned like what it actually is.

**This also directly subsumes spec 022's "in-window recent-tracks
switcher."** Spec 022's retention/eviction *policy* (which tracks stay
in memory, for how long) stays exactly as designed — that's backend logic,
unaffected by any of this. But its UI — a switcher inside the trim
window — becomes redundant if this spec ships, because the shell's own
navigation *is* that switcher, generalized across every session type
(captured tracks, imports, an in-progress mix), not just captured tracks.
See "Relationship to spec 022" below.

## Planner recommendations (need owner confirmation before this leaves DRAFT)

Codex raised four real forks in its blocked review of spec 019. Recommending
a default for each, but these are genuine product-shape decisions, not
implementation details — flagging clearly rather than deciding silently:

1. **Mix view layout: one shared editor panel with an A/B source
   selector, not two full panels side-by-side.** *Recommended.* The
   existing single-source editor (EQ + stems + sound effects + presets) is
   already visually dense — literally shown filling the whole trim window
   in normal use. Two full copies side-by-side would either be cramped to
   the point of unusable or require a window wider than fits most screens.
   An A/B toggle also reuses the *exact* mechanism this spec needs anyway
   for switching between retained sessions (see below) — one switching
   component, two uses, instead of inventing a second one just for Mix.
2. **No separate master/final effect stage after mixing.** *Recommended.*
   The original spec 019 already resolved this implicitly: a mixed export
   is just a new source you can reopen and shape further like any other
   clip. Adding a third effect stage inside the Mix view itself would be
   scope growth beyond even what this feedback asked for (per-source
   editing, not a new mixing-bus concept). Revisit only if real use shows
   people actually want it.
3. **Tray right-click keeps direct-jump shortcuts to each view, in
   addition to the shell existing.** *Recommended.* The shell is a
   discoverability/coherence upgrade, not a reason to slow down the
   one-click path power users already have today. Removing direct tray
   shortcuts would be a real regression for the "I know exactly what I
   want, get me there in one click" case — the same "don't cut a fast
   path to keep the UI simple" principle this project has used all
   session (spec 019's own duration policy, spec 022's hotkey behavior).
4. **Session model: one active editor plus an in-shell switcher,
   preserving every session — aligned with spec 022, not a different
   arrangement.** *Recommended.* Reuses spec 022's already-designed
   retention/eviction policy rather than inventing a second session model.
   Also keeps the single-managed-window discipline spec 014 had to fix a
   real bug to establish, generalized to "one shell window" instead of
   "one trim window."

## Relationship to spec 022

Spec 022 stays exactly as specified for its retention/eviction *policy*
(last 5 tracks or 20 minutes, evicted at track boundaries only, per-track
independent effect state) — that part is backend logic this spec doesn't
touch or duplicate. What changes: spec 022's own "in-window recent-tracks
switcher" bullet is superseded by this spec's shell navigation, which
generalizes the same idea (switch what you're looking at without closing
anything) across every session type, not just captured tracks. If this
spec (023) ships, spec 022 should be read with that one bullet replaced by
"reachable via the shell's navigation" rather than its own bespoke widget.
If owner decides *against* this spec's scope, spec 022's original
in-window switcher stands unchanged as designed.

## Scope boundary — what this spec is not

Not a redesign of any individual editor's controls (spec 020's help icons,
the EQ/stem/effects panels themselves) — those stay as they are, just
hosted inside shell views instead of standalone windows. Not a new feature
— every action reachable today (capture, import file/URL, mix, library)
stays reachable; this changes *how they're reached and how work persists*,
not what they do.

## Edge cases

- A session with unsaved/in-progress work (effects being tuned, a slow
  stem-separation running) when the user navigates to a different shell
  view — work must be preserved and resumable, not silently discarded;
  same principle spec 022 already established for its own switcher.
- Closing the shell window entirely (vs. navigating within it) — should
  this exit the app, or minimize to tray? Needs a decision at
  implementation time; today's separate-windows model doesn't have this
  ambiguity, the shell model does.
- A slow operation (stem separation, a URL download) started in one view
  while the user navigates away — must keep running and report back when
  the user returns to that view, not silently cancel just because it's
  not currently visible.
- Migration of the five existing independent window slots into one
  shell's internal state — must not regress spec 014's fix (no
  permanently-stuck-invisible window state) just because the mechanism
  changed from five slots to one.

## Acceptance criteria

To be finalized once the four planner recommendations above are confirmed
or corrected by the owner — deliberately not writing final criteria
against recommendations that might change.
