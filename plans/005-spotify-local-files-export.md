---
status: DONE
touches: [Hookline.App]
depends_on: [003]
---

# 005 — Default export location: Spotify Local Files

## Goal

Right now (spec 003) the default export folder is just `%USERPROFILE%\Music\Hookline`, with no attempt at the "ideal" default `docs/CONVENTIONS.md` originally described: a `Hookline` subfolder inside wherever the user's Spotify Local Files folder actually is, if that can be detected. This spec closes that gap — as far as it actually can be closed, given a real constraint discovered during investigation (see below).

## Investigation findings (reviewer, 2026-07-27)

Before specifying a detection mechanism, it's worth being precise about what's actually possible here, because it's more constrained than the original "if it can be detected" phrasing implied:

- Spotify desktop stores its configured Local Files scan folders in `%APPDATA%\Spotify\Users\<user-id>\watch-sources.bnk` and `local-files.bnk`. Confirmed on this machine: both files exist but are tiny (33–56 bytes) with no readable path strings — this installation has **zero Local Files sources configured**. These are undocumented, almost certainly protobuf-encoded, proprietary files. There is no public schema for them.
- Critically: **Spotify does not expose any API for a third-party app to register a folder as a Local Files source.** That's exclusively a manual toggle in Spotify's own Settings → Local Files → "Add a source". No amount of detection logic in Hookline changes this — even a perfectly-placed export still won't appear in Spotify until the user has pointed Spotify at that folder (or a parent of it) at least once, themselves, in Spotify's UI.
- So "fully automatic, zero manual steps" isn't actually achievable end-to-end. What *is* achievable: (a) if the user has already configured a Local Files source, detect and default into it so exports land somewhere Spotify already watches; (b) if not (this machine's current state), make the one remaining manual step (adding the source in Spotify once) as obvious and low-friction as possible instead of leaving the user to discover the disconnect themselves.

## Codex handoff

- This is a small, self-contained follow-up to spec 003 — touches `Hookline.App`'s `OutputFolderSettings` and the trim window's folder display, nothing else.
- Treat the `.bnk` parsing as inherently best-effort against an undocumented, versioned-by-Spotify format. It must never throw, never block startup, and must degrade to today's existing `Music\Hookline` fallback on any parse failure or format-shape surprise — same "no silent wrong guess" discipline as spec 002's loopback fallback.
- Verify the current file locations/format shape against a real installation before relying on this spec's investigation notes as gospel — Spotify can and does change its local storage layout between versions without notice.

## Resolved implementation decisions

- **Detection target:** attempt to parse `%APPDATA%\Spotify\Users\<user-id>\watch-sources.bnk` (there is exactly one `<user-id>` subfolder per logged-in account under `%APPDATA%\Spotify\Users\`; if there are multiple — e.g. multiple accounts have logged into this machine — use whichever was most recently modified). If it can't be parsed, or parses to zero folders, or the file/folder doesn't exist: fall back to today's `%USERPROFILE%\Music\Hookline`, silently in the sense of "no error dialog," but see the next point.
- **Default folder becomes:** the first successfully-detected Local Files source folder, plus a `Hookline` subfolder inside it (matching the original CONVENTIONS.md phrasing) — only on first run / when no output folder has been explicitly set yet. Once a user has used "Change..." to pick a folder (spec 003), never override that choice with a re-detection.
- **When no Local Files source is detected at all** (this machine's current state): keep the existing `Music\Hookline` fallback, but the trim window's "Saves to" area gets a small one-time dismissible hint: something like "Spotify isn't watching this folder yet — add it in Spotify's Settings → Local Files to see clips there." Dismissed state persists (don't nag every time the window opens once the user has dismissed it once, regardless of whether they actually went and added the source — this is a one-time nudge, not a nagging validation loop).
- **No polling/watching for Spotify's config changing later.** Detection runs once, at `OutputFolderSettings` construction (app startup), same lifecycle as today's default-folder logic. If the user adds a Local Files source in Spotify after Hookline is already running, that's picked up next app restart, not live.

## User story

As the user, I never think about where clips go — they're just already in Spotify's Local Files the next time I look, the same way spec 003's export already "just works" for everything else. If that's genuinely not possible yet because I've never told Spotify to watch any folder, I get told clearly, once, exactly what one thing to do about it — not left to wonder why my clips aren't showing up.

## Edge cases

- Spotify never installed, or installed but never run (no `%APPDATA%\Spotify\Users\` folder at all) — detection finds nothing, falls back cleanly, no error.
- Multiple Spotify accounts used on this machine (multiple user-id folders) — use the most recently modified one; don't guess wrong and don't error out picking between them.
- The `.bnk` file exists, is non-empty, but doesn't parse the way expected (format changed, corrupted, whatever) — treat exactly like "not found." Never crash, never partially apply a garbled path.
- User already explicitly set a custom output folder via spec 003's "Change..." button — detection must never override that, including across app restarts.
- User adds a Local Files source in Spotify *after* first launching Hookline — no live re-detection; a later manual "Change..." to point at it, or a fresh app restart, are the two ways this gets picked up. This is acceptable (see "Resolved implementation decisions") but should be discoverable, not a mystery.

## Acceptance criteria

- [x] On a machine with at least one configured Spotify Local Files source, first-run default output folder is a `Hookline` subfolder inside it, not `Music\Hookline`.
- [x] On a machine with none configured (this one, today), behavior is unchanged from spec 003 (falls back to `Music\Hookline`) plus the one-time dismissible in-app hint described above.
- [x] A previously user-chosen output folder (via "Change...") is never silently overridden by detection, on this run or any future one.
- [x] Any parse failure of Spotify's config is invisible to the user except as a clean fallback — no crash, no error dialog, no partially-applied bad path.
- [x] Non-UI detection/parsing logic is covered by unit tests against sample/fixture `.bnk`-shaped inputs (including empty, malformed, and multi-account cases), independent of an actual Spotify installation.

## Open questions

- The exact byte layout of `watch-sources.bnk` needs to be reverse-engineered or found documented somewhere at implementation time — this spec's investigation confirmed the file's *existence and location* but not its internal format (this machine's sample files were empty, which confirms the "no sources" case works trivially but doesn't give a populated example to reverse-engineer from). If a populated sample isn't obtainable during implementation, ship the "detect nothing → fallback + hint" path only, and note that limitation explicitly in "What shipped" rather than guessing at a format with no ground truth to verify against.

## Follow-up ideas

- Live-watching Spotify's config for a newly-added source while Hookline is already running, instead of requiring a restart.

## What shipped

- Added a bounded, best-effort `SpotifyLocalFilesSourceDetector`. Implementation-time verification against this machine's now-populated Spotify data confirmed the current `SPCO` bank envelope, gzip-compressed `WatchSources` payload, and directory-record shape. Standard watched folders are detected from that data; an additional conservative `LocalFilesStorage` path parser can identify a safe watched subtree for non-standard sources that already contain an indexed local file.
- First-run output now resolves to `<detected source>\Hookline`. If the detected source is itself named `Hookline`, Hookline uses it directly rather than creating `Hookline\Hookline`. A persisted folder chosen through "Change..." always wins.
- When detection is unavailable or either undocumented bank shape changes, startup falls back without error to `Music\Hookline`. The trim window shows a centralized, one-time "add this folder as a Spotify source" hint whose dismissal persists. A later app restart still adopts a newly-added Spotify source when no explicit Hookline folder was selected.
- Added independent fixture coverage for the populated compressed shape, indexed-file fallback, missing/malformed data, multiple accounts, explicit-setting precedence, restart re-detection, dedicated-folder handling, and hint dismissal.
- Deviation from the narrow handoff: `local-files.bnk` is used only as a safe supplementary fallback when `watch-sources.bnk` cannot identify a standard source. This improves custom-source coverage without guessing a path from an ambiguous directory tree. Both formats remain intentionally best-effort because Spotify does not publish them.

## Review notes (reviewer, 2026-07-27)

- Rebuilt and retested independently: 0 warnings/errors, 64/64 passing.
- Read `SpotifyLocalFilesSourceDetector.cs` in full: bounded bank/payload sizes (16MB/64MB caps before decompressing, guarding against a corrupt-or-hostile file), a narrow try/catch covering only the expected failure modes (IO/permissions/security/malformed-data), and detection restricted to a fixed list of well-known OS folders (Music, Downloads, Desktop, Documents, Videos) rather than attempting to reconstruct arbitrary custom paths from an undocumented format — an appropriately conservative scope given there's no public spec to verify against.
- Read `OutputFolderSettings.cs`: detection only runs when no explicit folder is already saved, never overrides a "Change..." choice, and the one-time hint dismissal is persisted correctly (including implicitly-dismissed once the user picks a folder manually).
- Matches the resolved decisions: no live re-detection, `Hookline\Hookline` double-nesting avoided, fallback is silent (no error dialog) with the one-time in-app hint instead.

Clean. Flipping to DONE.
