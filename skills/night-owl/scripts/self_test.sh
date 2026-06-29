#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
state_dir=$(mktemp -d)
trap 'rm -rf "$state_dir"' EXIT

NIGHT_OWL_STATE_DIR="$state_dir" "$script_dir/run_nightly.sh" --dry-run >/dev/null
python3 "$script_dir/download_jira_attachment.py" --self-test >/dev/null

printf '# Existing report\n' >"$state_dir/report.md"
NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CODEX=/bin/true NIGHT_OWL_TIMEOUT=1s NIGHT_OWL_RUN_HOURS=1 \
  "$script_dir/run_nightly.sh"
compgen -G "$state_dir/*.jsonl" >/dev/null
grep -q 'Existing report' "$state_dir/report.md"

capture_dir="$state_dir/captures"
mkdir -p "$capture_dir"
cat >"$state_dir/curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
python3 - "$@" <<'PY'
from pathlib import Path
import os
import sys

capture_dir = Path(os.environ["NIGHT_OWL_CAPTURE_DIR"])
args = sys.argv[1:]
payload = ""
for index, arg in enumerate(args[:-1]):
    if arg == "-d":
        payload = args[index + 1]
        break

count_file = capture_dir / "count"
count = int(count_file.read_text(encoding="utf-8")) if count_file.exists() else 0
count += 1
count_file.write_text(str(count), encoding="utf-8")
(capture_dir / f"{count}.json").write_text(payload, encoding="utf-8")
PY
EOF
chmod +x "$state_dir/curl"

printf "DISCORD_WEBHOOK_URL='https://example.invalid/test'\n" >"$state_dir/env"
NIGHT_OWL_CAPTURE_DIR="$capture_dir" NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh"
[[ ! -e "$state_dir/report.md" ]]
compgen -G "$state_dir/sent/*.md" >/dev/null
NIGHT_OWL_CAPTURE_DIR="$capture_dir" NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh" --test-pending KOMI-5 complete
NIGHT_OWL_CAPTURE_DIR="$capture_dir" NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh" --test-pending KOMI-5 questions
NIGHT_OWL_CAPTURE_DIR="$capture_dir" NIGHT_OWL_STATE_DIR="$state_dir" NIGHT_OWL_CONFIG_FILE="$state_dir/env" NIGHT_OWL_CURL="$state_dir/curl" \
  "$script_dir/send_report.sh"

python3 - "$capture_dir" <<'PY'
import json
import sys
from pathlib import Path

capture_dir = Path(sys.argv[1])
payloads = [json.loads(path.read_text(encoding="utf-8")) for path in sorted(capture_dir.glob("*.json"))]
assert len(payloads) == 4, len(payloads)
assert payloads[0]["embeds"][0]["footer"]["text"] == "Night Owl status: idle"
assert payloads[1]["embeds"][0]["footer"]["text"] == "Night Owl status: done"
assert payloads[2]["embeds"][0]["footer"]["text"] == "Night Owl status: question"
assert payloads[3]["embeds"][0]["footer"]["text"] == "Night Owl status: idle"
for payload in payloads:
    assert payload["embeds"][0]["image"]["url"].startswith("https://media.staging.atl-paas.net/")
PY

echo "Night Owl self-test passed"
