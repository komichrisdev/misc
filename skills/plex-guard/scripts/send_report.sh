#!/usr/bin/env bash
set -euo pipefail

STATE_DIR="${HOME}/.local/state/plex-guard"
LOG_DIR="${STATE_DIR}/logs"
REPORT_DIR="${STATE_DIR}/reports"
TODAY="${PLEX_GUARD_RUN_DATE:-$(date +%F)}"
LOG_FILE="${LOG_DIR}/${TODAY}.log"
AUDIT_JSON="${REPORT_DIR}/${TODAY}.json"
REPORT_FILE="${REPORT_DIR}/${TODAY}.md"
CONFIG_FILE="${HOME}/.config/plex-guard/discord.env"
SKILL_DIR="${HOME}/.codex/skills/plex-guard"
DISCORD_SCRIPT="${SKILL_DIR}/scripts/report_to_discord.py"

mkdir -p "$LOG_DIR" "$REPORT_DIR"

if [ -f "$CONFIG_FILE" ]; then
  set -a
  # shellcheck disable=SC1090
  source "$CONFIG_FILE"
  set +a
fi

if [ ! -s "$REPORT_FILE" ] || [ ! -s "$AUDIT_JSON" ]; then
  {
    printf '# Plex Guard Daily Security Report\n\n'
    printf 'Overall: **WARNING**\n\n'
    printf 'Plex Guard did not find a completed report for %s. Check the overnight scan/build logs.\n' "$TODAY"
  } > "$REPORT_FILE"
  printf '{"summary":{"overall":"warning"}}\n' > "$AUDIT_JSON"
fi

if [ -x "$DISCORD_SCRIPT" ]; then
  "$DISCORD_SCRIPT" "$REPORT_FILE" "$AUDIT_JSON" 0 >> "$LOG_FILE" 2>&1
  printf '%s sent plex-guard discord report for %s\n' "$(date -Is)" "$TODAY" >> "$LOG_FILE"
fi
