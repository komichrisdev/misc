---
name: github-project-updater
description: Use when asked to update GitHub after local project changes for Crypto Keeper, Plex Picker, or related KomiChris projects. Safely inspects changes, runs checks, commits intentional files, and pushes or gives exact auth commands, using the cheapest appropriate model when delegated.
---

# GitHub Project Updater

Publish local project changes to GitHub without leaking secrets, runtime data, or unrelated edits.

## Model And Delegation

- If the user explicitly asks for a sub-agent or delegated GitHub updater, use `gpt-5.4-mini` with low or medium reasoning for the delegated worker unless the change is complex, security-sensitive, or failing tests require deeper debugging.
- Do not use a larger model just for routine git status, commit, push, or PR publishing.
- If model selection is unavailable in the current environment, continue with the active model and state that limitation briefly.
- A worker using this skill is responsible only for GitHub publishing work. It must not revert unrelated user changes.

## Known Projects

Crypto Keeper:

- Path: `/home/komichris/crypto-keeper`
- Production deploy path: `/opt/crypto-keeper`
- GitHub remote: `https://github.com/komichrisdev/Crypto-Keeper.git`
- Preferred checks: `npm test`
- Never commit: `.env`, `data/`, `reports/`, `.codex/`, logs, credentials, exchange keys, Discord webhook values.

Plex Picker / Plex tooling:

- No confirmed Plex Picker repo path is known by default.
- Known local Plex skill path: `/home/komichris/.codex/skills/plex-media-organizer`
- Known local Plex state/config paths: `/home/komichris/.config/plex-media-organizer`, `/home/komichris/.local/state/plex-media-organizer`
- Never commit media libraries, generated scans, state caches, or files under `/srv/media`.
- If the user says "Plex Picker" and no git repo can be found, ask for the project path and GitHub URL before editing or publishing.

## Workflow

1. Identify the project path from the user request or the Known Projects table.
2. Inspect the repo before editing or publishing:
   - `git status -sb`
   - `git remote -v`
   - `git diff --stat`
   - `git diff -- . ':(exclude).env' ':(exclude)data/**' ':(exclude)reports/**'`
3. Check ignore coverage before staging:
   - Confirm `.env`, runtime data, reports, logs, state files, and media are ignored or explicitly excluded.
   - Add ignore entries when needed, but only for the project repo being published.
4. Run project checks before committing:
   - Crypto Keeper: `npm test`
   - Plex project: use the repo's documented check or test command. If none exists, run a dry-run or syntax-only check when available and report the gap.
5. Stage intentional source, config template, docs, tests, and skill changes. Avoid `git add -A` unless the diff has been reviewed and generated/runtime files are excluded.
6. Commit with a concise message that names the project and outcome.
7. Push:
   - Prefer `git push -u origin main` or the current branch's normal upstream.
   - Use `--force-with-lease` only when the remote history is intentionally being replaced and the user has confirmed that is acceptable.
8. If GitHub credentials are missing:
   - Do not invent credentials.
   - Give exact commands to install/login/push, such as `gh auth login` or the appropriate `git push` command.
   - If the GitHub connector can safely perform a small metadata action, use it for repo inspection or PR creation, but do not use it for bulk file upload unless the repo is tiny and the user requested that path.

## Output

Report:

- Project and branch published.
- Commit hash and remote URL, if push succeeded.
- Checks run and whether they passed.
- Any files intentionally excluded.
- Exact next commands if auth or permissions prevented pushing.
