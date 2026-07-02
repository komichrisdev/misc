#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import tempfile
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

VIDEO_EXTS = {
    ".avi",
    ".m4v",
    ".mkv",
    ".mov",
    ".mp4",
    ".mpeg",
    ".mpg",
    ".wmv",
}

EPISODE_RE = re.compile(r"(?i)\bS(?P<season>\d{1,2})E(?P<episode>\d{1,3})\b")
SECTION_HEADINGS = {"## Review Log", "## Missing Anime Episodes"}


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def absolute(path: Path) -> str:
    return str(path if path.is_absolute() else path.resolve(strict=False))


def load_json(path: Path, default: dict[str, Any] | None = None) -> dict[str, Any]:
    if not path.exists():
        return dict(default or {})
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return data


def base_log() -> dict[str, Any]:
    return {
        "version": 1,
        "updated_at": None,
        "directories": {},
        "missing_episode_gaps": [],
    }


def load_log(path: Path) -> dict[str, Any]:
    log = load_json(path, base_log())
    log.setdefault("version", 1)
    log.setdefault("directories", {})
    log.setdefault("missing_episode_gaps", [])
    return log


def atomic_write_json(path: Path, data: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False) as handle:
        json.dump(data, handle, indent=2, sort_keys=True)
        handle.write("\n")
        tmp_name = handle.name
    os.replace(tmp_name, path)


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def update_hash(hasher: Any, *parts: object) -> None:
    line = "\0".join(str(part) for part in parts)
    hasher.update(line.encode("utf-8", "surrogateescape"))
    hasher.update(b"\n")


def stat_signature(path: Path, rel: str, kind: str, hasher: Any) -> dict[str, int]:
    stats = {"file_count": 0, "dir_count": 0, "total_size": 0, "max_mtime_ns": 0}
    stat = path.lstat()
    stats["max_mtime_ns"] = stat.st_mtime_ns

    if kind == "dir":
        stats["dir_count"] = 1
        update_hash(hasher, kind, rel, stat.st_mode, stat.st_size, stat.st_mtime_ns)
    elif kind == "link":
        stats["file_count"] = 1
        target = os.readlink(path)
        update_hash(hasher, kind, rel, stat.st_mode, stat.st_size, stat.st_mtime_ns, target)
    else:
        stats["file_count"] = 1
        stats["total_size"] = stat.st_size
        update_hash(hasher, kind, rel, stat.st_mode, stat.st_size, stat.st_mtime_ns)

    return stats


def merge_stats(total: dict[str, int], item: dict[str, int]) -> None:
    total["file_count"] += item["file_count"]
    total["dir_count"] += item["dir_count"]
    total["total_size"] += item["total_size"]
    total["max_mtime_ns"] = max(total["max_mtime_ns"], item["max_mtime_ns"])


def fingerprint_entry(path: Path) -> dict[str, Any]:
    hasher = hashlib.sha256()
    totals = {"file_count": 0, "dir_count": 0, "total_size": 0, "max_mtime_ns": 0}
    errors: list[str] = []

    try:
        if path.is_symlink():
            merge_stats(totals, stat_signature(path, ".", "link", hasher))
        elif path.is_file():
            merge_stats(totals, stat_signature(path, ".", "file", hasher))
        elif path.is_dir():
            for dirpath, dirnames, filenames in os.walk(path, followlinks=False):
                dirnames.sort()
                filenames.sort()
                current = Path(dirpath)
                rel_dir = "." if current == path else current.relative_to(path).as_posix()
                try:
                    merge_stats(totals, stat_signature(current, rel_dir, "dir", hasher))
                except OSError as exc:
                    errors.append(f"{current}: {exc}")

                kept_dirnames = []
                for dirname in dirnames:
                    child = current / dirname
                    rel = child.relative_to(path).as_posix()
                    if child.is_symlink():
                        try:
                            merge_stats(totals, stat_signature(child, rel, "link", hasher))
                        except OSError as exc:
                            errors.append(f"{child}: {exc}")
                    else:
                        kept_dirnames.append(dirname)
                dirnames[:] = kept_dirnames

                for filename in filenames:
                    child = current / filename
                    rel = child.relative_to(path).as_posix()
                    try:
                        kind = "link" if child.is_symlink() else "file"
                        merge_stats(totals, stat_signature(child, rel, kind, hasher))
                    except OSError as exc:
                        errors.append(f"{child}: {exc}")
        else:
            update_hash(hasher, "missing", ".")
    except OSError as exc:
        errors.append(f"{path}: {exc}")

    return {
        "signature": f"sha256:{hasher.hexdigest()}",
        "file_count": totals["file_count"],
        "dir_count": totals["dir_count"],
        "total_size": totals["total_size"],
        "max_mtime_ns": totals["max_mtime_ns"],
        "scan_errors": errors,
    }


def iter_files(path: Path) -> list[Path]:
    if path.is_file() or path.is_symlink():
        return [path]

    files: list[Path] = []
    for dirpath, dirnames, filenames in os.walk(path, followlinks=False):
        dirnames.sort()
        filenames.sort()
        dirnames[:] = [name for name in dirnames if not (Path(dirpath) / name).is_symlink()]
        files.extend(Path(dirpath) / name for name in filenames)
    return files


def missing_episodes(entry_path: Path, series: str) -> list[dict[str, Any]]:
    episodes: dict[int, set[int]] = defaultdict(set)
    for path in iter_files(entry_path):
        if path.suffix.lower() not in VIDEO_EXTS:
            continue
        match = EPISODE_RE.search(path.name)
        if not match:
            continue
        season = int(match.group("season"))
        if season == 0:
            continue
        episodes[season].add(int(match.group("episode")))

    gaps: list[dict[str, Any]] = []
    for season, found in sorted(episodes.items()):
        if len(found) < 2:
            continue
        for episode in range(min(found), max(found) + 1):
            if episode not in found:
                gaps.append(
                    {
                        "series": series,
                        "season": season,
                        "episode": episode,
                        "display": f"{series} season {season:02d}: missing episode {episode:02d}",
                    }
                )
    return gaps


def scan_roots(roots: list[Path]) -> list[dict[str, Any]]:
    entries: list[dict[str, Any]] = []
    for root in roots:
        if not root.exists():
            continue
        library = root.name
        for child in sorted(root.iterdir(), key=lambda item: item.name.casefold()):
            if not (child.is_dir() or child.is_file() or child.is_symlink()):
                continue
            fingerprint = fingerprint_entry(child)
            entry = {
                "path": absolute(child),
                "root": absolute(root),
                "library": library,
                "name": child.name,
                "relative_path": child.relative_to(root).as_posix(),
                "kind": "directory" if child.is_dir() and not child.is_symlink() else "file",
                **fingerprint,
            }
            entry["missing_episode_gaps"] = (
                missing_episodes(child, child.name) if library == "anime" else []
            )
            entries.append(entry)
    return entries


def public_entry(entry: dict[str, Any], reason: str | None = None) -> dict[str, Any]:
    result = {
        "path": entry["path"],
        "library": entry["library"],
        "kind": entry["kind"],
        "name": entry["name"],
        "signature": entry["signature"],
        "file_count": entry["file_count"],
        "dir_count": entry["dir_count"],
        "total_size": entry["total_size"],
    }
    if reason:
        result["reason"] = reason
    if entry.get("scan_errors"):
        result["scan_errors"] = entry["scan_errors"]
    return result


def make_plan(log: dict[str, Any], entries: list[dict[str, Any]], log_path: Path) -> dict[str, Any]:
    directories = log.get("directories", {})
    current_paths = {entry["path"] for entry in entries}
    to_review: list[dict[str, Any]] = []
    skipped: list[dict[str, Any]] = []
    removed: list[dict[str, Any]] = []

    for entry in entries:
        old = directories.get(entry["path"])
        if old and old.get("last_status") == "pending_review":
            to_review.append(public_entry(entry, "pending review from previous failed run"))
            continue

        if old and old.get("signature") == entry["signature"] and old.get("last_reviewed_at"):
            skipped.append(public_entry(entry, "unchanged since last reviewed fix pass"))
            continue

        if not old:
            reason = "new media entry"
        elif not old.get("present", True):
            reason = "media entry reappeared"
        elif not old.get("last_reviewed_at"):
            reason = "not yet reviewed"
        else:
            reason = "changed since last reviewed fix pass"
        to_review.append(public_entry(entry, reason))

    for path, entry in sorted(directories.items()):
        if path not in current_paths and entry.get("present", True):
            removed.append(
                {
                    "path": path,
                    "library": entry.get("library"),
                    "kind": entry.get("kind", "directory"),
                    "name": entry.get("name"),
                    "last_reviewed_at": entry.get("last_reviewed_at"),
                }
            )

    return {
        "generated_at": utc_now(),
        "review_log": absolute(log_path),
        "review_needed": bool(to_review),
        "to_review": to_review,
        "skipped_unchanged": skipped,
        "removed_since_last_log": removed,
        "counts": {
            "to_review": len(to_review),
            "skipped_unchanged": len(skipped),
            "removed_since_last_log": len(removed),
            "tracked_in_log": len(directories),
        },
        "missing_episode_gaps_from_log": log.get("missing_episode_gaps", []),
    }


def format_count_by_library(entries: list[dict[str, Any]]) -> str:
    counts: dict[str, int] = defaultdict(int)
    for entry in entries:
        counts[entry.get("library", "unknown")] += 1
    if not counts:
        return "none"
    return ", ".join(f"{library}: {count}" for library, count in sorted(counts.items()))


def plan_markdown(plan: dict[str, Any]) -> str:
    lines = [
        "# Plex Media Organizer Review Plan",
        "",
        f"Generated: {plan['generated_at']}",
        f"Review log: `{plan['review_log']}`",
        "",
        "## Summary",
        f"- Directories to review: {plan['counts']['to_review']} ({format_count_by_library(plan['to_review'])})",
        f"- Skipped unchanged directories: {plan['counts']['skipped_unchanged']} ({format_count_by_library(plan['skipped_unchanged'])})",
        f"- Removed since last log: {plan['counts']['removed_since_last_log']}",
        "",
        "## Directories To Review",
    ]

    if plan["to_review"]:
        for entry in plan["to_review"]:
            lines.append(
                f"- `{entry['path']}` ({entry['library']}; {entry['reason']}; {entry['file_count']} files)"
            )
    else:
        lines.append("- None.")

    lines.extend(["", "## Skipped Unchanged Directories"])
    if plan["skipped_unchanged"]:
        lines.append(
            "- These entries already have matching fingerprints in the review log and should not be inventoried again."
        )
        for entry in plan["skipped_unchanged"]:
            lines.append(f"- `{entry['path']}` ({entry['library']}; {entry['file_count']} files)")
    else:
        lines.append("- None.")

    lines.extend(["", "## Removed Since Last Log"])
    if plan["removed_since_last_log"]:
        for entry in plan["removed_since_last_log"]:
            lines.append(f"- `{entry['path']}` ({entry.get('library') or 'unknown'})")
    else:
        lines.append("- None.")

    return "\n".join(lines).rstrip() + "\n"


def aggregate_missing(directories: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    missing: list[dict[str, Any]] = []
    for path, entry in sorted(directories.items()):
        if not entry.get("present", True):
            continue
        if entry.get("library") != "anime":
            continue
        for gap in entry.get("missing_episode_gaps", []):
            item = dict(gap)
            item["path"] = path
            missing.append(item)
    return missing


def missing_markdown(missing: list[dict[str, Any]]) -> str:
    lines = ["## Missing Anime Episodes", ""]
    if missing:
        lines.extend(f"- {gap['display']}" for gap in missing)
    else:
        lines.append("- None.")
    return "\n".join(lines).rstrip() + "\n"


def review_summary_markdown(log_path: Path, plan: dict[str, Any] | None, log: dict[str, Any]) -> str:
    last_run = log.get("last_run", {})
    missing_count = len(log.get("missing_episode_gaps", []))
    skipped = plan["counts"]["skipped_unchanged"] if plan else last_run.get("skipped_unchanged", 0)
    reviewed = last_run.get("reviewed", 0)
    tracked = sum(1 for entry in log.get("directories", {}).values() if entry.get("present", True))

    lines = [
        "## Review Log",
        "",
        f"- Log file: `{absolute(log_path)}`",
        f"- Reviewed changed/new directories this run: {reviewed}",
        f"- Skipped unchanged directories this run: {skipped}",
        f"- Present directories tracked: {tracked}",
        f"- Missing anime episode gaps tracked in log: {missing_count}",
    ]
    return "\n".join(lines).rstrip() + "\n"


def strip_sections(text: str) -> str:
    lines = text.splitlines()
    output: list[str] = []
    i = 0
    while i < len(lines):
        if lines[i] in SECTION_HEADINGS:
            i += 1
            while i < len(lines) and not lines[i].startswith("## "):
                i += 1
            continue
        output.append(lines[i])
        i += 1
    return "\n".join(output).rstrip()


def inject_report_sections(report_path: Path, sections: list[str]) -> None:
    existing = report_path.read_text(encoding="utf-8") if report_path.exists() else ""
    base = strip_sections(existing)
    output = base
    for section in sections:
        section = section.strip()
        if not section:
            continue
        if output:
            output += "\n\n"
        output += section
    write_text(report_path, output.rstrip() + "\n")


def update_log(
    log_path: Path,
    roots: list[Path],
    status: int,
    plan: dict[str, Any] | None,
) -> dict[str, Any]:
    now = utc_now()
    log = load_log(log_path)
    old_directories = log.get("directories", {})
    current_entries = scan_roots(roots)
    current_paths = {entry["path"] for entry in current_entries}
    planned_review_paths = {entry["path"] for entry in plan.get("to_review", [])} if plan else set()
    review_succeeded = status == 0
    reviewed_count = 0
    updated_directories: dict[str, dict[str, Any]] = {}

    for entry in current_entries:
        old = old_directories.get(entry["path"], {})
        old_signature = old.get("signature")
        new_entry = dict(old)
        new_entry.update(entry)
        new_entry["present"] = True
        new_entry.pop("removed_at", None)
        new_entry["last_seen_at"] = now
        new_entry["last_missing_episode_check_at"] = now if entry["library"] == "anime" else old.get(
            "last_missing_episode_check_at"
        )

        if not old_signature or old_signature != entry["signature"]:
            new_entry["last_changed_at"] = now

        should_mark_reviewed = review_succeeded and (
            plan is None or entry["path"] in planned_review_paths or not old.get("last_reviewed_at")
        )
        if should_mark_reviewed:
            if not new_entry.get("first_reviewed_at"):
                new_entry["first_reviewed_at"] = now
            new_entry["last_reviewed_at"] = now
            new_entry["review_count"] = int(old.get("review_count", 0)) + 1
            new_entry["last_status"] = "reviewed"
            reviewed_count += 1
        elif old.get("last_status") == "pending_review":
            new_entry["last_status"] = "pending_review"
        elif old_signature == entry["signature"] and old.get("last_reviewed_at"):
            new_entry["last_confirmed_unchanged_at"] = now
            new_entry["last_status"] = "unchanged"
        elif not review_succeeded:
            new_entry["last_status"] = "pending_review"

        updated_directories[entry["path"]] = new_entry

    for path, old in old_directories.items():
        if path in current_paths:
            continue
        removed_entry = dict(old)
        removed_entry["present"] = False
        if not removed_entry.get("removed_at"):
            removed_entry["removed_at"] = now
        removed_entry["last_seen_missing_at"] = now
        updated_directories[path] = removed_entry

    log["directories"] = updated_directories
    log["missing_episode_gaps"] = aggregate_missing(updated_directories)
    log["updated_at"] = now
    log["last_missing_episode_check_at"] = now
    log["last_run"] = {
        "finished_at": now,
        "status": status,
        "reviewed": reviewed_count,
        "skipped_unchanged": plan["counts"]["skipped_unchanged"] if plan else 0,
        "to_review": plan["counts"]["to_review"] if plan else len(current_entries),
        "tracked_present": len(current_entries),
        "missing_episode_gaps": len(log["missing_episode_gaps"]),
    }
    atomic_write_json(log_path, log)
    return log


def cmd_prepare(args: argparse.Namespace) -> int:
    roots = [Path(root) for root in args.roots]
    log_path = Path(args.log)
    log = load_log(log_path)
    plan = make_plan(log, scan_roots(roots), log_path)
    atomic_write_json(Path(args.plan_json), plan)
    write_text(Path(args.plan_md), plan_markdown(plan))
    return 0


def cmd_needs_review(args: argparse.Namespace) -> int:
    plan = load_json(Path(args.plan_json))
    return 0 if plan.get("review_needed") else 1


def cmd_update(args: argparse.Namespace) -> int:
    plan = load_json(Path(args.plan_json)) if args.plan_json and Path(args.plan_json).exists() else None
    log_path = Path(args.log)
    log = update_log(log_path, [Path(root) for root in args.roots], args.status, plan)
    missing_md = missing_markdown(log.get("missing_episode_gaps", []))
    summary_md = review_summary_markdown(log_path, plan, log)

    if args.missing_md:
        write_text(Path(args.missing_md), missing_md)
    if args.summary_md:
        write_text(Path(args.summary_md), summary_md)
    if args.report:
        inject_report_sections(Path(args.report), [summary_md, missing_md])
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Maintain Plex organizer review state.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    prepare = subparsers.add_parser("prepare", help="Build a review plan from current media state.")
    prepare.add_argument("--log", required=True)
    prepare.add_argument("--plan-json", required=True)
    prepare.add_argument("--plan-md", required=True)
    prepare.add_argument("roots", nargs="+")
    prepare.set_defaults(func=cmd_prepare)

    needs_review = subparsers.add_parser("needs-review", help="Exit 0 when the plan has work.")
    needs_review.add_argument("--plan-json", required=True)
    needs_review.set_defaults(func=cmd_needs_review)

    update = subparsers.add_parser("update", help="Update the review log after a run.")
    update.add_argument("--log", required=True)
    update.add_argument("--plan-json")
    update.add_argument("--status", required=True, type=int)
    update.add_argument("--missing-md")
    update.add_argument("--summary-md")
    update.add_argument("--report")
    update.add_argument("roots", nargs="+")
    update.set_defaults(func=cmd_update)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
