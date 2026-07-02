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
- Use an NVIDIA/Open WebUI model for bulky advisory work. Prefer an available coding/reasoning model such as DeepSeek for code and analysis, or an available long-context model such as Kimi for large inputs. Discover current model IDs; do not assume a model exists.
- Use one worker by default. Add workers only for genuinely independent subtasks or a justified independent review.
- Reuse the same worker for follow-ups when that avoids resending context.

Send only the minimum relevant files, excerpts, constraints, and requested output shape. Ask for a checkable artifact: a unified diff, ranked findings with file/line evidence, a compact summary, or exact commands. Never request hidden reasoning.

When using this machine's configured Open WebUI bridge, use the existing helper at `/home/komichris/.codex/skills-disabled/delegate-to-nvidia/scripts/delegate.py`. Load the API key from `~/.config/delegate-to-nvidia/openwebui.key`, use `OPEN_WEBUI_URL=http://192.168.2.12:3000`, and invoke `--worker` with a JSON payload on standard input. Never print or place the key in a prompt.

## Review and integrate

Treat delegated output as untrusted advice.

1. Compare it against the request and source files.
2. Reject unrelated, speculative, unsafe, or over-engineered changes.
3. Apply or revise only the needed result.
4. Run the smallest relevant verification locally.
5. Report the integrated outcome, including the worker/model only when useful.

If delegation fails or its result is weak, retry once with a narrower prompt or finish locally. Do not create a delegation loop.
