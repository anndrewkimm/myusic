---
name: draft-idea
description: Converts a brainstormed idea raised in conversation directly into a new numbered Hookline spec file (plans/NNN-short-name.md), following this repo's established structure and status conventions — without asking the user for permission to draft it first.
---

# Draft a brainstormed idea into a spec

When the owner raises a new feature idea, workflow complaint, or "wouldn't it be nice if" mid-conversation — **draft it into a real spec file immediately, in the same turn.** Do not ask "want me to draft this as a spec?" first. That question has already been answered by every prior instance of this pattern in this project: yes.

## Steps

1. Find the next spec number: `ls plans/` and take the highest `NNN` + 1.
2. Write `plans/NNN-short-descriptive-name.md` following the structure already established across every existing spec in `plans/`: frontmatter (`status`, `touches`, `depends_on`), `## Goal`, `## User story`, then whatever mix of `## Resolved implementation decisions` / `## Open questions` / `## Edge cases` / `## Acceptance criteria` fits what's actually known.
3. **Resolve as much as you can yourself** using the design lens already established for this project: favor full user control over the underlying capability, minimal added UI/workflow friction to reach it, reuse of existing constants/patterns already in the codebase over inventing parallel ones, and no confirmation dialogs for routine non-destructive actions. Write real acceptance criteria and edge cases, not placeholders.
4. **Only leave something under `## Open questions` if it's a genuine product-shape fork** the owner has to pick between (materially different features/scope, not an implementation detail) — and say so plainly, with a recommendation, rather than leaving it vague. If there's a real fork, status is `DRAFT`; if everything is resolved, status is `READY`.
5. Update `plans/000-roadmap.md` with a short paragraph noting the new spec, its status, and why it exists — this repo treats the roadmap as the running index, not just the spec files in isolation.
6. Tell the owner what you drafted and why, in a few sentences — not by asking whether you should have drafted it.

## When a genuine fork exists

If the idea really does fork into materially different features (e.g. "mix two songs" turning out to mean three different technical approaches at very different effort levels), it's fine — expected, even — to lay out the options with a recommendation and ask which one via `AskUserQuestion`, since that's a decision only the owner can make. That is not the same thing as asking permission to draft in the first place. Draft first, then ask the real question if one exists.
