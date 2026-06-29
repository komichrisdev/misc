---
name: night-owl
description: Process Jira work asynchronously every four hours. Use when Codex needs to triage the AIGents Kanban queue, resume In Progress work, implement eligible To Do issues, update Jira comments and worklogs, publish project changes to GitHub, hand work to Test Pending, or prepare the Night Owl morning report.
---

# Night Owl

Process Jira issues sequentially and leave every issue in a state a human can safely resume or test.

## Queue

1. Read `projects.json`.
2. Query Jira project `KAN` for non-Epic issues in `In Progress`; process these first by board rank.
3. Then query non-Epic issues in `To Do` by board rank.
4. Read each issue's description, attachments, links, and all comments before acting. Treat recent comments as updates or test feedback.
5. Process one issue at a time. Stop when the queue is empty or the runner's deadline is near.

Do not execute Epics. Do not pick up `Test Pending` or `Done` issues. Do not retry an issue already handled in the current run.

## Triage

Resolve the target repository from an explicit issue reference or `projects.json`. If more than one repository is plausible, do not guess.

- Use the active cheap model for routine work.
- Use cheap subagents for independent, well-bounded work when concurrency saves time.
- Escalate model strength only for complex, security-sensitive, or repeatedly failing work.
- Never run parallel workers against the same repository.
- Preserve unrelated local changes and never expose credentials, webhooks, `.env` files, runtime data, or logs.

If requirements or the target repository are ambiguous, add the exact questions and restart context to Jira, record time spent, move the issue to `Test Pending`, and continue to the next issue.

## Jira Attachments

The Jira connector returns attachment IDs and filenames but not file contents. When an attachment is required, use:

```bash
python3 scripts/download_jira_attachment.py ATTACHMENT_ID --output-dir /path/to/project/data
```

The helper reads `ATLASSIAN_SITE_URL`, `ATLASSIAN_EMAIL`, and `ATLASSIAN_API_TOKEN` from `~/.config/night-owl/env` or the environment. Keep that file mode `0600`, download only attachments belonging to the active issue, and never print or copy its contents into logs, Jira, Git, or prompts. The helper refuses plaintext HTTP, unsafe filenames, and overwrites.

If credentials are missing, ask the user to create an Atlassian API token, add the three values to the private config, and return the issue to `To Do`.

## Execute

1. Move actionable `To Do` work to `In Progress` before editing.
2. Inspect the repository status, instructions, existing patterns, and relevant callers before changing files.
3. Implement the smallest complete solution and run the repository's documented checks.
4. Commit and push only intentional files. Use an issue-specific branch and PR when the repository workflow requires one; otherwise follow its existing branch convention.
5. Add a Jira worklog for elapsed hands-on time.
6. Add a Jira comment with the outcome, checks, commit or PR links, and any residual risk.
7. Write or update `runs/<project>/<issue-key>.md` in the `misc` repository with a concise, secret-free execution journal and publish it.
8. Move completed or blocked work to `Test Pending`. Never move work to `Done`; human validation owns that transition.
9. Immediately call `scripts/send_report.sh --test-pending <issue-key> complete` for completed work or `scripts/send_report.sh --test-pending <issue-key> questions` for blocked work. Report a notification failure in Jira rather than hiding it.

When checks fail, attempt a root-cause fix within the available window. If the issue cannot be completed safely, preserve the working state, document the failure and exact resume steps, record time, and move it to `Test Pending`.

## Morning Report

Append to `~/.local/state/night-owl/report.md` when at least one issue was processed or the automation itself failed. Keep the accumulated report below 1,900 characters and list:

- completed issues and links
- blocked issues and questions
- failed checks or automation errors

Leave an existing report untouched after a no-work run. The 7:00 AM reporter sends the accumulated file once and archives it locally. If no report exists, send a daily no-work message.

Each `Test Pending` transition also sends one immediate message:

- `Work complete, awaiting review` for finished work
- `Work paused, I have questions` when human input is required

## Scripts

- Run `scripts/run_nightly.sh --dry-run` to validate prerequisites without starting Codex.
- Run `scripts/run_nightly.sh` for each recurring four-hour queue check.
- Run `scripts/send_report.sh` for the daily Discord report.
- Run `scripts/send_report.sh --test-pending <issue-key> complete|questions` after a `Test Pending` transition.
- Run `scripts/download_jira_attachment.py <attachment-id> --output-dir <directory>` for issue attachments.
- Run `scripts/self_test.sh` after changes.
