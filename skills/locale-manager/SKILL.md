---
name: locale-manager
description: Manage JSON .locale localization folders. Use when Codex needs to inspect, repair, update, translate, proofread, version, validate, or zip *.locale files named with language codes such as common_en.locale or common_it.locale; add new locale keys from English text; preserve keys/placeholders; detect duplicate keys; keep validation in .validated.json sidecars; and coordinate parallel subagents for translation and spelling/grammar validation.
---

# Locale Manager

Use this skill for folders of `.locale` files that are JSON objects with file names ending in a language code, such as `common_en.locale`, `common_fr.locale`, or `common_pt-BR.locale`.

## Workflow

1. Inspect first:

```powershell
node scripts/locale-manager.mjs inspect --dir "C:\path\to\locale-folder"
```

2. Stop if `duplicateKeys` is not empty. Report file, key, and line numbers, then ask the user which duplicate entry to delete.

3. Prepare requested English entries as JSON, for example:

```json
{"test_locale":"This is a test"}
```

4. Spawn cheap worker subagents in parallel by disjoint locale batches. Use `gpt-5.4-mini` unless the language content is unusually high risk. Tell workers:
   - Translate values only, never keys.
   - Preserve placeholders exactly: `%0`, `%1`, `{name}`, `\n`, HTML-like tags, currency, product names, and punctuation tokens that are part of the UI contract.
   - Proofread only keys that are missing or stale in the `.validated.json` sidecar, unless the user asks for `--recheck all` or specific keys.
   - Return strict JSON only.

5. Merge worker output into this shape:

```json
{
  "translations": {
    "fr": {"test_locale": "Ceci est un test"}
  },
  "corrections": {
    "en": {}
  },
  "validated": {
    "fr": ["test_locale"]
  },
  "warnings": []
}
```

`translations` is for requested new or changed English keys in non-English locale files. `corrections` is for spelling/grammar fixes to existing values. `validated` lists keys checked by the worker without text changes; use `"all"` only when the full file was actually checked.

6. Apply once from the parent agent:

```powershell
node scripts/locale-manager.mjs apply --dir "C:\path\to\locale-folder" --entries "{\"test_locale\":\"This is a test\"}" --agent-output "@C:\path\to\agent-output.json"
```

## Rules

- Keep `.locale_Version` as the first key in every file. Add it if missing.
- Bump the highest numeric version found by `0.1`, then write the same `vX.Y` value to every file.
- Do not write validation into `.locale` files. Locale files must stay flat `"x":"y"` maps and should contain no `.locale_Validated` key.
- Store validation in one folder sidecar named `commonLocales.validated.json`. Shape: `{ "common_en.locale": { "key": "sha256(current text)" } }`.
- If old per-locale sidecars or an old `.locale_Validated` key exist, treat them as legacy validation input, then write only `commonLocales.validated.json` on apply.
- Insert new keys near similar keys by prefix/token match; append near the end if no similar key exists.
- Repair only safe JSON issues: BOM, trailing commas, final newline, and consistent indentation.
- Never auto-delete duplicate keys. Stop and ask the user.
- Zip only `.locale` files after apply as `vX.Y_commonLocales.zip` in the locale directory. Do not include `commonLocales.validated.json` or any validation sidecar.

## Script

Use `scripts/locale-manager.mjs`.

- `inspect --dir <folder> [--recheck all|key1,key2]`
- `apply --dir <folder> --entries <json-or-@file> --agent-output <json-or-@file> [--recheck all|key1,key2]`

The script prints JSON summaries. Treat `ok: false` as a stop signal.
