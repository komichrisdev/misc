#!/usr/bin/env node
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const VERSION_KEY = ".locale_Version";
const VALIDATED_KEY = ".locale_Validated";
const META_KEYS = new Set([VERSION_KEY, VALIDATED_KEY]);
const PLACEHOLDER_PATTERN = /%(?:\d+|s|d|f)|\{[^}]+\}|<[^>]+>|\\n|\n|\$[A-Za-z0-9_]+/g;

const LANGUAGE_NAMES = {
  ar: "Arabic",
  ca: "Catalan",
  cs: "Czech",
  cy: "Welsh",
  da: "Danish",
  de: "German",
  el: "Greek",
  en: "English",
  es: "Spanish",
  fi: "Finnish",
  fr: "French",
  he: "Hebrew",
  hi: "Hindi",
  hr: "Croatian",
  hu: "Hungarian",
  id: "Indonesian",
  is: "Icelandic",
  it: "Italian",
  ja: "Japanese",
  ko: "Korean",
  lt: "Lithuanian",
  lv: "Latvian",
  ms: "Malay",
  nb: "Norwegian Bokmal",
  nl: "Dutch",
  no: "Norwegian",
  pl: "Polish",
  pt: "Portuguese",
  ro: "Romanian",
  ru: "Russian",
  sk: "Slovak",
  sl: "Slovenian",
  sr: "Serbian",
  sv: "Swedish",
  th: "Thai",
  tr: "Turkish",
  uk: "Ukrainian",
  vi: "Vietnamese",
  zh: "Chinese",
};

function usage() {
  console.error([
    "Use:",
    "  node scripts/locale-manager.mjs inspect --dir <folder> [--recheck all|key1,key2]",
    "  node scripts/locale-manager.mjs apply --dir <folder> --entries <json-or-@file> --agent-output <json-or-@file> [--recheck all|key1,key2]",
  ].join("\n"));
}

function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith("--")) {
      args._.push(arg);
      continue;
    }
    const key = arg.slice(2);
    const next = argv[i + 1];
    if (!next || next.startsWith("--")) {
      args[key] = true;
      continue;
    }
    args[key] = next;
    i += 1;
  }
  return args;
}

function readJsonArg(value, label) {
  if (!value || value === true) {
    throw new Error(`Missing ${label}`);
  }
  const raw = String(value).startsWith("@")
    ? fs.readFileSync(String(value).slice(1), "utf8")
    : String(value);
  return JSON.parse(raw);
}

function getLocaleCode(fileName) {
  const match = fileName.match(/(?:^|_)([A-Za-z]{2,3}(?:-[A-Za-z0-9]+)?)\.locale$/);
  return match ? match[1] : path.basename(fileName, ".locale");
}

function languageName(code) {
  const base = code.toLowerCase().split("-")[0];
  return LANGUAGE_NAMES[code] ?? LANGUAGE_NAMES[base] ?? code;
}

function readLocaleFiles(dir) {
  if (!dir || dir === true) {
    throw new Error("Missing --dir");
  }
  const resolvedDir = path.resolve(String(dir));
  const files = fs.readdirSync(resolvedDir)
    .filter((fileName) => fileName.endsWith(".locale"))
    .sort((a, b) => a.localeCompare(b))
    .map((fileName) => ({
      fileName,
      filePath: path.join(resolvedDir, fileName),
      locale: getLocaleCode(fileName),
      language: languageName(getLocaleCode(fileName)),
    }));

  if (files.length === 0) {
    throw new Error(`No .locale files found in ${resolvedDir}`);
  }
  return { dir: resolvedDir, files };
}

function safeRepairJsonText(raw) {
  let text = raw.replace(/^\uFEFF/, "");
  text = text.replace(/,\s*([}\]])/g, "$1");
  return text;
}

function hashText(value) {
  return crypto.createHash("sha256").update(String(value)).digest("hex");
}

function uniqueSorted(values) {
  return [...new Set(values)].sort((a, b) => a.localeCompare(b));
}

function placeholderSet(value) {
  return uniqueSorted(String(value).match(PLACEHOLDER_PATTERN) ?? []);
}

function samePlaceholders(a, b) {
  return placeholderSet(a).join("\u0000") === placeholderSet(b).join("\u0000");
}

function scanDuplicateKeys(text) {
  const duplicates = [];
  const stack = [{ keys: new Map(), path: "$" }];
  let inString = false;
  let escape = false;
  let current = "";
  let line = 1;
  let stringStartLine = 1;
  let lastString = null;

  function lookAheadIsColon(index) {
    for (let j = index + 1; j < text.length; j += 1) {
      const c = text[j];
      if (/\s/.test(c)) {
        continue;
      }
      return c === ":";
    }
    return false;
  }

  for (let i = 0; i < text.length; i += 1) {
    const c = text[i];
    if (c === "\n") {
      line += 1;
    }

    if (inString) {
      if (escape) {
        escape = false;
        current += c;
        continue;
      }
      if (c === "\\") {
        escape = true;
        current += c;
        continue;
      }
      if (c === "\"") {
        inString = false;
        lastString = { value: current, line: stringStartLine, isKey: lookAheadIsColon(i) };
        current = "";
        continue;
      }
      current += c;
      continue;
    }

    if (c === "\"") {
      inString = true;
      stringStartLine = line;
      current = "";
      continue;
    }

    if (c === "{") {
      stack.push({ keys: new Map(), path: "$" });
      lastString = null;
      continue;
    }

    if (c === "}") {
      stack.pop();
      lastString = null;
      continue;
    }

    if (c === ":" && lastString?.isKey) {
      const frame = stack[stack.length - 1];
      const previous = frame.keys.get(lastString.value);
      if (previous) {
        duplicates.push({
          key: lastString.value,
          firstLine: previous.line,
          duplicateLine: lastString.line,
        });
      } else {
        frame.keys.set(lastString.value, { line: lastString.line });
      }
      lastString = null;
      continue;
    }

    if (!/\s/.test(c)) {
      lastString = null;
    }
  }

  return duplicates;
}

function parseLocale(file) {
  const raw = fs.readFileSync(file.filePath, "utf8");
  const duplicates = scanDuplicateKeys(raw);
  const repaired = safeRepairJsonText(raw);
  const repairs = [];
  if (raw.charCodeAt(0) === 0xFEFF) {
    repairs.push("bom");
  }
  if (raw !== repaired && raw.replace(/^\uFEFF/, "") !== repaired) {
    repairs.push("trailing-commas");
  }

  let data = null;
  let jsonError = null;
  try {
    data = JSON.parse(repaired);
    if (!data || typeof data !== "object" || Array.isArray(data)) {
      jsonError = "Root JSON value must be an object";
      data = null;
    }
  } catch (error) {
    jsonError = error.message;
  }

  return { ...file, raw, repaired, repairs, duplicates, data, jsonError };
}

function parseVersion(value) {
  const match = String(value ?? "").match(/(\d+(?:\.\d+)?)/);
  return match ? Number(match[1]) : null;
}

function nextVersion(files) {
  const versions = files
    .map((file) => parseVersion(file.data?.[VERSION_KEY]))
    .filter((value) => Number.isFinite(value));
  const highest = versions.length > 0 ? Math.max(...versions) : 0;
  const nextTenths = Math.round(highest * 10) + 1;
  return `v${(nextTenths / 10).toFixed(1)}`;
}

function parseRecheck(value) {
  if (!value || value === true) {
    return { all: false, keys: new Set() };
  }
  if (String(value).toLowerCase() === "all") {
    return { all: true, keys: new Set() };
  }
  return {
    all: false,
    keys: new Set(String(value).split(",").map((key) => key.trim()).filter(Boolean)),
  };
}

function realKeys(data) {
  return Object.keys(data ?? {}).filter((key) => !META_KEYS.has(key));
}

function validationStatus(data, recheck) {
  const validated = data?.[VALIDATED_KEY] && typeof data[VALIDATED_KEY] === "object" && !Array.isArray(data[VALIDATED_KEY])
    ? data[VALIDATED_KEY]
    : {};
  const keys = realKeys(data);
  const stale = [];
  for (const key of keys) {
    const currentHash = hashText(data[key]);
    if (recheck.all || recheck.keys.has(key) || validated[key] !== currentHash) {
      stale.push(key);
    }
  }
  return {
    totalKeys: keys.length,
    validatedKeys: keys.length - stale.length,
    needsValidation: stale.length,
    sampleNeedsValidation: stale.slice(0, 25),
  };
}

function inspect(dir, recheckValue) {
  const recheck = parseRecheck(recheckValue);
  const { dir: resolvedDir, files } = readLocaleFiles(dir);
  const parsed = files.map(parseLocale);
  const duplicateKeys = [];
  const jsonErrors = [];

  for (const file of parsed) {
    for (const duplicate of file.duplicates) {
      duplicateKeys.push({
        file: file.fileName,
        locale: file.locale,
        key: duplicate.key,
        firstLine: duplicate.firstLine,
        duplicateLine: duplicate.duplicateLine,
      });
    }
    if (file.jsonError) {
      jsonErrors.push({ file: file.fileName, locale: file.locale, error: file.jsonError });
    }
  }

  const summary = {
    ok: duplicateKeys.length === 0 && jsonErrors.length === 0,
    dir: resolvedDir,
    fileCount: parsed.length,
    locales: parsed.map((file) => ({
      file: file.fileName,
      locale: file.locale,
      language: file.language,
      version: file.data?.[VERSION_KEY] ?? null,
      firstKey: file.data ? Object.keys(file.data)[0] ?? null : null,
      repairsAvailable: file.repairs,
      validation: file.data ? validationStatus(file.data, recheck) : null,
    })),
    duplicateKeys,
    jsonErrors,
    nextVersion: jsonErrors.length === 0 ? nextVersion(parsed.filter((file) => file.data)) : null,
  };

  return summary;
}

function normalizeAgentOutput(value) {
  const output = value && typeof value === "object" && !Array.isArray(value) ? value : {};
  return {
    translations: output.translations && typeof output.translations === "object" ? output.translations : {},
    corrections: output.corrections && typeof output.corrections === "object" ? output.corrections : {},
    validated: output.validated && typeof output.validated === "object" ? output.validated : {},
    warnings: Array.isArray(output.warnings) ? output.warnings : [],
  };
}

function tokens(key) {
  return String(key).toLowerCase().split(/[^a-z0-9]+/).filter(Boolean);
}

function commonPrefixLength(a, b) {
  const max = Math.min(a.length, b.length);
  let count = 0;
  while (count < max && a[count] === b[count]) {
    count += 1;
  }
  return count;
}

function insertionIndex(keys, newKey) {
  if (keys.length === 0) {
    return 0;
  }

  const newTokens = tokens(newKey);
  let best = { score: 0, index: keys.length - 1 };
  keys.forEach((key, index) => {
    const oldTokens = tokens(key);
    const shared = oldTokens.filter((token) => newTokens.includes(token)).length;
    const prefix = commonPrefixLength(String(key).toLowerCase(), String(newKey).toLowerCase());
    let score = shared;
    if (oldTokens[0] && oldTokens[0] === newTokens[0]) {
      score += 4;
    }
    if (prefix >= 3) {
      score += Math.min(prefix, 12) / 3;
    }
    if (score >= best.score) {
      best = { score, index };
    }
  });

  return best.score > 0 ? best.index + 1 : keys.length;
}

function setOrderedKey(pairs, key, value) {
  const existing = pairs.find((pair) => pair.key === key);
  if (existing) {
    existing.value = value;
    return;
  }
  const index = insertionIndex(pairs.map((pair) => pair.key), key);
  pairs.splice(index, 0, { key, value });
}

function buildOrderedObject(data, nextVersionValue, touchedKeys, validatedKeys) {
  const validation = data[VALIDATED_KEY] && typeof data[VALIDATED_KEY] === "object" && !Array.isArray(data[VALIDATED_KEY])
    ? { ...data[VALIDATED_KEY] }
    : {};

  for (const key of touchedKeys) {
    if (!META_KEYS.has(key) && Object.hasOwn(data, key)) {
      validation[key] = hashText(data[key]);
    }
  }
  for (const key of validatedKeys) {
    if (!META_KEYS.has(key) && Object.hasOwn(data, key)) {
      validation[key] = hashText(data[key]);
    }
  }

  const ordered = { [VERSION_KEY]: nextVersionValue };
  for (const key of Object.keys(data)) {
    if (!META_KEYS.has(key)) {
      ordered[key] = data[key];
    }
  }
  ordered[VALIDATED_KEY] = Object.fromEntries(
    Object.keys(validation)
      .filter((key) => Object.hasOwn(data, key) && !META_KEYS.has(key))
      .sort((a, b) => a.localeCompare(b))
      .map((key) => [key, validation[key]]),
  );
  return ordered;
}

function validateEntryShape(entries) {
  if (!entries || typeof entries !== "object" || Array.isArray(entries)) {
    throw new Error("--entries must be a JSON object");
  }
  for (const [key, value] of Object.entries(entries)) {
    if (!key || META_KEYS.has(key)) {
      throw new Error(`Invalid locale key: ${key}`);
    }
    if (typeof value !== "string") {
      throw new Error(`Entry ${key} must be a string`);
    }
  }
}

function validateTranslations(parsed, entries, agentOutput) {
  const errors = [];
  const entryKeys = Object.keys(entries);
  for (const file of parsed) {
    if (file.locale === "en") {
      continue;
    }
    const translations = agentOutput.translations[file.locale] ?? {};
    for (const key of entryKeys) {
      const translated = translations[key];
      if (typeof translated !== "string" || translated.length === 0) {
        errors.push(`${file.locale}: missing translation for ${key}`);
        continue;
      }
      if (!samePlaceholders(entries[key], translated)) {
        errors.push(`${file.locale}: placeholder mismatch for ${key}`);
      }
    }
  }
  return errors;
}

function apply(dir, entriesArg, agentOutputArg, recheckValue) {
  const recheck = parseRecheck(recheckValue);
  const entries = readJsonArg(entriesArg, "--entries");
  validateEntryShape(entries);
  const agentOutput = normalizeAgentOutput(readJsonArg(agentOutputArg, "--agent-output"));
  const { dir: resolvedDir, files } = readLocaleFiles(dir);
  const parsed = files.map(parseLocale);
  const duplicateKeys = parsed.flatMap((file) => file.duplicates.map((duplicate) => ({
    file: file.fileName,
    locale: file.locale,
    key: duplicate.key,
    firstLine: duplicate.firstLine,
    duplicateLine: duplicate.duplicateLine,
  })));
  const jsonErrors = parsed
    .filter((file) => file.jsonError)
    .map((file) => ({ file: file.fileName, locale: file.locale, error: file.jsonError }));

  if (duplicateKeys.length > 0 || jsonErrors.length > 0) {
    return {
      ok: false,
      dir: resolvedDir,
      duplicateKeys,
      jsonErrors,
      message: "Stop before writing. Fix duplicate keys or unsafe JSON first.",
    };
  }

  const translationErrors = validateTranslations(parsed, entries, agentOutput);
  if (translationErrors.length > 0) {
    return {
      ok: false,
      dir: resolvedDir,
      translationErrors,
      message: "Stop before writing. Missing or unsafe translations.",
    };
  }

  const nextVersionValue = nextVersion(parsed);
  const changedFiles = [];
  const touchedByLocale = {};

  for (const file of parsed) {
    const data = { ...file.data };
    const bodyPairs = Object.keys(data)
      .filter((key) => !META_KEYS.has(key))
      .map((key) => ({ key, value: data[key] }));
    const touched = new Set();
    const validated = new Set();

    const corrections = agentOutput.corrections[file.locale] ?? {};
    for (const [key, value] of Object.entries(corrections)) {
      if (META_KEYS.has(key)) {
        continue;
      }
      if (typeof value !== "string") {
        throw new Error(`${file.locale}: correction ${key} must be a string`);
      }
      setOrderedKey(bodyPairs, key, value);
      touched.add(key);
    }

    if (file.locale === "en") {
      for (const [key, value] of Object.entries(entries)) {
        setOrderedKey(bodyPairs, key, value);
        touched.add(key);
      }
    } else {
      const translations = agentOutput.translations[file.locale] ?? {};
      for (const [key] of Object.entries(entries)) {
        setOrderedKey(bodyPairs, key, translations[key]);
        touched.add(key);
      }
    }

    const validatedSpec = agentOutput.validated[file.locale];
    if (validatedSpec === "all") {
      for (const pair of bodyPairs) {
        validated.add(pair.key);
      }
    } else if (Array.isArray(validatedSpec)) {
      for (const key of validatedSpec) {
        validated.add(key);
      }
    }

    for (const key of recheck.keys) {
      validated.delete(key);
    }
    if (recheck.all) {
      validated.clear();
    }

    const nextData = {};
    for (const pair of bodyPairs) {
      nextData[pair.key] = pair.value;
    }
    const ordered = buildOrderedObject(nextData, nextVersionValue, touched, validated);
    fs.writeFileSync(file.filePath, `${JSON.stringify(ordered, null, 4)}\n`, "utf8");
    changedFiles.push(file.fileName);
    touchedByLocale[file.locale] = uniqueSorted([...touched]);
  }

  const zipPath = path.join(resolvedDir, `${nextVersionValue}_commonLocales.zip`);
  writeZip(zipPath, parsed.map((file) => ({
    name: file.fileName,
    data: fs.readFileSync(file.filePath),
  })));

  return {
    ok: true,
    dir: resolvedDir,
    version: nextVersionValue,
    changedFiles,
    touchedByLocale,
    zip: zipPath,
    warnings: agentOutput.warnings,
  };
}

function makeCrcTable() {
  const table = [];
  for (let n = 0; n < 256; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) {
      c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }
  return table;
}

const CRC_TABLE = makeCrcTable();

function crc32(buffer) {
  let crc = 0 ^ -1;
  for (const byte of buffer) {
    crc = (crc >>> 8) ^ CRC_TABLE[(crc ^ byte) & 0xFF];
  }
  return (crc ^ -1) >>> 0;
}

function dosDateTime(date = new Date()) {
  const year = Math.max(1980, date.getFullYear());
  const dosTime = (date.getHours() << 11) | (date.getMinutes() << 5) | Math.floor(date.getSeconds() / 2);
  const dosDate = ((year - 1980) << 9) | ((date.getMonth() + 1) << 5) | date.getDate();
  return { dosTime, dosDate };
}

function writeUInt32LE(value) {
  const buffer = Buffer.alloc(4);
  buffer.writeUInt32LE(value >>> 0, 0);
  return buffer;
}

function writeUInt16LE(value) {
  const buffer = Buffer.alloc(2);
  buffer.writeUInt16LE(value & 0xFFFF, 0);
  return buffer;
}

function writeZip(zipPath, entries) {
  const chunks = [];
  const central = [];
  let offset = 0;
  const { dosTime, dosDate } = dosDateTime();

  for (const entry of entries) {
    const nameBuffer = Buffer.from(entry.name.replace(/\\/g, "/"), "utf8");
    const data = Buffer.isBuffer(entry.data) ? entry.data : Buffer.from(entry.data);
    const crc = crc32(data);

    const local = Buffer.concat([
      writeUInt32LE(0x04034b50),
      writeUInt16LE(20),
      writeUInt16LE(0x0800),
      writeUInt16LE(0),
      writeUInt16LE(dosTime),
      writeUInt16LE(dosDate),
      writeUInt32LE(crc),
      writeUInt32LE(data.length),
      writeUInt32LE(data.length),
      writeUInt16LE(nameBuffer.length),
      writeUInt16LE(0),
      nameBuffer,
      data,
    ]);
    chunks.push(local);

    const centralHeader = Buffer.concat([
      writeUInt32LE(0x02014b50),
      writeUInt16LE(20),
      writeUInt16LE(20),
      writeUInt16LE(0x0800),
      writeUInt16LE(0),
      writeUInt16LE(dosTime),
      writeUInt16LE(dosDate),
      writeUInt32LE(crc),
      writeUInt32LE(data.length),
      writeUInt32LE(data.length),
      writeUInt16LE(nameBuffer.length),
      writeUInt16LE(0),
      writeUInt16LE(0),
      writeUInt16LE(0),
      writeUInt16LE(0),
      writeUInt32LE(0),
      writeUInt32LE(offset),
      nameBuffer,
    ]);
    central.push(centralHeader);
    offset += local.length;
  }

  const centralStart = offset;
  const centralBuffer = Buffer.concat(central);
  const end = Buffer.concat([
    writeUInt32LE(0x06054b50),
    writeUInt16LE(0),
    writeUInt16LE(0),
    writeUInt16LE(entries.length),
    writeUInt16LE(entries.length),
    writeUInt32LE(centralBuffer.length),
    writeUInt32LE(centralStart),
    writeUInt16LE(0),
  ]);

  fs.writeFileSync(zipPath, Buffer.concat([...chunks, centralBuffer, end]));
}

async function main() {
  const [command, ...rest] = process.argv.slice(2);
  const args = parseArgs(rest);

  if (!command || !["inspect", "apply"].includes(command)) {
    usage();
    process.exit(2);
  }

  try {
    const result = command === "inspect"
      ? inspect(args.dir, args.recheck)
      : apply(args.dir, args.entries, args["agent-output"], args.recheck);
    console.log(JSON.stringify(result, null, 2));
    if (!result.ok) {
      process.exit(1);
    }
  } catch (error) {
    console.error(JSON.stringify({ ok: false, error: error.message }, null, 2));
    process.exit(1);
  }
}

const isCli = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (isCli) {
  main();
}

export { apply, inspect, scanDuplicateKeys, safeRepairJsonText, writeZip };
