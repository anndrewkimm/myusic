---
status: DRAFT
touches: [Hookline.App, Hookline.Audio]
depends_on: [001, 002]
---

# 003 — Trim UI + export

> DRAFT until 001 and 002 are DONE. This is the first spec with a real user-facing surface — worth a design pass (screenshots/mockup, ideally from your designer friend) before flipping to READY, since UI decisions here are much more expensive to change after the fact than the headless plumbing in 001/002.

## Goal

The actual moment of use: user realizes they like what's playing, opens this window, sees a waveform of what was just captured, drags to select the good part, previews it, exports it as a correctly-tagged MP3. This is the highest-stakes spec for "does this feel good to use" — everything else is invisible plumbing in service of this one interaction.

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

## UX details (this is the part worth designer input on)

- The window should feel lightweight and fast to dismiss — this is meant to be used dozens of times a day without becoming a chore. A heavy multi-panel "DAW" feel is the wrong direction; think closer to a quick capture tool than an editor.
- Waveform should make it visually obvious where "now" (live edge of the buffer) is versus older buffered audio.
- Selection should have sensible defaults on open — e.g. defaulting to the last ~15-30 seconds selected already, since "the part I just heard" is the most common case, so a lot of the time the user can basically just hit Export immediately with minimal dragging.
- No modal blocking of the rest of the OS — should behave like a normal small utility window, closeable/cancelable without exporting.

## Edge cases

- User opens the trim window but the buffer is nearly empty (app just started, track just started playing) — show that honestly (a short/empty waveform), don't pretend there's more available than there is.
- User selects a region that includes an ad-flagged or paused segment (per spec 001/002 flags) — at minimum warn; ideally visually mark those regions on the waveform distinctly so the user sees it before exporting a clip with a chunk of ad in it.
- Track changes to a new song while the trim window is still open from the previous track — the window should keep showing the previous track's buffer (the one the user opened it for), not silently swap to the new track underneath them.
- Filename collisions on export (same artist/title exported twice, e.g. two different clips from the same song) — must not silently overwrite a previous clip; auto-increment or otherwise disambiguate.
- Missing metadata (album art unavailable, or even title/artist missing/garbled from Spotify) — export should still work, just with whatever tag data is actually available; never block export on missing metadata.
- Very short selections (sub-second) and selections spanning the entire buffer window — both should export correctly, not error out at either extreme.
- Output folder doesn't exist / isn't writable (e.g. user hasn't set up Spotify Local Files yet, or moved the folder) — clear error, not a silent failure or crash, and should offer to create the default folder if missing.

## Acceptance criteria

- [ ] Global hotkey and/or tray click opens the trim window with the current track's buffer visualized.
- [ ] Drag-to-select with independently adjustable edges works.
- [ ] Preview playback of the selection works, with visible playhead.
- [ ] Some fine-adjustment mechanism beyond raw mouse drag exists.
- [ ] Export produces a correctly-tagged MP3 in the configured folder, with collision-safe naming.
- [ ] Export success/failure is always visibly communicated, never silent.
- [ ] All edge cases above are handled per the notes (or explicitly punted with reasoning in "What shipped" — not silently skipped).

## Open questions

## Follow-up ideas
