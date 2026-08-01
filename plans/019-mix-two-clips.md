---
status: READY
touches: [Hookline.App, Hookline.Audio]
depends_on: [003, 004, 008, 009, 011, 014, 022]
---

# 019 — Mix two clips into one track

## Goal

Let someone combine two separately-captured/imported clips into a single
output, instead of Hookline only ever handling one continuous source at a
time. Owner's own framing: "I could cut this same part of the song and then
combine it with this song" — a mashup/layering feature, not just sequential
editing of one track.

## User story

I've got two clips I like — say, a moment from one song and a moment from
another. I want to combine them into one track instead of exporting two
separate MP3s and mixing them myself in some other tool.

## Grounding constraint (why this is necessarily post-hoc, not live)

Spotify plays one track at a time, and Hookline's loopback capture (spec
002) is tied to a single now-playing track's boundaries — there is no live
"record two songs playing at once" mode, because that's not a thing Spotify
itself can do. So this feature is necessarily: pick two clips that already
exist (already-exported catalog clips, a freshly-trimmed selection, or an
imported file), then combine them in a second pass. Worth saying explicitly
so nobody expects a live dual-capture mode — that's not what this is.

## Scope for this spec, and what comes after (owner decision, resolved 2026-07-31)

There are three materially different features hiding under "mix songs
together," at very different levels of effort and risk. Owner chose to
sequence more than one rather than pick just a single one outright:

- **A — Simple layer/overlay.** Pick two clips, play them on top of each
  other (each with its own independent volume), no tempo or pitch matching.
  Straightforward PCM mixing on top of infrastructure that already exists
  (both sources already normalize to the same 44.1kHz/16-bit/stereo format
  via the existing import/capture pipeline). Output length is whichever
  policy gets chosen below. This is buildable now, cleanly, on top of
  existing plumbing.
- **B — Stem-swap mashup.** Pick two clips, run spec 011's existing stem
  separation on each, then let the user pick which stems come from which
  source — e.g. vocals from clip A, everything else from clip B. Musically
  more satisfying than a raw overlay (won't just sound like two songs
  playing over each other), and reuses spec 011's Demucs pipeline entirely
  unchanged rather than building new DSP. Works best when the two source
  clips already happen to share a similar tempo/key — Hookline wouldn't be
  correcting for a mismatch, just leaving the result as musical or
  dissonant as the user's own clip choice makes it.
- **C — Beat-matched DJ-style mixing.** Detect BPM/beat grid on both clips,
  time-stretch one or both to match tempo, align beats, crossfade between
  them. This is what "mixing" means in a DJ-software sense, but it's a
  meaningfully bigger and riskier undertaking than A or B: real-time-quality
  tempo detection and time-stretching is a hard problem on its own (naive
  time-stretching audibly degrades audio — warbling/artifacts — well before
  reaching DJ-software quality), plus a beat-grid UI. This is
  backlog/maybe-never territory unless the owner specifically wants to
  commit real effort to it now, not a natural next step from A/B.

**Resolved sequencing:** this spec (019) builds **A only** — that's the
scope everything below (decisions/edge cases/acceptance criteria) describes
and what Codex should implement. **B is intentionally not part of this
spec** — it's queued as a distinct future spec once 019 has shipped and had
some real use, since it's mostly gluing spec 011's already-built stem
separation onto a two-source picker rather than new DSP, and specs stay
small and independently reviewable per this repo's own convention rather
than growing one spec to cover both. **C stays in Phase 3 backlog** in
`plans/000-roadmap.md` — it's a different scale of project (real tempo
detection and time-stretch quality work) and isn't being sequenced next,
just not ruled out forever.

## Resolved implementation decisions

- **Entry point: a new "Mix..." tray menu entry**, alongside the existing
  "Import audio file...", "Import from URL...", and "Open clip library"
  entries. Opens a dedicated two-source picker screen rather than being
  bolted onto the existing single-source trim window — two independent
  sources is a genuinely different mental model than one continuous
  selection, and forcing it into the trim window would mean redesigning a
  screen that already works well for its existing job.
- **Source selection**: both clips come from the same picker — either an
  already-exported catalog clip (spec 004) or a fresh trim/import, reusing
  existing selection UI rather than inventing a new file-picker. Picking the
  same source for both slots is allowed, not blocked — it's a harmless
  input (a cheap way to double/thicken a single clip), and blocking it would
  just be a special case for no real benefit.
- **Independent volume per source**, exposed as two plain sliders/percentage
  controls (same style as spec 011's per-stem volume) — "how loud is each
  layer relative to the other" is the one control this feature cannot ship
  without.
- **Output duration: the longer of the two selected clips.** The shorter
  clip loops to fill that length, reusing spec 009's existing seamless
  loop/crossfade primitive unchanged rather than building new looping logic.
  This is the most content-preserving default — nothing from either clip
  gets silently cut short to match the other. Anyone who wants a shorter
  result can already trim either source down first, in the same picker,
  before mixing — no separate "match to shorter" toggle needed for that.
- **Export reuses `Mp3ClipExporter`/tagging unchanged** — a mixed result is
  still just an `ImportedAudioFile`-shaped buffer by the time it reaches
  export, same discipline every prior import-adjacent spec (008/017/018)
  has held to. Title/artist tags: implementer's call on a sensible combined
  default (e.g. "Track A / Track B"), editable the same way any other
  clip's tags already are before export.
- **Interaction with spec 015 (per-segment effects) and spec 011 (stem
  isolation): out of scope for this spec.** A mixed two-source result is
  treated as its own new source afterward — open it back up from the
  catalog to segment-split or stem-isolate it further, same as any other
  clip. Not building combined mix+segment or mix+stem UI in this pass.

## Edge cases

- Two sources with very different loudness — covered by the independent
  per-source volume control above, not an automatic loudness-matching
  feature (that's a further-out idea, not part of this spec).
- Mixing a clip with itself (same source picked twice) — explicitly
  allowed, not an error state.
- The shorter clip's loop-to-match-length seam must be click-free, same
  seamless-loop guarantee spec 009 already established — this spec doesn't
  reinvent that, just reuses it.
- Combined output exceeding the existing 5-minute effects-extension cap
  (spec 009's existing ceiling) — same clear-message handling as today,
  not a new cap invented for this spec.
- One or both sources still mid-processing (e.g. a stem-isolation job
  running on a clip selected as a mix source) — same gating an in-progress
  slow operation already uses elsewhere; a source can't be picked into a
  mix while it's still busy.
- Picking two sources of very different original loudness/format (mono vs
  stereo, different sample rates) — both already normalize to the same
  44.1kHz/16-bit/stereo shape through the existing import/capture pipeline
  before either ever reaches this feature, so no new normalization logic
  is needed here.

## Acceptance criteria

- [ ] A "Mix..." tray entry opens a dedicated two-source picker, each slot
      fillable from the clip catalog or a fresh trim/import.
- [ ] Each source has its own independent volume control, applied before
      the two are summed into the mixed output.
- [ ] Output duration equals the longer selected source; the shorter source
      loops seamlessly (no click/gap at the seam) to fill that length.
- [ ] Mixing a clip with itself is allowed and produces a correct result,
      not an error.
- [ ] The mixed result exports through the existing `Mp3ClipExporter`
      unchanged, with sensible default tags the user can edit before
      export, same as any other clip.
- [ ] A mixed clip lands in the catalog like any other export and can be
      reopened afterward for further editing (segment-split, stem-isolate,
      etc.) as its own independent clip.
- [ ] All edge cases above are handled explicitly, not silently ignored.

## What shipped

- Added a dedicated `Mix two clips...` tray action and window. Each source
  can be loaded from the existing clip catalog or through the existing
  local audio import picker; already-decoded sources are cached within the
  window so selecting the same file again does not repeat the decode.
- Added independent 0-150% source volume controls, editable combined title
  and artist tags, and export through the existing cataloging
  `IClipExporter` path.
- Added the UI-agnostic `TwoSourceAudioMixer`: the longer source sets the
  exact output duration, the shorter source uses spec 009's existing
  crossfade loop primitive, sample sums clamp safely, same-source mixing is
  supported, cancellation is observed, and the existing five-minute
  effects cap produces a clear error.
- Mixed exports carry a reserved synthetic source identity; catalog re-trim
  recognizes that identity and decodes the saved MP3. This makes a mixed
  export reopen as its own independent editable source rather than
  incorrectly depending on either input's live buffer, without changing
  expired-buffer behavior for ordinary captures.
- Added mixer, view-model, same-source, tag-editing, limit, and WPF window
  initialization coverage. Full Debug and Release verification each pass
  179/179 tests with 0 warnings.

No deviations or known gaps within the acceptance criteria.

## Follow-up ideas

- The owner would prefer `Ctrl+Alt+H` to open a compact Hookline launcher
  window instead of a cursor-positioned tray menu. That is a separate UX
  decision because it changes every top-level action, not just mixing.
- Profile and plan the reported effect-control/preview latency separately,
  including stem-model warmup and clearer in-progress feedback for stem
  isolation. Stem separation itself is intentionally outside this spec.

## Owner-directed review revision (2026-07-31)

The owner rejected two central decisions in this revision after trying the
shipped workflow, so the spec cannot move from review to done as written:

- `Ctrl+Alt+H` should open one real Hookline workspace, not a six-item tray
  action menu. Capture, file/URL import, mixing, and the clip library should
  be views or tabs inside that single consistent application shell, with
  navigation preserving work instead of spawning separate task windows.
- Mixing must expose the same editing tools as the normal capture editor
  independently for each source (including EQ/bass, effects, and stems), so
  source A and source B can be shaped differently before they are combined.
  The current volume-only mix window and this spec's explicit exclusion of
  combined mix+effects UI do not satisfy that requirement.

The owner clarified that this work remains part of spec 019 and directed
Codex to replace the rejected workflow now. The revised decisions are:

1. `Ctrl+Alt+H` and tray left-click open one full-size Hookline workspace;
   they never open the six-item action popup.
2. The workspace contains persistent navigation for Home, Capture/Edit,
   Import, Mix, and Library. File and URL imports load into the same editor
   used by Capture rather than spawning another editor window.
3. Mix uses A/B source navigation with one consistent editor surface. Each
   source owns an independent complete effect state (segments, EQ, edit
   effects, and stems) before the two processed buffers are combined. No
   additional master-effects stage is added in this revision.
4. The tray context menu is reduced to Open Hookline and Exit. All creation,
   import, mixing, and library actions live inside the workspace.
5. One managed workspace window owns the active sessions and navigation;
   task-specific Hookline windows are no longer spawned. Native file/folder
   pickers and required confirmation dialogs remain normal OS dialogs.
6. Closing the workspace window minimizes to tray (does not exit the app)
   — consistent with today's tray-resident model where the app keeps
   running/capturing in the background; `Exit Hookline` in the tray menu
   is the only way to actually quit.
7. A slow operation (stem separation, a URL download) started in one view
   keeps running in the background when the user navigates to a different
   view — not just its state preserved, the operation itself continues —
   and reports its result when the user returns to that view.

### Revised acceptance criteria

- [x] `Ctrl+Alt+H` opens or focuses one full-size Hookline workspace and
      never displays the action popup.
- [x] Home, Capture/Edit, Import, Mix, and Library are reachable inside the
      workspace without opening separate Hookline task windows.
- [x] Capture, local-file import, and URL import all hand off to the same
      embedded editor surface and preserve the existing editing features.
- [x] Mix source A and source B each have independent access to the same
      segment, EQ, edit-effect, and stem tools as the ordinary editor before
      the existing two-source mixer combines them.
- [x] Navigation preserves in-progress source/edit state; switching views
      does not discard a selection, effect state, or completed slow result.
- [x] The clip library is usable inside the workspace, including play,
      rename, delete, reveal, and re-edit actions.
- [x] Tray right-click contains only Open Hookline and Exit Hookline; tray
      left-click opens/focuses the workspace.
- [x] Existing import, mixer, editor, and catalog behavior remains covered
      and the full Debug/Release suite stays green.
- [x] Closing the workspace window minimizes to tray and background
      capture keeps running; only "Exit Hookline" actually quits the app.
- [x] A slow operation (stem separation, URL download) started in one view
      keeps running when the user navigates elsewhere in the workspace,
      not just its prior state preserved — it completes in the background
      and reports its result when the user returns to that view.
- [x] Spec 014's guarantee (no permanently-stuck-invisible window after a
      failed show) holds under the new single-workspace model —
      regression tested against the new mechanism, not assumed to carry
      over from the retired per-action `ManagedWindowSlot` instances.

Specs 020-022 remain untouched until this revised spec returns to `DONE`.

## Planner review (2026-07-31, second pass — supersedes the note below)

Owner gave Codex direct, more specific instruction than the split-into-023
plan this note originally described: **this work stays inside spec 019**,
not a separate spec. Reviewed the "Owner-directed review revision" section
above against `plans/023-unified-app-shell.md` (which was independently
drafted and owner-confirmed earlier the same day) to reconcile the two
rather than leave conflicting specs both claiming this scope:

- **Three of four decisions match exactly**, and Codex's revision is
  *more* specific on one of them — welcome, not a conflict: "file/URL
  imports hand off to the same embedded editor surface Capture uses" is a
  sharper, better version of 023's looser "Import is a view."
- **One real conflict, resolved in favor of the more recent, more
  specific instruction**: spec 023 recommended keeping tray right-click
  direct-jump shortcuts alongside the shell (optimizing for power-user
  speed). This revision reduces the tray to just "Open Hookline" and
  "Exit" (optimizing for one consistent mental model — there is exactly
  one way to reach anything). Since this came from the owner directly and
  explicitly, it wins over the earlier recommendation. **Tray menu:
  Open Hookline + Exit only, full stop.**
- **Two edge cases from 023 worth carrying forward explicitly**, since
  they're real risks this revision's acceptance criteria don't fully
  spell out: (1) closing the shell window entirely — exit the app, or
  minimize to tray? Needs an explicit choice, not an assumption; (2) a
  slow operation (stem separation, a URL download) started in one view
  must keep *running* in the background when the user navigates away, not
  merely have its *state* preserved — these are different guarantees.
  Also: this consolidation touches the exact window-lifecycle code spec
  014 had to fix a real stuck-window bug in — regression-test that
  guarantee under the new single-shell model, don't just assume it
  carries over because the old `ManagedWindowSlot` mechanism did.
- `depends_on` updated below to include 014 (window-lifecycle precedent)
  and 022 (session-model alignment), matching what 023 already declared.

**`plans/023-unified-app-shell.md` is retired** — marked `DONE` with a
merge note, not picked up separately, so Codex only ever has one spec to
implement against for this scope. No code was ever written against 023
directly (it was never picked up as its own `IN_PROGRESS` spec), so
nothing is lost by folding it back in.

**Sign-off: Codex may proceed with implementation** against the "Owner-
directed review revision" section above, with the tray-menu resolution and
two edge cases from this review folded in as authoritative.

## What shipped (owner-directed review revision)

- Replaced the hotkey action popup and separate task entry points with one
  full-size Hookline workspace. `Ctrl+Alt+H` and tray left-click restore or
  focus it; tray right-click now contains only Open Hookline and Exit
  Hookline, and closing the workspace hides it without stopping capture.
- Added persistent Home, Capture/Edit, Import, Mix, and Library navigation.
  The existing capture editor, local/URL import workflow, mixer, and catalog
  are hosted in that shell, retain their state off-screen, and keep slow URL
  downloads or stem jobs running while another view is open.
- Gave mix source A and source B separate complete editor sessions. Each
  source independently retains its selection, segments, EQ, effects, and
  stem mix; export renders both editor states before applying the existing
  per-source volume controls and two-source mixer. Export stays gated while
  either editor has an active slow operation.
- Removed callable legacy paths that opened task-specific Hookline windows.
  Catalog re-edit now routes into the embedded editor, including mixed clips.
- Added workspace construction, independent A/B rendering, busy-source
  gating, hidden-workspace restore, and failed-show retry coverage. Debug and
  Release both pass 182/182 tests with zero build warnings.

No deviations or known gaps from the authoritative revised acceptance
criteria. The earlier tray-entry/window criteria are superseded by the
owner-directed review revision above.

## Review notes (2026-07-31)

Reviewed independently against commit `3b16f82` — all 11 items in the
revised acceptance criteria verified against actual code (not just these
notes), with file:line citations kept on file: single-workspace hotkey/tray
routing, tray right-click reduced to exactly Open Hookline + Exit (the four
old items are gone from the constructor, not just unwired), Capture/local
import/URL import all funnel through one shared `CreateEditor` surface, Mix
A/B uses one hosted surface with two independent full `TrimViewModel`
sessions (not side-by-side panels, full segment/EQ/effect/stem parity),
`Hookline.Audio` untouched (zero master-stage scope creep), in-progress
state genuinely cached rather than recreated on navigation, close
minimizes rather than exits, URL downloads and stem separation keep
running off-screen and export is correctly gated while either mix source
is still busy, and spec 014's stuck-window guarantee has a real new
regression test (`WorkspaceShowFailureAllowsTheNextHotkeyToRetry`) rather
than an assumed carryover. Release: 182/182, 0 warnings.

One real, minor finding — not blocking DONE on its own, but worth a clean
pass rather than leaving dead code from a rearchitecture this size: the old
`src/Hookline.App/Catalog/ClipRetrimLauncher.cs` (superseded by
`WorkspaceClipRetrimLauncher.cs`) is still in the tree with zero remaining
call sites. Please delete it — leaving superseded window-management code
sitting unused is exactly the kind of thing that causes confusion the next
time someone touches this area.

Also: Debug test verification is still blocked by a running
`Hookline.App.exe` (same recurring environmental issue as earlier reviews
today, not a code defect) — Release is fully green, but please get a
genuinely clean `dotnet test -c Debug` run on record once nothing has the
app open, same bar every other spec this session has been held to.

Back to `IN_PROGRESS` for the dead-code removal and Debug re-verification
— both small, neither calls the actual redesign into question.

## Review fixes shipped (2026-07-31)

- Gave the Library sort picker a fully explicit dark collapsed/dropdown
  template and gave Play, Re-trim, Show in folder, Rename, Save, and Cancel
  explicit dark backgrounds. Delete retains its existing red treatment.
- Added WPF construction coverage that verifies the Library action-button
  background and sort-picker template instead of relying on the Windows
  theme defaults.
- Removed the superseded `ClipRetrimLauncher`; the workspace launcher is now
  the only production catalog re-edit path.
- Clean Debug and Release verification both pass 182/182 tests with zero
  warnings after stopping the running app instance.

## Open questions (owner mixing feedback, 2026-07-31)

The owner says the present Mix setup is not beginner-friendly and that the
path to hearing/creating the combined result is unclear. The example given
was "the background/instrumental from one song plus the lyrics/vocals from
another," but the same feedback also said the clips should play "not
simultaneously." Those describe different output models, so implementation
is blocked on the following UX decisions rather than guessing silently:

1. Should the primary beginner flow create a concurrent **stem mashup**
   (vocals from A over the instrumental stems from B), a **sequential
   arrangement** (A followed by B on a simple timeline), or expose both as
   clearly named recipes? The stem-mashup interpretation best matches the
   concrete vocals/instrumental example.
2. For a stem mashup, should choosing a recipe automatically run stem
   isolation for both sources and set the stem gains, or should it guide the
   user through the two existing source editors? Automatic setup is simpler
   but entails model-download consent and a potentially long first run.
3. Is a combined **Preview mix** action required before the final **Export
   combined audio** action? The current screen only exposes `Export mixed
   MP3` at the bottom of Mix setup and has no preview of both processed
   sources together, which is the main discoverability gap observed here.

## Resolved (owner-confirmed 2026-07-31, second pass)

Question 1 and 2 above put to the owner directly rather than guessed —
answers below. Question 3 resolved by the planner without asking: every
other editing surface in this app previews before export (that's the whole
trim-window model), so Mix having no preview was the actual inconsistency,
not a real open question.

1. **Mix setup offers three named recipes, not one forced flow**:
   - **"Vocals + Instrumental Mashup"** — simultaneous. One source's vocal
     stem plays over the other's instrumental (everything-but-vocals)
     stems.
   - **"A then B"** — sequential. Source A's (possibly trimmed) selection
     plays fully, then Source B's, joined by a crossfade.
   - **"Custom"** — today's already-shipped manual two-full-editor flow,
     unchanged, just relabeled as one of three named choices instead of
     the only option.
   Matches the same "sequence multiple rather than force one" pattern the
   owner already chose for this spec's original A/B/C scope fork.
2. **Mashup recipe specifics**:
   - Picking it **immediately and automatically** runs stem separation on
     both sources — no separate consent click. The existing app-wide
     "usually several seconds or longer" timing disclosure and progress
     indicator (spec 011) still show; automatic means no *extra* click
     before starting, not that the wait becomes invisible.
   - The two picker slots are labeled **"Vocals source"** and
     **"Instrumental source"** when this recipe is active (not generic
     "Source A/B"), with a one-click swap if picked backwards — clearer
     than hardcoding which slot means what, and avoids a redundant
     click for the common case.
   - Default gains on selection: vocals source → its vocal stem at 100%,
     its other stems muted; instrumental source → every non-vocal stem
     (bass/drums/other, plus guitar/piano if 6-stem separation is active)
     at 100%, its vocal stem muted. Sliders remain fully adjustable
     afterward — this is a starting point, not a lock.
3. **Sequential recipe specifics**:
   - Joined by a crossfade — but **not** the existing 15ms
     `ClipFadeSettings.Duration` used for same-song internal seams (spec
     015's segment boundaries, export edge fades). That constant exists
     purely to prevent an audible click at a self-inflicted edit point
     within one continuous piece of audio; going from one song to an
     unrelated other song is a bigger transition and deserves to feel like
     one. Use a distinctly longer fixed crossfade — **1.5 seconds** — long
     enough to read as a deliberate transition without attempting real
     beat-matching (that's option C, explicitly out of scope). Not
     exposed as an adjustable control, same "sensible default, no new UI
     surface for something with one obviously-right answer" reasoning as
     the 15ms constant itself.
   - Output duration: A's (trimmed) length plus B's (trimmed) length,
     minus the crossfade overlap.
4. **Preview mix, resolved by the planner**: add a "Preview mix" action
   that renders the selected recipe's result into a buffer and plays it
   through the existing `AudioPreviewPlayer` (spec 003/016) — no new
   playback code, same infrastructure every other preview in this app
   already uses. Must be byte-identical to what "Export mixed MP3" would
   produce, same discipline held everywhere else in this codebase.

## Recipe edge cases

- A source with no isolable vocal content (instrumental-only track picked
  as "vocals source") — stem separation still runs and produces a
  near-silent/quiet vocal stem; not an error, same as spec 011's existing
  behavior for any track with a weak/absent vocal stem.
- Switching recipes after sources are already picked — re-picking `A then
  B` after `Mashup` (or vice versa) re-applies that recipe's default
  gains/roles rather than leaving stale settings from the previous
  recipe's assumptions; `Custom` always preserves whatever the two full
  editors currently hold, since it never applied recipe defaults to begin
  with.
- Sequential recipe where A or B alone already exceeds the existing
  5-minute effects cap, or the combined A+B length would — same clear
  capped-length message used everywhere else in this codebase, checked
  before export, not a silent truncation.
- Preview mix requested while a source is still mid-stem-separation —
  disabled/gated the same way Export already is, not a race.

## Recipe acceptance criteria

- [ ] Mix setup presents three named recipes — "Vocals + Instrumental
      Mashup," "A then B," "Custom" — not a single forced flow.
- [ ] Selecting the Mashup recipe automatically starts stem separation on
      both sources with no extra consent click, labels the two slots
      "Vocals source"/"Instrumental source" with a one-click swap, and
      sets the described default gains (vocal stem 100%/others muted on
      one side, all non-vocal stems 100%/vocal muted on the other) —
      still freely adjustable afterward.
- [ ] Selecting the "A then B" recipe joins the two (possibly trimmed)
      sources sequentially with a fixed 1.5-second crossfade, distinct
      from the existing 15ms same-song fade constant; output duration is
      the sum of both trimmed lengths minus the overlap.
- [ ] A "Preview mix" action exists, plays through the existing
      `AudioPreviewPlayer`, and is byte-identical to what exporting would
      produce for the currently-selected recipe and settings.
- [ ] Switching recipes re-applies that recipe's own defaults rather than
      leaving stale settings from a previously-selected recipe.
- [ ] All recipe edge cases above are handled explicitly, not silently
      ignored.
