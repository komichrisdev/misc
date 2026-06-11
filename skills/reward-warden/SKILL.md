---
name: reward-warden
description: Compare TT2 and ST3 reward JSON files, extract every concrete reward payload from both config folders, write side-by-side spreadsheet-ready TSV output in source order, validate the output back to both sources, and suggest ST3 rewards worth 70% of each TT2 reward's dollar value.
---

# Reward Warden

## When to use

Use this skill when the user wants a cross-game reward audit, reward valuation, side-by-side TT2 versus ST3 reward export, reward validation, or ST3 suggestion values based on TT2 rewards.

Default compare root:

`C:\Users\chris\Qublix Games Dropbox\Chris K\Cursor\File Compare`

Input folders:

- `TT2_data`
- `ST3_config`

## Workflow

1. First spawn `gpt-5.4-mini` subagents for parallel discovery or validation when the tool is available.
2. Run the extractor on the `File Compare` root.
3. Review the side-by-side TSV, summary, and ignore rules file.
4. Run validation against the generated TSV.
5. Edit the ignore rules file in `File Compare` when the user wants future exclusions.
6. Report totals, validation status, output paths, subagent usage, and any pairing assumptions.

## Pairing rule

The output keeps exact source order inside each matched reward group, not raw whole-dataset row number.

- TT2 stays in exact TT2 source order.
- ST3 stays in exact ST3 source order inside each matched group.
- The skill first tries themed group matches such as:
  - `DailyQuest.json` -> `Config.json` `DAILY_QUEST`
  - `WelcomeGifts.json` -> `Config.json` `WELCOME_BACK`
  - `TT2_*` files -> matching `ST3_*` files with the same feature name
  - obvious camp pairs like `camp_barrel` -> `barrel`
- If no good ST3 group match exists, TT2 rows stay on the left with blank ST3 cells.
- Any ST3 groups still unmatched are appended later with blank TT2 cells.
- This is best-effort pairing for review. Structures do not need to match 1:1.

## Pricing rules

### TT2

- `4000 coins = $4.99`
- `wraps = 600`
- `stripes = 500`
- `colorBomb = 700`
- `smash = 700`
- `dragon = 1400`
- `switch = 1000`
- `extraMoves = 1000`
- `extraMoves1 = 1300`
- `extraMoves2 = 2000`
- `extraMoves3 = 3000`
- `life` refill = `1000`
- `life` `3600` sec = `1300`
- `life` `10800` sec = `2500`
- `life` `21600` sec = `5000`
- `warps` is normalized to `wraps`

### ST3

- `3500 coins = $4.99`
- `preCards = 425`
- `preFireworks = 525`
- `preJokers = 950`
- `ogre = 500`
- `fairy = 300`
- `rogue = 250`
- `undo = 300`
- `joker = 700`
- `addCards = 500`
- `life` refill = `300`
- `life` `3600` sec = `390`
- `life` `10800` sec = `750`
- `life` `21600` sec = `1050`

## ST3 suggestion columns

For every TT2 reward row with a dollar value, the skill suggests a new ST3 reward value equal to `70%` of that TT2 dollar value.

Current suggestion rule:

- TT2 helper rewards should suggest the closest-value ST3 helper
- TT2 currency rewards should suggest `hard_currency2`
- suggested ST3 coin values should round to nicer steps based on `10`, `25`, `50`, and `100`

## Ignore rules

The live ignore file is:

`C:\Users\chris\Qublix Games Dropbox\Chris K\Cursor\File Compare\reward-warden-ignore-prefixes.txt`

Rule formats:

- `source_file|json_path_prefix`
- `dataset|source_file|json_path_prefix`

Current default:

- `TT2|Config.json|BUILDINGS_PROGRESSION_ORDER`

## Commands

Use the bundled Python runtime when needed:

```powershell
& "C:\Users\chris\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" "C:\Users\chris\.codex\skills\reward-warden\scripts\reward_warden.py" extract --data-dir "C:\Users\chris\Qublix Games Dropbox\Chris K\Cursor\File Compare"
```

```powershell
& "C:\Users\chris\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" "C:\Users\chris\.codex\skills\reward-warden\scripts\reward_warden.py" validate --data-dir "C:\Users\chris\Qublix Games Dropbox\Chris K\Cursor\File Compare"
```

## Output

The TSV now contains three column groups:

- TT2 reward columns
- ST3 reward columns
- suggested ST3 reward columns

Columns:

`row_index`, `tt2_source_file`, `tt2_source_path`, `tt2_reward_name`, `tt2_amount`, `tt2_unlimited_for_seconds`, `tt2_coin_value`, `tt2_dollar_value`, `st3_source_file`, `st3_source_path`, `st3_reward_name`, `st3_amount`, `st3_unlimited_for_seconds`, `st3_coin_value`, `st3_dollar_value`, `suggested_st3_reward_name`, `suggested_st3_amount`, `suggested_st3_unlimited_for_seconds`, `suggested_st3_coin_value`, `suggested_st3_dollar_value`, `suggested_st3_basis`

Output files go to `File Compare` by default:

- `reward-warden-output.txt`
- `reward-warden-summary.txt`
- `reward-warden-validation.txt`
- `reward-warden-ignore-prefixes.txt`
