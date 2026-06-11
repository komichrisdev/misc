import fs from "node:fs/promises";
import path from "node:path";

const artifactToolSpecifier =
  process.env.REWARD_WARDEN_ARTIFACT_TOOL_SPECIFIER ?? "@oai/artifact-tool";
const { Workbook, SpreadsheetFile } = await import(artifactToolSpecifier);

const SHORT_HEADERS = [
  ["tt2_file", "TT2 File"],
  ["tt2_path", "TT2 Path"],
  ["tt2_reward", "TT2 Reward"],
  ["tt2_amt", "TT2 Amt"],
  ["tt2_coins", "TT2 Coins"],
  ["tt2_usd", "TT2 $"],
  ["st3_file", "ST3 File"],
  ["st3_path", "ST3 Path"],
  ["st3_reward", "ST3 Reward"],
  ["st3_amt", "ST3 Amt"],
  ["st3_coins", "ST3 Coins"],
  ["st3_usd", "ST3 $"],
  ["s_reward", "Sug Reward"],
  ["s_amt", "Sug Amt"],
  ["s_coins", "Sug Coins"],
  ["s_usd", "Sug $"],
  ["s_basis", "Suggestion Basis"],
];

const DESIRED_TABS = [
  "config",
  "dailyQuest",
  "appleSE",
  "camp_config",
  "consecutive_challenge",
  "levels_race",
  "light_rush",
  "public_cup",
  "ToC",
  "LO",
  "misc",
];

const FILE_COLORS = [
  "#FDE68A",
  "#BFDBFE",
  "#FBCFE8",
  "#C7D2FE",
  "#BBF7D0",
  "#FED7AA",
  "#DDD6FE",
  "#A7F3D0",
  "#F9A8D4",
  "#93C5FD",
  "#86EFAC",
  "#FCA5A5",
  "#FDBA74",
  "#E9D5FF",
  "#67E8F9",
  "#FCD34D",
  "#C4B5FD",
  "#7DD3FC",
  "#6EE7B7",
  "#FDA4AF",
];

function columnLabel(index) {
  let value = index + 1;
  let label = "";
  while (value > 0) {
    const remainder = (value - 1) % 26;
    label = String.fromCharCode(65 + remainder) + label;
    value = Math.floor((value - 1) / 26);
  }
  return label;
}

function tsvRowToObject(header, line) {
  if (!line) {
    return Object.fromEntries(header.map((key) => [key, ""]));
  }
  const values = line.split("\t");
  return Object.fromEntries(header.map((key, index) => [key, values[index] ?? ""]));
}

async function loadTabRows(tabsDir, tabName) {
  const filePath = path.join(tabsDir, `${tabName}.txt`);
  try {
    const text = await fs.readFile(filePath, "utf8");
    const lines = text.replace(/\r/g, "").split("\n");
    const header = lines[0]?.split("\t") ?? [];
    const dataLines = lines.slice(1).filter((_, index, array) => !(index === array.length - 1 && array[index] === ""));
    return dataLines.map((line) => tsvRowToObject(header, line));
  } catch (error) {
    if (error && error.code === "ENOENT") {
      return [];
    }
    throw error;
  }
}

function buildColorMap(allRows) {
  const seen = [];
  for (const row of allRows) {
    for (const key of ["tt2_file", "st3_file"]) {
      const value = row[key];
      if (value && !seen.includes(value)) {
        seen.push(value);
      }
    }
  }
  return Object.fromEntries(seen.map((name, index) => [name, FILE_COLORS[index % FILE_COLORS.length]]));
}

function headerValues() {
  return SHORT_HEADERS.map(([, label]) => label);
}

function rowValues(row) {
  return SHORT_HEADERS.map(([key]) => row[key] ?? "");
}

function applyColumnWidths(sheet, rowCount) {
  const widths = [130, 100, 110, 75, 85, 70, 130, 100, 110, 75, 85, 70, 110, 75, 85, 70, 420];
  for (let index = 0; index < widths.length; index += 1) {
    sheet.getRange(`${columnLabel(index)}1:${columnLabel(index)}${Math.max(2, rowCount)}`).format.columnWidthPx = widths[index];
  }
}

async function buildWorkbook(compareRoot, outputPath) {
  const tabsDir = path.join(compareRoot, "reward-warden-tabs");
  const workbook = Workbook.create();
  const allRows = [];
  const tabRows = {};

  for (const tabName of DESIRED_TABS) {
    const rows = await loadTabRows(tabsDir, tabName);
    tabRows[tabName] = rows;
    allRows.push(...rows);
  }

  const colorMap = buildColorMap(allRows);

  for (const [tabIndex, tabName] of DESIRED_TABS.entries()) {
    const sheet = workbook.worksheets.getOrAdd(tabName, {
      renameFirstIfOnlyNewSpreadsheet: tabIndex === 0,
    });
    sheet.reset();

    const rows = tabRows[tabName];
    const values = [headerValues(), ...rows.map((row) => rowValues(row))];
    const endCell = `${columnLabel(SHORT_HEADERS.length - 1)}${values.length}`;
    sheet.getRange(`A1:${endCell}`).values = values;

    const headerRange = sheet.getRange(`A1:${columnLabel(SHORT_HEADERS.length - 1)}1`);
    headerRange.format.fill = "#1F4E78";
    headerRange.format.font = { color: "#FFFFFF", bold: true, size: 11 };
    headerRange.format.wrapText = true;
    headerRange.format.horizontalAlignment = "center";
    headerRange.format.verticalAlignment = "center";
    headerRange.format.rowHeightPx = 34;

    if (rows.length > 0) {
      const bodyRange = sheet.getRange(`A2:${columnLabel(SHORT_HEADERS.length - 1)}${values.length}`);
      bodyRange.format.wrapText = true;
      bodyRange.format.verticalAlignment = "center";
      bodyRange.format.borders = { preset: "outside", style: "thin", color: "#D1D5DB" };
    }

    for (let rowIndex = 0; rowIndex < rows.length; rowIndex += 1) {
      const workbookRow = rowIndex + 2;
      const row = rows[rowIndex];
      const isBlank = SHORT_HEADERS.every(([key]) => !(row[key] ?? ""));
      if (isBlank) {
        sheet.getRange(`A${workbookRow}:${columnLabel(SHORT_HEADERS.length - 1)}${workbookRow}`).format.rowHeightPx = 12;
        continue;
      }

      const basisCell = sheet.getRange(`Q${workbookRow}`);
      basisCell.format.fill = "#F8FAFC";

      if (row.tt2_file) {
        sheet.getRange(`A${workbookRow}`).format.fill = colorMap[row.tt2_file] ?? "#E5E7EB";
      }
      if (row.st3_file) {
        sheet.getRange(`G${workbookRow}`).format.fill = colorMap[row.st3_file] ?? "#E5E7EB";
      }
    }

    applyColumnWidths(sheet, values.length);
    sheet.freezePanes.freezeRows(1);
    sheet.showGridLines = true;
  }

  await fs.mkdir(path.dirname(outputPath), { recursive: true });
  const output = await SpreadsheetFile.exportXlsx(workbook);
  await output.save(outputPath);
  return outputPath;
}

const compareRoot = process.argv[2];
const outputPath = process.argv[3] ?? path.join(compareRoot, "reward-warden-sheet-upload.xlsx");

if (!compareRoot) {
  throw new Error("Usage: node build_reward_warden_workbook.mjs <compare-root> [output-path]");
}

const savedPath = await buildWorkbook(compareRoot, outputPath);
console.log(savedPath);
