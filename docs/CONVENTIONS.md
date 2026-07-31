# CONVENTIONS

Shared truth for stack, structure, and style. Specs should not repeat this — they should reference it.

## What Hookline is

A Windows desktop app that watches what's playing in the Spotify desktop app, continuously buffers the actual audio output in the background, and lets you trim a piece you like and export it as an MP3 — with zero manual "hit record in time" steps. The live-capture path is not a stream-ripper: it captures system audio output (the same category as recording what's playing through your speakers), not Spotify's encrypted data stream.

Hookline also has two other ways to get audio in: importing a file already on disk (spec 008, widened in spec 017), and — as of the 2026-07-30 decision below — importing directly from a URL (spec 018). That last path genuinely does fetch content from a third-party service rather than recording local output, which is a different and higher-risk category than the live-capture path; spec 018 documents the specific caveats and constraints (single video at a time, no playlists, no bulk fetching, personal-use framing shown in-app).

Personal/single-user/offline use throughout, for content the user has the right to use. No server component, and Hookline never hosts, shares, or redistributes imported content to other users — that's the actual line for "not a distribution service," not "never fetches from a URL." If published to GitHub, framed clearly as a personal-archiving tool, with the URL-import path's caveats stated plainly rather than glossed over.

V1 explicitly does **not** do: stem separation, note/pitch detection, audio effects beyond trim+fade, multi-source support (Spotify desktop only), or cross-platform support (Windows only). Those are backlog ideas — see `plans/000-roadmap.md`.

## Tech stack (decision + reasoning)

**Platform: Windows-only, .NET 8, WPF.**

Why WPF over alternatives:
- The two hardest technical pieces — reading "now playing" track metadata, and capturing only Spotify's audio output rather than the whole system mix — are both native Windows APIs (`GlobalSystemMediaTransportControlsSessionManager` for metadata; WASAPI process-loopback capture for audio). .NET has first-class, well-maintained access to both. Python/Electron would mean fighting P/Invoke or shelling out to do the same thing worse.
- A custom waveform-trim control is the visual centerpiece of this app (and the piece a designer should art-direct) — WPF's `DrawingContext`/`Canvas` give full custom-rendering control without fighting a web-view abstraction.
- Tray icon + global hotkey + tight OS integration are all easy, native, well-documented in WinForms/WPF interop.

If process-specific audio-loopback capture turns out to be too fragile/undocumented when actually implemented (see spec 002), the fallback is full-system-mix `WasapiLoopbackCapture` (rock-solid, in NAudio, no exotic APIs) — acceptable tradeoff is "don't play other loud audio while capturing." This fallback must be explicitly flagged if used, not silently substituted.

**Key libraries (verify current versions/APIs when implementing — don't assume these exact names/signatures are still current):**
- **NAudio** — WASAPI capture, playback, general audio plumbing.
- **NAudio.Lame** — MP3 encoding.
- **TagLibSharp** — ID3 tag + embedded album art writing on exported MP3s.
- **Windows.Media.Control** (WinRT, `GlobalSystemMediaTransportControlsSessionManager`) — now-playing metadata (title/artist/album art/playback state), read-only, no auth required, no Spotify API key needed.
- Process-specific WASAPI loopback: Windows 10 2004+ "process loopback capture" (`AUDIOCLIENT_ACTIVATION_PARAMS` / `PROCESS_LOOPBACK` activation). Confirm current sample/wrapper against Microsoft Learn before building on it — this corner of the API has shifted across SDK versions.

## Project layout

```
hookline/
  Hookline.sln
  src/
    Hookline.App/            # WPF app: windows, tray icon, hotkey, view models
    Hookline.Audio/          # capture, rolling buffer, MP3 export, tagging
    Hookline.NowPlaying/     # SMTC metadata watcher
  tests/
    Hookline.Audio.Tests/
    Hookline.NowPlaying.Tests/
  plans/
  docs/
  CLAUDE.md
  AGENTS.md
```

Each spec should say which project(s) it touches. Keep `Hookline.Audio` and `Hookline.NowPlaying` UI-agnostic (no WPF references) so they're independently testable.

## Style

- C# nullable reference types on.
- No global mutable state outside explicit service classes registered once at startup.
- Prefer small, named classes over anonymous logic in code-behind — view models should be testable without a UI thread where feasible.
- All user-facing strings live in one place (a `Strings` resource or similar) even though this is single-language today — cheap now, annoying to retrofit later.
- Every background operation (capture, encode, tag) must be cancellable and must not throw unhandled exceptions onto the UI thread — surface failures as a status message, never a crash.

## Local storage

- Exported clips + a small local catalog (SQLite via `Microsoft.Data.Sqlite`, or a flat JSON index — decide in spec 004) live under `%LOCALAPPDATA%\Hookline\`.
- The export folder should be user-configurable, defaulting to a `Hookline` subfolder inside wherever the user's Spotify "Local Files" folder is, if it can be detected — otherwise `%USERPROFILE%\Music\Hookline`.
