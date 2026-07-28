---
status: REVIEW
touches: [Hookline.App, Hookline.Audio]
depends_on: [003]
---

# 011 — Stem isolation (vocals / drums / bass / other)

## Goal

Let the user isolate and independently adjust the volume of a clip's vocals, drums, bass, and everything else, then remix and export — the same job iZotope RX's "Music Rebalance" does, and the closest realistic version of "isolate the vocals / isolate the bass / isolate the drums" that current technology actually supports.

**Read the granularity ceiling below before treating this as "isolate anything you want."** This is 4 solid stems, optionally 6 with acknowledged quality tradeoffs — not per-instrument (kick vs. snare vs. hi-hat) isolation, which isn't a solved problem anywhere, professional tools included.

## Research grounding (reviewer, 2026-07-27)

- **iZotope RX's Music Rebalance** — the real professional tool this idea maps to — separates into exactly **four stems: Vocals, Bass, Percussion, Others** (guitars/keys/everything else falls into "Others"). Even paid, professional software caps out here. [iZotope — Music Rebalance, RX 12](https://www.izotope.com/en/learn/stem-separation-music-rebalance), [iZotope — 7 ways to use Music Rebalance](https://www.izotope.com/en/learn/7-ways-to-use-music-rebalance-in-production-and-mixing.html)
- **Demucs** (the leading open-source separation model) ships a **6-stem variant (`htdemucs_6s`)** adding guitar and piano to the standard 4 — but its own documentation reports "okay quality for guitar, but a lot of bleeding and artifacts for the piano source." [GitHub — facebookresearch/demucs](https://github.com/facebookresearch/demucs), [HTDemucs model variant comparison](https://stemsplitter.github.io/research/model-comparison/)
- Critically: an **ONNX-exported version of Demucs already exists** (`htdemucs-6s-onnx` on Hugging Face) and "runs in onnxruntime on CPU out of the box." This means the model can run from .NET directly via the standard `Microsoft.ML.OnnxRuntime` NuGet package — no bundled Python/PyTorch runtime needed, which is a much lighter integration than a naive "just use Demucs" plan would imply. [StemSplitio/htdemucs-6s-onnx — Hugging Face](https://huggingface.co/StemSplitio/htdemucs-6s-onnx), [HT-Demucs FT to ONNX export writeup](https://stemsplit.io/blog/htdemucs-ft-onnx-export)

## Codex handoff

- This is a genuinely heavier feature than anything else in the app — a real ML model (hundreds of MB), real processing time per clip (likely tens of seconds on CPU, not instant), and a new package dependency. Treat it as clearly separate, both architecturally and in the UI, from the instant effects in specs 009/010 — don't let its weight leak into the fast, common trim/export path.
- Verify the exact model source, license terms for redistribution/download, and file size at implementation time before committing to a specific hosting/download approach — the Hugging Face listing above is a starting point, not a final answer.
- Default to the standard **4-stem** separation as the reliable path; offer 6-stem as a clearly-labeled secondary option given its documented quality issues on guitar/piano — never present 6-stem as equivalent quality to 4-stem.
- Once stems are separated and remixed, feed the result through the *existing* tagged-MP3/catalog export pipeline unchanged — same adapter-pattern discipline as specs 008/009/010.

## Resolved implementation decisions

- **Inference via `Microsoft.ML.OnnxRuntime`** against a pre-converted ONNX build of Demucs, not a bundled Python/PyTorch environment. CPU execution must always work as the baseline; a GPU-accelerated path (DirectML execution provider) is a nice-to-have, not required.
- **Model is downloaded on first use, not bundled in the base install** — clear one-time prompt stating it's a large download (verify actual size at implementation time) before starting, with visible progress and the ability to cancel. Never a silent multi-hundred-MB download the user didn't expect.
- **A distinct "Isolate stems..." action**, separate from the always-on effects row in specs 009/010, operating on the current trim selection. Clearly communicates before running that this will take real time (not instant like everything else in the app).
- **Separation runs as a cancelable background operation with visible progress** — never blocks or appears to freeze the UI, given realistic processing time.
- **Once separated: four independent volume controls** (Vocals, Bass, Drums, Other), live-remixed and previewable, matching the "Music Rebalance" experience this is modeled on. Muting a stem is just its volume control at zero — no separate mute button needed.
- **6-stem mode (adds Guitar, Piano) is opt-in and explicitly labeled as lower-quality/experimental**, consistent with Demucs' own documented caveats — never presented as a straightforward upgrade over the 4-stem default.
- **The final remix (whatever the stem volumes are set to) is what exports** — through the same export/tagging/catalog pipeline as every other clip, no special-casing there.

## User story

I've got a clip I like, but the vocals are a little loud for what I want, or I want to hear just the bassline for a second to check something. I hit "Isolate stems," wait a bit (I'm told up front this isn't instant), and then I've got four sliders — I can turn the vocals down, boost the bass, whatever — and preview the remix before exporting it as a normal tagged MP3, same as always.

## Edge cases

- Model not yet downloaded — clear first-use prompt with size/expectation-setting before anything starts; never a surprise multi-hundred-MB download.
- Download interrupted or fails — clearly retryable, never leaves a partial/corrupt model file silently in place that fails mysteriously later.
- Separation takes a long time on slower hardware — visible progress, cancelable, UI stays responsive throughout.
- 6-stem mode's known bleeding/artifacts on guitar/piano — communicated in the UI when that mode is selected, not silently presented as clean separation.
- Very short or very long selections — bounded consistently with the existing sanity caps already established in specs 008/009.
- User cancels mid-separation — clean cancellation, no dangling background work, no corrupted partial output.
- All four stem volumes left at their natural/default level — remix should sound like the original clip (a sanity check that the pipeline round-trips correctly).

## Acceptance criteria

- [x] A distinct "Isolate stems..." action exists on the current trim selection, clearly marked as a slower, heavier operation.
- [x] First use prompts for the one-time model download with visible progress and size expectation; later uses reuse the already-downloaded model without re-prompting.
- [x] Separation runs as a cancelable background operation with visible progress; the UI never appears frozen.
- [x] Once separated, Vocals/Bass/Drums/Other each have an independent, live-previewable volume control.
- [x] The remixed result exports through the existing tagged-MP3/catalog pipeline with no special-casing needed there.
- [x] Optional 6-stem mode (Guitar, Piano added) is available but clearly labeled as lower-quality/experimental, not equivalent to the 4-stem default.
- [x] Non-UI remix/mixdown math is covered by unit tests using synthetic fixture stems (not requiring the actual multi-hundred-MB model in CI); actual model-inference tests are gated separately given they can't reasonably run in an automated pipeline without the real model file.

## Open questions

- Resolved during implementation: first-use downloads come directly from StemSplitio's MIT-licensed Hugging Face repositories. The default is `htdemucs_fp16weights.onnx` (166 MB, SHA-256 `d05c269d0178d2a72ad484b10b11dd370193fc923201c3b27a99f848745db70a`); experimental mode uses `htdemucs_6s_fp16weights.onnx` (136 MB, SHA-256 `7ce55792e2231c93fbf92de95f5fd5b3a5e6c89f7db690dfd693e8f1dce56869`). Hookline verifies the hash before any model is cached or loaded.
- Resolved during implementation: each stem uses a simple 0-150% linear volume slider, with 100% as the natural/default level and 0% as mute.

## Follow-up ideas

- Per-stem effects — e.g., apply spec 010's EQ to just the isolated bass stem before remixing.
- Exporting individual isolated stems as their own separate files, not just a combined remix.
- GPU acceleration (DirectML) as a real feature if CPU-only processing time turns out to be a genuine pain point in practice.

## What shipped

- Added the CPU `Microsoft.ML.OnnxRuntime` 1.27.1 adapter for the publisher's fixed 44.1 kHz stereo model contract, including 7.8-second chunking, 25% overlap-add, prompt cancellation of active inference, and strict disposal of native runtime resources.
- Added a `%LOCALAPPDATA%\Hookline\Models` first-use cache with explicit consent, download percentage, cancellation, SHA-256 verification, atomic completion, partial-file cleanup, and reuse without another prompt.
- Added a separate, scroll-safe stem-isolation panel in the trim window. Four stems are the default; six stems are opt-in and visibly marked experimental/lower quality. Completed stems get independent 0-150% controls that live-remix preview and export.
- The remix feeds the existing speed/EQ/loop processor and unchanged tagged-MP3/catalog exporter. Selection edits invalidate stale stems, operations are capped at five minutes, and separation never runs on the UI thread.
- Added synthetic mixdown/clipping tests, cache integrity/reuse tests, view-model download/separation/cancellation/4-vs-6/preview-export tests, and an environment-gated real-model contract test (`HOOKLINE_FOUR_STEM_MODEL_PATH`). All 102 tests pass in Debug and Release. The large model was intentionally not downloaded during automated validation; real inference remains separately gated as required.
