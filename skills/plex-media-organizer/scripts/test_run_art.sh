#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

printf '## Applied Changes\n\n- Test anime updated.\n' > "$tmp/report.md"

cat > "$tmp/codex" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$CODEX_ARGS_FILE"
printf 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=' | base64 -d > "$RUN_ART_FILE"
SH
chmod +x "$tmp/codex"

cat > "$tmp/curl" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$CAPTURE_FILE"
SH
chmod +x "$tmp/curl"

CODEX_ARGS_FILE="$tmp/codex.args" CODEX_BIN="$tmp/codex" "$SCRIPT_DIR/generate_run_art.sh" "$tmp/report.md" "$tmp/run-art.png"
grep -Fxq -- '--add-dir' "$tmp/codex.args"
CAPTURE_FILE="$tmp/curl.args" \
  CURL_BIN="$tmp/curl" \
  PLEX_ORGANIZER_DISCORD_WEBHOOK_URL="https://example.invalid/webhook" \
  "$SCRIPT_DIR/report_to_discord.sh" "$tmp/report.md" "$tmp/run.log" 0 "$tmp/run-art.png"

grep -Fq "files[0]=@$tmp/run-art.png" "$tmp/curl.args"
grep -Fq 'attachment://run-art.png' "$tmp/curl.args"
