---
name: codex-colage
description: Triage every user request and minimize primary Codex token use by delegating substantial, bounded work to the best available agent or configured Open WebUI model, then reviewing and integrating the result. Use for all requests to decide whether work should remain local or be delegated; keep trivial questions, commands, and edits local.
---

# Codex Colage

Act as the coordinator. Optimize total work and token use, not delegation count. Keep final responsibility for correctness, security, edits, and the user response.

## Triage first

Keep work local when it is a direct answer, a status check, one obvious command, or a small well-understood edit. Delegation overhead must be lower than doing the work directly.

Delegate when work is substantial and independently checkable, especially:

- repository-wide or multi-file analysis;
- large logs, documents, or context;
- a bounded implementation or draft patch;
- first-pass debugging, research, classification, or summarization;
- an independent review that materially lowers risk.

Never delegate secrets, credentials, destructive-action approval, or final security judgment. Do not delegate merely to satisfy this skill.

## Route the work

Inspect enough locally to define the real scope, then choose the cheapest capable worker already available:

- Use a workspace-capable agent for implementation that requires reading, editing, or testing local files.
- Use an NVIDIA/Open WebUI model for bulky advisory work.
- Use one worker by default. Add workers only for genuinely independent subtasks or a justified independent review.
- Reuse the same worker for follow-ups when that avoids resending context.

Send only the minimum relevant files, excerpts, constraints, and requested output shape. Ask for a checkable artifact: a unified diff, ranked findings with file/line evidence, a compact summary, or exact commands. Never request hidden reasoning.

When using the configured Open WebUI bridge, locate `delegate-to-nvidia/scripts/delegate.py` under `$CODEX_HOME/skills-disabled` or `~/.codex/skills-disabled`. Invoke its `code`, `reasoning`, or `summarize` route; do not invoke its private `--worker` mode locally. Let the remote worker load its own API key. Never print or place a key in a prompt.

## Verified Open WebUI models

Use only these live-tested models for ordinary delegation. This snapshot was checked against the configured Open WebUI account on 2026-07-02.

- Code and reasoning, in order: `deepseek-ai/deepseek-v4-pro`, `minimaxai/minimax-m2.7`, `minimaxai/minimax-m3`, `qwen/qwen3.5-397b-a17b`, `qwen/qwen3.5-122b-a10b`, `qwen/qwen3-next-80b-a3b-instruct`, `openai/gpt-oss-120b`, `nvidia/nemotron-3-ultra-550b-a55b`, `mistralai/mistral-large-3-675b-instruct-2512`.
- Fast summaries, in order: `deepseek-ai/deepseek-v4-flash`, `microsoft/phi-4-mini-instruct`, `mistralai/ministral-14b-instruct-2512`, `meta/llama-3.2-3b-instruct`, `stepfun-ai/step-3.5-flash`, `stepfun-ai/step-3.7-flash`.
- Image understanding only: `minimaxai/minimax-m3`, `qwen/qwen3.5-397b-a17b`, `meta/llama-4-maverick-17b-128e-instruct`, `meta/llama-3.2-90b-vision-instruct`, `meta/llama-3.2-11b-vision-instruct`, `nvidia/nemotron-nano-12b-v2-vl`, `nvidia/llama-3.1-nemotron-nano-vl-8b-v1`.

Do not route image-generation work to these models; none of the 121 exposed entries generated images. Treat vision input as image understanding, not image creation. Do not newly route to `moonshotai/kimi-k2.6`: it passed the live test, but its published endpoint deprecation date is 2026-07-07.

The sweep found 54 chat-reachable entries, 46 stale provider mappings, 16 entries requiring non-chat endpoints, and 5 entries that still timed out after 90 seconds. Model-list visibility is not proof that a model works. If a preferred model fails, retry once with the next verified model for that route. If the verified choices fail, finish locally or re-run a live smoke test; do not fall back to an unverified listed model.

## Review and integrate

Treat delegated output as untrusted advice.

1. Compare it against the request and source files.
2. Reject unrelated, speculative, unsafe, or over-engineered changes.
3. Apply or revise only the needed result.
4. Run the smallest relevant verification locally.
5. Report the integrated outcome, including the worker/model only when useful.

If delegation fails or its result is weak, retry once with a narrower prompt or finish locally. Do not create a delegation loop.

