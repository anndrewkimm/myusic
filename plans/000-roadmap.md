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

A 2026-07-29 conversation (after live-testing the app) surfaced **spec 015 (per-segment effects within a single clip)**: split a trimmed selection into multiple time-ranges via draggable split points on the waveform, and give each range its own independent EQ/stem/sound-effect/edit-preset settings, exported as one continuous file. Its open questions were resolved 2026-07-31, favoring maximum creative control with minimal added UI friction: automatic non-configurable 15ms crossfade at every boundary (reuses the existing spec 009/010/013 fade constant), Speed/Loop-driven length changes sum into total export length with the trim window showing the live total whenever it diverges from the original selection, stem separation runs once and is reused by every segment, and live preview re-renders the stitched buffer on a 300ms debounce then resumes at the proportional position via spec 016's resume-by-fraction mechanism. Also resolved two gaps the original draft left implicit: new splits inherit the parent segment's settings instead of resetting to neutral (don't punish exploration by forcing re-tuning), and removing a split is a double-click with no confirmation dialog (matches spec 003's established no-extra-clicks precedent). Codex picked this up and correctly flagged it `BLOCKED`: the spec never said what gesture *creates* a split in the first place. Resolved 2026-07-31 by extending the same double-click gesture symmetrically — double-click empty waveform space to create a split, double-click an existing split to remove it — one gesture, no new UI chrome, no conflict with the existing click-drag-to-select/resize behavior. Back to `READY` for Codex.

A 2026-07-30 brainstorm surfaced two more items, both spec'd the same day:

**Spec 016 (preview keeps playing through effect changes instead of restarting from 0:00) is `DONE`** — root-caused in code: `TrimViewModel.EffectsChanged` always stopped and restarted `AudioPreviewPlayer`, which had no resume/seek capability at all. Implemented same day by Codex and reviewed same day: build/tests clean (140/140), the fraction-of-duration resume math is correctly `decimal`-based and end-clamped, and the Speed-2x resume scenario was hand-traced correct (400ms into a 1000ms buffer resumes at 200ms into the new 500ms buffer). One non-blocking residual note carried into the spec: no automated test opens a real `WaveOutEvent`, so perceived audio continuity during an actual slider-drag is worth a real listen during normal use. Also touches the same shared preview infrastructure spec 015's still-open "Live preview fidelity" question depends on.
- **Spec 017 (widen local import to accept common downloaded video containers, e.g. MP4) is `DONE`.** Confirmed the gap was exactly the file-picker's extension allowlist, not a missing feature: export already re-encodes anything importable to MP3 (spec 008). Reviewed independently 2026-07-31: build/tests clean (146/146), no special-casing added to the decode pipeline, a real MKV null-artist-metadata edge case was found and fixed (not just claimed), and the no-audio-track error path is distinct from the generic decode-failure path. One non-blocking note: the "drop unreliable containers" criterion rests on the implementer's manual fixture testing rather than checked-in binary fixtures — same trust level spec 008 already operated at.
- **Spec 018 (import audio directly from a URL, e.g. a YouTube link) is `DONE`.** Owner explicitly decided 2026-07-30 to build this despite the positioning conflict flagged when it first came up: `docs/CONVENTIONS.md` was rewritten the same day to describe it honestly (a third-party fetch, a genuinely different risk category from live capture) rather than pretend it fits the old "not a downloader" framing unchanged. Scoped tightly — single video URL at a time, no playlists, no bulk fetching, personal-use notice shown in-app, same adapter reuse pattern as specs 008/017. Reviewed independently 2026-07-31: build/tests clean (171/171 Release; Debug clean except App-layer tests blocked by a running Hookline instance locking its own DLLs — not a code defect), zero special-casing leaked into `TrimWindow`/`TrimViewModel`/`Mp3ClipExporter`/the catalog exactly as the spec demanded, YoutubeExplode used with no fallback needed, all new fetch logic UI-agnostic in `Hookline.Audio` with fake/stubbed network tests.

A 2026-07-31 conversation (owner testing the app live) surfaced two more ideas:

- **Spec 019 (mix two clips into one track)** — owner's own framing: cut a part of one song, combine it with another. Three materially different features hide under "mix songs together" at very different effort/risk levels; owner chose 2026-07-31 to sequence more than one rather than commit to just one: (A) simple two-source overlay with independent volume, no tempo matching — **this is spec 019's actual scope, `READY`**; (B) stem-swap mashup reusing spec 011's existing separation (e.g. vocals from one clip, instrumental from another) — intentionally **not** part of 019, queued as its own future spec once 019 has shipped and seen real use; (C) full DJ-style beat-matched mixing with tempo detection and time-stretching — stays in Phase 3 below given its real technical risk (naive time-stretching audibly degrades audio) and DJ-software-grade scope, not sequenced next. Grounded a constraint worth remembering: Spotify plays one track at a time, so this is necessarily built from already-captured/imported clips, never live simultaneous dual-capture. Output-length policy: the longer of the two sources wins, the shorter one loops (reusing spec 009's existing seamless loop) rather than either clip getting silently cut short.
- **Spec 020 (contextual (i) help icons on every effect control)** — owner's ask was explicitly *not* to simplify or hide any control, just make each one (Speed/Reverb/8D/Loop, the 10-band EQ, stem volume sliders, presets) explain itself on hover for someone still learning what it does. Drafted with exact tooltip copy for every control already written, plus keyboard-focus and `AutomationProperties.HelpText` accessibility wiring alongside the hover behavior. No real open fork — `DRAFT` pending only a quick owner glance at the copy/tone before flipping to `READY`.

The same conversation surfaced one more: owner's music discovery happens on Spotify, not YouTube, and asked whether a Spotify link could be turned into an MP3 the way a YouTube link (spec 018) can. It can't be, directly — Spotify's API only ever exposes metadata and short previews, never full audio, as a licensing boundary rather than a technical gap — but the real workaround (the same approach tools like spotDL use) is legitimate: resolve the Spotify link's public metadata, search YouTube for the matching video, confirm it with the user, then hand off to spec 018's existing fetch flow unchanged. **Spec 021 (import audio by pasting a Spotify link)** specs exactly that, `READY`. `docs/CONVENTIONS.md` was updated the same day (2026-07-31) to describe this honestly — it's a metadata-assisted convenience layer in front of spec 018's existing fetch, not a second fetch mechanism, and it inherits every one of 018's constraints. Key decisions: one shared "Import from URL..." dialog auto-detects a Spotify track link instead of adding a second menu entry (fewer things for the owner to remember); a visible "looking up this track" step and mandatory match confirmation before any download, since the YouTube match is search-based and never guaranteed correct; oEmbed (no setup) as the default metadata source with the official Web API as a documented, explicitly-flagged fallback if oEmbed's response proves insufficient, using the owner's own free Spotify Developer credentials rather than a bundled secret (this repo may end up on GitHub). Depends on spec 018 actually landing first — sequenced, not blocking 018's own priority.

Two new specs came out of a 2026-07-28 conversation about giving clips a "TikTok edit" feel:

- **Spec 013 (reverb, 8D auto-pan, and one-click "Slowed + Reverb"/"Sped Up"/"8D Audio" presets)** is `DONE` — implemented 2026-07-28, a test-coverage gap flagged in review was closed and re-verified 2026-07-29 (133/50 tests passing across the suite).
- **Spec 012 (playful "band view" for the spec-011 stem remixer — characters instead of sliders)** was implemented after 013 as planned and is `DONE` — reviewed 2026-07-29, all 8 acceptance criteria verified against code and tests, no gaps.

**Spec 014 (fix: trim/catalog windows can get permanently stuck invisible after a failed show) jumped the queue on 2026-07-28**, same reasoning as spec 006, and is now `DONE`. Root cause was two-fold: a WPF binding on `TrimWindow.xaml`'s `StemProgressPercent` `ProgressBar` throwing on every render (fixed with an explicit `Mode=OneWay`), plus a structural defect in `App.xaml.cs` assigning `_trimWindow`/`_catalogWindow` before `Show()` ran with no reset on failure, permanently wedging retries. Fixed via a shared `ManagedWindowSlot<TWindow>` lifecycle helper applied to the trim, catalog, and import-window paths, with regression tests and live-hotkey verification. Re-verified again 2026-07-29 after the owner hit a real-world hotkey failure (caused by no Hookline process running at the time, not a regression) — rebuilt, relaunched, and reconfirmed working.

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
- ~~Paste-a-URL fetch + convert~~ — surfaced 2026-07-30, owner decided the same day to build it. See spec 018 (and spec 017 for the narrower "already-on-disk file" version of this same itch). No longer a backlog maybe — moved up to specced/`READY` above.

## Naming

Name: **Hookline** — the part of a song that hooks you, which is exactly the use case (there's a part you like, you catch it). Other candidates considered: Momentune, Tunecatch, Earmark.
