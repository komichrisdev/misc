#!/usr/bin/env bash
set -euo pipefail

state_dir=${NIGHT_OWL_STATE_DIR:-"$HOME/.local/state/night-owl"}
config_file=${NIGHT_OWL_CONFIG_FILE:-"$HOME/.config/night-owl/env"}
curl_bin=${NIGHT_OWL_CURL:-curl}
report="$state_dir/report.md"

[[ -f "$config_file" ]] || { echo "Missing Night Owl config: $config_file" >&2; exit 1; }
# shellcheck disable=SC1090
source "$config_file"
: "${DISCORD_WEBHOOK_URL:?DISCORD_WEBHOOK_URL is required}"

send_message() {
  local payload
  payload=$(python3 -c 'import json, sys; print(json.dumps({"content": sys.argv[1][:1900]}))' "$1")
  "$curl_bin" -fsS -H 'Content-Type: application/json' -d "$payload" "$DISCORD_WEBHOOK_URL" >/dev/null
}

if [[ ${1:-} == --test-pending ]]; then
  issue=${2:?issue key is required}
  case ${3:-} in
    complete) nature='Work complete, awaiting review' ;;
    questions) nature='Work paused, I have questions' ;;
    *) echo 'Outcome must be complete or questions' >&2; exit 2 ;;
  esac
  send_message "$issue: $nature. https://komichris.atlassian.net/browse/$issue"
  exit 0
fi

if [[ -f "$report" ]]; then
  send_message "$(<"$report")"
else
  send_message $'# Night Owl daily report\n\n- No tasks moved to Test Pending since the previous report.'
  exit 0
fi

mkdir -p "$state_dir/sent"
mv "$report" "$state_dir/sent/$(date -u +%Y%m%dT%H%M%SZ).md"
