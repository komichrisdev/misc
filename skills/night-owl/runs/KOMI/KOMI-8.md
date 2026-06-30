# KOMI-8: Plex Media Organizer Upgrade

- Outcome: Implemented and pushed the Plex organizer updates for the Jira anime-intake path, review logging, missing-episode reporting, qBittorrent RSS sync, and qBittorrent health reporting.
- Update 2: Created English Plex directories and enabled advance qBittorrent rules for all seven requested titles, using both configured Nyaa and SubsPlease feeds.
- Update 3: Patched the Discord reporter so it attaches the newest run-art demo image from `~/.local/state/plex-media-organizer/demos/` when one exists.
- Checks: `bash -n` on the shell scripts, `python3 -m py_compile` on the new Python helpers, and `git diff --check`.
- Checks, continued: reporter smoke test passed with a fake curl capture and confirmed multipart upload plus `attachment://<filename>` image references.
- Live checks: validated the seven-rule change against a copy of `download_rules.json`, applied it through the qBittorrent WebUI, and read back all rule names, filters, feed bindings, categories, histories, and save paths.
- Commit: `c04813f` pushed to `https://github.com/komichrisdev/Plex-Picker`; reporter follow-up `b860b6c` pushed to `https://github.com/komichrisdev/Plex-Picker`.
- Jira: https://komichris.atlassian.net/browse/KOMI-8
