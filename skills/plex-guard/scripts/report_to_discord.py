#!/usr/bin/env python3
import json
import os
import sys
import urllib.error
import urllib.request
import uuid
from pathlib import Path


def truncate(text, limit=1850):
    if len(text) <= limit:
        return text
    head = int(limit * 0.65)
    tail = limit - head - 8
    return text[:head].rstrip() + "\n...\n" + text[-tail:].lstrip()


def image_for(overall, status):
    name = "attention.png" if overall == "attention" or status != "0" else "ok.png" if overall == "ok" else "warn.png"
    return Path(__file__).parent.parent / "assets" / name


def multipart(payload, image):
    boundary = uuid.uuid4().hex
    body = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"payload_json\"\r\n\r\n"
        f"{json.dumps(payload)}\r\n--{boundary}\r\n"
        f"Content-Disposition: form-data; name=\"files[0]\"; filename=\"{image.name}\"\r\n"
        "Content-Type: image/png\r\n\r\n"
    ).encode() + image.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
    return body, boundary


def self_test():
    assert image_for("attention", "0").name == "attention.png"
    assert image_for("ok", "0").name == "ok.png"
    assert image_for("warning", "0").name == "warn.png"
    assert image_for("ok", "1").name == "attention.png"
    body, boundary = multipart({"content": "test"}, image_for("ok", "0"))
    assert boundary.encode() in body and b'filename="ok.png"' in body and b'"content": "test"' in body
    print("Plex Guard Discord reporter self-test passed")


def main():
    if sys.argv[1:] == ["--self-test"]:
        self_test()
        return 0
    if len(sys.argv) != 4:
        print("usage: report_to_discord.py REPORT_FILE AUDIT_JSON STATUS", file=sys.stderr)
        return 2
    report_file = Path(sys.argv[1])
    audit_json = Path(sys.argv[2])
    status = sys.argv[3]
    webhook = os.environ.get("PLEX_GUARD_DISCORD_WEBHOOK_URL", "")
    username = os.environ.get("PLEX_GUARD_DISCORD_USERNAME", "Plex Guard")
    if not webhook:
        print("PLEX_GUARD_DISCORD_WEBHOOK_URL is not set", file=sys.stderr)
        return 0

    report = report_file.read_text(encoding="utf-8", errors="replace") if report_file.exists() else "No report file was written."
    audit = {}
    if audit_json.exists():
        try:
            audit = json.loads(audit_json.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            audit = {}
    overall = audit.get("summary", {}).get("overall", "unknown")
    title = "Plex Guard needs attention" if overall == "attention" or status != "0" else "Plex Guard daily clean report" if overall == "ok" else "Plex Guard daily warnings"
    body = f"**{title}**\nOverall: `{overall}`\n\n{truncate(report)}"
    image = image_for(overall, status)
    if not image.is_file():
        print(f"discord report failed: missing image {image}", file=sys.stderr)
        return 1
    payload = {"username": username, "content": body, "embeds": [{"image": {"url": f"attachment://{image.name}"}}]}
    data, boundary = multipart(payload, image)
    request = urllib.request.Request(
        webhook,
        data=data,
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "User-Agent": "PlexGuard/1.0 (+https://discord.com/api/webhooks)",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            response.read()
        print("discord report sent")
        return 0
    except urllib.error.HTTPError as exc:
        print(f"discord report failed: HTTP {exc.code} {exc.read().decode('utf-8', 'replace')}", file=sys.stderr)
        return 1
    except Exception as exc:
        print(f"discord report failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
