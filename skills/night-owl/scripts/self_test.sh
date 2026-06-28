#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
state_dir=$(mktemp -d)
trap 'rm -rf "$state_dir"' EXIT

NIGHT_OWL_STATE_DIR="$state_dir" "$script_dir/run_nightly.sh" --dry-run >/dev/null

printf '# Existing report\n' >"$state_dir/report.md"
NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CODEX=/bin/true NIGHT_OWL_TIMEOUT=1s NIGHT_OWL_RUN_HOURS=1 \
  "$script_dir/run_nightly.sh"
compgen -G "$state_dir/*.jsonl" >/dev/null
grep -q 'Existing report' "$state_dir/report.md"

printf "DISCORD_WEBHOOK_URL='https://example.invalid/test'\n" >"$state_dir/env"
printf '#!/usr/bin/env bash\nexit 0\n' >"$state_dir/curl"
chmod +x "$state_dir/curl"
NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh"
[[ ! -e "$state_dir/report.md" ]]
compgen -G "$state_dir/sent/*.md" >/dev/null
NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh" --test-pending KOMI-5 complete
NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh" --test-pending KOMI-5 questions
NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh"
echo "Night Owl self-test passed"
