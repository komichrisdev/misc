---
name: rate-rater
description: Use for Codex tasks that need a scope-vs-remaining-5h/weekly-usage check, a running log when the budget looks tight, or an hourly resume queue for incomplete tasks blocked by usage limits.
---

# Rate Rater

Use this skill for tasks that should police their own budget.

## Workflow

1. Open with a scope check: name the task size, then compare it with the remaining 5h and weekly usage.
2. If the task looks larger than the remaining budget, start a log before editing and append checkpoints while you work.
3. Record blocked work as `blocked-usage` with the next step, not as a dead end.
4. On hourly runs, read the log and rebuild the resume queue for every incomplete `blocked-usage` task.
5. Keep the runtime files under `~/.codex/rate-rater/` unless the user gives another path.

## Script

Use `scripts/rate-rater.ps1` for:

- `start` to create a task log entry.
- `note` to append a checkpoint.
- `block` to mark a task blocked by usage limits.
- `finish` to close a task.
- `hourly` to rebuild `resume-queue.md`.
- `install` to register the hourly Windows scheduled task.

## Resume rule

The hourly task does not guess. It rewrites the resume queue from the log, then the next Codex run starts from the newest blocked entry.
