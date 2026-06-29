#!/usr/bin/env bash
set -euo pipefail

STATE_DIR="${HOME}/.local/state/plex-guard"
LOG_DIR="${STATE_DIR}/logs"
REPORT_DIR="${STATE_DIR}/reports"
SCAN_DIR="${STATE_DIR}/scans"
TODAY="${PLEX_GUARD_RUN_DATE:-$(date +%F)}"
LOG_FILE="${LOG_DIR}/${TODAY}.log"
AUDIT_JSON="${REPORT_DIR}/${TODAY}.json"
BASE_REPORT="${REPORT_DIR}/${TODAY}.base.md"
REPORT_FILE="${REPORT_DIR}/${TODAY}.md"
SCAN_JSON="${SCAN_DIR}/${TODAY}.json"
LAST_MESSAGE="${STATE_DIR}/last-message.txt"
LOCK_DIR="${STATE_DIR}/report-${TODAY}.lock"
CONFIG_FILE="${HOME}/.config/plex-guard/discord.env"
SKILL_DIR="${HOME}/.codex/skills/plex-guard"
AUDIT_SCRIPT="${SKILL_DIR}/scripts/audit_system.py"
DISCORD_SCRIPT="${SKILL_DIR}/scripts/report_to_discord.py"
CODEX_BIN="${CODEX_BIN:-/usr/local/bin/codex}"
CODEX_MODEL="${CODEX_MODEL:-gpt-5.4-mini}"
PLEX_GUARD_USE_CODEX="${PLEX_GUARD_USE_CODEX:-1}"
PLEX_GUARD_SEND_DISCORD="${PLEX_GUARD_SEND_DISCORD:-1}"
PLEX_GUARD_SCAN_TIMEOUT_SECONDS="${PLEX_GUARD_SCAN_TIMEOUT_SECONDS:-1800}"
PLEX_GUARD_ROOTKIT_TIMEOUT_SECONDS="${PLEX_GUARD_ROOTKIT_TIMEOUT_SECONDS:-180}"

mkdir -p "$LOG_DIR" "$REPORT_DIR" "$SCAN_DIR"

if [ -f "$CONFIG_FILE" ]; then
  set -a
  # shellcheck disable=SC1090
  source "$CONFIG_FILE"
  set +a
fi

if ! mkdir "$LOCK_DIR" 2>/dev/null; then
  printf '%s another plex-guard run is already active\n' "$(date -Is)" >> "$LOG_FILE"
  exit 0
fi
trap 'rmdir "$LOCK_DIR"' EXIT

if [ ! -x "$CODEX_BIN" ]; then
  CODEX_BIN="$(command -v codex || true)"
fi

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

status=0
{
  printf '%s starting plex-guard audit\n' "$(date -Is)"
  "$AUDIT_SCRIPT" \
    --output-json "$AUDIT_JSON" \
    --output-md "$BASE_REPORT" \
    --scan-timeout "$PLEX_GUARD_SCAN_TIMEOUT_SECONDS" \
    --scan-result-json "$SCAN_JSON" \
    --rootkit-timeout "$PLEX_GUARD_ROOTKIT_TIMEOUT_SECONDS" \
    "${scan_args[@]}" || status=$?
  printf '%s audit script exited with status %s\n' "$(date -Is)" "$status"
} >> "$LOG_FILE" 2>&1

if [ ! -s "$BASE_REPORT" ]; then
  {
    printf '# Plex Guard Daily Security Report\n\n'
    printf 'Audit did not produce a report. See log: %s\n' "$LOG_FILE"
  } > "$BASE_REPORT"
fi

cp "$BASE_REPORT" "$REPORT_FILE"

if [ "$PLEX_GUARD_USE_CODEX" = "1" ] && [ -n "$CODEX_BIN" ]; then
  prompt=$(cat <<PROMPT_EOF
Use \$plex-guard.

Read the deterministic audit JSON at ${AUDIT_JSON} and the base report at ${BASE_REPORT}. Write a concise final daily report to ${REPORT_FILE}.

Rules:
- Do not edit system config, services, firewall, media files, Crypto Keeper, Plex media organizer, qBittorrent, PIA, Plex, or SSH.
- Preserve Plex remote access, PIA startup, qBittorrent local WebUI, SSH, Plex media organizer, and Crypto Keeper.
- Keep the report short enough for Discord.
- Lead with attention required items if any; otherwise write a clean daily report.
PROMPT_EOF
)
  {
    printf '%s starting codex summarizer with model %s\n' "$(date -Is)" "$CODEX_MODEL"
    "$CODEX_BIN" \
      exec \
      --model "$CODEX_MODEL" \
      --ask-for-approval never \
      --sandbox workspace-write \
      --skip-git-repo-check \
      --cd "$HOME" \
      --output-last-message "$LAST_MESSAGE" \
      "$prompt" || printf '%s codex summarizer failed; using deterministic report\n' "$(date -Is)"
  } >> "$LOG_FILE" 2>&1
fi

if [ ! -s "$REPORT_FILE" ]; then
  cp "$BASE_REPORT" "$REPORT_FILE"
fi

if [ "$PLEX_GUARD_SEND_DISCORD" = "1" ] && [ -x "$DISCORD_SCRIPT" ]; then
  "$DISCORD_SCRIPT" "$REPORT_FILE" "$AUDIT_JSON" "$status" >> "$LOG_FILE" 2>&1 || {
    printf '%s discord notify failed\n' "$(date -Is)" >> "$LOG_FILE"
  }
fi

printf '%s finished plex-guard report build\n' "$(date -Is)" >> "$LOG_FILE"
exit "$status"
