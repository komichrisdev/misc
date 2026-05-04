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

3. Run the cheap scan first.
   - Use a cheap subagent/model when available, such as `gpt-5.4-mini`, for the initial CSV summary, spike detection, and support history update. Do not do the first-pass assessment in the main thinking model unless subagents/model selection are unavailable.
   - If subagents or model selection are unavailable, run the helper script locally and keep the first-pass analysis concise.
   - Use `scripts/analyze_feedback.py` to parse the CSV, extract bracket tags like `[loading_issues]`, compare against game history, and update the game history file.
   - Do not review/process the whole CSV when game history already has a `last_assessed_date`. Process rows from the top of the export only until the first row on or before the last assessed day, then stop.
   - Even on the first history run, use earlier rows in the same CSV as baseline data. Compare the latest week so far against prior same weekdays and prior week-to-date totals.

4. Escalate only on spikes.
   - Treat the helper script's `spike_detected` value and `spikes` list as the first-pass flags.
   - If spikes are flagged, use the strongest available thinking model to inspect the flagged reports and examples.
   - Look for shared issue signals: repeated phrases, timeframe, country/locale, platform, app/app asset version, host application, and player clusters.

5. Report outcome.
   - If spikes are flagged, report likely issue, evidence, affected groups, and suggested engineering/support checks.
   - If no spikes are flagged, produce a concise trend report: messages received, current weekday comparison, top message types, notable country/locale/platform patterns, and history note.

## History

Use this history root by default:

`C:\Users\chris\Qublix Games Dropbox\Chris K\CodexSkillBundles\Support`

Keep one JSON file per game. Do not mix games. Do not overwrite manual notes or unrelated fields. The helper script preserves existing history, stores `last_assessed_date`, and avoids appending the same CSV twice by dataset hash.

## Helper Script

Run with the bundled Python executable when normal `python` is unavailable:

```powershell
& "C:\Users\chris\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" "C:\Users\chris\.codex\skills\support-manager\scripts\analyze_feedback.py" --csv "C:\Users\chris\Downloads\feedback.csv" --game "Game Name" --update-history
```

If no CSV path is provided, omit `--csv`; the script applies the Downloads rule and returns structured `needs_input` JSON when it must ask the user.

Important options:

- `--game`: required unless the caller wants the script to return `needs_input: game_name`.
- `--history-root`: override the default support history folder.
- `--update-history`: append the analyzed run and trend note to the game history.
- `--limit-examples`: control how many example rows are returned for flagged/top tags.

When history exists, the helper returns `processing_window` with `rows_seen`, `rows_processed`, `last_assessed_date_before_run`, and whether it stopped at the last assessed day. If no new rows exist after that day, report that clearly and do not append an empty run.

The helper always returns `comparisons` when dated rows exist. Use this for weekday and weekly comparisons even when the game has no prior saved history.

## Report Shape

Keep reports concise. Include:

- game name and CSV file analyzed
- date range and total messages
- processing window: rows processed and last assessed day cutoff
- spike status
- weekday comparison against history when available
- same-CSV weekday and week-to-date comparison when saved history is not enough
- top message types/tags
- top affected locales/countries/platforms
- history write status
- if escalated, findings with evidence and next checks
