---
status: READY
---

# Roadmap

## Guiding principle

Every phase should be something you can actually open and use, not just a technical milestone. If a phase ships and you wouldn't bother opening the app, the phase was scoped wrong.

## Current implementation posture (2026-07-27)

Phase 1 is fully implemented and reviewed. All four specs are DONE:

- Spec 001 (now-playing watcher) — verified live against a real Spotify session.
- Spec 002 (loopback capture + rolling buffer) — verified live: process-loopback capture, pause/ad exclusion, and track-segmentation/no-bleed all confirmed by ear.
- Spec 003 (trim UI + export) — design pass on 2026-07-27 resolved six open UX calls, Codex implemented it the same day. Independently reviewed: build/tests/code all check out, live smoke test confirmed correct rendering (dark theme, metadata, honest empty-buffer state, no default selection). One residual note, not a blocker: bringing the window to the foreground from an automated/background trigger didn't visibly succeed in testing — plausibly a Windows focus-lock artifact of non-interactive testing rather than an app bug; worth confirming with a real physical hotkey press during normal use.
- Spec 004 (clip catalog) — planned and implemented the same day: SQLite-backed catalog, separate tray-reachable window, rename/delete/reveal/re-trim all wired with rollback-safe persistence (quarantine-based delete, tag-rollback rename, orphan-cleanup export registration). Independently reviewed against every resolved decision — clean.

Full solution: 0 build warnings, 55/55 tests passing.

**Spec 006 (memory growth fix) is DONE.** Root cause: SMTC/WASAPI clock skew (~0.15ms per packet) was misread as thousands of real timeline gaps, each rendered as a filled rectangle plus ~24 hatch lines on the waveform — that's what was ballooning memory, not the audio buffer itself (which was always correctly bounded). Fixed by snapping packets within a 5ms jitter tolerance onto a continuous timeline, plus a sub-pixel rendering guard as defense in depth. Confirmed by both a targeted regression test and the owner's own real-world 35-minute run.

**Spec 005 (Spotify Local Files export) is also DONE.** Best-effort detection of Spotify's configured Local Files folders (parsing its undocumented `watch-sources.bnk`/`local-files.bnk`), defaults new installs into a detected folder when one exists, and shows a one-time in-app hint when none is configured yet (since Spotify itself has to be told to watch a folder — no app can do that for it).

Phase 1's exit bar is now genuinely met: all of specs 001-006 are `DONE`. Phase 2's backlog (below) remains unspecced by owner's choice for now.

Spec 007 (search a song from inside Hookline, auto-play it in Spotify) was drafted but never implemented, and was dropped by owner's decision 2026-07-27 — its only value was skipping an alt-tab to Spotify's own (better) search, not worth building. Removed from `plans/`.

Spec 008 (import a local audio file — MP3/WAV/M4A/AAC/WMA already on disk — into the same trim/preview/export/catalog pipeline as a live-captured clip) is `DONE`. Reviewed independently: build/tests clean, decode/caps/metadata-fallback logic all check out, and the adapter pattern held — no changes needed to the existing trim/export/catalog pipeline.

Spec 009 (clip sound effects: speed change, bass boost, loop/extend, applied live in the trim window before export) is `DONE`. Reviewed independently: build/tests clean (85/85), the neutral-defaults-means-no-regression guarantee is structural (returns the same object reference, not just equivalent bytes) rather than merely tested, and preview/export share the exact same processing call so they can't drift apart.

Spec 010 (10-band graphic equalizer with one-click character presets — Bass Boost, Treble Boost, Vocal, Bright, Mellow — grounded in Sony Headphones Connect's real preset naming and standard ISO EQ band frequencies) is `DONE`. Reviewed independently: build/tests clean (92/92), per-stage clamping between cascaded filters (not just at the end) is a genuinely careful detail, and the neutral fast-path guarantee carries forward correctly from spec 009. Replaced spec 009's single bass-boost knob rather than keeping both.

Spec 011 (stem isolation — vocals/bass/drums/other, modeled on iZotope RX's Music Rebalance, via a local ONNX-exported Demucs model through `Microsoft.ML.OnnxRuntime`) is `DONE`. Reviewed independently 2026-07-28: build/tests clean (102/102), all 7 acceptance criteria and all 7 edge cases verified against actual code — export path has zero stem-related special-casing, overlap-add math hand-traced correct, cancellation/staleness handling solid. Two minor non-blocking hardening notes left in the spec (a shutdown-ordering edge case, redundant re-hashing per click) but nothing that blocked DONE. Explicitly scoped to a real, verified granularity ceiling — 4 solid stems, optionally 6 with acknowledged quality tradeoffs — not per-instrument isolation, which isn't achievable with current technology regardless of tool.

**All of Phase 1 through the originally-scoped backlog (specs 001-011) is now `DONE`.** The product goal stays the same: leave the app running, hear something interesting, and get a clean clip with minimal effort.

Two new specs came out of a 2026-07-28 conversation about giving clips a "TikTok edit" feel:

- **Spec 013 (reverb, 8D auto-pan, and one-click "Slowed + Reverb"/"Sped Up"/"8D Audio" presets)** was implemented by Codex the same day and is at `REVIEW`, awaiting independent review.
- **Spec 012 (playful "band view" for the spec-011 stem remixer — characters instead of sliders)** is `DRAFT`, intentionally held back — going in order, 013 first.

**Spec 014 (fix: trim/catalog windows can get permanently stuck invisible after a failed show) jumped the queue on 2026-07-28**, same reasoning as spec 006: it breaks the app's one required interaction (the global hotkey that opens the trim window went silently unresponsive during live testing). Root cause traced to `App.xaml.cs` assigning `_trimWindow`/`_catalogWindow` before `Show()` runs, with no reset on failure — the same defect shape in both the trim and catalog window paths. `READY` for Codex, ahead of 012.

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
