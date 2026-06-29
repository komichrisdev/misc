#!/usr/bin/env python3
import argparse
import datetime as dt
import json
import re
import shutil
import subprocess
from pathlib import Path


def now_iso():
    return dt.datetime.now(dt.timezone.utc).astimezone().isoformat(timespec="seconds")


def parse_int_from_scan(text, label):
    match = re.search(rf"{re.escape(label)}:\s*([0-9]+)", text)
    if not match:
        return -1
    return int(match.group(1))


def tail(text, limit=4000):
    if len(text) <= limit:
        return text
    return text[-limit:]


def command(name):
    return shutil.which(name)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--output-log", required=True)
    parser.add_argument("--scan-path", action="append", default=[])
    parser.add_argument("--scan-timeout", type=int, default=1800)
    args = parser.parse_args()

    started_at = now_iso()
    scan_paths = args.scan_path or ["/home/komichris", "/srv/media", "/tmp"]
    existing_paths = [p for p in scan_paths if Path(p).exists()]
    missing_paths = [p for p in scan_paths if not Path(p).exists()]
    output_json = Path(args.output_json)
    output_log = Path(args.output_log)
    output_json.parent.mkdir(parents=True, exist_ok=True)
    output_log.parent.mkdir(parents=True, exist_ok=True)

    clamscan = command("clamscan")
    if not clamscan:
        result = {
            "started_at": started_at,
            "finished_at": now_iso(),
            "paths": scan_paths,
            "missing_paths": missing_paths,
            "returncode": 127,
            "timeout": False,
            "duration_seconds": 0,
            "infected_files": -1,
            "scanned_files": -1,
            "total_errors": -1,
            "output_file": str(output_log),
            "output_tail": "clamscan is not installed or not in PATH",
        }
        output_log.write_text(result["output_tail"] + "\n", encoding="utf-8")
        output_json.write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(json.dumps({"returncode": 127, "timeout": False}, indent=2))
        return 0

    if not existing_paths:
        result = {
            "started_at": started_at,
            "finished_at": now_iso(),
            "paths": scan_paths,
            "missing_paths": missing_paths,
            "returncode": 2,
            "timeout": False,
            "duration_seconds": 0,
            "infected_files": -1,
            "scanned_files": -1,
            "total_errors": -1,
            "output_file": str(output_log),
            "output_tail": "No configured scan paths exist.",
        }
        output_log.write_text(result["output_tail"] + "\n", encoding="utf-8")
        output_json.write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(json.dumps({"returncode": 2, "timeout": False}, indent=2))
        return 0

    scan_cmd = [
        clamscan,
        "--infected",
        "--recursive=yes",
        "--suppress-ok-results",
        "--cross-fs=no",
        "--max-filesize=100M",
        "--max-scansize=400M",
        *existing_paths,
    ]
    if command("ionice"):
        scan_cmd = ["ionice", "-c3", *scan_cmd]
    if command("nice"):
        scan_cmd = ["nice", "-n", "15", *scan_cmd]

    start_time = dt.datetime.now(dt.timezone.utc)
    timeout = False
    try:
        completed = subprocess.run(
            scan_cmd,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=args.scan_timeout,
            check=False,
        )
        returncode = completed.returncode
        stdout = completed.stdout or ""
        stderr = completed.stderr or ""
    except subprocess.TimeoutExpired as exc:
        timeout = True
        returncode = 124
        stdout = exc.stdout if isinstance(exc.stdout, str) else ""
        stderr = f"Timed out after {args.scan_timeout}s"

    finished_at = now_iso()
    duration = int((dt.datetime.now(dt.timezone.utc) - start_time).total_seconds())
    output_text = "\n".join(part.strip() for part in [stdout, stderr] if part and part.strip()).strip()
    output_log.write_text(output_text + ("\n" if output_text else ""), encoding="utf-8")
    result = {
        "started_at": started_at,
        "finished_at": finished_at,
        "paths": existing_paths,
        "missing_paths": missing_paths,
        "returncode": returncode,
        "timeout": timeout,
        "duration_seconds": duration,
        "infected_files": parse_int_from_scan(output_text, "Infected files"),
        "scanned_files": parse_int_from_scan(output_text, "Scanned files"),
        "total_errors": parse_int_from_scan(output_text, "Total errors"),
        "output_file": str(output_log),
        "output_tail": tail(output_text),
    }
    output_json.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"returncode": returncode, "timeout": timeout, "duration_seconds": duration}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
