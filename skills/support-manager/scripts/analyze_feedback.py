#!/usr/bin/env python3
"""Analyze Facebook Instant Game feedback CSVs and maintain support history."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import statistics
from collections import Counter
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Any


DEFAULT_DOWNLOADS = Path.home() / "Downloads"
DEFAULT_HISTORY_ROOT = Path(
    r"C:\Users\chris\Qublix Games Dropbox\Chris K\CodexSkillBundles\Support"
)
DEFAULT_GITHUB_LOG_ROOT = Path(r"C:\Users\chris\Documents\Playground\misc\support-checks")
SCHEMA_VERSION = 1
WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]
TAG_RE = re.compile(r"\[([^\[\]]{1,80})\]")


def json_exit(payload: dict[str, Any], code: int = 0) -> None:
    print(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True))
    raise SystemExit(code)


def slugify(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug or "unknown-game"


def parse_timestamp(value: str | None) -> datetime | None:
    if not value:
        return None
    raw = value.strip()
    if not raw:
        return None
    try:
        numeric = float(raw)
        if numeric > 10_000_000_000:
            numeric /= 1000.0
        return datetime.fromtimestamp(numeric, tz=timezone.utc)
    except (ValueError, OSError, OverflowError):
        pass
    try:
        parsed = datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def read_csv_rows(
    path: Path,
    stop_at_or_before_timestamp: datetime | None = None,
    stop_at_or_before_date: date | None = None,
) -> tuple[list[dict[str, str]], dict[str, Any]]:
    encodings = ("utf-8-sig", "utf-8", "cp1252")
    last_error: Exception | None = None
    for encoding in encodings:
        try:
            rows: list[dict[str, str]] = []
            rows_seen = 0
            stopped_at: dict[str, Any] | None = None
            with path.open("r", encoding=encoding, newline="") as handle:
                for row in csv.DictReader(handle):
                    rows_seen += 1
                    dt = parse_timestamp(row.get("Timestamp"))
                    if (
                        stop_at_or_before_timestamp is not None
                        and dt is not None
                        and dt <= stop_at_or_before_timestamp
                    ):
                        stopped_at = {
                            "row_number": rows_seen,
                            "date": dt.date().isoformat(),
                            "timestamp_utc": dt.isoformat(timespec="seconds").replace("+00:00", "Z"),
                        }
                        break
                    if (
                        stop_at_or_before_timestamp is None
                        and stop_at_or_before_date is not None
                        and dt is not None
                        and dt.date() <= stop_at_or_before_date
                    ):
                        stopped_at = {
                            "row_number": rows_seen,
                            "date": dt.date().isoformat(),
                            "timestamp_utc": dt.isoformat(timespec="seconds").replace("+00:00", "Z"),
                        }
                        break
                    rows.append(row)
            return rows, {
                "encoding": encoding,
                "rows_seen": rows_seen,
                "rows_processed": len(rows),
                "last_assessed_at_before_run": stop_at_or_before_timestamp.isoformat(timespec="seconds").replace("+00:00", "Z")
                if stop_at_or_before_timestamp
                else None,
                "last_assessed_date_before_run": stop_at_or_before_date.isoformat()
                if stop_at_or_before_date
                else None,
                "stopped_at_last_assessed_day": stopped_at is not None,
                "stop_row": stopped_at,
            }
        except UnicodeDecodeError as exc:
            last_error = exc
    raise RuntimeError(f"Could not decode CSV: {last_error}")


def resolve_csv(path_text: str | None, downloads: Path) -> tuple[Path | None, dict[str, Any] | None, int]:
    if path_text:
        path = Path(path_text).expanduser()
        if not path.exists():
            return None, {"needs_input": "csv_path", "error": f"CSV not found: {path}"}, 2
        if path.suffix.lower() != ".csv":
            return None, {"needs_input": "csv_path", "error": f"File is not a CSV: {path}"}, 2
        return path, None, 0
    candidates = sorted(downloads.glob("*.csv"), key=lambda p: p.stat().st_mtime, reverse=True)
    if len(candidates) == 1:
        return candidates[0], None, 0
    if not candidates:
        return None, {"needs_input": "csv_path", "error": f"No CSV files found in {downloads}"}, 3
    return None, {
        "needs_input": "csv_path",
        "error": f"Multiple CSV files found in {downloads}",
        "candidates": [str(path) for path in candidates],
    }, 4


def extract_tags(text: str | None) -> list[str]:
    if not text:
        return ["untagged"]
    tags = []
    for match in TAG_RE.findall(text):
        tag = re.sub(r"\s+", "_", match.strip().lower())
        if tag:
            tags.append(tag)
    return tags or ["untagged"]


def counter_top(counter: Counter[str], limit: int = 10) -> dict[str, int]:
    return {key: value for key, value in counter.most_common(limit)}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def load_history(path: Path, game: str, slug: str) -> dict[str, Any]:
    if not path.exists():
        now = utc_now()
        return {
            "schema_version": SCHEMA_VERSION,
            "game": game,
            "slug": slug,
            "created_at_utc": now,
            "updated_at_utc": now,
            "runs": [],
            "notes": [],
        }
    with path.open("r", encoding="utf-8-sig") as handle:
        history = json.load(handle)
    history.setdefault("schema_version", SCHEMA_VERSION)
    history.setdefault("game", game)
    history.setdefault("slug", slug)
    history.setdefault("runs", [])
    history.setdefault("notes", [])
    return history


def parse_date_key(value: Any) -> date | None:
    if value is None:
        return None
    raw = str(value).strip()
    if not raw or raw == "unknown":
        return None
    try:
        return date.fromisoformat(raw[:10])
    except ValueError:
        dt = parse_timestamp(raw)
        return dt.date() if dt else None


def last_assessed_day(history: dict[str, Any]) -> date | None:
    candidates: list[date] = []
    for key in ("last_assessed_date", "last_assessed_day"):
        parsed = parse_date_key(history.get(key))
        if parsed:
            candidates.append(parsed)
    for run in history.get("runs", []):
        for key in ("last_assessed_date", "last_assessed_day"):
            parsed = parse_date_key(run.get(key))
            if parsed:
                candidates.append(parsed)
        end = parse_date_key((run.get("date_range") or {}).get("end_utc"))
        if end:
            candidates.append(end)
        for date_key in (run.get("daily_counts") or {}).keys():
            parsed = parse_date_key(date_key)
            if parsed:
                candidates.append(parsed)
    return max(candidates) if candidates else None


def last_assessed_timestamp(history: dict[str, Any]) -> datetime | None:
    candidates: list[datetime] = []
    for key in ("last_assessed_at_utc", "last_assessed_timestamp_utc", "analyzed_until_utc"):
        parsed = parse_timestamp(history.get(key))
        if parsed:
            candidates.append(parsed)
    for run in history.get("runs", []):
        for key in ("last_assessed_at_utc", "last_assessed_timestamp_utc", "analyzed_until_utc"):
            parsed = parse_timestamp(run.get(key))
            if parsed:
                candidates.append(parsed)
        end = parse_timestamp((run.get("date_range") or {}).get("end_utc"))
        if end:
            candidates.append(end)
    return max(candidates) if candidates else None


def run_keys(run: dict[str, Any]) -> list[str]:
    keys: list[str] = []
    for key in ("run_id", "dataset_hash"):
        value = run.get(key)
        if value:
            keys.append(f"{key}:{value}")
    if not keys:
        keys.append(":".join(
            str(run.get(key) or "")
            for key in ("csv_name", "last_assessed_date", "total_messages", "analyzed_at_utc")
        ))
    return keys


def merge_runs(*run_groups: list[dict[str, Any]]) -> list[dict[str, Any]]:
    merged: list[dict[str, Any]] = []
    seen: set[str] = set()
    for group in run_groups:
        for run in group:
            keys = run_keys(run)
            if any(key in seen for key in keys):
                continue
            seen.update(keys)
            merged.append(run)
    return merged


def load_github_log_runs(root: Path | None, slug: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    if root is None:
        return [], {"available": False, "reason": "No GitHub log root was configured."}
    game_dir = root / slug
    status: dict[str, Any] = {
        "available": game_dir.exists(),
        "root": str(root),
        "game_log_dir": str(game_dir),
        "runs_found": 0,
        "errors": [],
    }
    if not game_dir.exists():
        status["reason"] = "No GitHub log directory exists for this game."
        return [], status
    runs: list[dict[str, Any]] = []
    for path in sorted(game_dir.glob("*.json")):
        if path.name.lower() == "index.json":
            continue
        try:
            with path.open("r", encoding="utf-8-sig") as handle:
                payload = json.load(handle)
        except (OSError, json.JSONDecodeError) as exc:
            status["errors"].append({"path": str(path), "error": str(exc)})
            continue
        run = payload.get("run_record") if isinstance(payload, dict) else None
        if not isinstance(run, dict):
            run = payload if isinstance(payload, dict) and payload.get("total_messages") is not None else None
        if not isinstance(run, dict):
            continue
        run = dict(run)
        run["github_log_path"] = str(path)
        runs.append(run)
    status["runs_found"] = len(runs)
    latest = last_assessed_day({"runs": runs})
    latest_timestamp = last_assessed_timestamp({"runs": runs})
    status["latest_last_assessed_date"] = latest.isoformat() if latest else None
    status["latest_last_assessed_at_utc"] = latest_timestamp.isoformat(timespec="seconds").replace("+00:00", "Z") if latest_timestamp else None
    return runs, status


def atomic_write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2, sort_keys=True)
        handle.write("\n")
    os.replace(tmp, path)


def mean(values: list[float]) -> float:
    return statistics.fmean(values) if values else 0.0


def pstdev(values: list[float]) -> float:
    return statistics.pstdev(values) if len(values) > 1 else 0.0


def threshold(values: list[float], min_lift: float, min_abs_lift: float) -> float:
    avg = mean(values)
    return max(avg * min_lift, avg + (2 * pstdev(values)), avg + min_abs_lift)


def summarize_rows(rows: list[dict[str, str]]) -> dict[str, Any]:
    daily_counts: Counter[str] = Counter()
    weekday_counts: Counter[str] = Counter()
    message_types: Counter[str] = Counter()
    countries: Counter[str] = Counter()
    locales: Counter[str] = Counter()
    platforms: Counter[str] = Counter()
    app_versions: Counter[str] = Counter()
    hosted_asset_versions: Counter[str] = Counter()
    players: set[str] = set()
    parsed_datetimes: list[datetime] = []
    enriched: list[dict[str, Any]] = []
    for row in rows:
        dt = parse_timestamp(row.get("Timestamp"))
        date_key = "unknown"
        weekday = "unknown"
        if dt is not None:
            parsed_datetimes.append(dt)
            date_key = dt.date().isoformat()
            weekday = WEEKDAYS[dt.weekday()]
        daily_counts[date_key] += 1
        weekday_counts[weekday] += 1
        text = row.get("Feedback_Text") or ""
        tags = extract_tags(text)
        for tag in tags:
            message_types[tag] += 1
        country = (row.get("Country") or "unknown").strip() or "unknown"
        locale = (row.get("Locale") or "unknown").strip() or "unknown"
        platform = (row.get("Platform") or "unknown").strip() or "unknown"
        app_version = (row.get("App_Version") or "unknown").strip() or "unknown"
        asset_version = (row.get("Hosted_Asset_Version") or "unknown").strip() or "unknown"
        player = (row.get("PlayerID") or "").strip()
        countries[country] += 1
        locales[locale] += 1
        platforms[platform] += 1
        app_versions[app_version] += 1
        hosted_asset_versions[asset_version] += 1
        if player:
            players.add(player)
        enriched.append({
            "timestamp_utc": dt.isoformat(timespec="seconds").replace("+00:00", "Z") if dt else None,
            "date": date_key,
            "weekday": weekday,
            "message_types": tags,
            "player_id": player or None,
            "locale": locale,
            "country": country,
            "platform": platform,
            "app_version": app_version,
            "hosted_asset_version": asset_version,
            "feedback_text": text,
        })
    date_range = {
        "start_utc": min(parsed_datetimes).isoformat(timespec="seconds").replace("+00:00", "Z") if parsed_datetimes else None,
        "end_utc": max(parsed_datetimes).isoformat(timespec="seconds").replace("+00:00", "Z") if parsed_datetimes else None,
    }
    return {
        "total_messages": len(rows),
        "unique_players": len(players),
        "date_range": date_range,
        "daily_counts": dict(sorted(daily_counts.items())),
        "weekday_counts": {day: weekday_counts.get(day, 0) for day in WEEKDAYS}
        | ({"unknown": weekday_counts["unknown"]} if weekday_counts["unknown"] else {}),
        "message_types": dict(message_types.most_common()),
        "top_countries": counter_top(countries),
        "top_locales": counter_top(locales),
        "top_platforms": counter_top(platforms),
        "top_app_versions": counter_top(app_versions),
        "top_hosted_asset_versions": counter_top(hosted_asset_versions),
        "enriched_rows": enriched,
    }


def historical_daily_samples(runs: list[dict[str, Any]], weekday: str) -> list[float]:
    samples: list[float] = []
    for run in runs:
        for date_key, count in (run.get("daily_counts") or {}).items():
            parsed = parse_date_key(date_key)
            if parsed and WEEKDAYS[parsed.weekday()] == weekday:
                samples.append(float(count))
    return samples


def build_in_csv_comparisons(summary: dict[str, Any]) -> dict[str, Any]:
    daily_counts = {
        parsed: int(count)
        for key, count in (summary.get("daily_counts") or {}).items()
        if (parsed := parse_date_key(key)) is not None
    }
    if not daily_counts:
        return {"available": False, "reason": "No dated rows were available for in-CSV comparison."}
    latest_day = max(daily_counts)
    week_start = latest_day.fromordinal(latest_day.toordinal() - latest_day.weekday())
    current_week_days = {day: count for day, count in daily_counts.items() if week_start <= day <= latest_day}
    prior_days = {day: count for day, count in daily_counts.items() if day < week_start}
    weekday_compare: dict[str, Any] = {}
    for day in sorted(current_week_days):
        samples = [count for prior_day, count in prior_days.items() if WEEKDAYS[prior_day.weekday()] == WEEKDAYS[day.weekday()]]
        avg = mean([float(value) for value in samples])
        weekday_compare[day.isoformat()] = {
            "weekday": WEEKDAYS[day.weekday()],
            "count": current_week_days[day],
            "prior_average": round(avg, 2) if samples else None,
            "sample_days": len(samples),
            "delta_vs_average": round(current_week_days[day] - avg, 2) if samples else None,
        }
    weekly_totals: Counter[str] = Counter()
    for day, count in daily_counts.items():
        start = day.fromordinal(day.toordinal() - day.weekday())
        weekly_totals[start.isoformat()] += count
    current_week_total = sum(current_week_days.values())
    prior_complete_week_totals = [
        int(count)
        for week_key, count in weekly_totals.items()
        if parse_date_key(week_key) is not None and parse_date_key(week_key) < week_start
    ]
    days_elapsed = (latest_day - week_start).days + 1
    prior_same_elapsed_totals: list[int] = []
    for week_key in weekly_totals.keys():
        prior_week_start = parse_date_key(week_key)
        if prior_week_start is None or prior_week_start >= week_start:
            continue
        total = 0
        for offset in range(days_elapsed):
            total += daily_counts.get(prior_week_start.fromordinal(prior_week_start.toordinal() + offset), 0)
        prior_same_elapsed_totals.append(total)
    return {
        "available": True,
        "current_week_start": week_start.isoformat(),
        "latest_day": latest_day.isoformat(),
        "current_week_days_elapsed": days_elapsed,
        "current_week_total": current_week_total,
        "weekday_compare": weekday_compare,
        "weekly_compare": {
            "prior_complete_week_average": round(mean([float(v) for v in prior_complete_week_totals]), 2) if prior_complete_week_totals else None,
            "prior_complete_week_samples": len(prior_complete_week_totals),
            "prior_same_elapsed_average": round(mean([float(v) for v in prior_same_elapsed_totals]), 2) if prior_same_elapsed_totals else None,
            "prior_same_elapsed_samples": len(prior_same_elapsed_totals),
            "delta_vs_same_elapsed_average": round(current_week_total - mean([float(v) for v in prior_same_elapsed_totals]), 2) if prior_same_elapsed_totals else None,
        },
    }


def detect_spikes(summary: dict[str, Any], history_runs: list[dict[str, Any]], comparisons: dict[str, Any] | None = None) -> list[dict[str, Any]]:
    spikes: list[dict[str, Any]] = []
    total = float(summary["total_messages"])
    prior_totals = [float(run.get("total_messages", 0)) for run in history_runs if run.get("total_messages")]
    if len(prior_totals) >= 3:
        total_threshold = threshold(prior_totals, min_lift=1.6, min_abs_lift=15)
        if total >= total_threshold:
            spikes.append({"kind": "total_messages", "label": "total message spike", "current": int(total), "historical_average": round(mean(prior_totals), 2), "threshold": round(total_threshold, 2), "confidence": "medium"})
    for date_key, count in (summary.get("daily_counts") or {}).items():
        parsed = parse_date_key(date_key)
        if not parsed:
            continue
        weekday = WEEKDAYS[parsed.weekday()]
        samples = historical_daily_samples(history_runs, weekday)
        if len(samples) < 3:
            continue
        day_threshold = threshold(samples, min_lift=1.75, min_abs_lift=10)
        if float(count) >= day_threshold:
            spikes.append({"kind": "weekday_daily_messages", "label": f"{weekday} message spike", "date": date_key, "weekday": weekday, "current": int(count), "historical_average": round(mean(samples), 2), "threshold": round(day_threshold, 2), "sample_days": len(samples), "confidence": "medium"})
    if comparisons and comparisons.get("available"):
        weekly_compare = comparisons.get("weekly_compare") or {}
        same_elapsed_avg = weekly_compare.get("prior_same_elapsed_average")
        if same_elapsed_avg and comparisons.get("current_week_total", 0) >= max(same_elapsed_avg * 1.75, same_elapsed_avg + 15):
            spikes.append({"kind": "in_csv_weekly_messages", "label": "current week message spike", "current": int(comparisons["current_week_total"]), "historical_average": same_elapsed_avg, "sample_weeks": weekly_compare.get("prior_same_elapsed_samples", 0), "confidence": "medium"})
        for day_key, info in (comparisons.get("weekday_compare") or {}).items():
            avg = info.get("prior_average")
            count = info.get("count", 0)
            if avg and count >= max(avg * 1.75, avg + 10):
                spikes.append({"kind": "in_csv_weekday_messages", "label": f"{info.get('weekday')} message spike", "date": day_key, "weekday": info.get("weekday"), "current": int(count), "historical_average": avg, "sample_days": info.get("sample_days", 0), "confidence": "medium"})
    prior_runs_with_totals = [run for run in history_runs if run.get("total_messages")]
    current_types = summary.get("message_types") or {}
    for tag, count in current_types.items():
        if tag == "untagged" or len(prior_runs_with_totals) < 3:
            continue
        current_share = float(count) / total if total else 0.0
        shares: list[float] = []
        historical_counts = 0
        for run in prior_runs_with_totals:
            run_total = float(run.get("total_messages") or 0)
            run_count = float((run.get("message_types") or {}).get(tag, 0))
            historical_counts += int(run_count)
            shares.append((run_count / run_total) if run_total else 0.0)
        avg_share = mean(shares)
        share_threshold = max(avg_share * 1.75, avg_share + (2 * pstdev(shares)), avg_share + 0.05)
        enough_volume = int(count) >= 5 and current_share >= 0.05
        if (historical_counts == 0 and enough_volume) or (enough_volume and current_share >= share_threshold):
            spikes.append({"kind": "message_type", "label": f"[{tag}] spike" if historical_counts else f"new [{tag}] volume", "message_type": tag, "current_count": int(count), "current_share": round(current_share, 4), "historical_average_share": round(avg_share, 4), "threshold_share": round(share_threshold, 4), "confidence": "high" if int(count) >= 20 else "medium"})
    return spikes


def example_rows(enriched_rows: list[dict[str, Any]], tags: list[str], limit: int) -> dict[str, list[dict[str, Any]]]:
    examples: dict[str, list[dict[str, Any]]] = {tag: [] for tag in tags}
    for row in enriched_rows:
        slim = {
            "timestamp_utc": row.get("timestamp_utc"),
            "player_id": row.get("player_id"),
            "locale": row.get("locale"),
            "country": row.get("country"),
            "platform": row.get("platform"),
            "app_version": row.get("app_version"),
            "hosted_asset_version": row.get("hosted_asset_version"),
            "feedback_text": (row.get("feedback_text") or "")[:240],
        }
        for tag in tags:
            if tag in (row.get("message_types") or []) and len(examples[tag]) < limit:
                examples[tag].append(slim)
    return {tag: rows for tag, rows in examples.items() if rows}


def clean_feedback_text(text: str | None, tag: str | None = None, limit: int = 220) -> str:
    raw = re.sub(r"\s+", " ", text or "").strip()
    if tag:
        raw = re.sub(rf"^\[{re.escape(tag)}\]\s*", "", raw, flags=re.IGNORECASE)
    raw = TAG_RE.sub("", raw).strip()
    if not raw:
        return f"[{tag}] tag only; no written detail." if tag else "No written detail."
    return raw[: limit - 3].rstrip() + "..." if len(raw) > limit else raw


def build_complaint_summary(enriched_rows: list[dict[str, Any]], limit_tags: int = 5, examples_per_tag: int = 3) -> dict[str, Any]:
    tag_counts: Counter[str] = Counter()
    rows_by_tag: dict[str, list[dict[str, Any]]] = {}
    for row in enriched_rows:
        for tag in row.get("message_types") or ["untagged"]:
            tag_counts[tag] += 1
            rows_by_tag.setdefault(tag, []).append(row)
    themes: list[dict[str, Any]] = []
    for tag, count in tag_counts.most_common(limit_tags):
        tag_rows = rows_by_tag.get(tag, [])
        players = Counter(str(row.get("player_id") or "unknown") for row in tag_rows)
        countries = Counter(str(row.get("country") or "unknown") for row in tag_rows)
        locales = Counter(str(row.get("locale") or "unknown") for row in tag_rows)
        platforms = Counter(str(row.get("platform") or "unknown") for row in tag_rows)
        samples: list[str] = []
        seen_samples: set[str] = set()
        for row in tag_rows:
            sample = clean_feedback_text(row.get("feedback_text"), tag)
            if sample in seen_samples:
                continue
            seen_samples.add(sample)
            samples.append(sample)
            if len(samples) >= examples_per_tag:
                break
        repeated_players = {
            player: value
            for player, value in players.most_common(5)
            if player != "unknown" and value > 1
        }
        theme = {
            "message_type": tag,
            "count": int(count),
            "unique_players": len([player for player in players if player != "unknown"]),
            "top_countries": counter_top(countries, limit=5),
            "top_locales": counter_top(locales, limit=5),
            "top_platforms": counter_top(platforms, limit=5),
            "repeated_players": repeated_players,
            "representative_complaints": samples,
        }
        themes.append(theme)
    plain_parts: list[str] = []
    for theme in themes[:3]:
        sample_text = theme["representative_complaints"][0] if theme["representative_complaints"] else "no detail"
        plain_parts.append(f"{theme['count']} [{theme['message_type']}] ({sample_text})")
    return {
        "total_complaints": len(enriched_rows),
        "theme_count": len(themes),
        "themes": themes,
        "plain_summary": "; ".join(plain_parts) if plain_parts else "No new complaints since last support check.",
    }


def build_trend_note(summary: dict[str, Any], spikes: list[dict[str, Any]], history_runs: list[dict[str, Any]]) -> str:
    top_types = list((summary.get("message_types") or {}).items())[:3]
    type_text = ", ".join(f"[{tag}] {count}" for tag, count in top_types) if top_types else "no tags"
    prior_totals = [float(run.get("total_messages", 0)) for run in history_runs if run.get("total_messages")]
    compared = f"historical run avg {mean(prior_totals):.1f}" if prior_totals else "no prior history"
    spike_text = "spikes flagged" if spikes else "no spikes flagged"
    return f"{summary['total_messages']} messages; {compared}; {type_text}; {spike_text}."


def rel_path(path: Path, root: Path) -> str:
    try:
        return str(path.relative_to(root)).replace("\\", "/")
    except ValueError:
        return str(path)


def write_github_log(root: Path, slug: str, run_record: dict[str, Any], payload: dict[str, Any]) -> dict[str, Any]:
    run_id = run_record.get("run_id") or "unknown-run"
    analyzed_at = str(run_record.get("analyzed_at_utc") or utc_now())
    safe_timestamp = re.sub(r"[^0-9A-Za-z]+", "", analyzed_at.replace("Z", "Z"))
    game_dir = root / slug
    log_path = game_dir / f"{safe_timestamp}-{run_id}.json"
    index_path = game_dir / "index.json"
    atomic_write_json(log_path, payload)
    try:
        with index_path.open("r", encoding="utf-8-sig") as handle:
            index = json.load(handle)
    except (OSError, json.JSONDecodeError):
        index = {"schema_version": SCHEMA_VERSION, "slug": slug, "logs": []}
    logs = [
        item for item in index.get("logs", [])
        if item.get("run_id") != run_id and item.get("dataset_hash") != run_record.get("dataset_hash")
    ]
    logs.append({
        "run_id": run_id,
        "dataset_hash": run_record.get("dataset_hash"),
        "analyzed_at_utc": run_record.get("analyzed_at_utc"),
        "last_assessed_at_utc": run_record.get("last_assessed_at_utc"),
        "last_assessed_date": run_record.get("last_assessed_date"),
        "total_messages": run_record.get("total_messages"),
        "spike_detected": run_record.get("spike_detected"),
        "path": rel_path(log_path, root),
    })
    logs = sorted(logs, key=lambda item: str(item.get("analyzed_at_utc") or ""))
    index.update({
        "schema_version": SCHEMA_VERSION,
        "slug": slug,
        "updated_at_utc": utc_now(),
        "latest_log_path": rel_path(log_path, root),
        "logs": logs,
    })
    atomic_write_json(index_path, index)
    return {
        "written": True,
        "root": str(root),
        "log_path": str(log_path),
        "index_path": str(index_path),
        "status": "written_local_commit_and_push_required",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", dest="csv_path", help="Feedback CSV path. If omitted, Downloads is checked.")
    parser.add_argument("--game", help="Human game name.")
    parser.add_argument("--downloads", default=str(DEFAULT_DOWNLOADS), help="Downloads folder to search.")
    parser.add_argument("--history-root", default=str(DEFAULT_HISTORY_ROOT), help="Support history root folder.")
    parser.add_argument("--github-log-root", default=str(DEFAULT_GITHUB_LOG_ROOT), help="Local GitHub support-check log root to read.")
    parser.add_argument("--update-history", action="store_true", help="Append this analysis to game history.")
    parser.add_argument("--write-github-log", action="store_true", help="Write this support check under --github-log-root for commit/push.")
    parser.add_argument("--limit-examples", type=int, default=5, help="Example rows per tag.")
    args = parser.parse_args()
    downloads = Path(args.downloads).expanduser()
    csv_path, problem, code = resolve_csv(args.csv_path, downloads)
    if problem:
        json_exit(problem, code)
    assert csv_path is not None
    if not args.game or not args.game.strip():
        json_exit({"needs_input": "game_name", "csv_path": str(csv_path)}, 5)
    game = args.game.strip()
    slug = slugify(game)
    history_root = Path(args.history_root).expanduser()
    history_path = history_root / f"{slug}.json"
    history = load_history(history_path, game, slug)
    github_log_root = Path(args.github_log_root).expanduser() if args.github_log_root else None
    github_runs, github_log_status = load_github_log_runs(github_log_root, slug)
    known_runs = merge_runs(history.get("runs", []), github_runs)
    combined_history = dict(history)
    combined_history["runs"] = known_runs
    cutoff_day = last_assessed_day(combined_history)
    cutoff_timestamp = last_assessed_timestamp(combined_history)
    rows, processing_window = read_csv_rows(
        csv_path,
        stop_at_or_before_timestamp=cutoff_timestamp,
        stop_at_or_before_date=cutoff_day,
    )
    summary = summarize_rows(rows)
    dataset_hash = sha256_file(csv_path)
    prior_runs = [run for run in known_runs if run.get("dataset_hash") != dataset_hash]
    duplicate_dataset = len(prior_runs) != len(known_runs)
    comparisons = build_in_csv_comparisons(summary)
    spikes = detect_spikes(summary, prior_runs, comparisons=comparisons)
    flagged_tags = [spike["message_type"] for spike in spikes if spike.get("kind") == "message_type"]
    top_tags = [tag for tag in (summary.get("message_types") or {}).keys() if tag != "untagged"]
    tags_for_examples = list(dict.fromkeys(flagged_tags + top_tags[:3]))
    enriched_rows = summary.pop("enriched_rows")
    complaints_since_last_check = build_complaint_summary(enriched_rows)
    examples = example_rows(enriched_rows, tags_for_examples, args.limit_examples)
    trend_note = build_trend_note(summary, spikes, prior_runs)
    if summary["total_messages"] == 0 and cutoff_day is not None:
        trend_note = f"0 new messages after last assessed day {cutoff_day.isoformat()}; no spikes flagged."
    processed_dates = [parse_date_key(date_key) for date_key in (summary.get("daily_counts") or {}).keys() if date_key != "unknown"]
    processed_dates = [value for value in processed_dates if value is not None]
    last_processed_at = parse_timestamp((summary.get("date_range") or {}).get("end_utc"))
    last_assessed_after_run_at = last_processed_at or cutoff_timestamp
    last_assessed_after_run = (
        last_assessed_after_run_at.date()
        if last_assessed_after_run_at is not None
        else (max(processed_dates) if processed_dates else cutoff_day)
    )
    run_id = hashlib.sha1(
        f"{dataset_hash}:{game}:{summary['total_messages']}:{cutoff_timestamp or cutoff_day}".encode("utf-8")
    ).hexdigest()[:16]
    run_record = {
        "run_id": run_id,
        "dataset_hash": dataset_hash,
        "csv_name": csv_path.name,
        "csv_path": str(csv_path),
        "analyzed_at_utc": utc_now(),
        "processing_window": processing_window,
        "last_assessed_at_utc": last_assessed_after_run_at.isoformat(timespec="seconds").replace("+00:00", "Z")
        if last_assessed_after_run_at
        else None,
        "last_assessed_date": last_assessed_after_run.isoformat() if last_assessed_after_run else None,
        "date_range": summary["date_range"],
        "total_messages": summary["total_messages"],
        "unique_players": summary["unique_players"],
        "daily_counts": summary["daily_counts"],
        "weekday_counts": summary["weekday_counts"],
        "message_types": summary["message_types"],
        "top_countries": summary["top_countries"],
        "top_locales": summary["top_locales"],
        "top_platforms": summary["top_platforms"],
        "top_app_versions": summary["top_app_versions"],
        "top_hosted_asset_versions": summary["top_hosted_asset_versions"],
        "comparisons": comparisons,
        "complaints_since_last_check": complaints_since_last_check,
        "spike_detected": bool(spikes),
        "spike_labels": [spike["label"] for spike in spikes],
        "trend_note": trend_note,
    }
    history_written = False
    history_write_status = "not_requested"
    if args.update_history:
        if summary["total_messages"] == 0:
            history_write_status = "no_new_rows_not_appended"
        elif duplicate_dataset:
            history_write_status = "duplicate_dataset_not_appended"
        else:
            now = utc_now()
            history["updated_at_utc"] = now
            history["game"] = game
            history["slug"] = slug
            if last_assessed_after_run is not None:
                history["last_assessed_date"] = last_assessed_after_run.isoformat()
            if last_assessed_after_run_at is not None:
                history["last_assessed_at_utc"] = last_assessed_after_run_at.isoformat(timespec="seconds").replace("+00:00", "Z")
            history.setdefault("runs", []).append(run_record)
            history.setdefault("notes", []).append({"at_utc": now, "run_id": run_id, "note": trend_note, "spike_detected": bool(spikes), "spike_labels": [spike["label"] for spike in spikes]})
            atomic_write_json(history_path, history)
            history_written = True
            history_write_status = "appended"
    output = {
        "game": game,
        "csv_path": str(csv_path),
        "history_path": str(history_path),
        "history_written": history_written,
        "history_write_status": history_write_status,
        "duplicate_dataset": duplicate_dataset,
        "historical_runs_compared": len(prior_runs),
        "github_log_status": github_log_status,
        "github_log_write_status": {"written": False, "status": "not_requested"},
        "processing_window": processing_window,
        "spike_detected": bool(spikes),
        "spikes": spikes,
        "comparisons": comparisons,
        "complaints_since_last_check": complaints_since_last_check,
        "summary": summary,
        "trend_note": trend_note,
        "example_rows": examples,
        "run_record": run_record,
    }
    if args.write_github_log:
        if github_log_root is None:
            output["github_log_write_status"] = {"written": False, "status": "no_github_log_root_configured"}
        else:
            output["github_log_write_status"] = write_github_log(github_log_root, slug, run_record, output)
            atomic_write_json(Path(output["github_log_write_status"]["log_path"]), output)
    json_exit(output)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        json_exit({"error": str(exc), "type": exc.__class__.__name__}, 1)
