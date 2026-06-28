#!/usr/bin/env bash
set -euo pipefail

skill_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
state_dir=${NIGHT_OWL_STATE_DIR:-"$HOME/.local/state/night-owl"}
codex_bin=${NIGHT_OWL_CODEX:-/usr/local/bin/codex}
run_hours=${NIGHT_OWL_RUN_HOURS:-4}
run_limit=${NIGHT_OWL_TIMEOUT:-${run_hours}h}

[[ $run_hours =~ ^[1-9][0-9]*$ ]] || { echo "NIGHT_OWL_RUN_HOURS must be a positive integer" >&2; exit 1; }

mkdir -p "$state_dir"
exec 9>"$state_dir/night-owl.lock"
flock -n 9 || exit 0

for command in flock timeout "$codex_bin"; do
  command -v "$command" >/dev/null || { echo "Missing command: $command" >&2; exit 1; }
done
[[ -f "$skill_dir/projects.json" ]] || { echo "Missing projects.json" >&2; exit 1; }

if [[ ${1:-} == --dry-run ]]; then
  echo "Night Owl ready: skill=$skill_dir state=$state_dir limit=$run_limit"
  exit 0
fi

run_id=$(date -u +%Y%m%dT%H%M%SZ)
log="$state_dir/$run_id.jsonl"
last_message="$state_dir/last-message.txt"
deadline=$(date -d "+$run_hours hours" --iso-8601=seconds)

set +e
timeout --signal=TERM --kill-after=5m "$run_limit" \
  "$codex_bin" exec --skip-git-repo-check -C "$HOME" \
  -m gpt-5.4-mini -c 'model_reasoning_effort="medium"' \
  -s workspace-write -a never --json -o "$last_message" \
  "Use \$night-owl to process the eligible Jira queue sequentially. Stop new work before $deadline so Jira and GitHub handoffs finish on time." \
  >"$log" 2>&1
status=$?
set -e

if (( status != 0 )); then
  cat >>"$state_dir/report.md" <<EOF
- Automation failed with exit code $status.
- Log: $log
EOF
fi

exit "$status"
