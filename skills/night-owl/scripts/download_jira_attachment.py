#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import io
import json
import os
import re
import shlex
import shutil
import tempfile
import urllib.error
import urllib.request
from pathlib import Path
from typing import BinaryIO, Callable

CONFIG_FILE = Path.home() / ".config/night-owl/env"
KEY_RE = re.compile(r"^[A-Z][A-Z0-9_]*$")


def load_config(path: Path) -> dict[str, str]:
    config: dict[str, str] = {}
    if not path.exists():
        return config
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        key, separator, raw_value = stripped.partition("=")
        key = key.strip()
        values = shlex.split(raw_value, comments=True)
        if not separator or not KEY_RE.fullmatch(key) or len(values) != 1:
            raise ValueError(f"Invalid config line {number} in {path}")
        config[key] = values[0]
    return config


def safe_filename(filename: str) -> str:
    name = Path(filename.replace("\\", "/")).name
    if not name or name in {".", ".."}:
        raise ValueError("Jira returned an invalid attachment filename")
    return name


def request(url: str, email: str, token: str) -> urllib.request.Request:
    credentials = base64.b64encode(f"{email}:{token}".encode()).decode()
    return urllib.request.Request(
        url,
        headers={"Accept": "application/json", "Authorization": f"Basic {credentials}"},
    )


def download(
    attachment_id: str,
    output_dir: Path,
    site_url: str,
    email: str,
    token: str,
    open_url: Callable[[urllib.request.Request], BinaryIO] = urllib.request.urlopen,
) -> Path:
    if not attachment_id.isdigit():
        raise ValueError("attachment_id must be numeric")
    if not site_url.startswith("https://"):
        raise ValueError("ATLASSIAN_SITE_URL must use HTTPS")

    base_url = site_url.rstrip("/")
    metadata_request = request(f"{base_url}/rest/api/3/attachment/{attachment_id}", email, token)
    with open_url(metadata_request) as response:
        metadata = json.load(response)

    filename = safe_filename(metadata["filename"])
    output_dir.mkdir(parents=True, exist_ok=True)
    destination = output_dir / filename
    if destination.exists():
        raise FileExistsError(f"Refusing to overwrite {destination}")

    content_request = request(
        f"{base_url}/rest/api/3/attachment/content/{attachment_id}?redirect=false",
        email,
        token,
    )
    temporary_name: str | None = None
    try:
        with open_url(content_request) as response:
            with tempfile.NamedTemporaryFile(dir=output_dir, delete=False) as temporary:
                temporary_name = temporary.name
                shutil.copyfileobj(response, temporary)
        os.replace(temporary_name, destination)
    finally:
        if temporary_name and os.path.exists(temporary_name):
            os.unlink(temporary_name)
    return destination


def self_test() -> None:
    responses = iter(
        [
            io.BytesIO(json.dumps({"filename": "../trades.csv"}).encode()),
            io.BytesIO(b"date,amount\n2026-01-01,1\n"),
        ]
    )
    seen: list[urllib.request.Request] = []

    def fake_open(req: urllib.request.Request) -> BinaryIO:
        seen.append(req)
        return next(responses)

    with tempfile.TemporaryDirectory() as directory:
        result = download(
            "10000",
            Path(directory),
            "https://example.atlassian.net",
            "user@example.com",
            "token",
            fake_open,
        )
        assert result.name == "trades.csv"
        assert result.read_bytes().startswith(b"date,amount")
        assert seen[1].full_url.endswith("/attachment/content/10000?redirect=false")
        assert seen[0].get_header("Authorization", "").startswith("Basic ")
    print("Jira attachment downloader self-test passed")


def main() -> int:
    parser = argparse.ArgumentParser(description="Download one Jira attachment without exposing credentials.")
    parser.add_argument("attachment_id", nargs="?")
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--config", type=Path, default=CONFIG_FILE)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        self_test()
        return 0
    if not args.attachment_id or not args.output_dir:
        parser.error("attachment_id and --output-dir are required")

    config = load_config(args.config)
    values = {**config, **os.environ}
    required = ("ATLASSIAN_SITE_URL", "ATLASSIAN_EMAIL", "ATLASSIAN_API_TOKEN")
    missing = [key for key in required if not values.get(key)]
    if missing:
        raise ValueError(f"Missing Jira configuration: {', '.join(missing)}")

    destination = download(
        args.attachment_id,
        args.output_dir,
        values["ATLASSIAN_SITE_URL"],
        values["ATLASSIAN_EMAIL"],
        values["ATLASSIAN_API_TOKEN"],
    )
    print(destination)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (KeyError, OSError, ValueError, urllib.error.URLError) as error:
        raise SystemExit(f"Jira attachment download failed: {error}") from None
