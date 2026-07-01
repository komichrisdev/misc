#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
state_dir=${NIGHT_OWL_STATE_DIR:-"$HOME/.local/state/night-owl"}
config_file=${NIGHT_OWL_CONFIG_FILE:-"$HOME/.config/night-owl/env"}
curl_bin=${NIGHT_OWL_CURL:-curl}
report="$state_dir/report.md"
status_dir="$script_dir/../assets/status"

[[ -f "$config_file" ]] || { echo "Missing Night Owl config: $config_file" >&2; exit 1; }
# shellcheck disable=SC1090
source "$config_file"
: "${DISCORD_WEBHOOK_URL:?DISCORD_WEBHOOK_URL is required}"

send_message() {
  local fallback image payload state
  local -a message
  mapfile -t message < <(python3 - "$1" "$2" <<'PY'
import json
import sys

content = sys.argv[1][:1900]
mode = sys.argv[2]

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
            "image": {"url": f"attachment://{state}.png"},
            "footer": {"text": f"Night Owl status: {state}"},
        }
    ],
}
print(state)
print(json.dumps(payload))
print(json.dumps({"content": content}))
PY
  )
  state=${message[0]}
  payload=${message[1]}
  fallback=${message[2]}
  image="$status_dir/$state.png"
  [[ -f "$image" ]] || { echo "Missing Night Owl status image: $image" >&2; return 1; }
  post_with_retry -fsS -F "payload_json=$payload" -F "files[0]=@$image" "$DISCORD_WEBHOOK_URL" >/dev/null ||
    post_with_retry -fsS -F "payload_json=$fallback" "$DISCORD_WEBHOOK_URL" >/dev/null
}

post_with_retry() {
  local attempt
  for attempt in 1 2; do
    if "$curl_bin" "$@"; then
      return 0
    fi
    [[ $attempt -eq 2 ]] && return 1
    sleep 1
  done
}

if [[ ${1:-} == --test-pending ]]; then
  issue=${2:?issue key is required}
  case ${3:-} in
    complete) nature='Work complete, awaiting review' ;;
    questions) nature='Work paused, I have questions' ;;
    *) echo 'Outcome must be complete or questions' >&2; exit 2 ;;
  esac
  mkdir -p "$state_dir"
  printf -- '- %s: %s. https://komichris.atlassian.net/browse/%s\n' "$issue" "$nature" "$issue" >>"$report"
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
