#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
[[ $($script_dir/send_discord.sh --pick-image 750) == */bills2.png ]]
[[ $($script_dir/send_discord.sh --pick-image 750.01) == */bills3.png ]]
! "$script_dir/send_discord.sh" --pick-image NaN >/dev/null 2>&1
printf "DISCORD_WEBHOOK_URL='https://example.invalid/test'\n" >"$tmp/env"
printf '#!/usr/bin/env bash\nprintf "%%s\\n" "$@" >"$RENT_SPLITTER_TEST_OUTPUT"\n' >"$tmp/curl"
chmod +x "$tmp/curl"
RENT_SPLITTER_CONFIG_FILE="$tmp/env" RENT_SPLITTER_CURL="$tmp/curl" RENT_SPLITTER_TEST_OUTPUT="$tmp/output" \
  "$script_dir/send_discord.sh" 'June 2026' 750.01 \
  'https://docs.google.com/spreadsheets/d/1Fu0B5-nEHg-GSWKDtzLqALG0n-szuwsmDmYxRyw0RVw/edit#gid=123'
grep -q 'bills3.png' "$tmp/output"
RENT_SPLITTER_STATE_DIR="$tmp/state" "$script_dir/run_monthly.sh" --dry-run >/dev/null
echo "Rent Splitter self-test passed"
