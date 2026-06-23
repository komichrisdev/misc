import { getHarLog, getInspectedTabId, sendRuntimeMessage } from "./browserApi.js";
import { normalizeRule, normalizeRules } from "./rules.js";
import { loadPrefs, loadRules, savePrefs, saveRules } from "./storage.js";
import { readRequestJson, subscribeToNetworkRequests } from "./network.js";

const MAX_REQUEST_ENTRIES = 500;

const state = {
  rules: [],
  prefs: {
    paused: false,
    maxResults: 500
  },
  selectedRuleId: null,
  draftRule: null,
  results: [],
  requestEntries: [],
  ruleSources: {},
  fieldPickerRuleId: null,
  fieldPickerExpandedPaths: new Set(),
  requestPickerOpen: false,
  draggedFieldRowId: null,
  pendingDeleteRuleId: null,
  rulesCollapsed: false
};

const dom = {};

export async function initNetworkSnifferUi() {
  cacheDom();
  bindEvents();

  state.rules = await loadRules();
  state.prefs = await loadPrefs();
  state.selectedRuleId = null;

  renderAll();

  subscribeToNetworkRequests({
    getRules: () => state.rules,
    isPaused: () => state.prefs.paused,
    onRequestSeen: addRequestEntry,
    onMatch: addResult,
    onError: (error) => showRuleMessage(error.message || String(error), true)
  });
}

function cacheDom() {
  dom.toolsButton = document.getElementById("toolsButton");
  dom.saveLogsButton = document.getElementById("saveLogsButton");
  dom.pauseButton = document.getElementById("pauseButton");
  dom.clearButton = document.getElementById("clearButton");
  dom.exportRulesButton = document.getElementById("exportRulesButton");
  dom.importRulesButton = document.getElementById("importRulesButton");
  dom.importRulesInput = document.getElementById("importRulesInput");
  dom.appShell = document.querySelector(".app-shell");
  dom.captureStatus = document.getElementById("captureStatus");
  dom.ruleList = document.getElementById("ruleList");
  dom.ruleFormHint = document.getElementById("ruleFormHint");
  dom.rulesCollapseButton = document.getElementById("rulesCollapseButton");
  dom.addRuleButton = document.getElementById("addRuleButton");
  dom.deleteRuleButton = document.getElementById("deleteRuleButton");
  dom.ruleForm = document.getElementById("ruleForm");
  dom.ruleNameInput = document.getElementById("ruleNameInput");
  dom.ruleEnabledInput = document.getElementById("ruleEnabledInput");
  dom.ruleMethodInput = document.getElementById("ruleMethodInput");
  dom.ruleMatchTypeInput = document.getElementById("ruleMatchTypeInput");
  dom.ruleMatchValueInput = document.getElementById("ruleMatchValueInput");
  dom.fieldsEditor = document.getElementById("fieldsEditor");
  dom.addFieldButton = document.getElementById("addFieldButton");
  dom.ruleMessage = document.getElementById("ruleMessage");
  dom.resultsBody = document.getElementById("resultsBody");
  dom.requestPickerModal = document.getElementById("requestPickerModal");
  dom.closeRequestPickerButton = document.getElementById("closeRequestPickerButton");
  dom.requestSearchInput = document.getElementById("requestSearchInput");
  dom.requestPickerList = document.getElementById("requestPickerList");
  dom.fieldPickerModal = document.getElementById("fieldPickerModal");
  dom.closeFieldPickerButton = document.getElementById("closeFieldPickerButton");
  dom.fieldPickerSubtitle = document.getElementById("fieldPickerSubtitle");
  dom.fieldPickerSearchInput = document.getElementById("fieldPickerSearchInput");
  dom.fieldPickerStatus = document.getElementById("fieldPickerStatus");
  dom.fieldPickerTree = document.getElementById("fieldPickerTree");
  dom.helpPopover = document.getElementById("helpPopover");
}

function bindEvents() {
  dom.toolsButton.addEventListener("click", () => {
    window.open("https://tools.qublixaws.com/", "_blank", "noopener");
  });

  dom.saveLogsButton.addEventListener("click", () => {
    saveLogsBundle().catch((error) => {
      showRuleMessage(error.message || String(error), true);
      dom.saveLogsButton.disabled = false;
      dom.saveLogsButton.textContent = "Save Logs";
    });
  });

  dom.pauseButton.addEventListener("click", async () => {
    state.prefs.paused = !state.prefs.paused;
    await persistPrefs();
    renderCaptureState();
  });

  dom.clearButton.addEventListener("click", () => {
    state.results = [];
    renderResults();
  });

  dom.exportRulesButton.addEventListener("click", exportRules);
  dom.importRulesButton.addEventListener("click", () => dom.importRulesInput.click());
  dom.importRulesInput.addEventListener("change", importRules);
  dom.rulesCollapseButton.addEventListener("click", toggleRulesColumn);

  dom.addRuleButton.addEventListener("click", openRequestPicker);

  dom.deleteRuleButton.addEventListener("click", async () => {
    const currentRuleId = state.selectedRuleId;
    if (!currentRuleId) {
      return;
    }

    if (state.pendingDeleteRuleId !== currentRuleId) {
      state.pendingDeleteRuleId = currentRuleId;
      dom.deleteRuleButton.textContent = "Confirm";
      showRuleMessage("Click Confirm to delete rule.", true);
      return;
    }

    const hadSavedRule = state.rules.some((rule) => rule.id === currentRuleId);
    state.rules = state.rules.filter((rule) => rule.id !== currentRuleId);
    state.pendingDeleteRuleId = null;
    state.selectedRuleId = null;
    state.draftRule = null;
    closeFieldPicker();

    if (hadSavedRule) {
      await persistRules();
      showRuleMessage("Rule deleted.", false);
    } else {
      showRuleMessage("Draft discarded.", false);
    }

    renderAll();
  });

  dom.ruleForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await saveSelectedRuleFromForm();
  });

  dom.closeRequestPickerButton.addEventListener("click", closeRequestPicker);
  dom.requestPickerModal.addEventListener("click", (event) => {
    if (event.target === dom.requestPickerModal) {
      closeRequestPicker();
    }
  });
  dom.requestSearchInput.addEventListener("input", renderRequestPickerList);
  dom.addFieldButton.addEventListener("click", openFieldPickerForSelectedRule);
  dom.closeFieldPickerButton.addEventListener("click", closeFieldPicker);
  dom.fieldPickerModal.addEventListener("click", (event) => {
    if (event.target === dom.fieldPickerModal) {
      closeFieldPicker();
    }
  });
  dom.fieldPickerSearchInput.addEventListener("input", refreshFieldPicker);
  for (const button of document.querySelectorAll(".info-button")) {
    button.addEventListener("mouseenter", () => showHelpPopover(button));
    button.addEventListener("mouseleave", hideHelpPopover);
    button.addEventListener("focus", () => showHelpPopover(button));
    button.addEventListener("blur", hideHelpPopover);
  }
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !dom.fieldPickerModal.classList.contains("hidden")) {
      closeFieldPicker();
    }
    if (event.key === "Escape" && !dom.requestPickerModal.classList.contains("hidden")) {
      closeRequestPicker();
    }
    if (event.key === "Escape") {
      hideHelpPopover();
    }
  });
  window.addEventListener("scroll", hideHelpPopover, true);
  window.addEventListener("resize", hideHelpPopover);
  dom.fieldsEditor.addEventListener("input", () => {
    refreshFieldPicker();
  });
}

function renderAll() {
  renderCaptureState();
  renderRulesColumn();
  renderRuleList();
  renderRuleForm();
  renderResults();
}

function renderCaptureState() {
  dom.saveLogsButton.disabled = false;
  dom.pauseButton.textContent = state.prefs.paused ? "Resume" : "Pause";
  dom.captureStatus.textContent = state.prefs.paused ? "Paused" : "Running";
  dom.captureStatus.classList.toggle("paused", state.prefs.paused);
}

function renderRulesColumn() {
  dom.appShell.classList.toggle("rules-collapsed", state.rulesCollapsed);
  dom.rulesCollapseButton.textContent = state.rulesCollapsed ? "›" : "‹";
  dom.rulesCollapseButton.setAttribute(
    "aria-label",
    state.rulesCollapsed ? "Expand rules column" : "Compress rules column"
  );
}

function renderRuleList() {
  dom.ruleList.textContent = "";

  if (!state.rules.length) {
    const empty = document.createElement("p");
    empty.className = "message";
    empty.textContent = "No rules yet.";
    dom.ruleList.append(empty);
    return;
  }

  for (const rule of state.rules) {
    const item = document.createElement("div");
    item.className = "rule-item";
    item.classList.toggle("selected", rule.id === state.selectedRuleId);

    const summary = document.createElement("div");
    summary.className = "rule-summary";

    const name = document.createElement("span");
    name.className = "rule-item-name";
    name.textContent = rule.name;

    const controls = document.createElement("div");
    controls.className = "rule-controls";

    const editButton = document.createElement("button");
    editButton.type = "button";
    editButton.className = `rule-edit-button${rule.id === state.selectedRuleId ? " active" : ""}`;
    editButton.textContent = rule.id === state.selectedRuleId ? "Close" : "Edit";
    editButton.addEventListener("click", () => {
      toggleRuleEditor(rule);
    });

    const toggle = document.createElement("button");
    toggle.type = "button";
    toggle.className = `enabled-pill enabled-toggle${rule.enabled ? "" : " off"}`;
    toggle.textContent = rule.enabled ? "On" : "Off";
    toggle.addEventListener("click", async (event) => {
      event.stopPropagation();
      await toggleRuleEnabled(rule.id);
    });

    const meta = document.createElement("span");
    meta.className = "rule-item-meta";
    meta.textContent = `${rule.method} ${rule.matchType}: ${rule.matchValue || "(empty)"}`;

    controls.append(editButton, toggle);
    summary.append(name, meta);
    item.append(summary, controls);
    dom.ruleList.append(item);
  }
}

function renderRuleForm() {
  const rule = getEditableRule();
  const hasRule = Boolean(rule);
  const saveButton = dom.ruleForm.querySelector("[type='submit']");
  const isDeleteConfirm = hasRule && state.pendingDeleteRuleId === rule.id;
  const showRuleEditor = hasRule && !state.rulesCollapsed;

  dom.ruleForm.hidden = !showRuleEditor;
  dom.ruleFormHint.hidden = showRuleEditor || state.rulesCollapsed;

  dom.deleteRuleButton.disabled = !hasRule;
  dom.deleteRuleButton.textContent = isDeleteConfirm ? "Confirm" : "Delete";
  dom.ruleNameInput.disabled = !hasRule;
  dom.ruleEnabledInput.disabled = !hasRule;
  dom.ruleMethodInput.disabled = !hasRule;
  dom.ruleMatchTypeInput.disabled = !hasRule;
  dom.ruleMatchValueInput.disabled = !hasRule;
  dom.addFieldButton.disabled = !hasRule;
  saveButton.disabled = !hasRule;
  dom.fieldsEditor.textContent = "";

  if (!hasRule) {
    dom.ruleNameInput.value = "";
    dom.ruleEnabledInput.checked = false;
    dom.ruleMethodInput.value = "ANY";
    dom.ruleMatchTypeInput.value = "file";
    dom.ruleMatchValueInput.value = "";
    return;
  }

  dom.ruleNameInput.value = rule.name;
  dom.ruleEnabledInput.checked = rule.enabled;
  dom.ruleMethodInput.value = rule.method;
  dom.ruleMatchTypeInput.value = rule.matchType;
  dom.ruleMatchValueInput.value = rule.matchValue;

  for (const field of rule.fields) {
    addFieldRow(field);
  }

  if (!rule.fields.length) {
    addFieldRow({ label: "", path: "" });
  }
}

function addFieldRow(field) {
  const row = document.createElement("div");
  row.className = "field-row";
  row.draggable = true;
  row.dataset.fieldRowId = createFieldRowId();

  const dragHandle = document.createElement("button");
  dragHandle.type = "button";
  dragHandle.className = "field-drag-handle";
  dragHandle.setAttribute("aria-label", "Drag to reorder field");
  dragHandle.tabIndex = -1;

  const dragDots = document.createElement("span");
  dragDots.className = "field-drag-dots";
  for (let index = 0; index < 6; index += 1) {
    const dot = document.createElement("span");
    dot.className = "field-drag-dot";
    dragDots.append(dot);
  }
  dragHandle.append(dragDots);

  const labelWrap = document.createElement("label");
  const labelText = document.createElement("span");
  labelText.textContent = "Label";
  const labelInput = document.createElement("input");
  labelInput.className = "field-label-input";
  labelInput.type = "text";
  labelInput.value = field.label || "";
  labelWrap.append(labelText, labelInput);

  const pathWrap = document.createElement("label");
  const pathText = document.createElement("span");
  pathText.textContent = "JSON path";
  const pathInput = document.createElement("input");
  pathInput.className = "field-path-input";
  pathInput.type = "text";
  pathInput.value = field.path || "";
  pathWrap.append(pathText, pathInput);

  const removeButton = document.createElement("button");
  removeButton.type = "button";
  removeButton.textContent = "Remove";
  removeButton.addEventListener("click", () => {
    row.remove();
    refreshFieldPicker();
  });

  row.addEventListener("dragstart", (event) => {
    state.draggedFieldRowId = row.dataset.fieldRowId;
    row.classList.add("dragging");
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", row.dataset.fieldRowId);
    }
  });

  row.addEventListener("dragend", () => {
    state.draggedFieldRowId = null;
    row.classList.remove("dragging");
    clearFieldDropMarkers();
  });

  row.addEventListener("dragover", (event) => {
    event.preventDefault();
    const draggedRow = getDraggedFieldRow();
    if (!draggedRow || draggedRow === row) {
      return;
    }

    const bounds = row.getBoundingClientRect();
    const placeAfter = event.clientY > bounds.top + bounds.height / 2;
    row.classList.toggle("drop-before", !placeAfter);
    row.classList.toggle("drop-after", placeAfter);
  });

  row.addEventListener("dragleave", () => {
    row.classList.remove("drop-before", "drop-after");
  });

  row.addEventListener("drop", (event) => {
    event.preventDefault();
    const draggedRow = getDraggedFieldRow();
    if (!draggedRow || draggedRow === row) {
      return;
    }

    const bounds = row.getBoundingClientRect();
    const placeAfter = event.clientY > bounds.top + bounds.height / 2;
    if (placeAfter) {
      row.after(draggedRow);
    } else {
      row.before(draggedRow);
    }

    clearFieldDropMarkers();
    refreshFieldPicker();
  });

  row.append(dragHandle, labelWrap, pathWrap, removeButton);
  dom.fieldsEditor.append(row);
}

async function openFieldPickerForSelectedRule() {
  const existing = getEditableRule();
  if (!existing) {
    return;
  }

  syncDraftRuleFromForm();
  const rule = getEditableRule();
  if (!rule) {
    return;
  }

  if (!rule.matchValue) {
    showRuleMessage("Add a match value first.", true);
    return;
  }

  const source = state.ruleSources[rule.id];
  if (!source) {
    showRuleMessage("No matched response yet for this rule. Capture one first.", true);
    return;
  }

  state.fieldPickerRuleId = rule.id;
  dom.fieldPickerModal.classList.remove("hidden");
  dom.fieldPickerModal.setAttribute("aria-hidden", "false");
  refreshFieldPicker();
}

async function saveSelectedRuleFromForm() {
  const existing = getEditableRule();
  if (!existing) {
    return;
  }

  syncDraftRuleFromForm();
  const nextRule = normalizeRule(state.draftRule);
  const existingIndex = state.rules.findIndex((rule) => rule.id === nextRule.id);

  if (existingIndex === -1) {
    state.rules = [...state.rules, nextRule];
  } else {
    state.rules = state.rules.map((rule) => (rule.id === nextRule.id ? nextRule : rule));
  }

  state.draftRule = null;
  state.selectedRuleId = null;
  state.pendingDeleteRuleId = null;
  await persistRules();
  closeFieldPicker();
  showRuleMessage("Rule saved.", false);
  renderRuleList();
  renderRuleForm();
}

function renderResults() {
  dom.resultsBody.textContent = "";
  const recentResultIds = getRecentResultIdsByActiveRule();

  if (!state.results.length) {
    const row = document.createElement("tr");
    row.className = "empty-row";
    const cell = document.createElement("td");
    cell.colSpan = 3;
    cell.textContent = "No matches captured yet.";
    row.append(cell);
    dom.resultsBody.append(row);
    return;
  }

  for (const result of state.results) {
    const row = document.createElement("tr");
    row.classList.toggle("recent-rule-match", recentResultIds.has(result.id));

    appendCell(row, formatTimestamp(result.timestamp), "timestamp-cell");
    row.append(createMoreCell(result));

    const valuesCell = document.createElement("td");
    valuesCell.className = "values-cell";
    valuesCell.append(renderExtractedValues(result));
    row.append(valuesCell);
    dom.resultsBody.append(row);
  }
}

function getRecentResultIdsByActiveRule() {
  const activeRuleIds = new Set(state.rules.filter((rule) => rule.enabled).map((rule) => rule.id));
  const seenRuleIds = new Set();
  const resultIds = new Set();

  for (const result of state.results) {
    if (!result || !activeRuleIds.has(result.ruleId) || seenRuleIds.has(result.ruleId)) {
      continue;
    }

    seenRuleIds.add(result.ruleId);
    resultIds.add(result.id);
  }

  return resultIds;
}

function addRequestEntry(entry) {
  if (!entry || !entry.id) {
    return;
  }

  state.requestEntries = [entry, ...state.requestEntries.filter((item) => item.id !== entry.id)];
  if (state.requestEntries.length > MAX_REQUEST_ENTRIES) {
    state.requestEntries = state.requestEntries.slice(0, MAX_REQUEST_ENTRIES);
  }

  if (state.requestPickerOpen) {
    renderRequestPickerList();
  }
}

function openRequestPicker() {
  state.requestPickerOpen = true;
  state.pendingDeleteRuleId = null;
  dom.requestSearchInput.value = "";
  dom.requestPickerModal.classList.remove("hidden");
  dom.requestPickerModal.setAttribute("aria-hidden", "false");
  hideHelpPopover();
  renderRequestPickerList();
  dom.requestSearchInput.focus();
}

function closeRequestPicker() {
  state.requestPickerOpen = false;
  dom.requestPickerModal.classList.add("hidden");
  dom.requestPickerModal.setAttribute("aria-hidden", "true");
}

function renderRequestPickerList() {
  dom.requestPickerList.textContent = "";

  const query = dom.requestSearchInput.value.trim().toLowerCase();
  const entries = state.requestEntries
    .filter(isRequestEntryDisplayable)
    .filter((entry) => matchesRequestSearch(entry, query));

  if (!entries.length) {
    const empty = document.createElement("div");
    empty.className = "traffic-empty";
    empty.textContent = query
      ? "No calls match this search yet."
      : "No parseable calls yet. Images, audio, and other binary files stay hidden.";
    dom.requestPickerList.append(empty);
    return;
  }

  for (const entry of entries) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "traffic-item";
    button.addEventListener("click", () => {
      selectRequestEntry(entry).catch((error) => {
        showRuleMessage(error.message || String(error), true);
      });
    });

    const head = document.createElement("span");
    head.className = "traffic-item-head";

    const badges = document.createElement("span");
    badges.className = "traffic-badges";

    const method = document.createElement("span");
    method.className = "traffic-method";
    method.textContent = entry.method || "ANY";

    const status = document.createElement("span");
    status.className = "traffic-status";
    status.textContent = entry.status ?? "n/a";

    const time = document.createElement("span");
    time.className = "traffic-time";
    time.textContent = formatTimestamp(entry.timestamp);

    const file = document.createElement("span");
    file.className = "traffic-file";
    file.textContent = entry.file || deriveRequestLabel(entry.url) || "(no file)";

    const url = document.createElement("span");
    url.className = "traffic-url";
    url.textContent = entry.url || "";

    badges.append(method, status);
    head.append(badges, time);
    button.append(head, file, url);
    dom.requestPickerList.append(button);
  }
}

function matchesRequestSearch(entry, query) {
  if (!query) {
    return true;
  }

  const haystack = [
    entry.method,
    entry.status,
    entry.file,
    entry.url,
    entry.mimeType
  ]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();

  return haystack.includes(query);
}

function isRequestEntryDisplayable(entry) {
  const mimeType = String(entry && entry.mimeType || "").toLowerCase();
  const url = String(entry && entry.url || "").toLowerCase();

  if (
    mimeType.startsWith("image/") ||
    mimeType.startsWith("audio/") ||
    mimeType.startsWith("video/") ||
    mimeType.startsWith("font/")
  ) {
    return false;
  }

  if (
    mimeType.includes("octet-stream") ||
    mimeType.includes("zip") ||
    mimeType.includes("pdf") ||
    mimeType.includes("protobuf") ||
    mimeType.includes("wasm")
  ) {
    return false;
  }

  return !/\.(?:png|jpe?g|gif|webp|bmp|svg|ico|mp3|wav|ogg|aac|m4a|flac|mp4|webm|mov|avi|woff2?|ttf|otf|eot|pdf|zip|7z|rar|bin|wasm)(?:[?#]|$)/.test(url);
}

async function selectRequestEntry(entry) {
  if (!entry) {
    return;
  }

  const draftRule = buildDraftRuleFromRequest(entry);
  state.pendingDeleteRuleId = null;
  state.rulesCollapsed = false;
  state.selectedRuleId = draftRule.id;
  state.draftRule = draftRule;

  closeFieldPicker();
  closeRequestPicker();
  renderRulesColumn();
  renderRuleList();
  renderRuleForm();

  try {
    state.ruleSources[draftRule.id] = await readRequestJson(entry.request);
  } catch (error) {
    showRuleMessage(error.message || String(error), true);
    dom.ruleNameInput.focus();
    dom.ruleNameInput.select();
    return;
  }

  state.fieldPickerRuleId = draftRule.id;
  dom.fieldPickerModal.classList.remove("hidden");
  dom.fieldPickerModal.setAttribute("aria-hidden", "false");
  refreshFieldPicker();
  showRuleMessage("Rule draft ready. Add fields, then save.", false);
  dom.ruleNameInput.focus();
  dom.ruleNameInput.select();
}

function renderExtractedValues(result) {
  const wrap = document.createElement("div");
  wrap.className = "value-list";

  if (result.parseError) {
    const error = document.createElement("span");
    error.className = "error-text";
    error.textContent = result.parseError;
    wrap.append(error);
    return wrap;
  }

  const entries = Object.entries(result.extracted || {});

  if (!entries.length) {
    const empty = document.createElement("span");
    empty.className = "value-label";
    empty.textContent = "No fields configured.";
    wrap.append(empty);
    return wrap;
  }

  for (const [label, value] of entries) {
    const isMissing = Array.isArray(result.missingLabels) && result.missingLabels.includes(label);
    const item = document.createElement("div");
    item.className = "value-item";

    const labelEl = document.createElement("span");
    labelEl.className = "value-label";
    labelEl.textContent = label;

    const valueEl = document.createElement("div");
    valueEl.className = "value-content";
    if (isMissing) {
      valueEl.classList.add("missing");
      valueEl.textContent = "Missing";
    } else {
      valueEl.append(renderExtractedValueContent(value));
    }

    const copyButton = document.createElement("button");
    copyButton.type = "button";
    copyButton.className = "copy-value-button";
    copyButton.textContent = "Copy";
    copyButton.disabled = isMissing;
    copyButton.addEventListener("click", () => copyText(stringifyValue(value)));

    item.append(labelEl, valueEl, copyButton);
    wrap.append(item);
  }

  return wrap;
}

function renderExtractedValueContent(value) {
  if (shouldCollapseValue(value)) {
    return renderCollapsibleValue(value);
  }

  if (value === null || typeof value !== "object") {
    return renderTokenValue(value);
  }

  const wrap = document.createElement("div");
  wrap.className = "nested-value-list";

  for (const [childKey, childValue] of getValueDisplayEntries(value)) {
    const child = document.createElement("div");
    child.className = "nested-value-item";

    const childLabel = document.createElement("span");
    childLabel.className = "nested-value-label";
    childLabel.textContent = childKey;

    const childContent = document.createElement("div");
    childContent.className = "nested-value-content";
    childContent.append(renderExtractedValueContent(childValue));

    child.append(childLabel, childContent);
    wrap.append(child);
  }

  if (!wrap.childElementCount) {
    const empty = document.createElement("pre");
    empty.className = "value-text";
    empty.append(createJsonToken("json-punctuation", Array.isArray(value) ? "[]" : "{}"));
    return empty;
  }

  return wrap;
}

function renderCollapsibleValue(value) {
  const details = document.createElement("details");
  details.className = "value-details";
  details.addEventListener("click", (event) => {
    if (event.target.closest("summary")) {
      return;
    }

    details.open = !details.open;
  });

  const summary = document.createElement("summary");
  summary.className = "value-summary";
  summary.append(renderSummaryValue(value));

  const body = document.createElement("div");
  body.className = "value-expanded";

  if (value !== null && typeof value === "object") {
    const children = document.createElement("div");
    children.className = "nested-value-list";

    for (const [childKey, childValue] of getValueDisplayEntries(value)) {
      const child = document.createElement("div");
      child.className = "nested-value-item waterfall-item";

      const childLabel = document.createElement("span");
      childLabel.className = "nested-value-label waterfall-label";
      childLabel.textContent = childKey;

      const childContent = document.createElement("div");
      childContent.className = "nested-value-content waterfall-content";
      childContent.append(renderExtractedValueContent(childValue));

      child.append(childLabel, childContent);
      children.append(child);
    }

    body.append(children);
  } else {
    body.append(renderTokenValue(value));
  }

  details.append(summary, body);
  return details;
}

function createMoreCell(result) {
  const cell = document.createElement("td");
  cell.className = "more-cell";

  const details = document.createElement("details");
  details.className = "more-details";

  const summary = document.createElement("summary");
  summary.className = "more-summary-button";
  summary.textContent = "Show";

  const ruleRow = document.createElement("div");
  ruleRow.className = "more-row";
  ruleRow.append(createMoreLabel("Rule"), createMoreValue(result.ruleName || result.ruleId || ""));

  const fileRow = document.createElement("div");
  fileRow.className = "more-row";
  fileRow.append(createMoreLabel("File"), createMoreValue(result.file || ""));

  const methodRow = document.createElement("div");
  methodRow.className = "more-row";
  methodRow.append(createMoreLabel("Method"), createMoreValue(result.method || ""));

  const urlRow = document.createElement("div");
  urlRow.className = "more-row";
  const urlValue = createMoreValue(result.url || "");
  urlValue.classList.add("url-cell");
  urlRow.append(createMoreLabel("URL"), urlValue);

  details.append(summary, ruleRow, fileRow, methodRow, urlRow);
  cell.append(details);
  return cell;
}

function createMoreLabel(text) {
  const label = document.createElement("span");
  label.className = "more-label";
  label.textContent = text;
  return label;
}

function createMoreValue(text) {
  const value = document.createElement("span");
  value.className = "more-value";
  value.textContent = text;
  return value;
}

function appendCell(row, text, className = "") {
  const cell = document.createElement("td");
  cell.textContent = text;
  if (className) {
    cell.className = className;
  }
  row.append(cell);
  return cell;
}

function addResult(result) {
  if (result && result.ruleId && result.sourceJson && !result.parseError) {
    state.ruleSources[result.ruleId] = result.sourceJson;
  }

  const { sourceJson, ...displayResult } = result;
  state.results = [displayResult, ...state.results];
  trimResults();
  renderResults();
}

function trimResults() {
  const maxResults = state.prefs.maxResults || 500;
  if (state.results.length > maxResults) {
    state.results = state.results.slice(0, maxResults);
  }
}

async function persistRules() {
  state.rules = normalizeRules(state.rules);
  await saveRules(state.rules);
}

async function toggleRuleEnabled(ruleId) {
  state.pendingDeleteRuleId = null;
  state.rules = state.rules.map((rule) => (
    rule.id === ruleId ? { ...rule, enabled: !rule.enabled } : rule
  ));
  if (state.draftRule && state.draftRule.id === ruleId) {
    const savedRule = state.rules.find((rule) => rule.id === ruleId);
    if (savedRule) {
      state.draftRule = normalizeRule({
        ...state.draftRule,
        enabled: savedRule.enabled
      });
    }
  }
  await persistRules();
  renderRuleList();
  renderRuleForm();
}

function toggleRuleEditor(rule) {
  if (!rule) {
    return;
  }

  state.pendingDeleteRuleId = null;

  if (state.selectedRuleId === rule.id) {
    state.selectedRuleId = null;
    state.draftRule = null;
    closeFieldPicker();
    renderRuleList();
    renderRuleForm();
    return;
  }

  state.selectedRuleId = rule.id;
  state.draftRule = cloneRule(rule);
  state.rulesCollapsed = false;
  closeFieldPicker();
  renderRulesColumn();
  renderRuleList();
  renderRuleForm();

  if (state.selectedRuleId === rule.id) {
    dom.ruleNameInput.focus();
    dom.ruleNameInput.select();
  }
}

async function persistPrefs() {
  await savePrefs(state.prefs);
}

function toggleRulesColumn() {
  state.rulesCollapsed = !state.rulesCollapsed;
  if (state.rulesCollapsed) {
    closeFieldPicker();
  }
  hideHelpPopover();
  renderRulesColumn();
  renderRuleForm();
}

function showHelpPopover(button) {
  const helpText = button.dataset.help;
  if (!helpText) {
    return;
  }

  dom.helpPopover.textContent = helpText;
  dom.helpPopover.classList.remove("hidden");
  dom.helpPopover.setAttribute("aria-hidden", "false");

  const rect = button.getBoundingClientRect();
  const popoverRect = dom.helpPopover.getBoundingClientRect();
  const top = Math.min(window.innerHeight - popoverRect.height - 12, rect.bottom + 10);
  const left = Math.min(window.innerWidth - popoverRect.width - 12, Math.max(12, rect.left - 8));

  dom.helpPopover.style.top = `${Math.max(12, top)}px`;
  dom.helpPopover.style.left = `${left}px`;
}

function hideHelpPopover() {
  dom.helpPopover.classList.add("hidden");
  dom.helpPopover.setAttribute("aria-hidden", "true");
}

function getSelectedRule() {
  return state.rules.find((rule) => rule.id === state.selectedRuleId) || null;
}

function getEditableRule() {
  return state.draftRule;
}

function cloneRule(rule) {
  const normalized = normalizeRule(rule);
  return {
    ...normalized,
    fields: normalized.fields.map((field) => ({ ...field }))
  };
}

function syncDraftRuleFromForm() {
  if (!state.draftRule) {
    return null;
  }

  state.draftRule = normalizeRule({
    ...state.draftRule,
    name: dom.ruleNameInput.value.trim() || state.draftRule.name || "Untitled rule",
    enabled: dom.ruleEnabledInput.checked,
    method: dom.ruleMethodInput.value,
    matchType: dom.ruleMatchTypeInput.value,
    matchValue: dom.ruleMatchValueInput.value.trim(),
    fields: readFieldRowsFromForm()
  });

  return state.draftRule;
}

function readFieldRowsFromForm() {
  return Array.from(dom.fieldsEditor.querySelectorAll(".field-row"))
    .map((row) => {
      const labelInput = row.querySelector(".field-label-input");
      const pathInput = row.querySelector(".field-path-input");
      const path = pathInput ? pathInput.value.trim() : "";
      const label = labelInput ? labelInput.value.trim() : "";

      return {
        label: label || deriveFieldLabel(path),
        path
      };
    })
    .filter((field) => field.path);
}

function createRuleId() {
  return `rule_${Date.now()}_${Math.random().toString(16).slice(2)}`;
}

function showRuleMessage(message, isError) {
  dom.ruleMessage.textContent = message;
  dom.ruleMessage.classList.toggle("error", Boolean(isError));
  window.setTimeout(() => {
    if (dom.ruleMessage.textContent === message) {
      dom.ruleMessage.textContent = "";
      dom.ruleMessage.classList.remove("error");
    }
  }, 3500);
}

function formatTimestamp(timestamp) {
  try {
    return new Date(timestamp).toLocaleTimeString();
  } catch (error) {
    return timestamp || "";
  }
}

function stringifyValue(value) {
  if (typeof value === "string") {
    return value;
  }

  if (value === null) {
    return "null";
  }

  if (typeof value === "object") {
    return JSON.stringify(value, null, 2);
  }

  return String(value);
}

async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    showRuleMessage("Copied.", false);
  } catch (error) {
    showRuleMessage(`Copy failed: ${error.message || error}`, true);
  }
}

async function saveLogsBundle() {
  dom.saveLogsButton.disabled = true;
  dom.saveLogsButton.textContent = "Saving...";

  try {
    const [consoleEntries, harLog] = await Promise.all([
      fetchConsoleLogs(),
      getHarLog()
    ]);

    const stamp = formatExportStamp(new Date());
    const logFilename = `${stamp}_LOG.log`;
    const harFilename = `${stamp}_HAR.har`;
    const logText = formatConsoleLogText(consoleEntries);
    const harText = JSON.stringify(harLog, null, 2);

    const response = await sendRuntimeMessage({
      type: "ice-pick:save-export-files",
      files: [
        {
          filename: logFilename,
          content: logText,
          mimeType: "text/plain;charset=utf-8"
        },
        {
          filename: harFilename,
          content: harText,
          mimeType: "application/x-http-archive+json;charset=utf-8"
        }
      ]
    });

    if (!response || response.ok !== true) {
      throw new Error(response && response.error ? response.error : "Save Logs failed.");
    }

    showRuleMessage(`Saved ${logFilename} and ${harFilename}.`, false);
  } finally {
    dom.saveLogsButton.disabled = false;
    dom.saveLogsButton.textContent = "Save Logs";
  }
}

function exportRules() {
  const blob = new Blob([JSON.stringify(state.rules, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = "ice-pick-rules.json";
  anchor.click();
  URL.revokeObjectURL(url);
}

async function importRules(event) {
  const file = event.target.files && event.target.files[0];
  event.target.value = "";

  if (!file) {
    return;
  }

  try {
    const text = await file.text();
    const parsed = JSON.parse(text);
    const imported = Array.isArray(parsed) ? parsed : parsed.rules;

    if (!Array.isArray(imported)) {
      throw new Error("Import file must contain a rules array.");
    }

    state.rules = normalizeRules(imported);
    state.pendingDeleteRuleId = null;
    state.selectedRuleId = null;
    state.draftRule = null;
    state.fieldPickerRuleId = null;
    await persistRules();
    renderAll();
    showRuleMessage("Rules imported.", false);
  } catch (error) {
    showRuleMessage(`Import failed: ${error.message || error}`, true);
  }
}

function closeFieldPicker() {
  state.fieldPickerRuleId = null;
  state.fieldPickerExpandedPaths = new Set();
  dom.fieldPickerModal.classList.add("hidden");
  dom.fieldPickerModal.setAttribute("aria-hidden", "true");
  dom.fieldPickerSearchInput.value = "";
  dom.fieldPickerTree.textContent = "";
}

function refreshFieldPicker() {
  if (dom.fieldPickerModal.classList.contains("hidden")) {
    return;
  }

  const rule = getEditableRule();
  const source = rule ? state.ruleSources[rule.id] : null;
  if (!rule || !source) {
    return;
  }

  dom.fieldPickerSubtitle.textContent = `Response preview for ${rule.name}`;
  dom.fieldPickerStatus.textContent = "Click Add on any path to create a field.";
  dom.fieldPickerStatus.classList.remove("error");
  const scrollTop = dom.fieldPickerTree.scrollTop;
  dom.fieldPickerTree.textContent = "";
  dom.fieldPickerTree.append(renderJsonRoot(source, dom.fieldPickerSearchInput.value.trim().toLowerCase()));
  dom.fieldPickerTree.scrollTop = scrollTop;
}

function renderJsonRoot(value, searchQuery = "") {
  const wrap = document.createElement("div");
  wrap.className = "json-root";
  let matchCount = 0;

  for (const [childKey, childValue] of getEntries(value)) {
    const childNode = renderJsonNode(childValue, String(childKey), String(childKey), searchQuery);
    if (!childNode) {
      continue;
    }

    wrap.append(childNode);
    matchCount += 1;
  }

  if (!matchCount) {
    const empty = document.createElement("div");
    empty.className = "json-empty";
    empty.textContent = searchQuery
      ? `No fields match "${dom.fieldPickerSearchInput.value.trim()}".`
      : "No fields available in this response.";
    wrap.append(empty);
  }

  return wrap;
}

function renderJsonNode(value, path, key = path, searchQuery = "", showFullSubtree = false) {
  const displayKey = formatPathDisplay(path, key);
  const selectionState = getPathSelectionState(path, value);
  const matchesSelf = doesJsonNodeMatchSearch(value, path, key, displayKey, searchQuery);

  if (isPrimitiveValue(value)) {
    if (searchQuery && !showFullSubtree && !matchesSelf) {
      return null;
    }

    return renderJsonLeaf(value, path, displayKey, selectionState);
  }

  const showChildren = showFullSubtree || matchesSelf;
  const details = document.createElement("details");
  details.className = "json-node";
  details.dataset.path = path;
  details.open = Boolean(searchQuery) || state.fieldPickerExpandedPaths.has(path) || path === "payload";
  applySelectionStateClass(details, selectionState);
  details.addEventListener("toggle", () => {
    if (details.open) {
      state.fieldPickerExpandedPaths.add(path);
      return;
    }

    state.fieldPickerExpandedPaths.delete(path);
  });

  const summary = document.createElement("summary");
  summary.className = "json-summary";

  const keyEl = document.createElement("span");
  keyEl.className = "json-key";
  keyEl.textContent = displayKey;

  const typeEl = document.createElement("span");
  typeEl.className = "json-type";
  typeEl.textContent = Array.isArray(value) ? `[${value.length}]` : "{...}";

  const addButton = createFieldPickerToggleButton(path);

  summary.append(keyEl, typeEl, addButton);
  details.append(summary);

  const children = document.createElement("div");
  children.className = "json-children";
  let childCount = 0;

  for (const childEntry of getJsonTreeEntries(value, path)) {
    const childNode = renderJsonNode(
      childEntry.value,
      childEntry.path,
      childEntry.displayKey,
      searchQuery,
      showChildren
    );
    if (!childNode) {
      continue;
    }

    children.append(childNode);
    childCount += 1;
  }

  if (searchQuery && !matchesSelf && !childCount) {
    return null;
  }

  details.append(children);
  return details;
}

function renderJsonLeaf(value, path, key, selectionState = "none") {
  const row = document.createElement("div");
  row.className = "json-leaf";
  applySelectionStateClass(row, selectionState);

  const meta = document.createElement("div");
  meta.className = "json-leaf-meta";

  const keyEl = document.createElement("span");
  keyEl.className = "json-key";
  keyEl.textContent = key;

  const valueEl = document.createElement("span");
  valueEl.className = "json-preview";
  valueEl.append(renderInlineJsonValue(value));

  meta.append(keyEl, valueEl);

  const addButton = createFieldPickerToggleButton(path);

  row.append(meta, addButton);
  return row;
}

function createFieldPickerToggleButton(path) {
  const isSelected = isExactSelectedPath(path);
  const button = document.createElement("button");
  button.type = "button";
  button.className = `json-add-button${isSelected ? " remove" : ""}`;
  button.textContent = isSelected ? "Remove" : "Add";
  button.addEventListener("click", (event) => {
    event.preventDefault();
    event.stopPropagation();

    if (isSelected) {
      removeFieldPath(path);
      return;
    }

    addFieldFromPath(path);
  });

  return button;
}

function addFieldFromPath(path) {
  if (isExactSelectedPath(path)) {
    removeFieldPath(path);
    return;
  }

  const label = deriveFieldLabel(path);
  addFieldRow({ label, path });
  dom.fieldPickerStatus.textContent = `Added ${path}`;
  dom.fieldPickerStatus.classList.remove("error");
  showRuleMessage(`Field added: ${path}`, false);
  refreshFieldPicker();
}

function removeFieldPath(path) {
  const normalizedPath = normalizeFieldPath(path);

  for (const row of dom.fieldsEditor.querySelectorAll(".field-row")) {
    const input = row.querySelector(".field-path-input");
    if (input && normalizeFieldPath(input.value) === normalizedPath) {
      row.remove();
    }
  }

  dom.fieldPickerStatus.textContent = `Removed ${path}`;
  dom.fieldPickerStatus.classList.remove("error");
  showRuleMessage(`Field removed: ${path}`, false);
  refreshFieldPicker();
}

function deriveFieldLabel(path) {
  const cleanPath = String(path || "").replace(/\[(\d+)\]/g, ".$1");
  const parts = cleanPath.split(".").filter(Boolean);
  const lastPart = parts[parts.length - 1] || "Value";
  const beforeLast = parts[parts.length - 2] || "";

  return /^\d+$/.test(beforeLast) ? `${beforeLast} ${lastPart}` : lastPart;
}

function doesJsonNodeMatchSearch(value, path, key, displayKey, searchQuery) {
  if (!searchQuery) {
    return false;
  }

  const searchText = [
    path,
    key,
    displayKey,
    isPrimitiveValue(value) ? getPrimitiveSearchText(value) : ""
  ]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();

  return searchText.includes(searchQuery);
}

function getPrimitiveSearchText(value) {
  if (typeof value === "string") {
    return value;
  }

  if (value === null) {
    return "null";
  }

  return String(value);
}

function getEntries(value) {
  if (Array.isArray(value)) {
    return value.map((item, index) => [String(index), item]);
  }

  return Object.entries(value || {});
}

function getValueDisplayEntries(value) {
  if (!Array.isArray(value)) {
    return getEntries(value);
  }

  return value.flatMap((item, index) => {
    const indexKey = String(index);

    if (item && typeof item === "object") {
      const entries = getEntries(item);
      if (entries.length) {
        return entries.map(([childKey, childValue]) => [`${indexKey} ${childKey}`, childValue]);
      }
    }

    return [[indexKey, item]];
  });
}

function getJsonTreeEntries(value, parentPath) {
  if (!Array.isArray(value)) {
    return getEntries(value).map(([childKey, childValue]) => ({
      displayKey: childKey,
      path: buildChildPath(parentPath, childKey, false),
      value: childValue
    }));
  }

  return value.flatMap((item, index) => {
    const indexKey = String(index);
    const indexPath = buildChildPath(parentPath, indexKey, true);

    if (item && typeof item === "object") {
      const entries = getEntries(item);
      if (entries.length) {
        return entries.map(([childKey, childValue]) => {
          const childPath = buildChildPath(indexPath, childKey, Array.isArray(item));
          const childDisplay = formatPathDisplay(childPath, childKey);

          return {
            displayKey: childDisplay.startsWith(`${indexKey} `) ? childDisplay : `${indexKey} ${childDisplay}`,
            path: childPath,
            value: childValue
          };
        });
      }
    }

    return [{
      displayKey: indexKey,
      path: indexPath,
      value: item
    }];
  });
}

function buildChildPath(parentPath, childKey, isArray) {
  return isArray ? `${parentPath}[${childKey}]` : `${parentPath}.${childKey}`;
}

function isPrimitiveValue(value) {
  return value === null || typeof value !== "object";
}

function previewValue(value) {
  if (typeof value === "string") {
    return JSON.stringify(value);
  }

  if (value === null) {
    return "null";
  }

  return String(value);
}

function shouldCollapseValue(value) {
  if (value !== null && typeof value === "object") {
    return true;
  }

  return typeof value === "string" && value.includes("\n");
}

function summarizeValue(value) {
  if (Array.isArray(value)) {
    return value.length ? `[${value.length}] ${summarizeValue(value[0])}` : "[]";
  }

  if (value !== null && typeof value === "object") {
    const entries = Object.entries(value);
    if (!entries.length) {
      return "{}";
    }

    return entries
      .slice(0, 3)
      .map(([key, childValue]) => `${key}: ${summarizeLeafValue(childValue)}`)
      .join(" | ");
  }

  return summarizeLeafValue(value);
}

function summarizeLeafValue(value) {
  const text = stringifyValue(value).replace(/\s+/g, " ").trim();
  return text.length > 80 ? `${text.slice(0, 77)}...` : text;
}

function renderSummaryValue(value) {
  const fragment = document.createDocumentFragment();

  if (Array.isArray(value)) {
    fragment.append(createJsonToken("json-punctuation", value.length ? `[${value.length}] ` : "[]"));
    if (value.length) {
      fragment.append(renderSummaryValue(value[0]));
    }
    return fragment;
  }

  if (value && typeof value === "object") {
    const entries = Object.entries(value);
    if (!entries.length) {
      fragment.append(createJsonToken("json-punctuation", "{}"));
      return fragment;
    }

    entries.slice(0, 3).forEach(([key, childValue], index) => {
      if (index > 0) {
        fragment.append(createJsonToken("json-punctuation", " | "));
      }
      fragment.append(createJsonToken("json-key-token", key));
      fragment.append(createJsonToken("json-punctuation", ": "));
      fragment.append(createJsonTokenClassForValue(childValue, summarizeLeafValue(childValue)));
    });
    return fragment;
  }

  fragment.append(createJsonTokenClassForValue(value, summarizeLeafValue(value)));
  return fragment;
}

function renderTokenValue(value) {
  const text = document.createElement("pre");
  text.className = "value-text";
  text.append(renderInlineJsonValue(value));
  return text;
}

function renderInlineJsonValue(value) {
  const fragment = document.createDocumentFragment();

  if (typeof value === "string") {
    fragment.append(createJsonToken("json-string", JSON.stringify(value)));
    return fragment;
  }

  if (typeof value === "number") {
    fragment.append(createJsonToken("json-number", String(value)));
    return fragment;
  }

  if (typeof value === "boolean") {
    fragment.append(createJsonToken("json-boolean", String(value)));
    return fragment;
  }

  if (value === null) {
    fragment.append(createJsonToken("json-null", "null"));
    return fragment;
  }

  if (Array.isArray(value)) {
    fragment.append(createJsonToken("json-punctuation", "["));
    value.forEach((item, index) => {
      if (index > 0) {
        fragment.append(createJsonToken("json-punctuation", ", "));
      }
      fragment.append(renderInlineJsonValue(item));
    });
    fragment.append(createJsonToken("json-punctuation", "]"));
    return fragment;
  }

  if (value && typeof value === "object") {
    fragment.append(createJsonToken("json-punctuation", "{"));
    Object.entries(value).forEach(([key, childValue], index) => {
      if (index > 0) {
        fragment.append(createJsonToken("json-punctuation", ", "));
      }
      fragment.append(createJsonToken("json-key-token", key));
      fragment.append(createJsonToken("json-punctuation", ": "));
      fragment.append(renderInlineJsonValue(childValue));
    });
    fragment.append(createJsonToken("json-punctuation", "}"));
    return fragment;
  }

  fragment.append(createJsonToken("json-text", String(value)));
  return fragment;
}

function createJsonToken(className, text) {
  const token = document.createElement("span");
  token.className = className;
  token.textContent = text;
  return token;
}

function createJsonTokenClassForValue(value, text) {
  if (typeof value === "string") {
    return createJsonToken("json-string", text);
  }

  if (typeof value === "number") {
    return createJsonToken("json-number", text);
  }

  if (typeof value === "boolean") {
    return createJsonToken("json-boolean", text);
  }

  if (value === null) {
    return createJsonToken("json-null", text);
  }

  return createJsonToken("json-text", text);
}

function formatPathDisplay(path, fallbackKey) {
  const parts = String(path || "")
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .filter(Boolean);

  if (parts.length >= 2 && /^\d+$/.test(parts[parts.length - 2])) {
    return `${parts[parts.length - 2]} ${parts[parts.length - 1]}`;
  }

  return fallbackKey;
}

function isHighlightedPath(path) {
  return getSelectedFieldPaths().some((selectedPath) => (
    path === selectedPath ||
    path.startsWith(`${selectedPath}.`) ||
    path.startsWith(`${selectedPath}[`)
  ));
}

function getPathSelectionState(path, value) {
  if (isPathCovered(path)) {
    return "full";
  }

  if (isPrimitiveValue(value)) {
    return "none";
  }

  const childStates = getEntries(value).map(([childKey, childValue]) => (
    getPathSelectionState(buildChildPath(path, childKey, Array.isArray(value)), childValue)
  ));

  const hasSelectedChild = childStates.some((state) => state !== "none");
  if (!hasSelectedChild) {
    return "none";
  }

  return childStates.every((state) => state === "full") ? "full" : "partial";
}

function isPathCovered(path) {
  const normalizedPath = normalizeFieldPath(path);
  return getSelectedFieldPaths().some((selectedPath) => (
    normalizedPath === normalizeFieldPath(selectedPath) ||
    normalizedPath.startsWith(`${normalizeFieldPath(selectedPath)}.`)
  ));
}

function isExactSelectedPath(path) {
  const normalizedPath = normalizeFieldPath(path);
  return getSelectedFieldPaths().some((selectedPath) => normalizeFieldPath(selectedPath) === normalizedPath);
}

function normalizeFieldPath(path) {
  return String(path || "").trim().replace(/\[(\d+)\]/g, ".$1");
}

function applySelectionStateClass(element, selectionState) {
  element.classList.toggle("selected-path", selectionState === "full");
  element.classList.toggle("partial-selected-path", selectionState === "partial");
}

function getSelectedFieldPaths() {
  return Array.from(dom.fieldsEditor.querySelectorAll(".field-path-input"))
    .map((input) => input.value.trim())
    .filter(Boolean);
}

function getDraggedFieldRow() {
  if (!state.draggedFieldRowId) {
    return null;
  }

  return dom.fieldsEditor.querySelector(`[data-field-row-id="${state.draggedFieldRowId}"]`);
}

function clearFieldDropMarkers() {
  for (const row of dom.fieldsEditor.querySelectorAll(".field-row")) {
    row.classList.remove("drop-before", "drop-after");
  }
}

function createFieldRowId() {
  return `field_${Date.now()}_${Math.random().toString(16).slice(2)}`;
}

function buildDraftRuleFromRequest(entry) {
  const file = entry.file || "";
  const label = file || deriveRequestLabel(entry.url) || "New rule";

  return normalizeRule({
    id: createRuleId(),
    name: label,
    enabled: true,
    method: entry.method || "ANY",
    matchType: file ? "file" : "urlContains",
    matchValue: file || entry.url || "",
    fields: []
  });
}

function deriveRequestLabel(url) {
  try {
    const parsed = new URL(String(url || ""));
    const parts = parsed.pathname.split("/").filter(Boolean);
    return parts[parts.length - 1] || parsed.hostname || "";
  } catch (error) {
    const value = String(url || "");
    const parts = value.split(/[/?#]/).filter(Boolean);
    return parts[parts.length - 1] || value;
  }
}

async function fetchConsoleLogs() {
  const tabId = getInspectedTabId();
  if (!Number.isInteger(tabId)) {
    throw new Error("Could not determine the inspected tab for console export.");
  }

  const response = await sendRuntimeMessage({
    type: "ice-pick:get-console-logs",
    tabId
  });

  if (!response || response.ok !== true) {
    throw new Error(response && response.error ? response.error : "Console export failed.");
  }

  return Array.isArray(response.entries) ? response.entries : [];
}

function formatConsoleLogText(entries) {
  if (!entries.length) {
    return "No console logs captured since the last refresh.\n";
  }

  return entries.map((entry) => {
    const parts = [
      `[${entry.timestamp || ""}]`,
      `[${String(entry.level || "log").toUpperCase()}]`
    ];

    if (entry.frameId !== undefined && entry.frameId !== null) {
      parts.push(`[frame ${entry.frameId}]`);
    }

    if (entry.url) {
      parts.push(entry.url);
    }

    const header = parts.join(" ");
    const body = entry.text || "";
    return `${header}\n${body}\n`;
  }).join("\n");
}

function formatExportStamp(date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  const hours = String(date.getHours()).padStart(2, "0");
  const minutes = String(date.getMinutes()).padStart(2, "0");
  const seconds = String(date.getSeconds()).padStart(2, "0");

  return `${year}-${month}-${day}_${hours}-${minutes}-${seconds}`;
}
