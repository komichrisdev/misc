#!/usr/bin/env bash
set -euo pipefail

REPORT_FILE=""
SERVICE_NAME="${QBITTORRENT_SERVICE_NAME:-qbittorrent-nox}"
EXPECTED_USER="${QBITTORRENT_SERVICE_USER:-qbittorrent}"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --report)
      REPORT_FILE="${2:?--report requires a path}"
      shift 2
      ;;
    *)
      printf 'unknown argument: %s\n' "$1" >&2
      exit 2
      ;;
  esac
done

if [ -z "$REPORT_FILE" ]; then
  printf 'missing required --report path\n' >&2
  exit 2
fi

mkdir -p "$(dirname "$REPORT_FILE")"
: > "$REPORT_FILE"

service_state="unknown"
service_pid="0"
service_cmd=""

if command -v systemctl >/dev/null 2>&1; then
  service_state="$(systemctl is-active "$SERVICE_NAME" 2>/dev/null || true)"
  service_pid="$(systemctl show "$SERVICE_NAME" -p MainPID --value 2>/dev/null || printf '0')"
  service_cmd="$(systemctl show "$SERVICE_NAME" -p ExecStart --value 2>/dev/null || true)"
fi

processes="$(ps -C qbittorrent-nox -o user:32=,pid=,ppid=,args= 2>/dev/null || true)"
process_count=0
manual_count=0
service_pid_seen=0

while IFS= read -r line; do
  [ -n "$line" ] || continue
  process_count=$((process_count + 1))
  set -- $line
  process_user="${1:-}"
  process_pid="${2:-}"
  if [ "$process_user" != "$EXPECTED_USER" ]; then
    manual_count=$((manual_count + 1))
  fi
  if [ "$process_pid" = "$service_pid" ] && [ "$service_pid" != "0" ]; then
    service_pid_seen=1
  fi
done <<EOF
$processes
EOF

issues=()
if [ "$process_count" -gt 1 ]; then
  issues+=("multiple qbittorrent-nox processes are running")
fi
if [ "$manual_count" -gt 0 ]; then
  issues+=("at least one qbittorrent-nox process is not running as ${EXPECTED_USER}")
fi
if [ "$service_state" != "active" ]; then
  issues+=("${SERVICE_NAME}.service is ${service_state:-unknown}")
fi
if [ "$service_state" = "active" ] && [ "$process_count" -gt 0 ] && [ "$service_pid_seen" -ne 1 ]; then
  issues+=("${SERVICE_NAME}.service MainPID does not match a visible qbittorrent-nox process")
fi

if [ "${#issues[@]}" -eq 0 ]; then
  exit 0
fi

{
  printf '## qBittorrent Health\n\n'
  printf -- '- Issue: qBittorrent process state can break RSS updates and search resolution.\n'
  for issue in "${issues[@]}"; do
    printf -- '- Detected: %s.\n' "$issue"
  done
  printf -- '- Service state: `%s`; MainPID: `%s`.\n' "${service_state:-unknown}" "${service_pid:-0}"
  if [ -n "$service_cmd" ]; then
    printf -- '- Service ExecStart: `%s`.\n' "$service_cmd"
  fi
  printf -- '- Running qBittorrent processes:\n\n'
  printf '```text\n'
  if [ -n "$processes" ]; then
    printf '%s\n' "$processes"
  else
    printf 'none\n'
  fi
  printf '```\n\n'
  printf '### qBittorrent RSS/Search Recovery\n\n'
  printf 'Run these blocks one at a time:\n\n'
  printf '```bash\n'
  printf 'sudo systemctl stop qbittorrent-nox\n'
  printf 'sudo pkill -TERM -x qbittorrent-nox\n'
  printf 'sleep 5\n'
  printf "ps -ef | grep -i '[q]bittorrent'\n"
  printf '```\n\n'
  printf 'If any `qbittorrent-nox` process remains:\n\n'
  printf '```bash\n'
  printf 'sudo pkill -KILL -x qbittorrent-nox\n'
  printf 'sleep 2\n'
  printf "ps -ef | grep -i '[q]bittorrent'\n"
  printf '```\n\n'
  printf 'Then start one clean service instance:\n\n'
  printf '```bash\n'
  printf 'sudo chown -R qbittorrent:qbittorrent /var/lib/qbittorrent/.config /var/lib/qbittorrent/.local\n'
  printf 'sudo systemctl daemon-reload\n'
  printf 'sudo systemctl start qbittorrent-nox\n'
  printf '```\n\n'
  printf 'Verify there is exactly one systemd-managed instance:\n\n'
  printf '```bash\n'
  printf 'systemctl status qbittorrent-nox --no-pager -l\n'
  printf "ps -ef | grep -i '[q]bittorrent'\n"
  printf "sudo ss -tlnp | grep -E ':(8080|8090|28157|30341)\\\\b'\n"
  printf '```\n\n'
  printf 'Test RSS connectivity as the service user:\n\n'
  printf '```bash\n'
  printf "sudo -u qbittorrent curl -4 -I -L --max-time 10 'https://subsplease.org/rss/?t&r=1080'\n"
  printf "sudo -u qbittorrent curl -4 --interface wgpia0 -I -L --max-time 10 'https://subsplease.org/rss/?t&r=1080'\n"
  printf '```\n'
} > "$REPORT_FILE"

exit 0
