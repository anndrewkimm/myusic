---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [003]
---

# 008 — Import a local audio file to trim

## Goal

Let the user open an audio file they already have on their PC (MP3, WAV, and other common formats) directly into the trim UI — same drag-to-select, preview, and tagged-MP3-export experience as a live-captured Spotify clip, just sourced from a file instead of the rolling buffer. No network involved, no capture involved — this is a local file picker, nothing more.

## Codex handoff

- The entire trim/preview/export/catalog-registration pipeline from specs 003/004 should work completely unmodified once an imported file is turned into the same shapes those specs already consume (`TrimSession`, `AudioBufferSnapshot`, `ClipExportMetadata`). The only new work is producing those shapes from a file on disk instead of from `HooklineAudioCaptureService`. If you find yourself changing `TrimWindow`, `TrimViewModel`, `Mp3ClipExporter`, or the catalog to special-case "imported" clips, stop and reconsider — that almost certainly means the adapter isn't shaped right yet.
- Verify current NAudio/Media Foundation format support at implementation time rather than assuming a fixed list — `MediaFoundationReader` covers a broad set of formats via Windows' built-in codecs, but confirm what's reliably available before deciding exactly which extensions to offer in the file picker.

## Resolved implementation decisions

- **Reachable from a new "Import audio file..." tray menu entry**, alongside spec 003/004's existing Open/Library/Exit. Opens a standard Windows file picker filtered to common audio extensions.
- **Decode the entire selected file into the same PCM format used everywhere else** (44.1kHz/16-bit/stereo — resampling if the source file differs) and wrap it as a synthetic `AudioBufferSnapshot`: the whole file is one continuous included range, no excluded ranges, `AvailableStart`/`AvailableEnd` spanning the full duration.
- **Metadata comes from the file's own tags when present** (via the existing TagLibSharp dependency), falling back to the filename (extension stripped) as the title when tags are missing or blank. Same "never block on missing metadata" rule as spec 003.
- **Imported files get a reserved, non-colliding synthetic track-instance ID** (e.g. a distinct negative range), never overlapping real SMTC-assigned instance IDs from spec 001. This is what lets the existing catalog/export pipeline treat an imported session as just another track instance without any special-casing.
- **Re-trim from the catalog is intentionally NOT specially implemented for imported-file clips in this spec.** Since an imported session's synthetic instance ID was never in the live capture buffer, a later re-trim attempt naturally and correctly falls into spec 004's existing "original audio no longer available" graceful-degradation path — this is accurate (the *live buffer* genuinely never had it) even though the original file may still be sitting on disk. Actually re-trimming from the original imported file is a clean follow-up, not required here — don't build it speculatively.
- **A generous sanity cap on decoded size/duration** (implementer's call on the exact number — think "long album side," not "5-minute song," so it doesn't get in the way of normal use) to avoid decoding something absurd into memory. This is a defensive bound, not a product constraint users should ever notice in practice.

## User story

I've got an MP3 sitting in a folder — maybe something I recorded myself, maybe a track I already legitimately own as a file — and I want to grab just a piece of it the exact same way I already grab pieces of whatever's playing in Spotify. I open Hookline, pick "Import audio file...", choose it, and I'm looking at the same waveform/drag/preview/export screen I already know, just fed from that file instead of a live buffer.

## Edge cases

- Unsupported or corrupt file selected — clear error, never a crash, never a half-loaded broken window.
- File has no embedded tags at all — title falls back to filename, artist/album blank; export still works with whatever's actually available.
- Extremely long file (beyond the sanity cap) — clear message explaining why, not a silent hang or an out-of-memory crash.
- User cancels the file picker — no-op, nothing opens, no error.
- User imports a file that happens to itself be a previous Hookline export — treated like any other file, no special-casing needed.
- Very short file (sub-second) — already handled by the existing trim pipeline per spec 003; should just work.

## Acceptance criteria

- [x] Tray menu has a working "Import audio file..." entry.
- [x] Selecting a supported file (MP3 and WAV at minimum) opens the trim window populated with that file's full waveform, no default selection, consistent with spec 003's resolved decisions.
- [x] Existing ID3/tag metadata is used when present (title/artist/album/art); filename is the fallback title when tags are missing.
- [x] Drag/preview/export all work identically to the live-capture flow and produce a correctly-tagged, collision-safe MP3 that registers in the catalog exactly like any other clip.
- [x] Corrupt/unsupported files and files exceeding the sanity cap produce a clear, specific error — never a crash or silent failure.
- [x] Non-UI logic (file decode to PCM, tag extraction with filename fallback, synthetic session/snapshot construction) is covered by unit tests using small fixture audio files, independent of the UI thread.

## Open questions

- Exact file-picker extension filter list and the exact sanity size/duration cap are implementer's calls — verify actual decode reliability per format at implementation time rather than promising broad format support up front.

## Follow-up ideas

- Re-trim directly from the original imported file (rather than requiring a fresh import) once a catalog entry's live-buffer window has expired — deliberately out of scope for this spec, see "Resolved implementation decisions."
- Consider opt-in FLAC/OGG support after testing the exact codecs available across Hookline's supported Windows versions; Media Foundation support for those formats is less uniform than the formats shipped here.

## What shipped

- Added an "Import audio file..." tray action and standard Windows picker for MP3, WAV, M4A, AAC, and WMA. Current NAudio documentation confirms `MediaFoundationReader` automatically produces PCM for formats Media Foundation can play; Microsoft documents native sources/decoders for the selected formats. FLAC/OGG were intentionally not promised because their availability is less uniform.
- `LocalAudioFileImporter` decodes off the UI thread, performs highest-quality Media Foundation resampling to Hookline's existing 44.1 kHz/16-bit/stereo format, and produces one continuous full-file `AudioBufferSnapshot`. Imports have unique negative instance IDs, while live capture remains positive-only.
- TagLibSharp supplies title, performer (with album-artist fallback), album, and front-cover art. Missing/unreadable tags degrade to a filename-derived title and blank optional fields without blocking a valid audio import.
- The existing `TrimWindow`, `TrimViewModel`, preview, slicer, MP3 exporter, and cataloging exporter remain unchanged. Imported sessions use the same adapter shapes with no default selection, so drag, preview, tagged collision-safe export, and catalog registration follow the established path.
- The sanity limit is 30 minutes and 320 MB of decoded PCM, roughly a long album side. Duration is checked before decoding when available and both duration and byte limits are enforced while decoding. Missing, unsupported, corrupt, empty, over-duration, and over-size inputs surface specific non-crashing errors.

## Review notes (reviewer, 2026-07-27)

- Rebuilt and retested independently: 0 warnings/errors, 73/73 passing (24 NowPlaying.Tests + 20 Audio.Tests + 29 App.Tests).
- Read `LocalAudioFileImporter.cs` in full: `MediaFoundationReader`/`MediaFoundationResampler` decode to the same 44.1kHz/16-bit/stereo format used everywhere else; both duration and decoded-byte caps are enforced *during* streaming decode (not only after fully decoding), so an oversized file fails fast rather than wasting time/memory decoding something that will be rejected anyway. Synthetic track-instance IDs count down via `Interlocked.Decrement`, guaranteeing no collision with real (positive) SMTC instance IDs. Metadata falls back to filename → blank artist/album exactly as specified, with a real front-cover-preferred art lookup.
- Confirmed the adapter pattern held: `ImportedAudioTrimSessionFactory.Create` builds a plain `TrimSession` from an `ImportedAudioFile` with no changes to `TrimWindow`, `TrimViewModel`, `Mp3ClipExporter`, or the catalog — exactly the constraint from "Codex handoff."
- Confirmed tray wiring end-to-end: `AppStrings.TrayImport` ("Import audio file...") → `App.xaml.cs`'s `ImportAudioFile()`, with a concurrent-import guard (`_isImporting`) and a friendly "already running" message rather than allowing overlapping imports.

Clean. Flipping to DONE.
- The catalog schema migrates from version 1 to version 2 so track instance IDs are required to be nonzero rather than positive. This preserves existing positive live entries while admitting reserved negative import IDs. Buffer append still rejects negative IDs; read-only queries accept them and naturally return no live audio, preserving the intended graceful "original audio no longer available" re-trim result.
- Fixture tests cover MP3 and mono/non-44.1 kHz WAV decoding, resampling, continuous ranges, metadata/art and filename fallback, unique negative IDs, corrupt/unsupported input, both caps, imported-session construction, version-1 catalog migration, unavailable re-trim, and an end-to-end imported selection through tagged MP3 export and catalog registration. All 73 Debug and isolated Release tests pass.
- Deviation from the handoff's assumption: the existing catalog schema and buffer-query guard both required positive IDs, so negative synthetic IDs could not actually use the unchanged pipeline. Their validation was generalized from "positive" to "nonzero," with a migration and regression coverage; no imported-file branch was added to the trim/export/catalog workflow.
