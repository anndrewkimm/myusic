---
status: DRAFT
touches: [Hookline.App, Hookline.Audio]
depends_on: [003]
---

# 004 — Clip catalog

> DRAFT until 003 is DONE.

## Goal

A simple in-app library view of every clip that's been exported, so clips don't just vanish into a folder and get forgotten. This is what makes the tool feel like a personal music tool rather than a one-off export utility — the user explicitly wants a "catalog," not just a folder of files.

## User story

I open the app (not to capture anything new, just browsing) and see every clip I've saved: what song it's from, when I saved it, maybe a mini-waveform thumbnail. I can play any of them right there, rename one, re-open one in the trim tool to adjust the start/end, delete ones I don't want anymore, or jump to it in Explorer/Spotify.

## Functional requirements

- Persist a local catalog of exported clips (see `docs/CONVENTIONS.md` for storage — SQLite vs flat JSON is this spec's call to finalize) recording at minimum: source track title/artist, export timestamp, file path, trim start/end (relative to the original track), duration.
- Catalog view lists all clips, most recent first by default, with basic sort/filter (at least by artist or by date — full search is Phase 2 unless trivial to include here).
- Playback directly from the catalog (no need to leave the app or open another player).
- Rename a clip (updates both the catalog entry and, reasonably, the ID3 title tag and/or filename — implementer's call on how deep the rename propagates, but the catalog and the actual file should never silently disagree on the name).
- Delete a clip — must remove both the catalog entry and the underlying file (with a confirmation, this is a destructive action).
- "Re-trim" — reopens spec 003's trim UI seeded with this clip's original track/selection, if the source buffer is still available (it likely won't be, since the buffer is short-lived — see edge cases). If the original audio isn't available anymore, this action should clearly say so rather than silently failing or corrupting the existing clip.
- "Reveal in folder" — opens Explorer at the file's location.

## UX details

- This should feel like a lightweight personal library, not a file manager clone — think a simple list/grid with cover art thumbnails (reuse album art from spec 001/003 where available) rather than a raw file table.
- Should be reachable quickly (tray icon menu item, or the same window the trim UI lives in via a tab/mode switch — implementer's call, but it shouldn't require hunting through a settings menu).

## Edge cases

- The underlying MP3 file was moved or deleted outside the app (user cleaned up the folder manually) — catalog entry should detect this (e.g. on load, or on interaction) and show a clear "missing file" state rather than crashing or silently pretending it's fine.
- Re-trim requested for a clip whose original buffer window is long gone (the buffer in spec 002 is only a few minutes, catalog entries can be days old) — must degrade gracefully: tell the user the original audio isn't available for re-trimming rather than erroring unhelpfully. (A stretch option worth noting but not required: re-trim from the *exported clip itself* as a smaller fallback, letting the user tighten an already-exported clip even without the original buffer — implementer's call whether this fits the spec's scope; if not, note it as a follow-up idea.)
- Two clips with identical display names (e.g. two takes of the same song) — catalog must disambiguate visually so the user isn't guessing which is which (timestamp or trim-range shown alongside the name is probably enough).
- Large catalogs over time (hundreds of clips eventually) — list should stay responsive; virtualized/lazy-loaded rendering if the UI framework needs it explicitly, not just "worked fine with 10 test clips."
- Deleting a clip that's currently mid-playback in the catalog's own preview player — must stop playback cleanly first, not error or leave a dangling file handle.

## Acceptance criteria

- [ ] Catalog persists across app restarts (real local storage, not in-memory).
- [ ] All exported clips (from spec 003 onward) appear automatically without manual import.
- [ ] Playback, rename, delete, reveal-in-folder all work correctly.
- [ ] Re-trim works when the buffer is available and fails gracefully with a clear message when it isn't.
- [ ] Missing/moved files are detected and shown clearly, not silently ignored or crash-inducing.
- [ ] Catalog stays responsive with a large (100+) number of entries — verified, not assumed.

## Open questions

## Follow-up ideas
