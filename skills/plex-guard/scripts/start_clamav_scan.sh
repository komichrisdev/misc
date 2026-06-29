#!/usr/bin/env bash
set -euo pipefail

STATE_DIR="${HOME}/.local/state/plex-guard"
LOG_DIR="${STATE_DIR}/logs"
SCAN_DIR="${STATE_DIR}/scans"
TODAY="${PLEX_GUARD_RUN_DATE:-$(date +%F)}"
LOG_FILE="${LOG_DIR}/${TODAY}-clamav.log"
SCAN_JSON="${SCAN_DIR}/${TODAY}.json"
SCAN_OUTPUT="${SCAN_DIR}/${TODAY}.clamscan.txt"
LOCK_DIR="${STATE_DIR}/clamav-${TODAY}.lock"
CONFIG_FILE="${HOME}/.config/plex-guard/discord.env"
SKILL_DIR="${HOME}/.codex/skills/plex-guard"
SCAN_SCRIPT="${SKILL_DIR}/scripts/start_clamav_scan.py"
PLEX_GUARD_SCAN_TIMEOUT_SECONDS="${PLEX_GUARD_SCAN_TIMEOUT_SECONDS:-1800}"

mkdir -p "$LOG_DIR" "$SCAN_DIR"

if [ -f "$CONFIG_FILE" ]; then
  set -a
  # shellcheck disable=SC1090
  source "$CONFIG_FILE"
  set +a
fi

if ! mkdir "$LOCK_DIR" 2>/dev/null; then
  printf '%s clamav scan already active for %s\n' "$(date -Is)" "$TODAY" >> "$LOG_FILE"
  exit 0
fi
trap 'rmdir "$LOCK_DIR"' EXIT

scan_args=()
if [ -n "${PLEX_GUARD_SCAN_PATHS:-}" ]; then
  # shellcheck disable=SC2206
  paths=( ${PLEX_GUARD_SCAN_PATHS} )
else
  paths=(/home/komichris /srv/media /tmp)
fi
for path in "${paths[@]}"; do
  scan_args+=(--scan-path "$path")
done

{
  printf '%s starting plex-guard clamav scan for %s\n' "$(date -Is)" "$TODAY"
  "$SCAN_SCRIPT" \
    --output-json "$SCAN_JSON" \
    --output-log "$SCAN_OUTPUT" \
    --scan-timeout "$PLEX_GUARD_SCAN_TIMEOUT_SECONDS" \
    "${scan_args[@]}"
  printf '%s finished plex-guard clamav scan for %s\n' "$(date -Is)" "$TODAY"
} >> "$LOG_FILE" 2>&1
