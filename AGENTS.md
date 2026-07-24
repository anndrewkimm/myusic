# Hookline — Codex's role

Codex is the **Implementer**. Claude Code is the Planner/Reviewer — don't redesign scope, follow the active spec.

## Rules

1. Work exactly one spec at a time: the one in `plans/` marked `READY` (or `IN_PROGRESS` if you're resuming). Read the whole spec, plus `docs/CONVENTIONS.md`, before writing code.
2. When you start, flip the spec's status to `IN_PROGRESS`.
3. Build only what's in "Acceptance criteria." Ideas outside the spec go in a `## Follow-up ideas` section at the bottom of the spec — don't build them now, don't silently skip mentioning them either.
4. If you hit a decision the spec doesn't answer (an ambiguous API choice, a UX judgment call, something that materially changes user experience): don't guess silently. Add it under `## Open questions` in the spec, flip status to `BLOCKED`, and stop that spec. Move to nothing else without asking — surface it.
5. If an external API/library detail in the spec is marked "verify against current docs," actually check current Microsoft Learn / library docs before implementing — spec text may be describing the *shape* of the right answer, not a guaranteed-current API signature.
6. When done: flip status to `REVIEW`, and write a short `## What shipped` note at the bottom of the spec (what you built, any deviations, any known gaps).
7. Don't touch other specs' status. Don't start the next spec until this one is `DONE`.

## Conventions

Follow `docs/CONVENTIONS.md` for stack, project layout, and style. If you think a convention is wrong, say so in the spec's Open questions rather than silently deviating.
