#!/usr/bin/env bash
set -euo pipefail

skill_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)

pick_image() {
  python3 - "$1" "$skill_dir" <<'PY'
from decimal import Decimal, InvalidOperation
from pathlib import Path
import sys

try:
    amount = Decimal(sys.argv[1].replace("$", "").replace(",", ""))
except InvalidOperation as exc:
    raise SystemExit("amount must be numeric") from exc
if not amount.is_finite() or amount < 0:
    raise SystemExit("amount must be a non-negative finite number")
print(Path(sys.argv[2], "assets", "bills3.png" if amount > 750 else "bills2.png"))
PY
}

if [[ ${1:-} == --pick-image ]]; then
  pick_image "${2:?amount is required}"
  exit 0
fi

month=${1:?month title is required}
amount=${2:?D20 amount is required}
sheet_url=${3:?sheet URL is required}
[[ $sheet_url == https://docs.google.com/spreadsheets/d/1Fu0B5-nEHg-GSWKDtzLqALG0n-szuwsmDmYxRyw0RVw/* ]] || {
  echo "Unexpected spreadsheet URL" >&2
  exit 2
}
image=$(pick_image "$amount")
[[ -f $image ]] || { echo "Missing image: $image" >&2; exit 1; }

config_file=${RENT_SPLITTER_CONFIG_FILE:-"$HOME/.config/rent-splitter/env"}
[[ -f $config_file ]] || config_file="$HOME/.config/night-owl/env"
[[ -f $config_file ]] || { echo "Missing Rent Splitter config" >&2; exit 1; }
# shellcheck disable=SC1090
source "$config_file"
: "${DISCORD_WEBHOOK_URL:?DISCORD_WEBHOOK_URL is required}"

payload=$(python3 - "$month" "$amount" "$sheet_url" <<'PY'
import json, sys
print(json.dumps({"content": f"{sys.argv[1]} rent split: ${sys.argv[2].lstrip('$')}\n{sys.argv[3]}"}))
PY
)
curl_bin=${RENT_SPLITTER_CURL:-curl}
"$curl_bin" -fsS --retry 3 -F "payload_json=$payload" -F "files[0]=@$image" "$DISCORD_WEBHOOK_URL" >/dev/null
