---
status: DONE
touches: [Hookline.App, Hookline.Audio]
depends_on: [003, 004]
---

# 014 — Fix: trim/catalog windows can get permanently stuck invisible after a failed show

## Goal

The global hotkey (Ctrl+Alt+H) that opens the trim window — the app's single primary entry point, per `plans/000-roadmap.md`'s whole premise of "leave the app running... whenever you hear something you like, get a clean tagged MP3 clip in two clicks" — stopped responding entirely during live testing on 2026-07-28. This is find-and-fix, prioritized above specs 012/013's further feature work, the same way spec 006 jumped the queue for a severity reason.

## Severity / why this jumps the queue

This breaks the app's one required interaction. If it fails once, silently, and never recovers without a full app restart, the app fails its own core promise on exactly the kind of ordinary use it's meant for.

## Reviewer's findings (2026-07-28) — confirmed root cause, plus a separate structural defect

- Live repro: user pressed Ctrl+Alt+H, nothing visibly happened. The user then captured the actual tray error balloon (previously invisible to the reviewer, who only had command-line access to the machine): **"Hookline could not open the trim window. A TwoWay or OneWayToSource binding cannot work on the read-only property 'StemProgressPercent' of type 'Hookline.App...'"**. This is the confirmed root cause, superseding the buffer-timing hypothesis originally written here.
- **Confirmed defect**: `TrimWindow.xaml:663` binds a `ProgressBar`'s `Value` to `StemProgressPercent` with no explicit `Mode`: `Value="{Binding StemProgressPercent}"`. `TrimViewModel.cs:252` declares `StemProgressPercent` with `private set` — read-only from outside the view model. `ProgressBar.Value` is `RangeBase.ValueProperty`, the same dependency property `Slider` uses, and its framework metadata defaults to two-way binding (so `Slider` works out of the box) — `ProgressBar` inherits that same default even though it's normally read-only/display-only. WPF doesn't catch this at compile time; it only throws when the binding is evaluated, i.e. the first time the window actually tries to render — which is every single time the trim window opens, unconditionally. This explains the 100%-repeatable failure, not an intermittent one tied to buffer state.
- This binding predates today (`StemProgressPercent` is spec 011's stem-isolation progress control), but spec 013 modified both `TrimWindow.xaml` and `TrimViewModel.cs` in the same session this broke in. Confirm at implementation time whether spec 013 changed something that newly exposed this (e.g. restructured the stem panel so this element now participates in initial layout when it didn't before) or whether it was already broken and simply hadn't been exercised via a live window yet — spec 011's own review only covered build/tests/code reading, never opening a real window, so a XAML-only runtime binding error would have slipped past it entirely regardless of spec 013.
- **The fix for the confirmed cause is small and mechanical**: the binding needs `Mode=OneWay` (a `ProgressBar` should never write back to its source). Grep the rest of `TrimWindow.xaml` for other `RangeBase`-derived read-display bindings (any other `ProgressBar`) that might have the same unset-`Mode` gap.
- **Separately, a real structural defect exists independent of this specific trigger**: read `App.xaml.cs`'s `ShowTrimWindow()` (~line 113) — `_trimWindow = window;` is assigned *before* `window.Show(); window.Activate();` run, and the surrounding `catch (Exception exception)` block does **not** reset `_trimWindow` back to `null` on failure. Once any exception occurs during construction/show (this one, or a future unrelated one), `_trimWindow` permanently references a broken window, and every subsequent call to `ShowTrimWindow()` — reached by *both* the global hotkey and the tray icon's own menu entry (same method, wired at construction ~line 38) — takes the "reuse existing window" branch (~lines 126-134), which only handles `WindowState.Minimized` and calls `.Activate()` with no enclosing try/catch and no visibility check, so the user sees nothing on retry either, with no recovery short of restarting the whole process.
- **This structural defect is not trim-window-specific.** `ShowCatalogWindow()` (~line 315) has the identical shape: `_catalogWindow = window;` assigned before `Show()`, same unreset `catch`, same unguarded `.Activate()`-only reuse branch. `ImportAudioFile()`'s window path (~line 226, keyed in a dictionary rather than a single field) should be checked for the same defect shape too.

## Codex handoff

- Fix the confirmed cause first: set `Mode=OneWay` on the `StemProgressPercent` `ProgressBar` binding at `TrimWindow.xaml:663`, and audit for the same unset-`Mode`-on-a-read-only-property gap elsewhere in the file.
- Then fix the structural defect, independent of this specific trigger: any failure in a window's create-and-show path must leave the app able to retry cleanly on the next attempt — at minimum, reset the relevant field (`_trimWindow`, `_catalogWindow`, and the import-window dictionary entry) to `null`/removed on failure, so the "already open, just reuse it" branch is never left pointing at a broken window.
- Also harden the reuse branch itself: before assuming an existing window reference is good, confirm it's actually usable (e.g., check `IsVisible`/`IsLoaded`) rather than unconditionally trusting a non-null reference forever. If it's not in a good state, fall through to recreating it instead of silently no-op'ing.
- Apply the resilience fix shape consistently across `ShowTrimWindow()`, `ShowCatalogWindow()`, and the import-window path — this is one systemic pattern in the window-lifecycle code, not three separate bugs. A small shared helper is reasonable if it avoids repeating the same fix three times, but isn't required if the three call sites are different enough that a helper would just add indirection — implementer's call.
- The resilience fix matters even though the immediate trigger is now understood: it's the difference between "one bad binding breaks the app until restart" and "one bad binding shows an error and the next attempt still works." Don't treat the one-line binding fix as a substitute for it.

## Edge cases

- Pressing the hotkey with genuinely no current track detected — already handled today via the existing `_watcher?.CurrentTrack is not { } track` branch (shows `AppStrings.NoCurrentTrack`); confirm this path is unaffected by whatever fix is made here.
- Pressing the hotkey (or the tray menu entry) multiple times in rapid succession while a window is still being constructed — must not create duplicate windows or double-subscribe the `Closed` handler.
- A track change while the trim window is already open and healthy — confirm existing behavior isn't regressed.
- The specific repro condition (hotkey pressed very soon after a track starts, before much has been captured) — confirm this either no longer fails, or fails in a way that's now recoverable (clear tray error, retry works) rather than silently and permanently.
- After a genuine failure (of any cause), pressing the hotkey again must either succeed or show a clear tray error — never silently do nothing a second time.

## Acceptance criteria

- [x] The `StemProgressPercent` `ProgressBar` binding (and any sibling bindings with the same unset-`Mode`-on-a-read-only-property gap) is fixed so the trim window opens successfully every time, confirmed by a live hotkey press, not just by code inspection.
- [x] `ShowTrimWindow()`, `ShowCatalogWindow()`, and the import-window path all correctly clear their window reference/state on any construction-or-show failure, so a subsequent attempt can retry cleanly instead of getting permanently stuck.
- [x] The "reuse existing window" branches no longer trust a stale/broken reference unconditionally — a window that isn't actually in a usable state is detected and recreated rather than silently no-op'd on.
- [x] A live repro (press hotkey, confirm the trim window opens and is visible) succeeds, and a forced failure (e.g. a temporarily reintroduced bad binding, or any other injected construction failure) confirms recovery on the next attempt without needing to restart the app.
- [x] A regression test exists that would have caught the "stuck after failure" pattern specifically, independent of this specific binding bug — name explicitly, per spec 006's precedent, any gap between what's testable in CI (no live WPF window, no real hotkey, so a raw XAML binding-mode error like this one isn't caught by the existing unit tests) and what was actually verified live.
- [x] No regression to the existing "no current track" tray message, rapid-press behavior, or normal track-change-while-open behavior.

## Open questions

- Resolved during review: `git diff` confirms spec 013's uncommitted changes to `TrimWindow.xaml` don't touch the `StemProgressPercent` binding line at all — this bug is pre-existing, latent in spec 011's already-committed, already-`DONE` code. It was never caught because spec 011's review (build, 102 unit tests, code reading) never actually rendered a live WPF window, so a XAML-only runtime binding-mode error had no way to surface. Worth a note in spec 011's own review notes once this is fixed, and a reminder that "build + unit tests + code reading" is structurally unable to catch this whole class of bug going forward.

## Follow-up ideas

- Add an STA/live-WPF render smoke test if CI gains a reliable interactive Windows desktop; build and ordinary unit tests still cannot evaluate every runtime binding metadata combination.
- Decide and spec launch-at-login behavior. The global hotkey is registered by the tray process, so it cannot respond when Hookline is not running; the repository currently has no Windows Startup shortcut, `Run` registration, installer, or other auto-launch mechanism.

## What shipped

- Fixed the confirmed runtime exception by making the read-only `StemProgressPercent` `ProgressBar.Value` binding explicitly `Mode=OneWay`. The XAML audit found no sibling `ProgressBar` binding with the same gap.
- Added a UI-thread-agnostic `ManagedWindowSlot<TWindow>` lifecycle transaction and applied it consistently to the primary trim window, catalog window, and imported-audio window registry. A construction, subscription, show, or activation failure now clears the reference and attempts to close any partial window before the error is surfaced.
- Existing windows are reused only while both loaded and visible. Stale/invisible windows are discarded and recreated, minimized healthy windows are restored, and a reentrant open request during `Show()` is treated as already in progress so it cannot create a duplicate or double-subscribe `Closed`.
- The import registry now removes its entry both from the window's `Closed` callback and explicitly on construction/show failure. Shutdown closes each lifecycle slot and clears the registry.
- Added seven `ManagedWindowSlotTests` covering constructor failure, `Show()` failure followed by a successful retry without restart, stale-window replacement, activation failure, healthy reuse, reentrant rapid-open suppression, registry cleanup, and normal close cleanup. These tests catch the systemic "failed show leaves a poisoned reference" bug independently of the specific XAML trigger.
- Live verification on the rebuilt Release app succeeded against the running Spotify session: Ctrl+Alt+H opened a visible, responsive `Hookline — capture a moment` window (process 8080, handle 722990). Three additional rapid hotkey presses reused that exact handle and left the process responsive.
- CI gap, stated explicitly: the lifecycle failure/retry behavior is fully covered without a UI thread, but ordinary CI unit tests still do not render the real WPF/BAML window and therefore would not independently discover a future raw XAML binding-mode mistake. This spec's concrete binding fix was verified by the live hotkey render above.
- The no-current-track branch and healthy-window/track-change reuse flow remain in their original order and behavior; the new slot only replaces reference ownership and failure cleanup. All 120 tests pass in Debug and Release (24 NowPlaying, 49 Audio, 47 App); formatting and `git diff --check` are clean.
- Re-verified after the owner's 2026-07-29 report. No Hookline process was running at the time of the failed physical hotkey press, so no process existed to receive the registered hotkey. Rebuilt and launched the Release tray app, sent a real Ctrl+Alt+H keystroke through the interactive desktop, and confirmed a visible `Hookline — capture a moment` window. Escape closed it while leaving the tray process responsive; three immediate Ctrl+Alt+H presses then reopened one visible, responsive window in the same process. The rebuilt app was deliberately left running. Debug and Release again pass all 120 tests, `dotnet format --verify-no-changes` passes, and `git diff --check` is clean.

## Review notes

All 6 acceptance criteria verified against actual code and tests: `TrimWindow.xaml:663` confirmed `Mode=OneWay`, with no sibling `RangeBase` binding gap elsewhere in the file; `ManagedWindowSlot.cs` genuinely clears state and attempts to close partial windows on any construction/subscribe/show/activate failure; the reuse path checks `IsLoaded`/`IsVisible` and discards stale windows; reentrancy is guarded; all 7 `ManagedWindowSlotTests` are real failure/recovery exercises, independently run (120/120 total suite passing). No regressions to the no-current-track or track-change paths. The live-hotkey verification claims are Codex's self-reported manual testing and inherently outside what code review or CI can confirm — the spec already states this limitation explicitly rather than papering over it, so it's accepted as a known, named gap rather than a defect. Accepted as DONE.
