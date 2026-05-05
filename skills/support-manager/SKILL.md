---
name: support-manager
description: Analyze exported Facebook Instant Game feedback CSVs for support triage, spike detection, message-type trends, and local per-game support history. Use when Codex is given or needs to find a .csv export of FB feedback, compare message counts or tags to historical weekday averages, update support notes, or investigate spikes such as loading issues, purchase issues, crashes, and bug reports for an instant game.
---

# Support Manager

## Workflow

1. Find the feedback CSV.
   - If the user gives a CSV path, use that file.
   - If no path is given, check `C:\Users\chris\Downloads` for `*.csv`.
   - If exactly one CSV is present, use it.
   - If no CSVs are present, ask the user for the CSV path.
   - If multiple CSVs are present, ask the user which CSV to analyze.

2. Identify the game.
   - If the user does not name the game, ask which game the CSV is for.
   - Preserve the human game name in reports and history.
   - Use a sanitized slug only for file names.

3. Run the cheap scan in a subagent first.
   - Every support-manager invocation must default to a `gpt-5.4-mini` worker subagent for the initial CSV summary, complaint summary, player-message summary, spike detection, history update, and GitHub log write.
   - The main agent coordinates, checks the worker output, and reports. It must not do the first-pass assessment itself unless subagents/model selection are unavailable.
   - If subagents or model selection are unavailable, state that clearly, run the helper script locally, and keep the first-pass analysis concise.
   - Before the helper runs, check GitHub logs first. Pull or inspect `C:\Users\chris\Documents\Playground\misc\skills\support-manager\support-checks` from `komichrisdev/misc` when available, then pass it with `--github-log-root`.
   - Use `scripts/analyze_feedback.py` to parse the CSV, extract bracket tags like `[loading_issues]`, summarize complaints sent since the last support check, compare against game history/GitHub logs, update the game history file, and write a support-check log.
   - Do not review/process the whole CSV when history already has a `last_assessed_at_utc` or `last_assessed_date`. Process rows from the top of the export only until the first row on or before that timestamp/date, then stop.
   - Even on the first history run, use earlier rows in the same CSV as baseline data. Compare the latest week so far against prior same weekdays and prior week-to-date totals.

4. Escalate concerning spikes or messages.
   - Treat the helper script's `spike_detected` value and `spikes` list as the first-pass flags.
   - If spikes are flagged, escalate to `gpt-5.5` thinking or the strongest available thinking model to inspect the flagged reports and examples.
   - Even when no volume spike is flagged, escalate concerning message content to `gpt-5.5` thinking when player messages suggest blockers, impossible levels, lost progress, purchase/payment loss, crashes, loading failures, repeated severe frustration, or multiple players sharing the same concrete issue.
   - Look for shared issue signals: repeated phrases, timeframe, country/locale, platform, app/app asset version, host application, and player clusters.

5. Report outcome.
   - If spikes are flagged, report likely issue, evidence, affected groups, and suggested engineering/support checks.
   - If no spikes are flagged, produce a concise trend report: messages received, current weekday comparison, top message types, notable country/locale/platform patterns, and history note.
   - Always include the complaint summary for messages sent since the last support check.
   - Always include a player-message summary with player id plus feedback content, e.g. `7964882253556716 says level 465 is impossible.` Important individual messages must be visible even when there is no spike.
   - After the check, commit and push the generated support-check log to GitHub. If the push cannot run, report the local log path and exact status.

## History

Use this history root by default:

`C:\Users\chris\Qublix Games Dropbox\Chris K\CodexSkillBundles\Support`

Keep one JSON file per game. Do not mix games. Do not overwrite manual notes or unrelated fields. The helper script preserves existing history, stores `last_assessed_date`, and avoids appending the same CSV twice by dataset hash.

## GitHub Logs

Use this GitHub log root by default:

`C:\Users\chris\Documents\Playground\misc\skills\support-manager\support-checks`

Before each check, refresh or inspect the `komichrisdev/misc` clone when available. The helper reads prior logs from `skills\support-manager\support-checks\<game-slug>\*.json` and treats them as remote support history for cutoff and comparisons.

After each check, run the helper with `--write-github-log`; then commit and push the changed files under `skills/support-manager/support-checks`.

## Helper Script

Run with the bundled Python executable when normal `python` is unavailable:

```powershell
& "C:\Users\chris\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" "C:\Users\chris\.codex\skills\support-manager\scripts\analyze_feedback.py" --csv "C:\Users\chris\Downloads\feedback.csv" --game "Game Name" --update-history --github-log-root "C:\Users\chris\Documents\Playground\misc\skills\support-manager\support-checks" --write-github-log
```

If no CSV path is provided, omit `--csv`; the script applies the Downloads rule and returns structured `needs_input` JSON when it must ask the user.

Important options:

- `--game`: required unless the caller wants the script to return `needs_input: game_name`.
- `--history-root`: override the default support history folder.
- `--update-history`: append the analyzed run and trend note to the game history.
- `--github-log-root`: read prior GitHub support-check logs from a local clone.
- `--write-github-log`: write the current structured check log under the GitHub log root for commit/push.
- `--limit-examples`: control how many example rows are returned for flagged/top tags.
- `--limit-player-messages`: control how many player id plus feedback summaries are returned. The helper shows concerning messages first.

When history exists, the helper returns `processing_window` with `rows_seen`, `rows_processed`, `last_assessed_at_before_run`, `last_assessed_date_before_run`, and whether it stopped at the last assessed cutoff. If no new rows exist after that cutoff, report that clearly and do not append an empty run.

The helper always returns `comparisons` when dated rows exist. Use this for weekday and weekly comparisons even when the game has no prior saved history.

## Report Shape

Keep reports concise. Include:

- game name and CSV file analyzed
- date range and total messages
- processing window: rows processed and last assessed day cutoff
- spike status
- complaints sent since last support check: top themes, counts, representative complaint snippets, and repeated-player/locale/platform patterns
- player-message summary: player id, feedback content, message type, locale/country/platform, and timestamp
- weekday comparison against history when available
- same-CSV weekday and week-to-date comparison when saved history is not enough
- top message types/tags
- top affected locales/countries/platforms
- history write status
- GitHub log read/write status and push status
- if escalated, findings with evidence and next checks
