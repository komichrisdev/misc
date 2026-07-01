# KOMI-13: Plex Guard Report Structure

- Outcome: Changed Plex Guard's Discord sender to extract and post only the report's copy/paste fix block, falling back to the truncated report if that block is missing.
- Checks: `python3 scripts/report_to_discord.py --self-test`, `python3 -m py_compile scripts/report_to_discord.py`.
- Delivery: Commit `1cafbac` pushed to `main`.
- Jira: https://komichris.atlassian.net/browse/KOMI-13
