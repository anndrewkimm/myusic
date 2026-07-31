---
status: READY
touches: [Hookline.App, Hookline.Audio]
depends_on: [002, 003, 004, 014, 015, 016]
---

# 022 — Retain recent tracks and switch between them without closing the trim window

## Goal

Two related complaints from actually using the app, addressed together
because they share one root cause:

1. Switch away from a song for a few minutes and its captured audio is
   gone — spec 002's rolling buffer is a flat 5-minute *time* window, not
   track-aware, so going back to trim something you heard earlier means
   it's simply been evicted.
2. With the trim window already open editing one song, there's no way to
   switch it to a different song (e.g. whatever's playing now) without
   closing the window and reopening it from scratch.

Both come from the same gap: the app only ever tracks "the current
moment," never "recent moments." Fixing the retention policy once and
adding a way to reach what it retains solves both.

## Research done before writing this spec

The owner's own first instinct was suggesting Redis for this. Worth
recording why that's not the right tool, since the reasoning shapes the
design below: Redis is an out-of-process cache meant for sharing state
*across multiple processes/machines* — Hookline is a single local
process talking to itself, so Redis would mean bundling and running an
entire second server to hold data the app already has direct access to
in its own memory. The actual requirement ("keep several recent tracks
reachable, evict the oldest automatically") is a bounded in-process cache
with an eviction policy — exactly the kind of thing spec 002 already
built, just scoped to tracks instead of a flat time window. No new
dependency needed.

## Why this needs real discipline, not just "keep more stuff around"

This app has already had one real memory-growth incident — spec 006, where
a rolling buffer that was *supposed* to be correctly bounded turned out
not to be, and it took a real root-cause investigation to fix. Retaining
multiple tracks' worth of raw PCM (44.1kHz/16-bit/stereo — roughly 10MB
per minute of audio) is a deliberate, meaningful increase in what this app
holds in memory at once. The eviction policy below is deliberately
conservative and deliberately explicit about its ceiling for that reason —
this is not a place to be loose.

## Resolved implementation decisions

- **Retention policy: last N tracks, capped by total duration, whichever
  limit is hit first — evicted only at track boundaries, never mid-track.**
  Extends spec 002's existing rolling buffer rather than replacing it:
  same mechanism, the eviction trigger changes from "older than N minutes"
  to "more than N tracks retained, or more than M total minutes retained."
  Evicting only at track boundaries (never truncating a track that's
  still being captured) reuses spec 002's existing "buffer never straddles
  two different songs" invariant instead of risking a corrupted partial
  retained track.
- **Default: last 5 tracks or 20 minutes total, whichever is smaller** —
  configurable, same pattern as spec 002's own buffer-duration setting
  (`plans/002-loopback-capture-buffer.md`, "configurable; default
  suggestion: 5 minutes"). 20 minutes of raw PCM is roughly 200MB — a
  deliberate, bounded, explicit ceiling, not an open-ended history.
- **Each retained track keeps its own independent trim/effect state**,
  not just raw audio — segment splits (spec 015), EQ, stem volumes, sound
  effects, whatever was being dialed in. Switching away and back means
  it's genuinely "just there," not just the audio with settings reset.
  This is the actual point of the feature: not merely "don't lose the
  audio" but "don't lose the work."
- **The live-capture/hotkey-triggered trim window stays a single managed
  slot** (`App.xaml.cs`'s `_trimWindowSlot`, `ManagedWindowSlot`, spec
  014) — this spec does not multiply *that* window. (The app already runs
  multiple concurrent `TrimWindow` instances elsewhere by design — one per
  imported track via `_importWindowSlots`, one per catalog re-trim via
  `ClipRetrimLauncher` — so "single window" isn't an app-wide invariant;
  it's specifically true of the one hotkey/tray-triggered slot this spec
  touches, and that's the scope worth preserving.) Spec 014 was a real bug
  fix for exactly the class of problem multiple *live-capture* window
  instances would invite; multiplying that specific slot to solve this
  would reintroduce that risk for a problem that doesn't need solving that
  way.
- **In-window "recent tracks" switcher** — a small list/dropdown inside
  the existing trim window (not a new separate window) showing every
  currently-retained track; picking one swaps the window's content to
  that track's own independent session state from the point above.
  **Superseded if `plans/023-unified-app-shell.md` ships**: that spec
  (drafted 2026-07-31, from direct owner feedback after trying spec 019)
  proposes a shell-wide navigation/switcher generalizing this same idea
  across every session type, not just captured tracks — if adopted, this
  bullet becomes "reachable via the shell's navigation" instead of its
  own bespoke widget. The retention/eviction *policy* below is unaffected
  either way — that's backend logic, not UI. If 023 is not adopted, this
  bullet stands exactly as written.
- **"Capture a moment" always takes you to what's currently playing.**
  Note this targets the `ShowTrimWindow()` action itself, not the raw
  `Ctrl+Alt+H` keypress — spec 018's second-pass fix (shipped the same day
  as this spec was drafted) changed the hotkey to open the tray action
  menu first rather than jumping straight to the trim window, so
  "Capture a moment" is now reached via that menu (or a direct tray-icon
  interaction), and this spec's change applies wherever `ShowTrimWindow()`
  is invoked from, regardless of that entry point. Today, invoking it
  while the trim window is already open just re-activates/re-focuses
  whatever session is already showing (`App.xaml.cs`,
  `_trimWindowSlot.TryActivateExisting`) — that's the concrete cause of
  "I have to close and reopen to edit a different song." New behavior: if
  the window is already open and showing a different track than what's
  currently playing, invoking it switches the window to the current
  track's session (creating one if it doesn't exist yet) instead of just
  re-focusing the stale one. If it's already showing the current track,
  behavior is unchanged (re-focus). One consistent rule, no new
  keybinding, no confirmation dialog needed — the previous track's
  session isn't lost, it's simply still reachable from the switcher above.
- **Scope boundary: this is about the live rolling capture buffer only**,
  not the exported clip catalog (spec 004), which already persists
  indefinitely to disk/SQLite and is unaffected by any of this.

## Edge cases

- The currently-playing track was already evicted from retention (e.g.
  the user was away from Spotify long enough, or cycled through more than
  N tracks) — invoking "Capture a moment" starts a fresh session for it
  exactly like today's current behavior; it was never going to be recoverable
  once genuinely evicted, no different from today.
- Switching the trim window to a different retained track while the
  current one has an in-progress slow operation (stem separation
  running) — the operation keeps running against its own track's state in
  the background; switching back later should show its result if it's
  finished, or still-in-progress if not, not silently cancel it.
- Retention cap hit exactly while a track that's currently open in the
  trim window would be the one evicted — the currently-open-in-a-window
  track is never evicted while a window still references it, same
  "don't pull the rug out from under an open editor" principle spec 014's
  fix embodies elsewhere.
- Very short tracks in quick succession (e.g. skipping through several
  30-second songs) hitting the N-tracks cap quickly — expected, working
  as designed; the count-based cap exists for exactly this.
- Preview audio (spec 016/015's debounced re-render) for a track that
  isn't the currently-visible one — never plays audio for a
  not-currently-displayed session; switching stops whatever the previous
  session's preview was doing, same as closing/reopening does today.
- App restart — retained tracks are in-memory only (same as today's
  buffer), so a restart clears retention entirely; this is consistent
  with spec 002's existing "buffer is not exported-clip storage" framing,
  not a new limitation being introduced.

## Acceptance criteria

- [ ] The rolling buffer retains up to N recently-played tracks (default
      5) or M total minutes (default 20), whichever limit is hit first,
      evicting the oldest track only at a track boundary.
- [ ] Each retained track has its own independent trim selection and
      effect state, unaffected by switching away and back.
- [ ] The trim window shows a switcher listing every currently-retained
      track; selecting one swaps the window to that track's session
      without closing/reopening the window.
- [ ] Invoking "Capture a moment" (via hotkey → tray menu, or any other
      entry point that calls `ShowTrimWindow()`) while the trim window is
      open and showing a different track than what's currently playing
      switches it to the current track's session (creating one if
      needed); if already showing the current track, behavior is
      unchanged.
- [ ] A track still open in the trim window (or referenced by the
      switcher's current selection) is never evicted out from under it.
- [ ] The exported clip catalog (spec 004) is entirely unaffected by any
      of this — this spec only touches the live rolling buffer.
- [ ] Both the track-count and total-duration caps are configurable,
      following spec 002's existing settings pattern.
- [ ] All edge cases above are handled explicitly, not silently ignored.
