#!/usr/bin/env bash
set -euo pipefail

MARKER="plex-media-organizer-once-20260425-1600"
RUNNER="/home/komichris/.codex/skills/plex-media-organizer/scripts/run_daily.sh"

status=0
"$RUNNER" || status=$?

tmp="$(mktemp)"
if crontab -l 2>/dev/null | grep -v "$MARKER" > "$tmp"; then
  crontab "$tmp"
fi
rm -f "$tmp"

exit "$status"
