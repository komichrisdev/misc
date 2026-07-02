#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import ssl
import json
import os
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

DEFAULT_RULES_PATH = Path("/var/lib/qbittorrent/.config/qBittorrent/rss/download_rules.json")
DEFAULT_WEBUI_URL = "https://127.0.0.1:8090"


def load_json(path: Path, default: dict[str, Any] | None = None) -> dict[str, Any]:
    if not path.exists():
        return dict(default or {})
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return data


def write_json_atomic(path: Path, data: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False) as handle:
        json.dump(data, handle, indent=4, ensure_ascii=True)
        handle.write("\n")
        temp_name = handle.name
    os.replace(temp_name, path)


def load_operations(path: Path) -> list[dict[str, Any]]:
    payload = load_json(path, {"version": 1, "rss_rule_updates": []})
    updates = payload.get("rss_rule_updates", [])
    if not isinstance(updates, list):
        raise ValueError("rss_rule_updates must be a JSON array")
    normalized: list[dict[str, Any]] = []
    for index, item in enumerate(updates, start=1):
        if not isinstance(item, dict):
            raise ValueError(f"rss_rule_updates[{index}] must be an object")
        normalized.append(item)
    return normalized


def ensure_rule_name(update: dict[str, Any]) -> str:
    rule_name = update.get("rule_name")
    if not isinstance(rule_name, str) or not rule_name.strip():
        raise ValueError("Each rss_rule_update must include a non-empty string rule_name")
    return rule_name


def ensure_save_path(update: dict[str, Any]) -> Path:
    save_path = update.get("save_path")
    if not isinstance(save_path, str) or not save_path.strip():
        raise ValueError(f"Rule {update.get('rule_name')!r} must include a non-empty save_path")
    return Path(save_path)


def set_if_present(rule: dict[str, Any], field: str, value: Any) -> None:
    if value is not None:
        rule[field] = value


def apply_update(
    rules: dict[str, Any],
    original_rules: dict[str, Any],
    update: dict[str, Any],
    applied: list[str],
    held: list[str],
    update_torrent_params: bool,
) -> bool:
    rule_name = ensure_rule_name(update)
    save_path = ensure_save_path(update)

    if not save_path.exists() or not save_path.is_dir():
        held.append(f"`{rule_name}`: held because save path `{save_path}` does not exist as a directory.")
        return False

    source_rule_name = update.get("source_rule")
    existing = rules.get(rule_name)

    if existing is None:
        if not source_rule_name:
            held.append(f"`{rule_name}`: held because the rule does not exist and no source_rule was provided.")
            return False
        source_rule = original_rules.get(source_rule_name)
        if not isinstance(source_rule, dict):
            held.append(f"`{rule_name}`: held because source rule `{source_rule_name}` was not found.")
            return False
        existing = copy.deepcopy(source_rule)
        existing["lastMatch"] = ""
        existing["previouslyMatchedEpisodes"] = []
        rules[rule_name] = existing
        action = "created"
    else:
        action = "updated"

    if not isinstance(existing, dict):
        held.append(f"`{rule_name}`: held because the existing rule payload is not an object.")
        return False

    old_save_path = existing.get("savePath", "")
    existing["savePath"] = str(save_path)

    torrent_params: dict[str, Any] | None = None
    if update_torrent_params:
        torrent_params = existing.setdefault("torrentParams", {})
        if not isinstance(torrent_params, dict):
            held.append(f"`{rule_name}`: held because torrentParams is not an object.")
            return False
        torrent_params["save_path"] = str(save_path)

    must_contain = update.get("must_contain")
    must_not_contain = update.get("must_not_contain")
    assigned_category = update.get("assigned_category")
    use_auto_tmm = update.get("use_auto_tmm")
    reset_history = bool(update.get("reset_history", False))

    set_if_present(existing, "mustContain", must_contain)
    set_if_present(existing, "mustNotContain", must_not_contain)
    set_if_present(existing, "assignedCategory", assigned_category)
    if assigned_category is not None and torrent_params is not None:
        torrent_params["category"] = assigned_category
    if use_auto_tmm is not None and torrent_params is not None:
        torrent_params["use_auto_tmm"] = bool(use_auto_tmm)
    if reset_history:
        existing["lastMatch"] = ""
        existing["previouslyMatchedEpisodes"] = []

    detail = f"`{rule_name}`: {action} save path `{old_save_path}` -> `{save_path}`."
    if source_rule_name and action == "created":
        detail = f"`{rule_name}`: created from `{source_rule_name}` with save path `{save_path}`."
    applied.append(detail)
    return True


def report_markdown(applied: list[str], held: list[str], target_label: str) -> str:
    if not applied and not held:
        return ""

    lines = ["## RSS Rule Sync", "", f"- Target: `{target_label}`"]
    if applied:
        lines.extend([""] + [f"- {item}" for item in applied])
    if held:
        lines.extend([""] + [f"- {item}" for item in held])
    return "\n".join(lines).rstrip() + "\n"


def write_report(path: Path | None, applied: list[str], held: list[str], target_label: str) -> None:
    if not path:
        return
    path.write_text(report_markdown(applied, held, target_label), encoding="utf-8", newline="\n")


def normalize_api_base(url: str) -> str:
    return url.rstrip("/")


def build_webui_opener(verify_tls: bool) -> urllib.request.OpenerDirector:
    handlers: list[Any] = [urllib.request.HTTPCookieProcessor()]
    if not verify_tls:
        context = ssl.create_default_context()
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        handlers.append(urllib.request.HTTPSHandler(context=context))
    return urllib.request.build_opener(*handlers)


def webui_request(
    opener: urllib.request.OpenerDirector,
    method: str,
    url: str,
    referer: str,
    form: dict[str, Any] | None = None,
) -> bytes:
    body = None
    headers = {"Referer": referer, "Origin": referer}
    if form is not None:
        body = urllib.parse.urlencode(form).encode("utf-8")
    request = urllib.request.Request(url, data=body, headers=headers, method=method)
    with opener.open(request) as response:
        return response.read()


def webui_login(
    opener: urllib.request.OpenerDirector,
    api_base: str,
    username: str,
    password: str,
) -> None:
    payload = {"username": username, "password": password}
    webui_request(opener, "POST", f"{api_base}/api/v2/auth/login", api_base, payload)


def load_rules_via_webui(
    opener: urllib.request.OpenerDirector,
    api_base: str,
) -> dict[str, Any]:
    payload = webui_request(opener, "GET", f"{api_base}/api/v2/rss/rules", api_base)
    data = json.loads(payload.decode("utf-8"))
    if not isinstance(data, dict):
        raise ValueError("qBittorrent WebUI rss/rules response must be a JSON object")
    return data


def write_rules_via_webui(
    opener: urllib.request.OpenerDirector,
    api_base: str,
    rules: dict[str, Any],
) -> None:
    for rule_name, rule_def in rules.items():
        if not isinstance(rule_def, dict):
            raise ValueError(f"WebUI rule {rule_name!r} is not an object")
        payload = {"ruleName": rule_name, "ruleDef": json.dumps(rule_def, separators=(",", ":"))}
        webui_request(opener, "POST", f"{api_base}/api/v2/rss/setRule", api_base, payload)


def main() -> int:
    parser = argparse.ArgumentParser(description="Apply Plex organizer-driven qBittorrent RSS rule updates.")
    parser.add_argument("--operations", required=True, help="JSON file with rss_rule_updates emitted by the organizer run.")
    parser.add_argument("--rules", default=str(DEFAULT_RULES_PATH), help="Path to qBittorrent rss/download_rules.json")
    parser.add_argument("--report", help="Optional markdown report file to append RSS sync results to.")
    parser.add_argument("--webui-url", default=os.environ.get("QBITTORRENT_WEBUI_URL", DEFAULT_WEBUI_URL), help="Optional qBittorrent WebUI base URL, e.g. https://127.0.0.1:8090")
    parser.add_argument("--webui-username", default=os.environ.get("QBITTORRENT_WEBUI_USERNAME"), help="Optional qBittorrent WebUI username")
    parser.add_argument("--webui-password", default=os.environ.get("QBITTORRENT_WEBUI_PASSWORD"), help="Optional qBittorrent WebUI password")
    parser.add_argument("--webui-verify-tls", action="store_true", default=os.environ.get("QBITTORRENT_WEBUI_VERIFY_TLS", "").lower() in {"1", "true", "yes"}, help="Verify the qBittorrent WebUI TLS certificate")
    args = parser.parse_args()

    operations_path = Path(args.operations)
    rules_path = Path(args.rules)
    applied: list[str] = []
    held: list[str] = []
    target_label = str(rules_path)

    updates = load_operations(operations_path)
    report_path = Path(args.report) if args.report else None

    if not updates:
        write_report(report_path, applied, held, target_label)
        return 0

    use_webui = bool(args.webui_username and args.webui_password)
    if use_webui:
        api_base = normalize_api_base(args.webui_url)
        target_label = f"{api_base}/api/v2/rss"
        try:
            opener = build_webui_opener(args.webui_verify_tls)
            webui_login(opener, api_base, args.webui_username, args.webui_password)
            rules = load_rules_via_webui(opener, api_base)
            if not rules:
                held.extend(
                    f"`{ensure_rule_name(update)}`: held because qBittorrent WebUI returned no RSS rules."
                    for update in updates
                )
                write_report(report_path, applied, held, target_label)
                return 0

            changed = False
            original_rules = copy.deepcopy(rules)
            for update in updates:
                changed = apply_update(rules, original_rules, update, applied, held, update_torrent_params=True) or changed
            if changed:
                write_rules_via_webui(opener, api_base, rules)
            write_report(report_path, applied, held, target_label)
            return 0
        except Exception as exc:
            held.append(f"WebUI sync failed for `{target_label}`: {exc}")
            write_report(report_path, [], held, target_label)
            return 1

    if not rules_path.exists():
        held.extend(
            f"`{ensure_rule_name(update)}`: held because qBittorrent rules file `{rules_path}` was not found."
            for update in updates
        )
        write_report(report_path, applied, held, target_label)
        return 0

    rules = load_json(rules_path)
    if not rules:
        held.extend(
            f"`{ensure_rule_name(update)}`: held because qBittorrent rules file `{rules_path}` is empty."
            for update in updates
        )
        write_report(report_path, applied, held, target_label)
        return 0

    changed = False
    original_rules = copy.deepcopy(rules)
    for update in updates:
        changed = apply_update(rules, original_rules, update, applied, held, update_torrent_params=True) or changed

    try:
        if changed:
            write_json_atomic(rules_path, rules)
    except OSError as exc:
        held.append(f"write failed for `{rules_path}`: {exc}")
        held.append(
            "No qBittorrent WebUI credentials were configured. Set "
            "`QBITTORRENT_WEBUI_URL`, `QBITTORRENT_WEBUI_USERNAME`, and "
            "`QBITTORRENT_WEBUI_PASSWORD` for API-backed sync, or grant the organizer user write access to the RSS rules directory."
        )
        write_report(report_path, [], held, target_label)
        return 1

    write_report(report_path, applied, held, target_label)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
