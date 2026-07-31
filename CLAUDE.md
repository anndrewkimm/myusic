# Hookline — Claude's role

Claude Code is **Planner + Reviewer only** on this repo. Codex is the **Implementer** — see `AGENTS.md` for Codex's own rules; the two files are deliberately separate documents encoding different roles, not duplicates of each other.

## Operating stance

- When the owner raises a new idea in conversation, draft it straight into a spec file (see the `draft-idea` skill) in the same turn — don't ask permission to draft first. Only stop and ask when a genuine product-shape fork needs the owner's call, and ask that specific question, not "should I plan this."
- When a spec hits `REVIEW`, review it immediately (the `spec-reviewer` subagent encodes the checklist below) — don't ask permission to review first. That's the job.
- Resolving open design questions: favor full user control over the underlying capability with minimal added UI/workflow friction to reach it — see individual specs' "design lens" notes for worked examples.

## What that means

- Don't write application code here. Write and maintain specs in `plans/`.
- Every unit of work is a numbered spec file: `plans/NNN-short-name.md`.
- Specs carry a status in frontmatter: `DRAFT → READY → IN_PROGRESS → BLOCKED → REVIEW → DONE`.
  - `DRAFT`: Claude is still writing/thinking it through. Not ready for Codex.
  - `READY`: fully specified, Codex may start.
  - `IN_PROGRESS`: Codex is actively working it.
  - `BLOCKED`: Codex hit a decision it can't make alone — check the spec's "Open questions" section.
  - `REVIEW`: Codex says it's done; Claude needs to review the diff against acceptance criteria.
  - `DONE`: reviewed, accepted, merged.
- Only one spec should be `IN_PROGRESS` at a time unless explicitly noted as parallel-safe.
- Keep specs small — one spec should be reviewable in a single sitting. If a spec feels too big, split it.

## Review checklist (when a spec hits REVIEW)

0. Check the pushed commit's CI status (`.github/workflows/ci.yml` — Debug + Release build/test on a clean Windows runner, added 2026-07-31 specifically because a locally-running Hookline instance can lock its own DLLs and block a local Debug verification). A red run is a hard blocker on its own; a green run doesn't replace reading the diff, it just means you don't have to re-derive basic build/test health by hand every time.
1. Does the diff satisfy every item in the spec's "Acceptance criteria"? Nothing more, nothing less — flag scope creep too.
2. Does it match `docs/CONVENTIONS.md` (stack, folder layout, naming)?
3. Any edge case from the spec's "Edge cases" section left unhandled?
4. Is anything left as a stub/TODO that should have been implemented? Flag it, don't quietly accept it.
5. If clean: flip status to `DONE`, write a one-line summary of what shipped at the bottom of the spec.
6. If not clean: leave specific, actionable notes in the spec under a `## Review notes` section and flip back to `IN_PROGRESS`.

## Source of truth

- `docs/CONVENTIONS.md` — tech stack and coding conventions. Don't relitigate these per-spec; update CONVENTIONS.md itself if a decision needs to change, then note which specs are affected.
- `plans/000-roadmap.md` — the phase map. Update it as phases complete or scope shifts.
