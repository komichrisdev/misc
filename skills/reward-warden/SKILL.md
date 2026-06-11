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
3. Review the side-by-side TSV, per-tab TSV folder, summary, and ignore rules file.
4. Run validation against the generated main TSV.
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

Reward labels inside each source file are normalized into file-local group labels:

- `Reward 1_1`
- `Reward 1_2`
- `Reward 2_1`

These labels replace raw JSON source paths in the user-facing TSV output. Exact raw paths are still used internally for pairing and validation.

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

- TT2 life rewards should suggest the closest-value ST3 life reward
- TT2 helper rewards should suggest the closest-value ST3 helper
- TT2 currency rewards should suggest `hard_currency2`
- suggested ST3 coin values should round to nicer steps based on `10`, `25`, `50`, and `100`
- suggestion basis text should include the TT2 reward-group total, the 70% ST3 target, the resulting suggested ST3 total, and the concrete TT2 -> ST3 reward changes used for that reward group

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

The main TSV now contains three column groups:

- TT2 reward columns
- ST3 reward columns
- suggested ST3 reward columns

Columns:

`tt2_file`, `tt2_path`, `tt2_reward`, `tt2_amt`, `tt2_coins`, `tt2_usd`, `st3_file`, `st3_path`, `st3_reward`, `st3_amt`, `st3_coins`, `st3_usd`, `s_reward`, `s_amt`, `s_coins`, `s_usd`, `s_basis`

Merged amount rule:

- amount columns now hold either the numeric amount or a time label such as `1h`, `3h`, or `6h`
- separate `unlimited_for` columns are no longer emitted
- `row_index` is no longer emitted

Per-tab output rules:

- the extractor also writes one TSV per Google Sheet tab under `reward-warden-tabs`
- tab names are:
  - `config`
  - `dailyQuest`
  - direct-match tabs such as `appleSE`, `light_rush`, `camp_config`, `consecutive_challenge`, `levels_race`, `public_cup`, `ToC`
  - `LO`
  - `misc`
- `LO` contains:
  - `camp_barrel.json` <-> `barrel.json`
  - `camp_bridge.json` <-> `rope.json`
  - `camp_cave.json` <-> `cave.json`
  - `camp_crane.json` <-> `cart.json`
  - `camp_pyramid.json` <-> `golf.json`
  - `camp_shipwreck.json` <-> `pirate.json`
  - `camp_temple.json` <-> `witch.json`
  - `camp_tower.json` <-> `mansion.json`
  - `camp_trainpet.json` <-> `training.json`
  - `camp_twotowers.json` <-> `forest.json`
- `dailyQuest` includes:
  - `DailyQuest.json`
  - `DAILY_QUEST` inside `Config.json`
- pretty tab TSVs include a blank separator row between reward groups and suppress repeated file names inside a contiguous block for easier sheet pasting

Output files go to `File Compare` by default:

- `reward-warden-output.txt`
- `reward-warden-tabs\*.txt`
- `reward-warden-summary.txt`
- `reward-warden-validation.txt`
- `reward-warden-ignore-prefixes.txt`
