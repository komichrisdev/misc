#!/usr/bin/env bash
set -euo pipefail

REPORT_FILE="${1:?report file required}"
LOG_FILE="${2:?log file required}"
STATUS="${3:?status required}"
STATE_DIR="${HOME}/.local/state/plex-media-organizer"
DEMOS_DIR="${STATE_DIR}/demos"
curl_bin="${CURL_BIN:-curl}"

WEBHOOK_URL="${PLEX_ORGANIZER_DISCORD_WEBHOOK_URL:-}"
USERNAME="${PLEX_ORGANIZER_DISCORD_USERNAME:-Plex Organizer}"

if [ -z "$WEBHOOK_URL" ]; then
  printf '%s discord notify skipped: PLEX_ORGANIZER_DISCORD_WEBHOOK_URL is not set\n' "$(date -Is)" >> "$LOG_FILE"
  exit 0
fi

if [ "$STATUS" -eq 0 ]; then
  TITLE="Plex cleanup processed"
else
  TITLE="Plex cleanup had issues"
fi

if [ "$#" -ge 4 ]; then
  IMAGE_FILE="$4"
else
  IMAGE_FILE="$(
  python3 - "$REPORT_FILE" "$DEMOS_DIR" <<'PY'
import sys
from pathlib import Path

report_file = Path(sys.argv[1])
demos_dir = Path(sys.argv[2])
if not report_file.exists() or not demos_dir.is_dir():
    raise SystemExit(0)

report_mtime = report_file.stat().st_mtime_ns
choices = []
for path in demos_dir.iterdir():
    if path.suffix.lower() not in {".png", ".jpg", ".jpeg", ".webp"}:
        continue
    try:
        stat = path.stat()
    except OSError:
        continue
    if stat.st_mtime_ns >= report_mtime:
        choices.append((stat.st_mtime_ns, path))

if choices:
    print(max(choices, key=lambda item: item[0])[1])
PY
)"
fi

if [ -n "$IMAGE_FILE" ] && [ ! -f "$IMAGE_FILE" ]; then
  printf '%s discord notify failed: image not found: %s\n' "$(date -Is)" "$IMAGE_FILE" >> "$LOG_FILE"
  exit 1
fi

export TITLE REPORT_FILE STATUS USERNAME IMAGE_FILE

payload="$(
  python3 - <<'PY'
import json
import os
from pathlib import Path

title = os.environ["TITLE"]
report_file = os.environ["REPORT_FILE"]
status = os.environ["STATUS"]
username = os.environ["USERNAME"]

try:
    lines = Path(report_file).read_text(encoding="utf-8").splitlines()
except FileNotFoundError:
    lines = []

def parse_sections(markdown_lines):
    sections = []
    current_title = None
    current_body = []
    preamble = []

    for line in markdown_lines:
        if line.startswith("## "):
            if current_title is None and preamble:
                sections.append(("", preamble))
                preamble = []
            elif current_title is not None:
                sections.append((current_title, current_body))
            current_title = line
            current_body = []
            continue

        if current_title is None:
            preamble.append(line)
        else:
            current_body.append(line)

    if current_title is None and preamble:
        sections.append(("", preamble))
    elif current_title is not None:
        sections.append((current_title, current_body))
    return sections

def cleaned_body(body_lines):
    body = [line.rstrip() for line in body_lines]
    while body and not body[0]:
        body.pop(0)
    while body and not body[-1]:
        body.pop()
    return body

def section_is_empty(title, body_lines):
    body = cleaned_body(body_lines)
    if not body:
        return True
    non_blank = [line for line in body if line.strip()]
    if not non_blank:
        return True
    if all(line == "- None." for line in non_blank):
        return True
    if title == "## RSS Sync" and non_blank == ["- No qBittorrent RSS rule updates were needed."]:
        return True
    return False

if lines:
    rendered = []
    for title_line, body_lines in parse_sections(lines):
        if title_line and section_is_empty(title_line, body_lines):
            continue
        body = cleaned_body(body_lines)
        if title_line:
            rendered.append(title_line)
        rendered.extend(body)
        rendered.append("")
    while rendered and not rendered[-1]:
        rendered.pop()
    summary = "\n".join(rendered).strip()
else:
    summary = "No report was written. The cleaner may have stopped before it could produce details."

result = "Success" if status == "0" else f"Issue exit code {status}"
body = f"**{title}**\nResult: {result}"
if summary:
  body = f"{body}\n\n**Run report**\n{summary}"
if len(body) > 1900:
    prefix = f"**{title}**\nResult: {result}\n\n**Run report**\n"
    budget = 1900 - len(prefix) - len("\n...\n")
    if budget > 0 and summary:
        head_budget = max(budget * 2 // 3, 1)
        tail_budget = max(budget - head_budget, 1)
        if len(summary) > budget:
            summary = f"{summary[:head_budget].rstrip()}\n...\n{summary[-tail_budget:].lstrip()}"
        body = f"{prefix}{summary}"
    if len(body) > 1900:
        body = body[:1897].rstrip() + "..."

payload = {"username": username, "content": body}
image_file = os.environ.get("IMAGE_FILE", "")
if image_file:
    payload["embeds"] = [{"image": {"url": f"attachment://{Path(image_file).name}"}}]

print(json.dumps(payload))
PY
)"

if [ -n "$IMAGE_FILE" ]; then
  "$curl_bin" --fail --silent --show-error \
    --form-string "payload_json=$payload" \
    -F "files[0]=@$IMAGE_FILE" \
    "$WEBHOOK_URL" >/dev/null
else
  "$curl_bin" --fail --silent --show-error \
    -H 'Content-Type: application/json' \
    -d "$payload" \
    "$WEBHOOK_URL" >/dev/null
fi

qbit_payload="$(
  python3 - <<'PY'
import json
import os
from pathlib import Path

report_file = os.environ["REPORT_FILE"]
username = os.environ["USERNAME"]

try:
    report = Path(report_file).read_text(encoding="utf-8")
except FileNotFoundError:
    report = ""

if "## qBittorrent Health" not in report:
    raise SystemExit(0)

body = """**qBittorrent RSS/Search Recovery**
The organizer detected a qBittorrent process-state issue. Run these blocks one at a time:

```bash
sudo systemctl stop qbittorrent-nox
sudo pkill -TERM -x qbittorrent-nox
sleep 5
ps -ef | grep -i '[q]bittorrent'
```

If any `qbittorrent-nox` process remains:

```bash
sudo pkill -KILL -x qbittorrent-nox
sleep 2
ps -ef | grep -i '[q]bittorrent'
```

Then start one clean service instance:

```bash
sudo chown -R qbittorrent:qbittorrent /var/lib/qbittorrent/.config /var/lib/qbittorrent/.local
sudo systemctl daemon-reload
sudo systemctl start qbittorrent-nox
```

Verify and test RSS:

```bash
systemctl status qbittorrent-nox --no-pager -l
ps -ef | grep -i '[q]bittorrent'
sudo ss -tlnp | grep -E ':(8080|8090|28157|30341)\\b'
sudo -u qbittorrent curl -4 -I -L --max-time 10 'https://subsplease.org/rss/?t&r=1080'
sudo -u qbittorrent curl -4 --interface wgpia0 -I -L --max-time 10 'https://subsplease.org/rss/?t&r=1080'
```"""

print(json.dumps({"username": username, "content": body}))
PY
)"

if [ -n "$qbit_payload" ]; then
  "$curl_bin" --fail --silent --show-error \
    -H 'Content-Type: application/json' \
    -d "$qbit_payload" \
    "$WEBHOOK_URL" >/dev/null
fi

printf '%s discord notify sent\n' "$(date -Is)" >> "$LOG_FILE"
