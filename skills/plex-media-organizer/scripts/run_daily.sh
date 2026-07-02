#!/usr/bin/env bash
set -euo pipefail

STATE_DIR="${HOME}/.local/state/plex-media-organizer"
LOG_DIR="${STATE_DIR}/logs"
REPORT_DIR="${STATE_DIR}/reports"
LOCK_DIR="${STATE_DIR}/run.lock"
CODEX_BIN="${CODEX_BIN:-/usr/local/bin/codex}"
TODAY="$(date +%F)"
LOG_FILE="${LOG_DIR}/${TODAY}.log"
REPORT_FILE="${REPORT_DIR}/${TODAY}.md"
SANDBOX_REPORT_FILE=""
LAST_MESSAGE="${STATE_DIR}/last-message.txt"
CONFIG_FILE="${HOME}/.config/plex-media-organizer/discord.env"
DISCORD_REPORTER="/home/komichris/.codex/skills/plex-media-organizer/scripts/report_to_discord.sh"
RUN_ART_GENERATOR="/home/komichris/.codex/skills/plex-media-organizer/scripts/generate_run_art.sh"
ANIME_MISSING_CHECKER="/home/komichris/.codex/skills/plex-media-organizer/scripts/check_anime_missing_episodes.py"
REVIEW_LOGGER="/home/komichris/.codex/skills/plex-media-organizer/scripts/review_log.py"
RSS_RULE_SYNCER="/home/komichris/.codex/skills/plex-media-organizer/scripts/sync_qbittorrent_rules.py"
QBITTORRENT_HEALTH_CHECKER="/home/komichris/.codex/skills/plex-media-organizer/scripts/check_qbittorrent_health.sh"
CODEX_MODEL="gpt-5.4-mini"
RSS_RULES_FILE="${RSS_RULES_FILE:-/var/lib/qbittorrent/.config/qBittorrent/rss/download_rules.json}"
FORCE_RSS_RULE_SYNC="${FORCE_RSS_RULE_SYNC:-0}"
REVIEW_LOG_FILE="${STATE_DIR}/review-log.json"
REVIEW_PLAN_JSON="${STATE_DIR}/review-plan.json"
REVIEW_PLAN_FILE="${STATE_DIR}/review-plan.md"
REVIEW_SUMMARY_FILE="${STATE_DIR}/review-summary.md"
MISSING_REPORT_FILE="${STATE_DIR}/missing-anime-report.txt"
RSS_SYNC_REPORT_FILE="${STATE_DIR}/rss-rule-sync.md"
RSS_SYNC_OPERATIONS_FILE="${STATE_DIR}/rss-rule-updates.json"
QBITTORRENT_HEALTH_REPORT_FILE="${STATE_DIR}/qbittorrent-health.md"
RUN_ART_FILE=""

mkdir -p "$LOG_DIR" "$REPORT_DIR"
: > "$REPORT_FILE"

if [ -f "$CONFIG_FILE" ]; then
  set -a
  # shellcheck disable=SC1090
  source "$CONFIG_FILE"
  set +a
fi

if ! mkdir "$LOCK_DIR" 2>/dev/null; then
  printf '%s another plex-media-organizer run is already active\n' "$(date -Is)" >> "$LOG_FILE"
  exit 0
fi
SANDBOX_REPORT_FILE="$(mktemp "${TMPDIR:-/tmp}/plex-media-organizer-report.XXXXXX.md")"
SANDBOX_RSS_UPDATES_FILE="$(mktemp "${TMPDIR:-/tmp}/plex-media-organizer-rss-updates.XXXXXX.json")"
SANDBOX_RSS_RULES_SNAPSHOT="$(mktemp "${TMPDIR:-/tmp}/plex-media-organizer-rss-rules.XXXXXX.json")"
printf '%s\n' '{"version":1,"rss_rule_updates":[]}' > "$SANDBOX_RSS_UPDATES_FILE"
printf '%s\n' '{}' > "$SANDBOX_RSS_RULES_SNAPSHOT"
if [ -r "$RSS_RULES_FILE" ]; then
  cp "$RSS_RULES_FILE" "$SANDBOX_RSS_RULES_SNAPSHOT" 2>/dev/null || true
fi
cleanup() {
  if [ -n "$SANDBOX_REPORT_FILE" ]; then
    rm -f "$SANDBOX_REPORT_FILE"
  fi
  if [ -n "$SANDBOX_RSS_UPDATES_FILE" ]; then
    rm -f "$SANDBOX_RSS_UPDATES_FILE"
  fi
  if [ -n "$SANDBOX_RSS_RULES_SNAPSHOT" ]; then
    rm -f "$SANDBOX_RSS_RULES_SNAPSHOT"
  fi
  rmdir "$LOCK_DIR"
}
trap cleanup EXIT

append_report_section() {
  local report_file="$1"
  local section_file="$2"
  python3 - "$report_file" "$section_file" <<'PY'
import sys
from pathlib import Path

report_path = Path(sys.argv[1])
section_path = Path(sys.argv[2])

if not section_path.exists():
    raise SystemExit(0)

section = section_path.read_text(encoding="utf-8").strip()
if not section:
    raise SystemExit(0)

report = report_path.read_text(encoding="utf-8").rstrip() if report_path.exists() else ""
if report:
    report += "\n\n"
report += section
report_path.write_text(report.rstrip() + "\n", encoding="utf-8")
PY
}

if [ ! -x "$CODEX_BIN" ]; then
  CODEX_BIN="$(command -v codex)"
fi

REVIEW_LOG_ENABLED=0
REVIEW_NEEDED=1
REVIEW_PLAN_INSTRUCTIONS=""

if [ -x "$REVIEW_LOGGER" ]; then
  if "$REVIEW_LOGGER" prepare \
    --log "$REVIEW_LOG_FILE" \
    --plan-json "$REVIEW_PLAN_JSON" \
    --plan-md "$REVIEW_PLAN_FILE" \
    /srv/media/anime /srv/media/tv /srv/media/movies >> "$LOG_FILE" 2>&1; then
    REVIEW_LOG_ENABLED=1
    if "$REVIEW_LOGGER" needs-review --plan-json "$REVIEW_PLAN_JSON" >> "$LOG_FILE" 2>&1; then
      REVIEW_NEEDED=1
    else
      REVIEW_NEEDED=0
    fi
    if [ "$FORCE_RSS_RULE_SYNC" = "1" ]; then
      REVIEW_NEEDED=1
    fi
    REVIEW_PLAN_INSTRUCTIONS=$(cat <<PLAN_EOF

Review log:
- Persistent review log: ${REVIEW_LOG_FILE}
- Generated review plan: ${REVIEW_PLAN_FILE}
- Review only media entries listed under "Directories To Review" in the plan.
- Do not inventory or recheck entries listed under "Skipped Unchanged Directories"; their fingerprints match the prior reviewed/fixed state.
- You may check whether a skipped path exists only when needed for collision safety.
PLAN_EOF
)
  else
    printf '%s review log prepare failed; falling back to full review\n' "$(date -Is)" >> "$LOG_FILE"
  fi
fi

RSS_RULE_INSTRUCTIONS=$(cat <<RULES_EOF

qBittorrent RSS rules:
- Live rules file: ${RSS_RULES_FILE}
- Snapshot for this run: ${SANDBOX_RSS_RULES_SNAPSHOT}
- Inspect active auto-download rules before deciding whether a directory rename or Season NN move should also change a qBittorrent save path.
- If FORCE_RSS_RULE_SYNC is active, you may emit RSS-only updates even when no media files need moving, but only for high-confidence mismatches between active rules and the current Plex-ready library layout.
- Prefer updating an existing rule by exact rule name when it clearly maps to the series or season you are reviewing.
- If a rule name already includes a season marker and the library clearly uses a matching Season NN folder, you may retarget that rule in place and tighten mustContain or mustNotContain only when the mapping is unambiguous.
RULES_EOF
)

PROMPT=$(cat <<PROMPT_EOF
Use \$plex-media-organizer.

Run the daily conservative Plex media organization pass for:
- /srv/media/anime
- /srv/media/tv
- /srv/media/movies
${REVIEW_PLAN_INSTRUCTIONS}
${RSS_RULE_INSTRUCTIONS}

Requirements:
- Use live web search when needed to confirm accurate English titles, years, seasons, and episode mapping.
- Rename only high-confidence matches into Plex-friendly formats.
- TV/anime target format: Series Title/Season NN/Series Title - S01E01 - Episode Title.ext.
- Movie target format: Movie Title (Year)/Movie Title (Year).ext.
- Keep subtitle sidecars aligned with renamed media basenames when unambiguous.
- Do not delete files.
- When multiple complete copies of the same episode are present, prefer `SubsPlease` as the canonical keep unless a higher-confidence reason exists to pick a different release, such as a clearly superior `v2` or repack that is already established as the intended replacement.
- Do not guess uncertain anime specials, OVAs, movies, alternate cuts, absolute episode numbering, collections, duplicate candidates, or weak web matches.
- If uncertain, leave files unchanged and record the reason.
- Write the final report to ${SANDBOX_REPORT_FILE}. Include applied changes, held changes, and any errors.
- Keep the report concise. Omit scope, review-plan details, sources used, and review-log paths.
- Omit empty sections instead of writing `None`.
- Write machine-readable RSS auto-download rule updates to ${SANDBOX_RSS_UPDATES_FILE} as JSON with schema:
  {"version":1,"rss_rule_updates":[{"rule_name":"...","save_path":"...","source_rule":"optional","must_contain":"optional","must_not_contain":"optional","assigned_category":"optional","use_auto_tmm":false,"reset_history":false}]}
- Only include RSS updates when you made a high-confidence directory rename or moved new-season downloads into a Season NN folder and qBittorrent should follow that new path.
- You may also include RSS-only updates when FORCE_RSS_RULE_SYNC is active and the active rule clearly points at an outdated destination compared with the current library layout.
- If an existing broad rule must keep season 1 while a new season-specific rule should be created, emit two updates: one for the old rule and one cloned from it for the new season rule.
- Do not write directly to the review log or ${STATE_DIR}; the outer runner will copy the report and update persistent state after Codex exits.
PROMPT_EOF
)

status=0
if [ "$REVIEW_NEEDED" -eq 0 ]; then
  {
    printf '%s starting plex-media-organizer\n' "$(date -Is)"
    printf '%s no changed media directories; Codex review skipped by review log\n' "$(date -Is)"
    {
      printf '## Notes\n\n'
      printf -- '- No changed media directories were queued by the review log.\n'
    } > "$REPORT_FILE"
    printf '%s finished plex-media-organizer\n' "$(date -Is)"
  } >> "$LOG_FILE" 2>&1
else
  {
    printf '%s starting plex-media-organizer\n' "$(date -Is)"
    "$CODEX_BIN" \
      --search \
      --ask-for-approval never \
      exec \
      --model "$CODEX_MODEL" \
      --sandbox workspace-write \
      --skip-git-repo-check \
      --cd /srv/media \
      --output-last-message "$LAST_MESSAGE" \
      "$PROMPT" || status=$?
    printf '%s finished plex-media-organizer\n' "$(date -Is)"
  } >> "$LOG_FILE" 2>&1
fi

if [ -s "$SANDBOX_REPORT_FILE" ]; then
  cp "$SANDBOX_REPORT_FILE" "$REPORT_FILE"
fi

cp "$SANDBOX_RSS_UPDATES_FILE" "$RSS_SYNC_OPERATIONS_FILE"

if [ "$status" -eq 0 ] && [ -f "$RSS_RULE_SYNCER" ]; then
  if python3 "$RSS_RULE_SYNCER" \
    --operations "$SANDBOX_RSS_UPDATES_FILE" \
    --rules "$RSS_RULES_FILE" \
    --report "$RSS_SYNC_REPORT_FILE" >> "$LOG_FILE" 2>&1; then
    :
  else
    printf '%s rss rule sync failed\n' "$(date -Is)" >> "$LOG_FILE"
  fi
  if [ -s "$RSS_SYNC_REPORT_FILE" ]; then
    append_report_section "$REPORT_FILE" "$RSS_SYNC_REPORT_FILE"
  fi
fi

if [ "$status" -ne 0 ] && [ ! -s "$REPORT_FILE" ]; then
  {
    printf '## Run Error\n\n'
    printf 'Codex exited with status %s before writing a cleanup report.\n\n' "$status"
    printf 'See log: %s\n' "$LOG_FILE"
  } >> "$REPORT_FILE"
fi

if [ "$REVIEW_LOG_ENABLED" -eq 1 ]; then
  "$REVIEW_LOGGER" update \
    --log "$REVIEW_LOG_FILE" \
    --plan-json "$REVIEW_PLAN_JSON" \
    --status "$status" \
    --missing-md "$MISSING_REPORT_FILE" \
    --summary-md "$REVIEW_SUMMARY_FILE" \
    --report "$REPORT_FILE" \
    /srv/media/anime /srv/media/tv /srv/media/movies >> "$LOG_FILE" 2>&1 || {
      printf '%s review log update failed\n' "$(date -Is)" >> "$LOG_FILE"
    }
elif [ -x "$ANIME_MISSING_CHECKER" ]; then
  missing_report="$("$ANIME_MISSING_CHECKER" /srv/media/anime 2>> "$LOG_FILE" || true)"
  if [ -n "$missing_report" ]; then
    printf '%s\n' "$missing_report" > "$MISSING_REPORT_FILE"
    python3 - "$REPORT_FILE" "$MISSING_REPORT_FILE" <<'PY'
import sys
from pathlib import Path

report_path = Path(sys.argv[1])
missing_path = Path(sys.argv[2])

report_lines = report_path.read_text(encoding="utf-8").splitlines() if report_path.exists() else []
missing_text = missing_path.read_text(encoding="utf-8").rstrip("\n")
missing_lines = missing_text.splitlines()

clean_lines = []
i = 0
while i < len(report_lines):
    if report_lines[i] == "## Missing Anime Episodes":
        i += 1
        while i < len(report_lines) and not (
            report_lines[i].startswith("## ") and report_lines[i] != "## Missing Anime Episodes"
        ):
            i += 1
        continue
    clean_lines.append(report_lines[i])
    i += 1

output = "\n".join(clean_lines).rstrip()
if output:
    output += "\n\n"
output += "\n".join(missing_lines).rstrip() + "\n"
report_path.write_text(output, encoding="utf-8")
PY
  fi
fi

if [ -f "$QBITTORRENT_HEALTH_CHECKER" ]; then
  if bash "$QBITTORRENT_HEALTH_CHECKER" --report "$QBITTORRENT_HEALTH_REPORT_FILE" >> "$LOG_FILE" 2>&1; then
    append_report_section "$REPORT_FILE" "$QBITTORRENT_HEALTH_REPORT_FILE"
  else
    printf '%s qBittorrent health check failed\n' "$(date -Is)" >> "$LOG_FILE"
  fi
fi

if [ -x "$RUN_ART_GENERATOR" ]; then
  RUN_ART_FILE="${STATE_DIR}/demos/${TODAY}-$(date +%H%M%S)-run-art.png"
  if ! CODEX_BIN="$CODEX_BIN" CODEX_MODEL="$CODEX_MODEL" \
    "$RUN_ART_GENERATOR" "$REPORT_FILE" "$RUN_ART_FILE" >> "$LOG_FILE" 2>&1; then
    RUN_ART_FILE=""
    status=1
    printf '%s run art generation failed\n' "$(date -Is)" >> "$LOG_FILE"
    printf '\n## Run Art Error\n\n- Required report image generation failed.\n' >> "$REPORT_FILE"
  fi
else
  status=1
  printf '%s run art generator missing or not executable\n' "$(date -Is)" >> "$LOG_FILE"
  printf '\n## Run Art Error\n\n- Required report image generator is unavailable.\n' >> "$REPORT_FILE"
fi

if [ -x "$DISCORD_REPORTER" ]; then
  "$DISCORD_REPORTER" "$REPORT_FILE" "$LOG_FILE" "$status" "$RUN_ART_FILE" >> "$LOG_FILE" 2>&1 || {
    printf '%s discord notify failed\n' "$(date -Is)" >> "$LOG_FILE"
  }
fi

exit "$status"
