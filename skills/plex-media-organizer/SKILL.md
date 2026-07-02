---
name: plex-media-organizer
description: Organize Plex media libraries under /srv/media by conservatively renaming anime, TV, and movie directories/files into Plex-friendly formats, preferring accurate English titles and reporting uncertain cases instead of guessing. Use it also to create character- and style-matched Discord art from Plex Organizer run reports.
---

# Plex Media Organizer

Use this skill when organizing the Plex media roots:

- `/srv/media/anime`
- `/srv/media/tv`
- `/srv/media/movies`

The goal is clean Plex matching, not aesthetic perfection. Prefer conservative, reversible operations and leave a report for anything uncertain.

## Model

This skill should always run with `gpt-5.4-mini` wherever the local runner controls model selection. This task needs enough reasoning for title cleanup and web lookups, but it does not need a frontier model.

## Safety Rules

- Never delete media or subtitle files.
- Rename only when the intended title, year, season, and episode number are high confidence.
- Prefer English titles when official or common English titles are clear.
- When multiple complete copies of the same episode are present, prefer `SubsPlease` as the canonical keep unless a higher-confidence reason exists to pick a different release, such as a clearly superior `v2` or repack that is already established as the intended replacement.
- Do not flatten or split collections unless the target Plex layout is obvious.
- Do not guess season numbering for specials, OVAs, movies, alternate cuts, or absolute-numbered anime.
- If a rename could collide with an existing path, report it instead of applying it.
- Leave uncertain cases in the daily report with the current path, proposed path, and reason for holding.

## Plex Naming Targets

TV and anime:

```text
Series Title/
  Season 01/
    Series Title - S01E01 - Episode Title.ext
```

Movies:

```text
Movie Title (Year)/
  Movie Title (Year).ext
```

Keep subtitle sidecars aligned with the renamed media basename when the match is unambiguous.

## Cleanup Heuristics

Strip release noise from titles and filenames when it is not part of the actual title:

- Release groups such as `[Anime Time]`, `[Judas]`, `[SubsPlease]`, `YTS`, `GalaxyRG`, `EMBER`, `PSA`, `RAV1NE`.
- Quality/source/codec/audio tags such as `1080p`, `2160p`, `BluRay`, `WEBRip`, `WEB-DL`, `x265`, `HEVC`, `10bit`, `AAC`, `DDP`, `Dual Audio`.
- Tracker and site prefixes such as `www.UIndex.org -`, `YTS.MX`, `TGx`, `RARBG`.
- Bracketed metadata that is not part of the title.

Preserve meaningful title punctuation where Plex accepts it, but avoid characters that are awkward on filesystems when a plain equivalent is common.

## Workflow

1. Build a review plan from `~/.local/state/plex-media-organizer/review-log.json`.
2. Inventory only new or changed media directories under `/srv/media/anime`, `/srv/media/tv`, and `/srv/media/movies`.
3. Skip directories whose review-log fingerprint matches the prior reviewed/fixed state.
4. Identify obvious Plex naming violations and torrent-style naming noise.
5. Use web search for title/year/season confirmation when local evidence is weak.
6. Apply only high-confidence renames.
7. Emit structured RSS auto-download rule updates when a high-confidence rename or `Season NN` re-home means qBittorrent should follow the new path.
8. Update the review log with post-run directory fingerprints and missing anime episode gaps.
9. Write a concise report listing applied changes, held changes, review-log skip counts, missing anime episodes, RSS rule sync results, and unresolved questions.

The review log lives at:

```text
~/.local/state/plex-media-organizer/review-log.json
```

It records reviewed directory fingerprints, removed directories, and the current `SxxEyy` missing-episode gap list for anime. The daily runner generates `review-plan.md` before invoking Codex; if that plan has no new or changed directories, the organizer skips the expensive Codex review and only refreshes the log/report state.

## Discord Run Art

Generate art for every daily run report:

1. Read the final report and choose the anime with the most applied changes, then held changes, then missing episodes. If the report names no anime, use an original media-organizer scene.
2. Use `$imagegen` in its default built-in mode. Find an official anime site or publisher key visual, download one reference image to `/tmp`, and label it as the character and visual-style reference. Never copy its composition, text, logos, or other characters.
3. Depict the updated anime's recognizable lead character sorting physical media as a visual metaphor for the run. Reflect the actual organizer action when practical, but never trade character accuracy for a literal background or prop layout. Never substitute a generic mascot when a changed title provides a character.
4. Generate one wide `illustration-story` Discord image for the selected anime and every report, including no-change and error runs.
5. Treat the reference character's face, hair, clothing, proportions, palette, and 2D rendering style as strict requirements. Backgrounds may be simplified or invented and do not need to match the changed files or locations exactly. Keep technical detail in the Discord text; use simple visual cues for applied, held, RSS, or error states. Avoid readable text, logos, filenames, and watermarks.
6. Inspect character identity, art-style match, anatomy, and requested visual counts. Fix one defect per targeted edit and recheck before posting.
7. Copy the selected image to a versioned path under `~/.local/state/plex-media-organizer/demos/`; never overwrite an earlier demo.
8. Post the run facts and exact generated image with Discord multipart upload. Use `--form-string "payload_json=$payload"` so punctuation in report text cannot corrupt JSON, attach the file with `-F "files[0]=@$image"`, and reference it as `attachment://<filename>` in the embed.

## qBittorrent RSS Sync

When the organizer renames a series directory or moves active downloads into a `Season NN` folder, it should also emit machine-readable RSS rule updates for the outer runner. The runner applies those updates conservatively against qBittorrent's `download_rules.json` only after a successful media pass.

Guidelines:

- Update RSS rules only when the destination path is high confidence and already exists after the organizer move.
- Prefer updating an existing rule by exact rule name.
- Clone a rule only when you need a new season-specific destination and can identify the source rule confidently.
- When a forced RSS sync is requested, you may emit RSS-only corrections for active rules that clearly lag behind the current Plex-ready library layout, even if no files need moving in that run.
- Do not guess qBittorrent rule names or matcher text. If the rule mapping is uncertain, leave the RSS update out and mention it in the report.

For full hands-off sync, use one of these bootstrap options:

- Direct file sync: the organizer user must be able to rewrite qBittorrent's `rss/download_rules.json`.
- WebUI sync: set `QBITTORRENT_WEBUI_URL`, `QBITTORRENT_WEBUI_USERNAME`, and `QBITTORRENT_WEBUI_PASSWORD` in `~/.config/plex-media-organizer/discord.env`. Set `QBITTORRENT_WEBUI_VERIFY_TLS=true` only if the local WebUI certificate is trusted by the host.

If WebUI credentials are present, the sync helper prefers qBittorrent's `/api/v2/rss/rules` and `/api/v2/rss/setRule` endpoints over direct file writes.

The daily runner also checks for qBittorrent process-state drift before posting to Discord. If multiple `qbittorrent-nox` processes are running, the service is inactive while qBittorrent is still running, or a process is running outside the `qbittorrent` service account, it appends a `qBittorrent Health` report section and sends a Discord follow-up with paste-ready RSS/search recovery commands.

Manual invocation:

```bash
/home/komichris/.codex/skills/plex-media-organizer/scripts/run_daily.sh
```

Discord reporting:

```bash
mkdir -p ~/.config/plex-media-organizer
cp /home/komichris/.codex/skills/plex-media-organizer/config/discord.env.example ~/.config/plex-media-organizer/discord.env
```

Set `PLEX_ORGANIZER_DISCORD_WEBHOOK_URL` in `~/.config/plex-media-organizer/discord.env`.
After each daily run, the automation posts the final report summary, missing anime episode gaps, and marks non-zero runs as issues.
