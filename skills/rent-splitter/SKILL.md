---
name: rent-splitter
description: Prepare the monthly shared-bills spreadsheet from Gmail billing notices, copy or update the current month from the Google Sheets Template tab, verify the calculated split, and post the finished tab to Discord. Use for the recurring Bell, TELUS, and Toronto Hydro rent-split workflow or when running, checking, or repairing that month-end automation.
---

# Rent Splitter

Update one month only, preserve the spreadsheet's formulas and formatting, and do not modify Gmail.

## Sources

- Spreadsheet: `1Fu0B5-nEHg-GSWKDtzLqALG0n-szuwsmDmYxRyw0RVw`
- Template tab: `Template`
- Gmail account: `chriskomisar@gmail.com`
- Bell: `from:ebill@bell.ca subject:"Your Bell e-Bill is ready"`
- TELUS: `from:telusbilling@info.telus.com subject:"Your mobility e.Bill is ready"`
- Hydro: `subject:"Your Toronto Hydro bill is ready"` (often forwarded from `thekomisars@gmail.com`)

Use Gmail only for search and read operations. Never label, archive, delete, draft, forward, or send mail.

## Monthly Workflow

1. Resolve the requested billing month, defaulting to the current month in `America/New_York`.
2. Search Gmail separately for each vendor, bounded from the first day of that month through the first day of the next month. Read the selected messages.
3. Extract one CAD amount per vendor from these anchors:
   - Bell: `Amount due`
   - TELUS: `Total due`
   - Toronto Hydro: `Total amount due`
4. Stop without editing if any bill is missing, outside the billing month, or has conflicting candidates. Report exactly what needs review; never guess an amount.
5. Read spreadsheet metadata first. Record the exact `Template` sheet ID, visible tab naming pattern, and target month's existing sheet if present.
6. If the target tab does not exist, duplicate `Template` with the native Sheets `duplicateSheet` request and follow the workbook's observed month-title pattern. If it exists, update it in place so retries are idempotent.
7. Read the target tab's labels, amount cells, formulas, formatting, and validation. Locate the Bell, TELUS, and Hydro input cells from their visible labels rather than hard-coding coordinates.
8. Write only the three vendor input amounts. Preserve recurring values, formulas, formatting, validation, and unrelated cells.
9. Read back the three inputs and `D20`. Treat the run as failed unless the inputs match Gmail and `D20` has a numeric calculated value.
10. Build the tab URL from the spreadsheet ID and the newly observed target `sheetId`; do not invent a gid.
11. Run `scripts/send_discord.sh "<tab title>" "<D20 amount>" "<verified tab URL>"`. It posts `bills3.png` when `D20` is over `$750`; otherwise it posts `bills2.png`.
12. End a successful automated run with a line containing only `RENT_SPLITTER_COMPLETE`.

## Safety

- Read live spreadsheet metadata and cells before every edit pass.
- Prefer one coherent batch for the three values after the target cells are grounded.
- Do not create a second monthly tab on retry.
- Do not post to Discord until Sheets readback succeeds.
- Do not include account numbers, email bodies, or webhook values in logs or Discord.

## Automation

- `scripts/run_monthly.sh`: run from cron near month end; it executes only on the calendar month's last day and marks a month complete only after the success sentinel.
- `scripts/send_discord.sh`: validate and send the verified tab link with the threshold-selected image.
- `scripts/self_test.sh`: run the local, network-free checks after changes.

Discord configuration defaults to `~/.config/rent-splitter/env`, then reuses `~/.config/night-owl/env`. The config must define `DISCORD_WEBHOOK_URL`.
