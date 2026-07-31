---
status: DONE
touches: [Hookline.App]
depends_on: [003, 004, 014, 018, 019, 022]
---

# 023 — One Hookline workspace instead of five separate windows

## Retired — merged into spec 019 (2026-07-31)

**Do not implement this spec separately.** Owner gave Codex direct
instruction to keep the unified-shell redesign inside `plans/019-mix-two-clips.md`
rather than as its own spec — see that spec's "Owner-directed review
revision" and "Planner review (second pass)" sections for the authoritative,
current version of everything below. This file is kept only as a record of
the original research (window-slot inventory, the Redis-adjacent reasoning
tie-in, the four sub-decisions and their rationale) and was never picked up
as its own `IN_PROGRESS` implementation, so nothing is lost by retiring it.
One real difference to note: this spec recommended keeping tray right-click
direct-jump shortcuts; spec 019's revision reduces the tray to "Open
Hookline" + "Exit" only, per more specific direct owner instruction — that
supersedes the recommendation below.

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

## Resolved decisions (owner-confirmed 2026-07-31)

Codex raised four real forks in its blocked review of spec 019. Owner
confirmed all four recommended defaults as-is rather than any narrower
alternative — this section is no longer "recommended," it's resolved:

1. **Mix view layout: one shared editor panel with an A/B source
   selector, not two full panels side-by-side.** *Confirmed.* The
   existing single-source editor (EQ + stems + sound effects + presets) is
   already visually dense — literally shown filling the whole trim window
   in normal use. Two full copies side-by-side would either be cramped to
   the point of unusable or require a window wider than fits most screens.
   An A/B toggle also reuses the *exact* mechanism this spec needs anyway
   for switching between retained sessions (see below) — one switching
   component, two uses, instead of inventing a second one just for Mix.
2. **No separate master/final effect stage after mixing.** *Confirmed.*
   The original spec 019 already resolved this implicitly: a mixed export
   is just a new source you can reopen and shape further like any other
   clip. Adding a third effect stage inside the Mix view itself would be
   scope growth beyond even what this feedback asked for (per-source
   editing, not a new mixing-bus concept). Revisit only if real use shows
   people actually want it.
3. **Tray right-click keeps direct-jump shortcuts to each view, in
   addition to the shell existing.** *Confirmed.* The shell is a
   discoverability/coherence upgrade, not a reason to slow down the
   one-click path power users already have today. Removing direct tray
   shortcuts would be a real regression for the "I know exactly what I
   want, get me there in one click" case — the same "don't cut a fast
   path to keep the UI simple" principle this project has used all
   session (spec 019's own duration policy, spec 022's hotkey behavior).
4. **Session model: one active editor plus an in-shell switcher,
   preserving every session — aligned with spec 022, not a different
   arrangement.** *Confirmed.* Reuses spec 022's already-designed
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

- [ ] `Ctrl+Alt+H` (and the tray icon's left-click/default action) opens
      one persistent shell window with views for Capture/Trim, Import
      (local file + URL, spec 018/021), Mix (spec 019), and Library
      (spec 004) — not separate independent windows per action.
- [ ] Navigating between shell views preserves in-progress work in each
      (effect settings being tuned, a running slow operation) — switching
      away and back never silently discards or cancels anything.
- [ ] The five existing independent window-management structures in
      `App.xaml.cs` (`_trimWindowSlot`, `_catalogWindowSlot`,
      `_urlImportWindowSlot`, `_mixWindowSlot`, `_importWindowSlots`) and
      `ClipRetrimLauncher`'s separate dictionary are consolidated into the
      shell's own session/navigation state — not left running in parallel
      alongside it.
- [ ] Spec 014's guarantee (no permanently-stuck-invisible window after a
      failed show) holds under the new single-shell model — regression
      tested, not just assumed to carry over.
- [ ] The Mix view hosts one shared full editor (EQ, effects, stems — full
      parity with the normal capture/trim editor) with an A/B source
      toggle, applied independently per source before mixing — not a
      volume-only control, and not two full editor panels side-by-side.
- [ ] No new master/final effect stage exists after mixing; a mixed export
      remains reopenable afterward as its own independent source for
      further shaping, same as today.
- [ ] Tray right-click still exposes direct-jump shortcuts to each view
      (Capture, Import, Mix, Library) alongside the shell — the one-click
      path is not removed or made shell-only.
- [ ] Session switching inside the shell follows spec 022's model exactly:
      one active editor, every retained session reachable and resumable,
      nothing lost when switching. Spec 022's retention/eviction policy
      itself is reused unchanged, not reimplemented.
- [ ] `plans/019-mix-two-clips.md` is rewritten against the now-real shell
      APIs/components (not speculatively before they exist) once this
      spec ships, reusing its already-shipped `TwoSourceAudioMixer` DSP
      core unchanged — only the window/UI layer around it changes.
- [ ] All edge cases above are handled explicitly, not silently ignored.
