#!/usr/bin/env python3
import re
import sys
from collections import defaultdict
from pathlib import Path

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


def series_name(anime_root: Path, path: Path) -> str:
    try:
        return path.relative_to(anime_root).parts[0]
    except (IndexError, ValueError):
        return path.parent.name


def main() -> int:
    anime_root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/srv/media/anime")
    if not anime_root.exists():
        return 0

    episodes = defaultdict(set)

    for path in anime_root.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in VIDEO_EXTS:
            continue
        match = EPISODE_RE.search(path.name)
        if not match:
            continue
        if int(match.group("season")) == 0:
            continue
        key = (series_name(anime_root, path), int(match.group("season")))
        episodes[key].add(int(match.group("episode")))

    missing_lines = []
    for (series, season), found in sorted(episodes.items()):
        if len(found) < 2:
            continue
        for episode in range(min(found), max(found) + 1):
            if episode not in found:
                missing_lines.append(f"- {series} season {season:02d}: missing episode {episode:02d}")

    if missing_lines:
        print("## Missing Anime Episodes")
        print()
        print("\n".join(missing_lines))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
