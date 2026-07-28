---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [003]
---

# 004 — Clip catalog

> REVIEW. Implementation and automated validation completed on
> 2026-07-27.

## Goal

A simple in-app library view of every clip that's been exported, so clips don't just vanish into a folder and get forgotten. This is what makes the tool feel like a personal music tool rather than a one-off export utility — the user explicitly wants a "catalog," not just a folder of files.

## Codex handoff

- This is the last Phase-1 spec. Once it's DONE, the roadmap's Phase 1 exit bar ("leave the app running, get a clean tagged MP3 clip in two clicks") is already satisfied by spec 003 alone — this spec is what keeps those clips from vanishing into a folder afterward, not a prerequisite for the core loop.
- Reuse spec 003's `Hookline.App` process (tray icon, WPF shell) — don't stand up a second application entry point.
- Same efficiency bar as 003: fewest clicks/steps, no unintended dismiss/delete behavior, confirmation dialogs only where genuinely destructive (delete is the one case that earns one here).

## Resolved implementation decisions

- **Storage: SQLite via `Microsoft.Data.Sqlite`**, not flat JSON (`docs/CONVENTIONS.md` left this open). Chosen because the "stays responsive with 100+ entries, verified" acceptance criterion and rename/delete/query all want indexed access, not a full-file parse-and-rewrite on every change.
- **Catalog is a separate window**, opened from the tray icon menu — not a tab/mode bolted onto the trim window. Keeps spec 003's careful track-swap-safety logic (its frozen per-open snapshot) fully decoupled from catalog browsing; the two windows don't need to know about each other's state.
- **Rename propagates to the catalog entry and the ID3 title tag, not the on-disk filename.** The exported filename is already a collision-safe, effectively-internal identifier (spec 003). Renaming files on disk risks a locked-file error (Spotify's Local Files scan may have it open) for no real user-facing benefit — what the user actually sees (catalog list, ID3 title) always stays consistent, which is what "catalog and file never silently disagree" actually requires.
- **Re-trim-from-exported-clip fallback: out of scope.** Only re-trims from the original spec-002 buffer when it's still available; otherwise shows the "no longer available" message the edge cases already call for. Re-encoding from an already-exported, already-faded MP3 is a meaningfully different lossy-to-lossy feature — not building it speculatively (see Follow-up ideas below).
- **Sort for v1: "Most recent" (default) and "By artist" only.** No free-text search — that's explicitly Phase 2 per this spec's own functional requirements.
- **Missing-file detection: checked on catalog load, and re-checked lazily right before playback/reveal/re-trim.** No background file-system watcher — unnecessary overhead for a personal, low-frequency-use catalog.
- **Large-catalog responsiveness: rely on WPF's built-in list virtualization** (`VirtualizingStackPanel`, on by default in `ListView`/`ListBox`) rather than a custom solution — just don't disable it.

## User story

I open the app (not to capture anything new, just browsing) and see every clip I've saved: what song it's from, when I saved it, maybe a mini-waveform thumbnail. I can play any of them right there, rename one, re-open one in the trim tool to adjust the start/end, delete ones I don't want anymore, or jump to it in Explorer/Spotify.

## Functional requirements

- Persist a local catalog of exported clips (SQLite via `Microsoft.Data.Sqlite` — see "Resolved implementation decisions") recording at minimum: source track title/artist, export timestamp, file path, trim start/end (relative to the original track), duration.
- Catalog view lists all clips, most recent first by default, with a sort toggle for by-artist (see "Resolved implementation decisions" — full search is Phase 2).
- Playback directly from the catalog (no need to leave the app or open another player).
- Rename a clip (updates the catalog entry and the ID3 title tag — see "Resolved implementation decisions" for why the on-disk filename is intentionally left alone).
- Delete a clip — must remove both the catalog entry and the underlying file (with a confirmation, this is a destructive action).
- "Re-trim" — reopens spec 003's trim UI seeded with this clip's original track/selection, if the source buffer is still available (it likely won't be, since the buffer is short-lived — see edge cases). If the original audio isn't available anymore, this action should clearly say so rather than silently failing or corrupting the existing clip.
- "Reveal in folder" — opens Explorer at the file's location.

## UX details

- This should feel like a lightweight personal library, not a file manager clone — think a simple list/grid with cover art thumbnails (reuse album art from spec 001/003 where available) rather than a raw file table.
- Reachable from the tray icon menu (see "Resolved implementation decisions") — shouldn't require hunting through a settings menu.

## Edge cases

- The underlying MP3 file was moved or deleted outside the app (user cleaned up the folder manually) — catalog entry should detect this (e.g. on load, or on interaction) and show a clear "missing file" state rather than crashing or silently pretending it's fine.
- Re-trim requested for a clip whose original buffer window is long gone (the buffer in spec 002 is only a few minutes, catalog entries can be days old) — must degrade gracefully: tell the user the original audio isn't available for re-trimming rather than erroring unhelpfully. Re-trimming from the exported clip itself as a fallback is explicitly out of scope for this spec (see "Resolved implementation decisions") — the "not available" message is the whole answer here.
- Two clips with identical display names (e.g. two takes of the same song) — catalog must disambiguate visually so the user isn't guessing which is which (timestamp or trim-range shown alongside the name is probably enough).
- Large catalogs over time (hundreds of clips eventually) — list should stay responsive; virtualized/lazy-loaded rendering if the UI framework needs it explicitly, not just "worked fine with 10 test clips."
- Deleting a clip that's currently mid-playback in the catalog's own preview player — must stop playback cleanly first, not error or leave a dangling file handle.

## Acceptance criteria

- [x] Catalog persists across app restarts (real local storage, not in-memory).
- [x] All exported clips (from spec 003 onward) appear automatically without manual import.
- [x] Playback, rename, delete, reveal-in-folder all work correctly.
- [x] Re-trim works when the buffer is available and fails gracefully with a clear message when it isn't.
- [x] Missing/moved files are detected and shown clearly, not silently ignored or crash-inducing.
- [x] Catalog stays responsive with a large (100+) number of entries — verified, not assumed.
- [x] Non-UI catalog logic (persistence read/write, missing-file detection, rename/delete semantics) is covered by unit tests independent of the UI thread, following the pattern in `Hookline.Audio.Tests`/`Hookline.App.Tests`.

## Open questions

None. Resolved during the 2026-07-27 planning pass — see "Resolved implementation decisions."

## Follow-up ideas

- Re-trim from the exported clip itself (not just the original live buffer), for when the spec-002 buffer window has already passed. Deliberately out of scope for v1 (see "Resolved implementation decisions").

## What shipped

- Added a versioned SQLite catalog under `%LOCALAPPDATA%\Hookline\catalog.db`, with indexed recent/artist sorting, persisted source metadata, trim coordinates, file paths, timestamps, and album art.
- Every successful MP3 export now registers atomically in the catalog. Registration failures remove the uncataloged export when possible and surface a clear error.
- Added a virtualized tray-opened clip library with artwork, direct playback, one-click inline rename, confirmed delete, re-trim, reveal-in-Explorer, and explicit missing-file states.
- Rename updates both SQLite and the ID3v2 title with rollback protection. Delete stops in-app playback first and coordinates file/catalog removal through a recoverable quarantine step.
- Re-trim reopens the existing trim UI with the original selection when that track range is still fully buffered; otherwise it reports that the original audio expired.
- Added non-UI coverage for persistence/reopen, sorting, automatic registration, missing-file detection, rename/delete semantics, playback/file-action orchestration, ID3 rename, seeded re-trim, and a 250-entry catalog with representative artwork. All 55 Release tests pass; the App Release build has zero warnings.
- Deviations: none. Known gaps: none within this spec; exported-MP3 re-trimming remains the documented follow-up.

## Review notes (reviewer, 2026-07-27)

- Rebuilt and retested independently: 0 warnings/errors, 55/55 passing
  (17 App.Tests + 14 Audio.Tests + 24 NowPlaying.Tests).
- Read the repository, service, view-model, cataloging exporter, and
  re-trim launcher in full. Confirmed against "Resolved implementation
  decisions": versioned SQLite schema with indices for both sort orders and
  a unique index on file path; rename updates ID3 + DB with rollback of the
  tag if the DB write fails; delete uses a rename-to-quarantine step so a
  failed catalog delete restores the original file instead of losing it;
  export registration failure deletes the orphaned MP3 rather than leaving
  an uncataloged file behind; missing-file checks run on load and again
  right before playback/rename/delete/reveal/retrim; retrim reuses spec
  003's `TrimWindow`/`TrimViewModel` seeded with the original selection and
  checks buffer availability before opening.
- Confirmed the delete confirmation dialog exists at the view layer
  (Yes/No, defaults to No, warning icon) — satisfies the one acceptance
  criterion that isn't purely mechanical.
- Tray menu now has Open / Library / Exit, consistent with the "reachable
  from tray, not buried in settings" decision.
- No stubs/TODOs found in the new `Catalog/` folder.

Clean. Flipping to DONE.
