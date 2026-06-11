#!/usr/bin/env python3
import argparse
import csv
import json
from dataclasses import dataclass
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path
from typing import Any


OUTPUT_COLUMNS = [
    "row_index",
    "tt2_source_file",
    "tt2_source_path",
    "tt2_reward_name",
    "tt2_amount",
    "tt2_unlimited_for_seconds",
    "tt2_coin_value",
    "tt2_dollar_value",
    "st3_source_file",
    "st3_source_path",
    "st3_reward_name",
    "st3_amount",
    "st3_unlimited_for_seconds",
    "st3_coin_value",
    "st3_dollar_value",
    "suggested_st3_reward_name",
    "suggested_st3_amount",
    "suggested_st3_unlimited_for_seconds",
    "suggested_st3_coin_value",
    "suggested_st3_dollar_value",
    "suggested_st3_basis",
]
CONTAINER_KEYS = {"reward", "rewards", "award", "awards"}
DATASET_TT2 = "TT2"
DATASET_ST3 = "ST3"
DATASET_DIRS = {
    DATASET_TT2: "TT2_data",
    DATASET_ST3: "ST3_config",
}
GROUP_MATCH_OVERRIDES = {
    "dailyquest": "dailyquest",
    "welcomeback": "welcomeback",
    "applese": "applese",
    "campconfig": "campconfig",
    "consecutivechallenge": "consecutivechallenge",
    "levelsrace": "levelsrace",
    "lightrush": "lightrush",
    "publiccup": "publiccup",
    "championstournaments": "toc",
    "barrel": "barrel",
    "bridge": "rope",
    "cave": "cave",
    "crane": "cart",
    "pyramid": "golf",
    "shipwreck": "pirate",
    "temple": "witch",
    "tower": "mansion",
    "trainpet": "training",
    "twotowers": "forest",
}
SUGGESTED_CURRENCY_STEPS = [Decimal("10"), Decimal("25"), Decimal("50"), Decimal("100")]


@dataclass(frozen=True)
class DatasetConfig:
    coin_pack_coins: Decimal
    coin_pack_dollars: Decimal
    helper_prices: dict[str, Decimal]
    life_refill_price: Decimal
    life_hourly_price: Decimal
    life_exact_prices: dict[int, Decimal]
    normalize_aliases: dict[str, str]


@dataclass(frozen=True)
class RewardRow:
    dataset: str
    source_file: str
    source_path: str
    group_key: str
    reward_name: str
    amount: str
    unlimited_for_seconds: str
    coin_value: str
    dollar_value: str


DATASET_CONFIGS = {
    DATASET_TT2: DatasetConfig(
        coin_pack_coins=Decimal("4000"),
        coin_pack_dollars=Decimal("4.99"),
        helper_prices={
            "stripes": Decimal("500"),
            "colorBomb": Decimal("700"),
            "wraps": Decimal("600"),
            "warps": Decimal("600"),
            "smash": Decimal("700"),
            "dragon": Decimal("1400"),
            "switch": Decimal("1000"),
            "extraMoves": Decimal("1000"),
            "extraMoves1": Decimal("1300"),
            "extraMoves2": Decimal("2000"),
            "extraMoves3": Decimal("3000"),
            "life": Decimal("1000"),
        },
        life_refill_price=Decimal("1000"),
        life_hourly_price=Decimal("1300"),
        life_exact_prices={
            3600: Decimal("1300"),
            10800: Decimal("2500"),
            21600: Decimal("5000"),
        },
        normalize_aliases={"warps": "wraps"},
    ),
    DATASET_ST3: DatasetConfig(
        coin_pack_coins=Decimal("3500"),
        coin_pack_dollars=Decimal("4.99"),
        helper_prices={
            "preCards": Decimal("425"),
            "preFireworks": Decimal("525"),
            "preJokers": Decimal("950"),
            "ogre": Decimal("500"),
            "fairy": Decimal("300"),
            "rogue": Decimal("250"),
            "undo": Decimal("300"),
            "joker": Decimal("700"),
            "addCards": Decimal("500"),
            "life": Decimal("300"),
        },
        life_refill_price=Decimal("300"),
        life_hourly_price=Decimal("390"),
        life_exact_prices={
            3600: Decimal("390"),
            10800: Decimal("750"),
            21600: Decimal("1050"),
        },
        normalize_aliases={},
    ),
}


def decimal_to_str(value: Decimal) -> str:
    quantized = value.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
    if quantized == quantized.to_integral():
        return str(quantized.quantize(Decimal("1")))
    return format(quantized.normalize(), "f")


def money_to_str(value: Decimal) -> str:
    return format(value.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP), "f")


def dataset_coins_to_dollars(dataset: str, coin_value: Decimal) -> Decimal:
    config = DATASET_CONFIGS[dataset]
    return (coin_value * config.coin_pack_dollars) / config.coin_pack_coins


def normalize_reward_name(dataset: str, name: str) -> str:
    return DATASET_CONFIGS[dataset].normalize_aliases.get(name, name)


def is_currency_reward(row: RewardRow | None) -> bool:
    return row is not None and row.reward_name == "hard_currency2"


def is_life_reward(row: RewardRow | None) -> bool:
    return row is not None and row.reward_name == "life"


def is_helper_reward(row: RewardRow | None) -> bool:
    return row is not None and not is_currency_reward(row) and not is_life_reward(row)


def canonical_name(text: str) -> str:
    return "".join(character for character in text.lower() if character.isalnum())


def strip_index_suffix(text: str) -> str:
    return text.split("[", 1)[0]


def canonical_file_group(source_file: str) -> str:
    stem = Path(source_file).stem
    lowered = stem.lower()
    for prefix in ("tt2_", "st3_"):
        if lowered.startswith(prefix):
            stem = stem[len(prefix) :]
            lowered = stem.lower()
    if lowered.startswith("camp_"):
        stem = stem[5:]
    return canonical_name(stem)


def derive_group_key(dataset: str, source_file: str, source_path: str) -> str:
    file_group = canonical_file_group(source_file)
    parts = parse_rendered_path(source_path)
    first_part = canonical_name(strip_index_suffix(parts[0])) if parts else ""

    if dataset == DATASET_TT2:
        if file_group == "dailyquest":
            return "dailyquest"
        if file_group == "welcomegifts":
            return "welcomeback"
        if file_group == "endgameloop":
            return "endgameloop"
        if file_group == "config":
            return f"config:{first_part}" if first_part else "config"
        return file_group

    if source_file.lower() == "config.json":
        if first_part == "dailyquest":
            return "dailyquest"
        if first_part == "welcomeback":
            return "welcomeback"
        if first_part == "levelendrewards":
            return "levelendrewards"
        return f"config:{first_part}" if first_part else "config"

    return file_group


def default_ignore_file(root_dir: Path) -> Path:
    return root_dir / "reward-warden-ignore-prefixes.txt"


def resolve_root_dir(input_path: Path) -> Path:
    if input_path.name in DATASET_DIRS.values():
        return input_path.parent
    return input_path


def render_path(parts: list[str]) -> str:
    output: list[str] = []
    for part in parts:
        if part.startswith("["):
            if output:
                output[-1] = output[-1] + part
            else:
                output.append(part)
        else:
            output.append(part)
    return ".".join(output)


def parse_rendered_path(path_text: str) -> tuple[str, ...]:
    parts: list[str] = []
    buffer = ""
    bracket_depth = 0
    for char in path_text:
        if char == "." and bracket_depth == 0:
            if buffer:
                parts.append(buffer)
                buffer = ""
            continue
        if char == "[":
            bracket_depth += 1
        elif char == "]":
            bracket_depth -= 1
        buffer += char
    if buffer:
        parts.append(buffer)
    return tuple(parts)


def is_reward_item(node: Any) -> bool:
    return isinstance(node, dict) and "type" in node and ("name" in node or "amount" in node or "unlimitedFor" in node) and (
        "amount" in node or "unlimitedFor" in node
    )


def walk_reward_items(node: Any, path_parts: list[str]):
    if is_reward_item(node):
        yield render_path(path_parts), node
        return
    if isinstance(node, list):
        for index, value in enumerate(node):
            yield from walk_reward_items(value, path_parts + [f"[{index}]"])
        return
    if isinstance(node, dict):
        for key, value in node.items():
            yield from walk_reward_items(value, path_parts + [str(key)])


def path_starts_with(path_parts: tuple[str, ...], prefix_parts: tuple[str, ...]) -> bool:
    return len(path_parts) >= len(prefix_parts) and path_parts[: len(prefix_parts)] == prefix_parts


def load_ignore_prefixes(ignore_file: Path) -> list[tuple[str | None, str, tuple[str, ...]]]:
    if not ignore_file.exists():
        return []
    rules: list[tuple[str | None, str, tuple[str, ...]]] = []
    for raw_line in ignore_file.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [part.strip() for part in line.split("|")]
        if len(parts) == 2:
            source_file, path_prefix = parts
            rules.append((None, source_file, parse_rendered_path(path_prefix)))
        elif len(parts) == 3:
            dataset, source_file, path_prefix = parts
            rules.append((dataset.upper(), source_file, parse_rendered_path(path_prefix)))
    return rules


def should_ignore_container(
    dataset: str, source_file: str, container_parts: tuple[str, ...], ignore_rules: list[tuple[str | None, str, tuple[str, ...]]]
) -> bool:
    for rule_dataset, rule_file, rule_prefix in ignore_rules:
        if rule_dataset not in (None, dataset):
            continue
        if rule_file == source_file and path_starts_with(container_parts, rule_prefix):
            return True
    return False


def find_reward_containers(node: Any, path_parts: tuple[str, ...]):
    if isinstance(node, dict):
        for key, value in node.items():
            next_parts = path_parts + (str(key),)
            if key in CONTAINER_KEYS:
                yield next_parts, key, value
            yield from find_reward_containers(value, next_parts)
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from find_reward_containers(value, path_parts + (f"[{index}]",))


def find_leaf_reward_containers(node: Any) -> list[tuple[tuple[str, ...], str, Any]]:
    all_containers = list(find_reward_containers(node, ()))
    leaf_containers: list[tuple[tuple[str, ...], str, Any]] = []
    for container_parts, container_key, container_value in all_containers:
        is_parent = any(
            other_parts != container_parts and path_starts_with(other_parts, container_parts)
            for other_parts, _, _ in all_containers
        )
        if not is_parent:
            leaf_containers.append((container_parts, container_key, container_value))
    return leaf_containers


def price_reward(dataset: str, item: dict[str, Any]) -> tuple[str, str, str]:
    config = DATASET_CONFIGS[dataset]
    reward_type = str(item.get("type", ""))
    reward_name = normalize_reward_name(dataset, str(item.get("name", item.get("type", ""))))
    amount = item.get("amount")
    unlimited_for = item.get("unlimitedFor")

    if reward_type == "currency" and reward_name == "hard_currency2" and amount is not None:
        coin_value = Decimal(str(amount))
        return reward_name, decimal_to_str(coin_value), money_to_str(dataset_coins_to_dollars(dataset, coin_value))

    if reward_name == "life":
        if unlimited_for is not None:
            seconds = int(unlimited_for)
            if seconds in config.life_exact_prices:
                coin_value = config.life_exact_prices[seconds]
            else:
                coin_value = (Decimal(str(seconds)) / Decimal("3600")) * config.life_hourly_price
            return reward_name, decimal_to_str(coin_value), money_to_str(dataset_coins_to_dollars(dataset, coin_value))
        if amount is not None:
            coin_value = Decimal(str(amount)) * config.life_refill_price
            return reward_name, decimal_to_str(coin_value), money_to_str(dataset_coins_to_dollars(dataset, coin_value))

    if reward_name in config.helper_prices and amount is not None:
        coin_value = Decimal(str(amount)) * config.helper_prices[reward_name]
        return reward_name, decimal_to_str(coin_value), money_to_str(dataset_coins_to_dollars(dataset, coin_value))

    return reward_name, "", ""


def build_nice_currency_candidates(target_coin: Decimal) -> list[Decimal]:
    if target_coin <= 0:
        return [Decimal("0")]

    candidates: set[Decimal] = set()
    exponent = len(str(int(target_coin))) - 1

    for scale_power in range(max(0, exponent - 2), exponent + 3):
        scale = Decimal(10) ** scale_power
        for step in SUGGESTED_CURRENCY_STEPS:
            candidates.add(step * scale / Decimal("10"))

    return sorted(candidate for candidate in candidates if candidate > 0)


def round_to_nice_currency(target_coin: Decimal) -> Decimal:
    candidates = build_nice_currency_candidates(target_coin)
    return min(candidates, key=lambda candidate: (abs(candidate - target_coin), candidate))


def closest_helper_suggestion(target_coin: Decimal, source_amount: Decimal) -> tuple[str, Decimal]:
    helper_prices = {
        name: price
        for name, price in DATASET_CONFIGS[DATASET_ST3].helper_prices.items()
        if name != "life"
    }
    best_name = "preCards"
    best_amount = source_amount
    best_delta = Decimal("Infinity")

    for helper_name, helper_price in helper_prices.items():
        total_coin = helper_price * source_amount
        delta = abs(total_coin - target_coin)
        if delta < best_delta or (delta == best_delta and total_coin < helper_prices[best_name] * best_amount):
            best_name = helper_name
            best_amount = source_amount
            best_delta = delta

    return best_name, best_amount


def closest_life_suggestion(tt2_row: RewardRow, target_coin: Decimal) -> tuple[str, str, Decimal]:
    life_options: list[tuple[str, str, Decimal]] = [
        ("1", "", DATASET_CONFIGS[DATASET_ST3].life_refill_price),
        ("", "3600", DATASET_CONFIGS[DATASET_ST3].life_exact_prices[3600]),
        ("", "10800", DATASET_CONFIGS[DATASET_ST3].life_exact_prices[10800]),
        ("", "21600", DATASET_CONFIGS[DATASET_ST3].life_exact_prices[21600]),
    ]
    if tt2_row.unlimited_for_seconds:
        seconds = int(tt2_row.unlimited_for_seconds)
        prorated_coin = (Decimal(seconds) / Decimal("3600")) * DATASET_CONFIGS[DATASET_ST3].life_hourly_price
        life_options.append(("", tt2_row.unlimited_for_seconds, prorated_coin))

    return min(life_options, key=lambda option: (abs(option[2] - target_coin), option[2]))


def extract_dataset_rows(
    dataset: str, data_dir: Path, ignore_rules: list[tuple[str | None, str, tuple[str, ...]]]
) -> tuple[list[RewardRow], list[str]]:
    rows: list[RewardRow] = []
    issues: list[str] = []
    json_files = sorted(path for path in data_dir.glob("*.json") if path.is_file())
    for json_file in json_files:
        data = json.loads(json_file.read_text(encoding="utf-8"))
        for container_parts, _, container_value in find_leaf_reward_containers(data):
            if should_ignore_container(dataset, json_file.name, container_parts, ignore_rules):
                continue
            matched_any = False
            for reward_path, reward_item in walk_reward_items(container_value, list(container_parts)):
                matched_any = True
                reward_name, coin_value, dollar_value = price_reward(dataset, reward_item)
                rows.append(
                    RewardRow(
                        dataset=dataset,
                        source_file=json_file.name,
                        source_path=reward_path,
                        group_key=derive_group_key(dataset, json_file.name, reward_path),
                        reward_name=reward_name,
                        amount="" if reward_item.get("amount") is None else str(reward_item.get("amount")),
                        unlimited_for_seconds="" if reward_item.get("unlimitedFor") is None else str(reward_item.get("unlimitedFor")),
                        coin_value=coin_value,
                        dollar_value=dollar_value,
                    )
                )
            if not matched_any:
                issues.append(f"{dataset} {json_file.name}: {render_path(list(container_parts))} had no concrete reward payloads")
    return rows, issues


def build_st3_suggestion(tt2_row: RewardRow | None) -> dict[str, str]:
    if tt2_row is None or not tt2_row.dollar_value:
        return {
            "suggested_st3_reward_name": "",
            "suggested_st3_amount": "",
            "suggested_st3_unlimited_for_seconds": "",
            "suggested_st3_coin_value": "",
            "suggested_st3_dollar_value": "",
            "suggested_st3_basis": "",
        }
    target_dollar = Decimal(tt2_row.dollar_value) * Decimal("0.70")
    st3_config = DATASET_CONFIGS[DATASET_ST3]
    target_coin = (target_dollar * st3_config.coin_pack_coins / st3_config.coin_pack_dollars).quantize(
        Decimal("1"), rounding=ROUND_HALF_UP
    )
    if is_life_reward(tt2_row):
        suggested_amount, suggested_seconds, suggested_coin = closest_life_suggestion(tt2_row, target_coin)
        suggested_dollar = dataset_coins_to_dollars(DATASET_ST3, suggested_coin)
        return {
            "suggested_st3_reward_name": "life",
            "suggested_st3_amount": suggested_amount,
            "suggested_st3_unlimited_for_seconds": suggested_seconds,
            "suggested_st3_coin_value": decimal_to_str(suggested_coin),
            "suggested_st3_dollar_value": money_to_str(suggested_dollar),
            "suggested_st3_basis": "70% of TT2 life value mapped to closest-value ST3 life reward",
        }
    if is_helper_reward(tt2_row):
        helper_amount = Decimal(tt2_row.amount) if tt2_row.amount else Decimal("1")
        helper_name, helper_amount = closest_helper_suggestion(target_coin, helper_amount)
        helper_coin = DATASET_CONFIGS[DATASET_ST3].helper_prices[helper_name] * helper_amount
        helper_dollar = dataset_coins_to_dollars(DATASET_ST3, helper_coin)
        return {
            "suggested_st3_reward_name": helper_name,
            "suggested_st3_amount": decimal_to_str(helper_amount),
            "suggested_st3_unlimited_for_seconds": "",
            "suggested_st3_coin_value": decimal_to_str(helper_coin),
            "suggested_st3_dollar_value": money_to_str(helper_dollar),
            "suggested_st3_basis": "70% of TT2 helper value mapped to closest-value ST3 helper",
        }

    rounded_coin = round_to_nice_currency(target_coin)
    rounded_dollar = dataset_coins_to_dollars(DATASET_ST3, rounded_coin)
    return {
        "suggested_st3_reward_name": "hard_currency2",
        "suggested_st3_amount": decimal_to_str(rounded_coin),
        "suggested_st3_unlimited_for_seconds": "",
        "suggested_st3_coin_value": decimal_to_str(rounded_coin),
        "suggested_st3_dollar_value": money_to_str(rounded_dollar),
        "suggested_st3_basis": "70% of TT2 dollar value converted into nicely rounded ST3 hard_currency2",
    }


def group_rows(rows: list[RewardRow]) -> tuple[list[str], dict[str, list[RewardRow]]]:
    order: list[str] = []
    grouped: dict[str, list[RewardRow]] = {}
    for row in rows:
        if row.group_key not in grouped:
            grouped[row.group_key] = []
            order.append(row.group_key)
        grouped[row.group_key].append(row)
    return order, grouped


def choose_st3_group(tt2_group: str, available_st3_groups: set[str]) -> str | None:
    explicit = GROUP_MATCH_OVERRIDES.get(tt2_group)
    if explicit in available_st3_groups:
        return explicit
    if tt2_group in available_st3_groups:
        return tt2_group
    return None


def build_paired_group_sequence(tt2_rows: list[RewardRow], st3_rows: list[RewardRow]) -> list[tuple[list[RewardRow], list[RewardRow]]]:
    tt2_order, tt2_grouped = group_rows(tt2_rows)
    st3_order, st3_grouped = group_rows(st3_rows)
    remaining_st3 = set(st3_grouped)
    paired_groups: list[tuple[list[RewardRow], list[RewardRow]]] = []

    for tt2_group in tt2_order:
        st3_group = choose_st3_group(tt2_group, remaining_st3)
        tt2_group_rows = tt2_grouped[tt2_group]
        st3_group_rows = st3_grouped[st3_group] if st3_group is not None else []
        if st3_group is not None:
            remaining_st3.remove(st3_group)
        paired_groups.append((tt2_group_rows, st3_group_rows))

    for st3_group in st3_order:
        if st3_group in remaining_st3:
            paired_groups.append(([], st3_grouped[st3_group]))
            remaining_st3.remove(st3_group)

    return paired_groups


def build_combined_rows(tt2_rows: list[RewardRow], st3_rows: list[RewardRow]) -> list[dict[str, str]]:
    combined: list[dict[str, str]] = []
    row_index = 1
    for tt2_group_rows, st3_group_rows in build_paired_group_sequence(tt2_rows, st3_rows):
        pair_count = max(len(tt2_group_rows), len(st3_group_rows))
        for index in range(pair_count):
            tt2_row = tt2_group_rows[index] if index < len(tt2_group_rows) else None
            st3_row = st3_group_rows[index] if index < len(st3_group_rows) else None
            row = {
                "row_index": str(row_index),
                "tt2_source_file": "" if tt2_row is None else tt2_row.source_file,
                "tt2_source_path": "" if tt2_row is None else tt2_row.source_path,
                "tt2_reward_name": "" if tt2_row is None else tt2_row.reward_name,
                "tt2_amount": "" if tt2_row is None else tt2_row.amount,
                "tt2_unlimited_for_seconds": "" if tt2_row is None else tt2_row.unlimited_for_seconds,
                "tt2_coin_value": "" if tt2_row is None else tt2_row.coin_value,
                "tt2_dollar_value": "" if tt2_row is None else tt2_row.dollar_value,
                "st3_source_file": "" if st3_row is None else st3_row.source_file,
                "st3_source_path": "" if st3_row is None else st3_row.source_path,
                "st3_reward_name": "" if st3_row is None else st3_row.reward_name,
                "st3_amount": "" if st3_row is None else st3_row.amount,
                "st3_unlimited_for_seconds": "" if st3_row is None else st3_row.unlimited_for_seconds,
                "st3_coin_value": "" if st3_row is None else st3_row.coin_value,
                "st3_dollar_value": "" if st3_row is None else st3_row.dollar_value,
            }
            row.update(build_st3_suggestion(tt2_row))
            combined.append(row)
            row_index += 1
    return combined


def write_tsv(rows: list[dict[str, str]], output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=OUTPUT_COLUMNS, delimiter="\t")
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def parse_tsv(input_tsv: Path) -> list[dict[str, str]]:
    with input_tsv.open("r", encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        return [{column: row.get(column, "") for column in OUTPUT_COLUMNS} for row in reader]


def build_summary(
    root_dir: Path,
    tt2_rows: list[RewardRow],
    st3_rows: list[RewardRow],
    issues: list[str],
) -> str:
    tt2_coin_total = sum((Decimal(row.coin_value) for row in tt2_rows if row.coin_value), Decimal("0"))
    st3_coin_total = sum((Decimal(row.coin_value) for row in st3_rows if row.coin_value), Decimal("0"))
    tt2_dollar_total = sum((Decimal(row.dollar_value) for row in tt2_rows if row.dollar_value), Decimal("0"))
    st3_dollar_total = sum((Decimal(row.dollar_value) for row in st3_rows if row.dollar_value), Decimal("0"))
    lines = [
        "Reward Warden Summary",
        f"root_dir\t{root_dir}",
        f"tt2_json_files_scanned\t{len(list((root_dir / DATASET_DIRS[DATASET_TT2]).glob('*.json')))}",
        f"st3_json_files_scanned\t{len(list((root_dir / DATASET_DIRS[DATASET_ST3]).glob('*.json')))}",
        f"tt2_rows_emitted\t{len(tt2_rows)}",
        f"st3_rows_emitted\t{len(st3_rows)}",
        f"comparison_rows_emitted\t{max(len(tt2_rows), len(st3_rows))}",
        f"tt2_priced_coin_total\t{decimal_to_str(tt2_coin_total)}",
        f"tt2_priced_dollar_total\t{money_to_str(tt2_dollar_total)}",
        f"st3_priced_coin_total\t{decimal_to_str(st3_coin_total)}",
        f"st3_priced_dollar_total\t{money_to_str(st3_dollar_total)}",
        f"non_payload_reward_refs\t{len(issues)}",
    ]
    if issues:
        lines.extend(["", "Non-payload reward references"])
        lines.extend(issues)
    return "\n".join(lines) + "\n"


def validate_rows(expected_rows: list[dict[str, str]], actual_rows: list[dict[str, str]], issues: list[str]) -> str:
    mismatches: list[str] = []
    if len(expected_rows) != len(actual_rows):
        mismatches.append(f"row count mismatch: expected {len(expected_rows)} actual {len(actual_rows)}")
    compare_count = min(len(expected_rows), len(actual_rows))
    for index in range(compare_count):
        expected = expected_rows[index]
        actual = actual_rows[index]
        for column in OUTPUT_COLUMNS:
            if expected[column] != actual[column]:
                mismatches.append(
                    f"row {index + 2} column {column}: expected '{expected[column]}' actual '{actual[column]}'"
                )
    status = "PASS" if not mismatches else "FAIL"
    lines = [
        f"status\t{status}",
        f"checked_rows\t{compare_count}",
        f"expected_rows\t{len(expected_rows)}",
        f"actual_rows\t{len(actual_rows)}",
        f"mismatches\t{len(mismatches)}",
        f"notes\t{len(issues)} non-payload reward references seen during source extraction",
    ]
    if issues:
        lines.extend(["", "non_payload_reward_refs"])
        lines.extend(issues)
    if mismatches:
        lines.extend(["", "mismatch_details"])
        lines.extend(mismatches)
    return "\n".join(lines) + "\n"


def extract_all(root_dir: Path, ignore_rules: list[tuple[str | None, str, tuple[str, ...]]]):
    tt2_rows, tt2_issues = extract_dataset_rows(DATASET_TT2, root_dir / DATASET_DIRS[DATASET_TT2], ignore_rules)
    st3_rows, st3_issues = extract_dataset_rows(DATASET_ST3, root_dir / DATASET_DIRS[DATASET_ST3], ignore_rules)
    return tt2_rows, st3_rows, tt2_issues + st3_issues


def cmd_extract(args: argparse.Namespace) -> int:
    root_dir = resolve_root_dir(Path(args.data_dir))
    output_tsv = Path(args.output_tsv) if args.output_tsv else root_dir / "reward-warden-output.txt"
    summary_out = Path(args.summary_out) if args.summary_out else root_dir / "reward-warden-summary.txt"
    ignore_file = Path(args.ignore_file) if args.ignore_file else default_ignore_file(root_dir)
    ignore_rules = load_ignore_prefixes(ignore_file)
    tt2_rows, st3_rows, issues = extract_all(root_dir, ignore_rules)
    combined_rows = build_combined_rows(tt2_rows, st3_rows)
    write_tsv(combined_rows, output_tsv)
    summary_out.write_text(build_summary(root_dir, tt2_rows, st3_rows, issues), encoding="utf-8")
    print(
        json.dumps(
            {
                "status": "ok",
                "tt2_rows": len(tt2_rows),
                "st3_rows": len(st3_rows),
                "rows_emitted": len(combined_rows),
                "output_tsv": str(output_tsv),
                "summary_out": str(summary_out),
                "ignore_file": str(ignore_file),
                "non_payload_reward_refs": len(issues),
            },
            indent=2,
        )
    )
    return 0


def cmd_validate(args: argparse.Namespace) -> int:
    root_dir = resolve_root_dir(Path(args.data_dir))
    input_tsv = Path(args.input_tsv) if args.input_tsv else root_dir / "reward-warden-output.txt"
    report_out = Path(args.report_out) if args.report_out else root_dir / "reward-warden-validation.txt"
    ignore_file = Path(args.ignore_file) if args.ignore_file else default_ignore_file(root_dir)
    ignore_rules = load_ignore_prefixes(ignore_file)
    tt2_rows, st3_rows, issues = extract_all(root_dir, ignore_rules)
    expected_rows = build_combined_rows(tt2_rows, st3_rows)
    actual_rows = parse_tsv(input_tsv)
    report = validate_rows(expected_rows, actual_rows, issues)
    report_out.write_text(report, encoding="utf-8")
    status = "PASS" if "status\tPASS" in report else "FAIL"
    print(
        json.dumps(
            {
                "status": status,
                "report_out": str(report_out),
                "expected_rows": len(expected_rows),
                "actual_rows": len(actual_rows),
            },
            indent=2,
        )
    )
    return 0 if status == "PASS" else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Extract and compare TT2 and ST3 rewards.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    extract_parser = subparsers.add_parser("extract")
    extract_parser.add_argument("--data-dir", required=True)
    extract_parser.add_argument("--output-tsv")
    extract_parser.add_argument("--summary-out")
    extract_parser.add_argument("--ignore-file")
    extract_parser.set_defaults(func=cmd_extract)

    validate_parser = subparsers.add_parser("validate")
    validate_parser.add_argument("--data-dir", required=True)
    validate_parser.add_argument("--input-tsv")
    validate_parser.add_argument("--report-out")
    validate_parser.add_argument("--ignore-file")
    validate_parser.set_defaults(func=cmd_validate)
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
