#!/usr/bin/env bash
set -euo pipefail

REPORT_FILE="${1:?report file required}"
RUN_ART_FILE="${2:?output image required}"
CODEX_BIN="${CODEX_BIN:-/usr/local/bin/codex}"
CODEX_MODEL="${CODEX_MODEL:-gpt-5.4-mini}"

mkdir -p "$(dirname "$RUN_ART_FILE")"
export RUN_ART_FILE

"$CODEX_BIN" \
  --search \
  --ask-for-approval never \
  exec \
  --model "$CODEX_MODEL" \
  --sandbox workspace-write \
  --add-dir "$(dirname "$RUN_ART_FILE")" \
  --skip-git-repo-check \
  --cd "$(dirname "$REPORT_FILE")" \
  "Use \$plex-media-organizer and \$imagegen. Generate the required Discord run art for the final report at ${REPORT_FILE}. Follow the skill's Discord Run Art workflow, including no-change and error runs. Use the built-in imagegen tool, inspect the result, then copy the selected PNG from \$CODEX_HOME/generated_images into the exact path ${RUN_ART_FILE}. Do not post to Discord. Do not finish until ${RUN_ART_FILE} exists."

test -s "$RUN_ART_FILE"
test "$(file -b --mime-type "$RUN_ART_FILE")" = "image/png"
