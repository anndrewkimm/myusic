# Hookline

A Windows app that watches what you're listening to in Spotify, continuously captures the audio in the background, and lets you trim the part you like into a tagged MP3 — no manual recording, no downloading a file yourself.

**How it captures audio:** Hookline records Spotify's audio *output* (the same category as recording what's playing through your speakers), not Spotify's internal data stream — it isn't a stream-ripper or DRM-circumvention tool.

**Intended use:** personal archiving of moments from music you already have legitimate access to — not a distribution or download service. If you use this, that's on you to keep to personal use.

## Status

Early planning. See `plans/000-roadmap.md` for the phase breakdown, and `docs/CONVENTIONS.md` for the tech stack and project layout. This repo follows a Claude-plans/Codex-implements workflow — see `CLAUDE.md` and `AGENTS.md`.

## Stack

Windows, .NET 8, WPF. See `docs/CONVENTIONS.md` for why.
