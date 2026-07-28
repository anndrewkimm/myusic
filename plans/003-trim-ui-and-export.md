---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [001, 002]
---

# 003 — Trim UI + export

> READY for Codex. 001 and 002 are both DONE and were verified live against a real Spotify session. A wireframe design pass happened on 2026-07-27 — the owner reviewed a mockup of the trim window plus its two key edge-case states (sparse buffer, ad/paused overlap) and made the calls below. Treat "Resolved implementation decisions" as settled, not open for re-litigation during implementation.

## Goal

The actual moment of use: user realizes they like what's playing, opens this window, sees a waveform of what was just captured, drags to select the good part, previews it, exports it as a correctly-tagged MP3. This is the highest-stakes spec for "does this feel good to use" — everything else is invisible plumbing in service of this one interaction.

## Codex handoff

- Keep the interaction model as lightweight as the spec's own framing demands: a quick-capture tool, not an editor. Every control should be reachable in the fewest possible clicks/keystrokes — no confirmation dialogs for non-destructive actions, no multi-step flows where one step will do.
- The waveform/drag-select surface itself is unchanged from the functional requirements below. What changed on review is the initial state plus several previously-open implementer calls, both captured in "Resolved implementation decisions."
- Prioritize the golden path (open → drag a selection → preview → Export) feeling instant and obvious over covering every edge case with equal visual polish — edge cases must still be handled per the notes below, but never at the cost of adding friction to the common case.

## Resolved implementation decisions

- **No default selection on open.** The window opens with the full recent buffer visible and *nothing* pre-selected. The user drags out exactly the amount of time they want, from scratch, every time. (This reverses the original draft's "pre-select the last 15–30s" suggestion — the owner's call after seeing the mockup: a guessed default is a hidden decision the user has to notice and undo when it's wrong, which is worse than always starting from a clean slate.)
- **Nudge increment:** 0.1s per click on the fine-adjust ▲▼ steppers. Left/Right arrow keys perform the same nudge on whichever edge (start or end) last had focus — keyboard and mouse paths must do the same thing, not diverge. Hold Shift for a coarser 1s jump to cross a large gap quickly.
- **Excluded (ad/paused) region overlap:** warn, never block. If the selection overlaps a flagged span, show an inline warning (as mocked — hatched region on the waveform, short text warning near Export) but Export still completes in one click. Do not add a confirmation step here — the user already saw the warning; making them click through it again is exactly the extra-step friction this spec is trying to avoid.
- **Global hotkey default:** `Ctrl+Alt+H`. Not user-configurable yet (Phase 2), but it must be surfaced somewhere discoverable (tray icon tooltip/menu is sufficient).
- **Window theming:** the trim window is always dark, regardless of the OS light/dark setting. Deliberate and permanent for this window specifically — waveform legibility matters more than matching system chrome, and it avoids building/tuning a second theme for a window this small and short-lived.
- **Dismiss paths:** the ✕ button and the `Esc` key both cancel with no side effects. Clicking outside the window does **not** auto-dismiss — this window is meant to sit alongside other work, and closing it just because focus moved elsewhere (e.g. glancing back at Spotify) would itself be the kind of unintended behavior this spec should avoid.
- **Efficiency bar for the whole spec:** every control should be operable with either mouse or keyboard, and the common path (open → Export) should be completable without ever touching the fine-adjust controls — those are a fallback for precision, not a required step.

## User story

I'm listening to Spotify, doing something else, half-paying-attention to the tray icon showing the current track. A part I like plays. I hit a global hotkey (or click the tray icon). A small window pops up already showing a waveform of roughly the last minute or two, already scrolled to "now." I drag across the part I want, hit play to check it, adjust the edges a bit, hit Export. Window closes itself (or I close it), and the clip is just... there, in my Spotify Local Files, next time I open Spotify.

## Functional requirements

- Global hotkey (default binding TBD by implementer, must be user-visible/discoverable, doesn't need to be user-configurable yet — that's Phase 2) opens the trim window.
- Trim window shows a waveform for the current track's buffered audio (via spec 002's query API), with current now-playing metadata (title/artist/art from spec 001) displayed.
- Click-and-drag to select a region on the waveform. Both edges independently draggable after initial selection (fine-tuning).
- Playback preview of just the selected region, with a visible playhead moving across the waveform during preview.
- Numeric/fine-adjust fallback for the selection edges (drag-only is imprecise; some way to nudge by small increments) — doesn't need to be fancy, but pure mouse-drag-only trimming is a real precision pain point, don't ship without some finer control.
- Export button: renders the selection to MP3, writes ID3 tags (title, artist, album, album art if available) reflecting the source track (clip title can default to "Artist - Title" with maybe a suffix, exact naming scheme is implementer's call but must be sensible and collision-safe — see edge cases), saves to the configured output folder (see `docs/CONVENTIONS.md`).
- Clear, non-blocking feedback that export succeeded (or failed) — this must never fail silently.
- Optional-but-recommended for this spec (implementer's judgment on whether it fits without bloating scope — if it doesn't fit cleanly, punt to Phase 2 and say so): short fade-in/fade-out on export so trims don't have a hard audio click at the edges.

## UX details (reviewed via wireframe 2026-07-27 — see "Resolved implementation decisions")

- The window should feel lightweight and fast to dismiss — this is meant to be used dozens of times a day without becoming a chore. A heavy multi-panel "DAW" feel is the wrong direction; think closer to a quick capture tool than an editor.
- Waveform should make it visually obvious where "now" (live edge of the buffer) is versus older buffered audio.
- Selection starts empty on open — no pre-selected default range (see "Resolved implementation decisions"). The user drags out exactly the amount they want, every time.
- No modal blocking of the rest of the OS — should behave like a normal small utility window, closeable/cancelable without exporting.

## Edge cases

- User opens the trim window but the buffer is nearly empty (app just started, track just started playing) — show that honestly (a short/empty waveform), don't pretend there's more available than there is.
- User selects a region that includes an ad-flagged or paused segment (per spec 001/002 flags) — visually mark the region on the waveform distinctly and show an inline warning when the current selection overlaps it. This never blocks Export (see "Resolved implementation decisions").
- Track changes to a new song while the trim window is still open from the previous track — the window should keep showing the previous track's buffer (the one the user opened it for), not silently swap to the new track underneath them.
- Filename collisions on export (same artist/title exported twice, e.g. two different clips from the same song) — must not silently overwrite a previous clip; auto-increment or otherwise disambiguate.
- Missing metadata (album art unavailable, or even title/artist missing/garbled from Spotify) — export should still work, just with whatever tag data is actually available; never block export on missing metadata.
- Very short selections (sub-second) and selections spanning the entire buffer window — both should export correctly, not error out at either extreme.
- Output folder doesn't exist / isn't writable (e.g. user hasn't set up Spotify Local Files yet, or moved the folder) — clear error, not a silent failure or crash, and should offer to create the default folder if missing.

## Acceptance criteria

- [x] Global hotkey and/or tray click opens the trim window with the current track's buffer visualized.
- [x] Drag-to-select with independently adjustable edges works.
- [x] Preview playback of the selection works, with visible playhead.
- [x] Some fine-adjustment mechanism beyond raw mouse drag exists.
- [x] Export produces a correctly-tagged MP3 in the configured folder, with collision-safe naming.
- [x] Export success/failure is always visibly communicated, never silent.
- [x] All edge cases above are handled per the notes (or explicitly punted with reasoning in "What shipped" — not silently skipped).

## Open questions

None. Resolved during the 2026-07-27 wireframe review — see "Resolved implementation decisions."

## Follow-up ideas

None identified during implementation.

## What shipped

- Added the first `Hookline.App` WPF executable. It runs from the tray, starts
  the existing watcher/capture pipeline, registers the discoverable
  `Ctrl+Alt+H` global hotkey, and opens a non-modal, always-dark trim window
  from either the hotkey or a left click/menu command on the tray icon.
- Added a custom-rendered waveform surface with an honest empty-buffer state,
  a visible live edge, drag-to-select, independently draggable handles, and
  hatched paused/excluded spans. Opening the window freezes an immutable track
  snapshot, so a later track change cannot replace the waveform or metadata
  underneath the user.
- Added mouse and keyboard fine adjustment. Stepper clicks and Left/Right move
  the last-focused edge by 0.1 seconds; Shift+Left/Right moves it by one second.
  The window starts with no selection, and Escape or the close button dismisses
  it without side effects.
- Added selection-only NAudio preview with a render-rate playhead that maps
  across excluded timeline spans. A selection that overlaps a paused/excluded
  span gets an inline warning but is never blocked from preview or export.
- Added cancellable MP3 export through the verified current stable packages
  NAudio.Lame 2.1.0 and TagLibSharp 2.3.0. Exports use 192 kbps MP3,
  ID3v2.4 title/artist/album/cover tags, 15 ms edge fades, sanitized filenames,
  and atomic collision-safe suffixing without overwriting an existing clip.
- Added a persistent output-folder choice in
  `%LOCALAPPDATA%\Hookline\settings.json`. The UI exposes the current folder
  and a keyboard-accessible Change action. The default falls back to
  `%USERPROFILE%\Music\Hookline`; missing folders are created on export, and
  unwritable folders surface an inline error.
- Added timeline-range/snapshot slicing coverage, real MP3 encoding and tag
  verification, sub-second and whole-buffer exports, missing-folder creation,
  collision handling, view-model nudge/warning/failure behavior, frozen
  preview snapshots, and settings persistence.

Verification completed:

- `dotnet format Hookline.sln --no-restore --verify-no-changes`
- `dotnet build Hookline.sln -c Release --no-restore`: zero warnings/errors
- `dotnet test Hookline.sln -c Release --no-build`: 43/43 passing
- Live Release smoke test launched the tray app against the real Spotify
  session and invoked `Ctrl+Alt+H`. The 880x610 trim window opened with the
  correct title, artist, album art, dark theme, and empty-buffer state.

Review validation remaining:

- The live Spotify session exposed metadata but had no buffered audio during
  the smoke test, so a reviewer should play a track and validate drag, audible
  preview/playhead, and one real export by ear. Snapshot behavior and a real
  native-LAME MP3/tagging path are covered deterministically in the test suite.

## Review notes (reviewer, 2026-07-27)

Independent verification, separate from Codex's own smoke test:

- `dotnet build -c Release` and `dotnet test -c Release`: reconfirmed clean —
  0 warnings/errors, 43/43 passing (5 App.Tests, 14 Audio.Tests, 24
  NowPlaying.Tests).
- Read every new file in `Hookline.App` plus `Mp3ClipExporter.cs` and its
  tests. Every "Resolved implementation decisions" item checks out in code:
  `Ctrl+Alt+H` (`GlobalHotkey.cs`), 0.1s/1s nudge with arrow-key parity
  (`TrimWindow.xaml.cs`), no default selection (`TrimViewModel` starts with
  null start/end), warn-not-block on excluded overlap (`SelectionOverlapsExcluded`
  never gates `CanExport`), Esc/✕-only dismiss (no deactivate-close handler),
  fixed-dark waveform palette, and an immutable per-open `TrimSession` snapshot
  (queried once by track-instance ID, not re-queried against "current track")
  so a later track change can't swap the window's content underneath the user.
  `Mp3ClipExporterTests` genuinely encodes/tags/verifies real MP3s (not
  stubbed) — confirms title/artist/album/art and collision-safe renaming
  against actual file output.
- Live smoke test: launched the real `Hookline.App.exe` release build against
  a live Spotify session, triggered the actual registered `Ctrl+Alt+H` hotkey,
  and captured the resulting window's real rendered content. Confirmed
  visually: correct title bar copy, real album art, correct
  title/artist, dark theme, empty Start/End/Selection fields (no default
  selection), the in-UI nudge-increment hint text, the configured output
  folder with a Change... action, and an honest "No buffered audio yet" empty
  state (Spotify was paused at test time, so this also incidentally verified
  that edge case).
  - Note: bringing the window to the actual foreground (`Activate()` /
    `SetForegroundWindow`) did not visibly succeed when triggered via
    synthetic input from this automated session — Windows' foreground-lock
    restrictions plausibly block background/automated callers regardless of
    the app's own code, and a real physical keypress is one of the
    documented exceptions to that restriction. Rendered content was instead
    captured directly via `PrintWindow`. **Inconclusive, not a confirmed
    bug** — worth a real physical `Ctrl+Alt+H` press to confirm the window
    actually pops to the foreground in normal use, since that's central to
    the spec's whole premise.
- Minor, non-blocking deviation from `docs/CONVENTIONS.md`: the ideal default
  output folder is "a Hookline subfolder inside the user's Spotify Local
  Files folder, if it can be detected — otherwise `%USERPROFILE%\Music\Hookline`."
  This implementation always uses the `Music\Hookline` fallback with no
  detection attempt. Reasonable given Spotify's Local Files folder set isn't
  reliably discoverable and CONVENTIONS names this fallback as an explicitly
  acceptable outcome; the folder is fully user-configurable regardless.
  Followed up in spec 005.
- **Not yet verified**: an actual drag-select → audible preview → export
  round trip against real buffered audio — the live session had nothing
  buffered at test time (matches Codex's own noted gap). Everything
  upstream and downstream of that interaction is verified (encoding/tagging
  via tests, UI rendering via the live smoke test), so this is a narrow,
  well-bounded remaining check, not a sign of a deeper problem.

Status held at REVIEW pending that one live check (physical hotkey press
while foreground, plus one real drag/preview/export) — flip to DONE once
confirmed.
