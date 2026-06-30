# KOMI-12: Night Owl Discord Reports

- Outcome: Batched `--test-pending` notifications into the Night Owl run summary, kept daily no-work reporting, and had the runner send the combined report only when a run recorded work.
- Checks: `bash -n`, `scripts/self_test.sh`, and a direct `run_nightly.sh` smoke check with a no-op codex binary.
- Risk: The summary now ships from the end-of-run report instead of per-issue webhooks, so future changes should keep the run report and the runner call in sync.
- Jira: https://komichris.atlassian.net/browse/KOMI-12
