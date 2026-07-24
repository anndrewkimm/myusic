---
status: READY
---

# Roadmap

## Guiding principle

Every phase should be something you can actually open and use, not just a technical milestone. If a phase ships and you wouldn't bother opening the app, the phase was scoped wrong.

## Phase 1 — "It just works" (this is the MVP, specs 001–004)

The whole point: play a song in Spotify, the app already knows what it is and is already capturing it, you trim the part you like, you get an MP3 with correct tags sitting in your Spotify Local Files folder. No manual steps, no music-theory knowledge required, no editing complexity beyond "drag to select, hit export."

Broken into 4 small, sequentially reviewable specs so nothing gets built on a shaky foundation:

1. **001 — Now-playing watcher**: detect what's playing in Spotify, react to track changes. No audio yet — just prove the metadata pipeline works, visibly (a debug window printing track changes is a fine deliverable here).
2. **002 — Loopback capture + rolling buffer**: capture Spotify's audio output into an in-memory rolling buffer (last N minutes), tied to now-playing track boundaries so a buffer never straddles two different songs. No UI yet beyond a way to dump the buffer to a WAV file to prove it's correct.
3. **003 — Trim UI + export**: the actual app window — live waveform of the buffer, drag-to-select, preview playback, export to tagged MP3.
4. **004 — Clip catalog**: in-app list of everything you've exported (rename, re-trim, delete, reveal in folder). This is the "boom boom, catalog" piece — without it every clip just vanishes into a folder and you lose track of what you saved.

Phase 1 is done when: you can leave the app running, listen to Spotify normally, and whenever you hear something you like, get a clean tagged MP3 clip in two clicks with zero setup per-song.

## Phase 2 — Polish & real-world rough edges (not specced yet)

Things that will matter once Phase 1 is actually being used day to day, but aren't worth designing blind:
- Handling Spotify ads gracefully (pause/flag capture during ad breaks for free-tier accounts).
- Startup behavior (launch with Windows? stay in tray?).
- Global hotkey customization UI.
- Better waveform UX: zoom, snap-to-silence for clean trim edges, fade in/out on export.
- Multiple simultaneous "interesting moments" — quick-mark a timestamp without opening the trim UI, trim later.
- Handling device/output changes mid-capture (headphones unplugged, etc.).

## Phase 3 — Backlog / maybe-never (deliberately unspecced)

Bigger ideas from the original brainstorm, kept but not committed to:
- Stem separation (drums/bass/vocals/other) via Demucs — cool, heavy, only worth it if Phase 1 gets real use.
- Pitch/note visualization — only relevant if there's ever an actual editing use case, not just clip-saving.
- Support for other now-playing sources (YouTube Music, local media players) — the now-playing watcher (spec 001) should be designed with this in mind (an interface, not a Spotify-only hardcode) even though only Spotify ships in Phase 1.
- Chorus/"best part" auto-detection to suggest a trim range instead of you finding it manually.

## Naming

Name: **Hookline** — the part of a song that hooks you, which is exactly the use case (there's a part you like, you catch it). Other candidates considered: Momentune, Tunecatch, Earmark.
