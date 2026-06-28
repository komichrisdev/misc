#!/usr/bin/env bash
set -euo pipefail

state_dir=${NIGHT_OWL_STATE_DIR:-"$HOME/.local/state/night-owl"}
config_file=${NIGHT_OWL_CONFIG_FILE:-"$HOME/.config/night-owl/env"}
curl_bin=${NIGHT_OWL_CURL:-curl}
report="$state_dir/report.md"

[[ -f "$report" ]] || exit 0
[[ -f "$config_file" ]] || { echo "Missing Night Owl config: $config_file" >&2; exit 1; }
# shellcheck disable=SC1090
source "$config_file"
: "${DISCORD_WEBHOOK_URL:?DISCORD_WEBHOOK_URL is required}"

payload=$(python3 -c 'import json, pathlib, sys; print(json.dumps({"content": pathlib.Path(sys.argv[1]).read_text()[:1900]}))' "$report")
"$curl_bin" -fsS -H 'Content-Type: application/json' -d "$payload" "$DISCORD_WEBHOOK_URL" >/dev/null

mkdir -p "$state_dir/sent"
mv "$report" "$state_dir/sent/$(date -u +%Y%m%dT%H%M%SZ).md"
