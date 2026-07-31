---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [008, 017]
---

# 018 — Import audio directly from a URL

## Goal

Collapse "find a song on YouTube, run it through a separate converter site,
download the MP3, then import that file into Hookline" into one step: paste
a video URL into Hookline, it fetches the audio and drops you straight into
the same trim/preview/export/catalog screen as any other import.

## User story

I've got a YouTube link for a song I want a clip of. Today that means
leaving Hookline entirely — a converter website, a download, then finally
importing the result. I want to paste the link into Hookline and have it
land in the trim window directly, the same way an already-downloaded file
does today.

## Why this is a bigger decision than specs 008/017

Owner explicitly decided 2026-07-30 to build this after it was first
flagged as a positioning conflict. `docs/CONVENTIONS.md` was rewritten the
same day: Hookline's live-capture path records local audio output (not a
stream-ripper), but this path genuinely fetches content from a third-party
service, which is a different and higher-risk category. Read the updated
CONVENTIONS.md intro before touching this spec — the constraints below
(single video, no playlists, no bulk fetching, personal-use notice) are
what keep this consistent with the rest of the app's personal-archiving
framing rather than becoming something else.

## Codex handoff

- **Almost the entire pipeline is reused unchanged.** `LocalAudioFileImporter.ImportAsync`
  (`src/Hookline.Audio/LocalAudioFileImporter.cs`) already takes a file path,
  decodes via `MediaFoundationReader`, applies the same duration/size sanity
  caps, and returns an `ImportedAudioFile` with a synthetic negative
  `TrackInstanceId` from its own `_nextSyntheticTrackInstanceId` counter.
  This spec's new code should **download the fetched audio to a temp file on
  disk, then call the existing, already-registered `LocalAudioFileImporter`
  instance on that temp path unchanged** — don't fork or duplicate its
  decode/metadata/ID-allocation logic. Reusing the same registered instance
  (not a second one) is what keeps synthetic track-instance IDs from ever
  colliding between local-file imports and URL imports — no new
  coordination needed there.
- Delete the temp file after import completes, success or failure.
- If you find yourself changing `TrimWindow`, `TrimViewModel`,
  `Mp3ClipExporter`, `ImportedAudioTrimSessionFactory`, or the catalog to
  special-case a URL-sourced clip, stop — the whole point is that
  `ImportedAudioFile` already looks identical regardless of source.
- **Prefer fetching an M4A/AAC audio-only stream specifically** (already a
  supported extension in `LocalAudioFileImporter`) over a WebM/Opus stream.
  If a given video only exposes WebM/Opus audio, note that `.webm` needs to
  already be in `LocalAudioFileImporter`'s supported-extensions set — that's
  spec 017's job, hence the `depends_on: [017]`. Don't add `.webm` support
  redundantly in this spec if 017 already did it.
- New fetch/extraction logic belongs in `Hookline.Audio` (network + decode,
  UI-agnostic, independently testable per `docs/CONVENTIONS.md`), separate
  from the new "Import from URL..." dialog UI in `Hookline.App`.

## Resolved implementation decisions

- **Reachable from a new "Import from URL..." tray menu entry**, alongside
  the existing "Import audio file..." (spec 008) and Open/Library/Exit
  entries.
- **A small dialog**: URL text field, a "Fetch" action, then — before
  anything is downloaded — show the resolved video's title/thumbnail/duration
  for the user to confirm it's the right video (best-effort; exact accuracy
  of thumbnail/duration isn't critical). Only after confirmation does the
  actual audio download begin.
- **Single video URL only.** A playlist or channel URL is explicitly
  rejected with a clear message asking for a single video link — this is
  not a bulk/batch importer, by design, matching the personal-use framing in
  `docs/CONVENTIONS.md`.
- **Download shows progress and supports cancellation.** Unlike spec
  008/017's local-file import (effectively instant), a network fetch takes
  real time; the dialog needs a progress indicator and a working Cancel that
  cleanly aborts the in-flight download and any partial temp file.
- **Metadata**: title from the video's title, artist from the channel name
  (best-effort mapping — YouTube videos don't have real artist/album tags),
  album left blank, thumbnail used as album art. Falls back to the same
  filename-derived title behavior as spec 008 only if video metadata can't
  be resolved at all.
- **Reuses spec 008's existing duration/size sanity caps** unchanged (30
  minutes / 320MB decoded) — no separate cap for this path.
- **One-time, non-blocking in-app notice** the first time "Import from
  URL..." is used, reminding the user this is for personal-use content they
  have the right to use — same pattern as spec 005's one-time Local Files
  hint, not a blocking confirmation dialog every time.
- **Extraction library**: use a pure-.NET library (e.g. YoutubeExplode) to
  resolve stream URLs, avoiding a bundled external process/Python
  dependency, consistent with the rest of the stack's "first-class .NET
  access" preference. Verify current viability at implementation time —
  this specific corner of the ecosystem has faced real takedown/legal
  pressure before (e.g. a 2023 Germany-specific GitHub takedown of a
  similar library over a rights-holder complaint, later reinstated) and
  breaks whenever YouTube changes its internal APIs, so treat "confirm this
  library still works and is still available" as a first implementation
  step, not an assumption. If it's not viable when this is actually built,
  the documented fallback is shelling out to a bundled `yt-dlp.exe` binary
  (more mature, actively maintained, but adds an external binary dependency
  and its own update burden) — must be explicitly flagged if used, same
  "don't silently substitute the fallback" discipline as the WASAPI
  process-loopback fallback in `docs/CONVENTIONS.md`.

## Edge cases

- Invalid/malformed URL, or a URL that isn't a video link at all — clear
  error, no crash.
- Playlist or channel URL — explicitly rejected with a message asking for a
  single video link, not silently importing just the first video.
- Private, deleted, age-restricted, or region-locked video — clear,
  specific error; never a crash or a silent empty import.
- Video with no audio track, or only a codec/container this app can't
  decode — same "clear non-crashing error" handling as spec 008/017.
- Network failure mid-download — clear error, temp file cleaned up, no
  partial/corrupt import reaches the trim window.
- User cancels during fetch or during download — clean abort, temp file
  removed, dialog returns to its initial state, no orphaned background work.
- Video longer than the existing sanity cap — same clear message as spec
  008's oversized-file handling, ideally checked from metadata *before*
  downloading the full stream where the source provides duration upfront.
- Extraction library itself breaks (upstream API change) — surfaces as a
  clear "couldn't fetch this video" error, never an unhandled exception
  reaching the UI thread (per `docs/CONVENTIONS.md`'s background-operation
  rule).

## Acceptance criteria

- [x] Tray menu has a working "Import from URL..." entry, separate from
      "Import audio file...".
- [x] Pasting a valid single-video URL and confirming fetches audio-only,
      decodes it through the existing `LocalAudioFileImporter` unchanged
      (via a temp file), and opens the trim window exactly like any other
      import — full waveform, no default selection.
- [x] Playlist/channel URLs are rejected with a clear message rather than
      importing only the first video.
- [x] Download shows progress and Cancel actually aborts cleanly (no
      orphaned temp files, no background task still running after cancel).
- [x] Video title/channel/thumbnail populate as title/artist/album-art with
      the same fallback discipline as spec 008 when unavailable.
- [x] A one-time personal-use notice appears on first use of this feature
      and not on subsequent uses.
- [x] All edge cases above produce clear, non-crashing, specific errors.
- [x] New fetch/extraction logic lives in `Hookline.Audio`, is UI-agnostic,
      and has unit test coverage independent of the UI thread and independent
      of real network access (fake/stubbed stream resolution).
- [x] No changes to `TrimWindow`, `TrimViewModel`, `Mp3ClipExporter`, or the
      catalog beyond what spec 008 already established.

## Open questions (implementer-level, non-blocking)

- Exact extraction library/version and exact audio-stream-selection logic
  (bitrate/container preference) are implementer's calls — verify against
  the library's current documented behavior at implementation time rather
  than assuming any specific API surface is still current.
- Whether to surface available audio quality/bitrate choices to the user or
  always silently pick the best available — recommend always picking the
  best available, consistent with the app's "no manual steps" ethos
  elsewhere.

## What shipped

- Added a separate tray action and URL-import dialog that resolves one video
  first, shows its title/channel/thumbnail/duration for confirmation, and
  then reports cancellable audio-download progress.
- Added a UI-agnostic `Hookline.Audio` fetch/import pipeline using
  YoutubeExplode 6.6.0. It prefers the highest-bitrate MP4 audio-only stream
  saved as M4A, falls back to WebM audio-only, applies the existing duration
  and decoded-size caps, and always removes its unique temporary directory.
- URL imports reuse the startup-registered `LocalAudioFileImporter` and the
  existing imported-audio trim path unchanged, including synthetic ID
  allocation, full-waveform startup, preview/export, and catalog behavior.
- Added the persisted one-time personal-use notice plus network-independent
  tests for URL validation, preflight limits, metadata mapping, progress,
  cancellation, failure cleanup, dialog state, and shared importer IDs.
- No implementation deviations. Thumbnail retrieval remains best-effort and
  extraction still depends on YouTube's upstream delivery behavior, with a
  specific non-crashing error if it changes.

## Review notes (2026-07-31)

Reviewed commit e209c32 against every acceptance criterion, edge case, and
the Codex-handoff "don't special-case a URL clip" warning by reading every
changed file's full diff, not just the "what shipped" summary. Verdict:
clean, flipped to DONE.

- Confirmed `TrimWindow`, `TrimViewModel`, `Mp3ClipExporter`,
  `LocalAudioFileImporter`, and the catalog are untouched by e209c32
  (`git show e209c32 --stat`) — the warning was heeded.
- `VideoUrlParser` (in `UrlAudioImportService.cs`) rejects non-YouTube
  hosts, malformed URLs, and playlist/channel URLs (`list=` query param,
  `/playlist`, `/channel`, `/c`, `/user`, `/@handle` paths) before any
  network call — verified against 5 theory cases in
  `UrlAudioImportServiceTests.InvalidAndBulkUrlsAreRejectedBeforeResolution`.
- Confirm-before-download flow, real progress (YoutubeExplode's own download
  progress callback, not synthetic), and clean cancellation are all present;
  `UrlAudioImportService.ImportAsync`'s `finally` block always deletes the
  temp file and its unique temp directory, on success, failure, or
  cancellation alike (`UrlAudioImportService.cs:96-101`).
- One-time personal-use notice is a dismissible in-window banner (not a
  blocking `MessageBox`; `UrlImportWindow` opens via `.Show()`, not
  `.ShowDialog()`), persisted to the same JSON settings document as spec
  005's Local Files hint, marked shown on first window open regardless of
  dismissal — `OutputFolderSettingsTests.UrlImportNoticeIsShownOnlyOnce`
  covers persistence across instances.
- Extraction library is YoutubeExplode 6.6.0 (pure .NET, no bundled
  external binary); no yt-dlp fallback was needed or used, so there was
  nothing to flag per the "don't silently substitute" discipline.
- New fetch/import logic lives entirely in `Hookline.Audio`
  (`UrlAudioImportService`, `YoutubeVideoAudioSource`, `IVideoAudioSource`)
  with zero `YoutubeExplode` or network references in `Hookline.App` —
  confirmed by grep. All 6 new `Hookline.Audio.Tests` and dialog-state
  `Hookline.App.Tests` use fakes/stubs, no real network access.
- Matches `docs/CONVENTIONS.md`'s 2026-07-30 URL-fetch paragraph:
  personal-use language in-app, single video only, no bulk/playlist path.
- Tests: Release build/test — 171/171 passed (24 NowPlaying + 69 Audio + 78
  App). Debug — NowPlaying (24) and Audio (69) passed, but
  `Hookline.App`/`Hookline.App.Tests` could not build: `Hookline.App.exe`
  (PID 10984) was running during review and file-locked
  `Hookline.Audio.dll`/`Hookline.NowPlaying.dll` in the App project's output
  dir (`MSB3027`), the same known environmental blocker flagged in a prior
  review earlier today. Not a code defect — no `#if DEBUG` conditionals
  exist anywhere in the new files, and the App-layer code is
  build-config-agnostic. Re-run `dotnet test Hookline.sln -c Debug` once
  that process is closed to get a fully clean Debug run on record.
- Minor process nit only, not a functional gap: the spec's acceptance
  checkboxes were left unchecked by the implementer despite "what shipped"
  claiming completion; checked them off above after independently verifying
  each one against the diff.

## Owner review feedback (2026-07-31)

- Fix the URL-import dialog's runtime failure caused by WPF attempting a
  two-way binding to its read-only progress property.
- Make Ctrl+Alt+H expose the existing capture/local-file/URL-import actions
  without requiring a tray-icon right click. The hotkey now opens the same
  action menu at the cursor; the trim window remains unchanged.

## Review fixes shipped

- Corrected the runtime `ProgressPercent` failure by making every read-only
  URL-dialog binding explicitly one-way.
- Added a real STA/WPF regression test that constructs, shows, lays out, and
  closes the dialog; both Debug and Release now pass all 172 tests.
- Changed Ctrl+Alt+H to open the existing action menu at the cursor, exposing
  capture, local-file import, URL import, and the library without requiring a
  tray-icon right click.
- Live-smoked the complete URL path against a public 3:33 YouTube video:
  metadata resolution, M4A download, Media Foundation decode, metadata
  handoff, waveform snapshot, and temporary-file cleanup all succeeded.

## Review notes (2026-07-31, second pass)

Re-reviewed after the owner's two direct bug reports were fixed. Both
fixes verified against the actual diff (commit on top of `a1f9d0c`), not
just the notes above: the `ProgressPercent` crash was a real WPF
`RangeBase.Value` two-way-binds-by-default footgun against a read-only
property, now explicitly `Mode=OneWay`; the hotkey change reroutes
`Ctrl+Alt+H` through the existing tray context menu rather than adding any
new keybinding/config surface — confirmed no scope creep into a bigger
hotkey-remapping feature. New `UrlImportWindowTests.cs` is a genuine
STA/WPF regression test, exactly the kind that would have caught the
original crash. **172/172 tests pass in both Debug and Release** — the
DLL-lock issue that blocked Debug verification twice earlier today did
not recur this run. Clean. Flipped to `DONE`.
