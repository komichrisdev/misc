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
payload=$(python3 - "$1" "$2" <<'PY'
import json
import sys

content = sys.argv[1][:1900]
mode = sys.argv[2]

status_images = {
    "idle": "https://media.staging.atl-paas.net/?type=file&localId=bf6ce1467f61&id=88fc2384-97dc-42f0-8175-bb7719b339fe&&collection=&height=1178&occurrenceKey=null&width=1335&__contextId=null&__displayType=null&__external=false&__fileMimeType=null&__fileName=null&__fileSize=null&__mediaTraceId=null&url=null",
    "question": "https://media.staging.atl-paas.net/?type=file&localId=51648e593615&id=4546063b-880d-49b7-96d6-c4b136d6fcc7&&collection=&height=1186&occurrenceKey=null&width=1326&__contextId=null&__displayType=null&__external=false&__fileMimeType=null&__fileName=null&__fileSize=null&__mediaTraceId=null&url=null",
    "done": "https://media.staging.atl-paas.net/?type=file&localId=84d794ea285f&id=7ac71a7f-a7a6-4dc5-aaf5-9f7c3590a72f&&collection=&height=1247&occurrenceKey=null&width=1261&__contextId=null&__displayType=null&__external=false&__fileMimeType=null&__fileName=null&__fileSize=null&__mediaTraceId=null&url=null",
    "working": "https://media.staging.atl-paas.net/?type=file&localId=ce91d9cef6c5&id=5535b52c-bcc7-49b1-bbe1-c0bd2c060569&&collection=&height=1199&occurrenceKey=null&width=1312&__contextId=null&__displayType=null&__external=false&__fileMimeType=null&__fileName=null&__fileSize=null&__mediaTraceId=null&url=null",
}

state = "idle"
if mode == "questions":
  state = "question"
elif mode == "complete":
  state = "done"
else:
  lowered = content.lower()
  if "paused" in lowered or "questions" in lowered or "blocked" in lowered:
    state = "question"
  elif "working" in lowered or "in progress" in lowered:
    state = "working"
  elif "complete" in lowered or "completed" in lowered:
    state = "done"

payload = {
    "content": content,
    "embeds": [
        {
            "image": {"url": status_images[state]},
            "footer": {"text": f"Night Owl status: {state}"},
        }
    ],
}
print(json.dumps(payload))
PY
)
  "$curl_bin" -fsS -H 'Content-Type: application/json' -d "$payload" "$DISCORD_WEBHOOK_URL" >/dev/null
}

if [[ ${1:-} == --test-pending ]]; then
  issue=${2:?issue key is required}
  case ${3:-} in
    complete) nature='Work complete, awaiting review' ;;
    questions) nature='Work paused, I have questions' ;;
    *) echo 'Outcome must be complete or questions' >&2; exit 2 ;;
  esac
  send_message "$issue: $nature. https://komichris.atlassian.net/browse/$issue" "${3:-}"
  exit 0
fi

if [[ -f "$report" ]]; then
  send_message "$(<"$report")" report
else
  send_message $'# Night Owl daily report\n\n- No tasks moved to Test Pending since the previous report.' report
  exit 0
fi

mkdir -p "$state_dir/sent"
mv "$report" "$state_dir/sent/$(date -u +%Y%m%dT%H%M%SZ).md"
