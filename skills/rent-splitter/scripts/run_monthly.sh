#!/usr/bin/env bash
set -euo pipefail

skill_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
state_dir=${RENT_SPLITTER_STATE_DIR:-"$HOME/.local/state/rent-splitter"}
codex_bin=${RENT_SPLITTER_CODEX:-/usr/local/bin/codex}
period=${RENT_SPLITTER_PERIOD:-$(date +%Y-%m)}

if [[ ${1:-} == --dry-run ]]; then
  command -v flock >/dev/null
  command -v timeout >/dev/null
  command -v "$codex_bin" >/dev/null
  [[ -f $skill_dir/assets/bills2.png && -f $skill_dir/assets/bills3.png ]]
  echo "Rent Splitter ready: period=$period"
  exit 0
fi

[[ ${1:-} == --force || $(date -d tomorrow +%m) != $(date +%m) ]] || exit 0
mkdir -p "$state_dir"
exec 9>"$state_dir/rent-splitter.lock"
flock -n 9 || exit 0
marker="$state_dir/$period.complete"
[[ ! -e $marker ]] || exit 0

log="$state_dir/$period.jsonl"
last_message="$state_dir/$period.last-message.txt"
set +e
timeout --signal=TERM --kill-after=5m 2h \
  "$codex_bin" -a never exec --skip-git-repo-check -C "$HOME" \
  -m gpt-5.4-mini -c 'model_reasoning_effort="medium"' \
  -s workspace-write --json -o "$last_message" \
  "Use \$rent-splitter to process billing month $period. Read Gmail only, update the verified Google Sheet tab, and send the verified result to Discord." \
  >"$log" 2>&1
status=$?
set -e

if (( status == 0 )) && grep -qx 'RENT_SPLITTER_COMPLETE' "$last_message"; then
  touch "$marker"
  exit 0
fi

echo "Rent Splitter failed for $period; inspect $log" >&2
(( status != 0 )) || status=1
exit "$status"
