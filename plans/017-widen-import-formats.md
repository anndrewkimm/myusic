---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [008]
---

# 017 — Widen local import to cover common downloaded video containers

## Goal

Spec 008 already lets you import a local audio file and export it as a
tagged MP3 through the same trim/preview/export pipeline as a live-captured
Spotify clip — the export step already re-encodes to MP3 regardless of the
source format. Today's file-picker filter only offers MP3, WAV, M4A, AAC,
and WMA, so a plain video file (the most common thing someone actually has
sitting in a downloads folder — e.g. an MP4) isn't importable as-is, even
though `MediaFoundationReader` can typically pull just the audio track out
of a video container. This spec widens the accepted-format list so "whatever
file you already have" imports directly, without a separate conversion step
first.

## User story

I've got a video file (not already an audio-only file) sitting in a folder.
Today I'd have to convert it to MP3 myself before Hookline will open it.
I want to just pick the file directly and have Hookline pull the audio out
and drop me into the same trim screen I already know — no separate
conversion tool needed.

## Why this is (probably) most of the ask, not a new subsystem

Hookline doesn't need a distinct "MP3 converter" feature: export already
converts anything it can import into a tagged MP3
(`plans/008-import-local-audio-file.md`, "What shipped"). The actual gap is
narrower — the file picker's extension allowlist and `MediaFoundationReader`
compatibility haven't been verified against common video containers. Confirm
that first; this spec may turn out to be a small, low-risk addition to
`LocalAudioFileImporter` rather than a new pipeline.

## Resolved implementation decisions

- **Widen the "Import audio file..." file-picker filter** to include common
  video containers people actually download (at minimum MP4; verify MKV and
  WEBM at implementation time — Media Foundation's native container/codec
  support varies more for these than for the audio-only formats spec 008
  already ships).
- **Reuse `LocalAudioFileImporter` unchanged in spirit**: `MediaFoundationReader`
  already decodes to the same PCM pipeline regardless of container; the
  question is purely whether Media Foundation can produce an audio-only
  `IMFSourceReader` topology from a file that also contains a video stream.
  Confirm this at implementation time per container (same "verify current
  API/codec support, don't assume" discipline spec 008 already used) rather
  than promising broad container support up front.
- **A file with no audio track at all** (e.g. a silent/corrupted video) is
  treated the same as any other unsupported/corrupt input from spec 008 —
  clear error, never a crash, never a half-loaded window.
- **No UI beyond the widened file-picker filter** — same "Import audio
  file..." tray entry, same trim/preview/export/catalog flow, no new
  screens. If this needs a distinct UI (e.g. a progress indicator for a
  slow video demux), treat that as a signal the container choice was too
  ambitious for this spec's scope and scale back the supported list instead.

## Edge cases

- Video file with an audio track Media Foundation can't decode (unsupported
  codec inside a supported container) — clear, specific error, not a crash,
  matching spec 008's existing corrupt/unsupported handling.
- Video file with multiple audio tracks (e.g. multiple language tracks) —
  implementer's call whether to pick the first/default track or surface a
  picker; picking the default track and documenting that choice is an
  acceptable minimum.
- Very large video file (video track inflates file size but not decoded PCM
  duration) — the existing sanity cap from spec 008 is on *decoded PCM*
  size/duration, not source file size, so this should already be handled;
  confirm it still holds when the source file is much larger than its
  decoded audio.
- A container extension is added to the picker filter but a real-world file
  in that container fails to decode reliably at implementation time — drop
  it from the shipped filter list rather than shipping a format that mostly
  doesn't work, same judgment call spec 008 already made for FLAC/OGG.

## Acceptance criteria

- [ ] The "Import audio file..." picker accepts at least MP4 in addition to
      spec 008's existing MP3/WAV/M4A/AAC/WMA.
- [ ] Importing a video file with an audio track flows through the exact
      same trim/preview/export/catalog pipeline as any other imported file,
      with no special-casing.
- [ ] A video file whose audio can't be decoded, or that has no audio
      track, produces a clear, specific, non-crashing error.
- [ ] Any container tested but not reliably decodable is left out of the
      shipped filter list, with a one-line note on why (mirrors spec 008's
      FLAC/OGG follow-up note).
- [ ] Existing spec 008 acceptance criteria remain unaffected (no
      regression to MP3/WAV/M4A/AAC/WMA import).

## Out of scope

Fetching audio from a URL (e.g. pasting a YouTube link) is handled by a
separate spec, not here: see `plans/018-import-from-youtube-url.md`. This
spec is only about widening which *already-on-disk* files the existing
picker accepts.

## What shipped

- Widened both the file-picker filter and `LocalAudioFileImporter` allowlist
  to MP4, MKV, and WebM while preserving MP3/WAV/M4A/AAC/WMA.
- Kept the existing import architecture unchanged: `MediaFoundationReader`
  selects the first audio stream and the importer normalizes it into the
  same 44.1 kHz/16-bit/stereo snapshot consumed by the existing
  trim/preview/export/catalog pipeline.
- Verified current support against Microsoft Learn and NAudio 2.x sources.
  This Windows runtime registered MP4, MKV, and WebM Media Foundation
  handlers; representative files in all three extensions imported through
  Hookline's public API. No tested container was excluded.
- A video-only file now maps Media Foundation's invalid-audio-stream result
  to the specific existing message, "The selected file does not contain
  decodable audio." Unsupported codecs still receive the distinct
  corrupt-or-unsupported-audio-codec message.
- Fixed a container-specific metadata edge case found during live fixture
  validation: TagLibSharp can return a null performer collection for MKV,
  which now uses the existing blank-artist fallback instead of escaping as
  an unhandled exception.
- Added filter, extension allowlist, no-audio error mapping, and null
  metadata regression coverage. All 146 tests pass in Debug and Release
  (24 NowPlaying, 55 Audio, 67 App); solution formatting and
  `git diff --check` are clean.
- No scope deviations. As with every Media Foundation import, an accepted
  container can still hold an audio codec unavailable on a particular
  Windows installation; that path now fails clearly without opening a
  partial trim window.

## Review notes (2026-07-31)

Reviewed independently against the diff (commit `81f093a`). All 5 acceptance
criteria and all 4 edge cases verified against actual code, not just the
"What shipped" claims:

- Filter string (`AppStrings.cs`) and `LocalAudioFileImporter`'s extension
  allowlist both include mp4/mkv/webm; single file-picker call site, no
  bypass path.
- No special-casing: `Decode()` is structurally unchanged, same
  `MediaFoundationReader` → `MediaFoundationResampler` → PCM path for every
  format.
- New `LocalAudioImportErrorMapper` correctly distinguishes the
  no-audio-track HRESULT (`MF_E_INVALIDSTREAMNUMBER`) from generic decode
  failure, both still funneled through the existing catch — never an
  unhandled crash. Covered by a direct test.
- Decoded-PCM size cap (not source file size) confirmed unaffected by
  video's larger file size.
- MKV null-artist metadata fix (`JoinArtists` now null-safe) is real and
  tested, not just claimed.
- No scope creep: no YouTube/URL-fetch code leaked in under this spec's
  name (spec 018 stays a doc-only reference in the same commit).
- 146/146 tests pass in Debug and Release; `dotnet format --verify-no-changes`
  clean.

One non-blocking note: acceptance criterion 4 ("drop any container that
doesn't reliably decode") couldn't be fully verified by automated tests
alone, since there are no binary MP4/MKV/WebM fixtures checked into the
repo — the claim that real fixtures were hand-tested during implementation
is trusted but not independently re-verified here. Not a blocker; same
trust level spec 008 already operated at for its own format claims.

Clean. Flipped to `DONE`.
