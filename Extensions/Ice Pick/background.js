const ext = globalThis.browser ?? globalThis.chrome;

const RESPONSE_TTL_MS = 5 * 60 * 1000;
const MAX_RECORDS_PER_TAB = 1000;
const MAX_CAPTURED_BYTES = 2 * 1024 * 1024;
const MAX_CONSOLE_LOGS_PER_TAB = 2000;
const consoleLogsByTabId = new Map();

const capturedResponsesByTabId = new Map();
const inflightCaptures = new Map();
const supportsFirefoxResponseCapture = Boolean(
  globalThis.browser &&
  ext &&
  ext.webRequest &&
  typeof ext.webRequest.filterResponseData === "function"
);

if (ext && ext.runtime && ext.runtime.onMessage) {
  ext.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (!message || typeof message.type !== "string") {
      return false;
    }

    if (message.type === "ice-pick:get-response-body") {
      Promise.resolve(findCapturedResponse(message))
        .then((response) => sendResponse(response))
        .catch((error) => {
          sendResponse({
            ok: false,
            error: error && error.message ? error.message : String(error)
          });
        });

      return true;
    }

    if (message.type === "ice-pick:get-console-logs") {
      Promise.resolve(getCapturedConsoleLogs(message))
        .then((response) => sendResponse(response))
        .catch((error) => {
          sendResponse({
            ok: false,
            error: error && error.message ? error.message : String(error)
          });
        });

      return true;
    }

    if (message.type === "ice-pick:save-export-files") {
      Promise.resolve(saveExportFiles(message))
        .then((response) => sendResponse(response))
        .catch((error) => {
          sendResponse({
            ok: false,
            error: error && error.message ? error.message : String(error)
          });
        });

      return true;
    }

    if (message.type === "ice-pick:console-reset") {
      resetConsoleLogs(sender);
      return false;
    }

    if (message.type === "ice-pick:console-entry") {
      appendConsoleLog(sender, message.payload);
      return false;
    }

    return false;
  });
}

if (supportsFirefoxResponseCapture) {
  ext.webRequest.onBeforeRequest.addListener(
    handleBeforeRequest,
    { urls: ["<all_urls>"] }
  );

  ext.webRequest.onErrorOccurred.addListener(
    handleErrorOccurred,
    { urls: ["<all_urls>"] }
  );
}

if (ext && ext.tabs && ext.tabs.onRemoved) {
  ext.tabs.onRemoved.addListener((tabId) => {
    capturedResponsesByTabId.delete(tabId);
    consoleLogsByTabId.delete(tabId);
  });
}

function handleBeforeRequest(details) {
  if (!shouldCaptureResponse(details)) {
    return;
  }

  let filter;
  try {
    filter = ext.webRequest.filterResponseData(details.requestId);
  } catch (error) {
    return;
  }

  const decoder = new TextDecoder();
  const capture = {
    requestId: details.requestId,
    tabId: details.tabId,
    url: details.url,
    method: normalizeMethod(details.method),
    startedAt: Number.isFinite(details.timeStamp) ? details.timeStamp : Date.now(),
    byteCount: 0,
    truncated: false,
    text: ""
  };

  inflightCaptures.set(details.requestId, capture);

  filter.ondata = (event) => {
    const bytes = new Uint8Array(event.data);

    if (capture.byteCount < MAX_CAPTURED_BYTES) {
      const remaining = MAX_CAPTURED_BYTES - capture.byteCount;
      const nextBytes = remaining >= bytes.byteLength ? bytes : bytes.subarray(0, remaining);
      capture.text += decoder.decode(nextBytes, { stream: true });
      capture.byteCount += nextBytes.byteLength;
      if (nextBytes.byteLength < bytes.byteLength) {
        capture.truncated = true;
      }
    } else {
      capture.truncated = true;
    }

    filter.write(event.data);
  };

  filter.onstop = () => {
    try {
      capture.text += decoder.decode();
      commitCapture(capture);
    } finally {
      inflightCaptures.delete(details.requestId);
      safeCloseFilter(filter);
    }
  };

  filter.onerror = () => {
    inflightCaptures.delete(details.requestId);
    safeDisconnectFilter(filter);
  };
}

function handleErrorOccurred(details) {
  inflightCaptures.delete(details.requestId);
}

function shouldCaptureResponse(details) {
  return Boolean(
    details &&
    details.tabId >= 0 &&
    typeof details.url === "string" &&
    /^https?:/i.test(details.url)
  );
}

function commitCapture(capture) {
  pruneExpiredResponses();

  const records = capturedResponsesByTabId.get(capture.tabId) || [];
  records.push({
    url: capture.url,
    method: capture.method,
    startedAt: capture.startedAt,
    truncated: capture.truncated,
    text: capture.text
  });

  if (records.length > MAX_RECORDS_PER_TAB) {
    records.splice(0, records.length - MAX_RECORDS_PER_TAB);
  }

  capturedResponsesByTabId.set(capture.tabId, records);
}

function findCapturedResponse(message) {
  if (!supportsFirefoxResponseCapture) {
    return {
      ok: false,
      error: "Firefox response capture is unavailable in this browser."
    };
  }

  pruneExpiredResponses();

  const tabId = Number(message.tabId);
  if (!Number.isInteger(tabId)) {
    return {
      ok: false,
      error: "Missing inspected tab id."
    };
  }

  const url = String(message.url || "");
  const method = normalizeMethod(message.method);
  const startedAt = parseStartedAt(message.startedDateTime);
  const records = capturedResponsesByTabId.get(tabId) || [];

  const exactCandidates = records.filter((record) => (
    record.url === url && record.method === method
  ));
  const urlCandidates = records.filter((record) => record.url === url);
  const candidate = rankRecords(exactCandidates, startedAt)[0] || rankRecords(urlCandidates, startedAt)[0];

  if (!candidate) {
    return {
      ok: false,
      error: "No captured Firefox response body matched this request."
    };
  }

  return {
    ok: true,
    content: candidate.text,
    truncated: candidate.truncated
  };
}

function rankRecords(records, startedAt) {
  return [...records]
    .map((record) => ({
      record,
      score: startedAt === null ? 0 : Math.abs(record.startedAt - startedAt)
    }))
    .sort((left, right) => {
      if (left.score !== right.score) {
        return left.score - right.score;
      }

      return right.record.startedAt - left.record.startedAt;
    })
    .map((entry) => entry.record);
}

function parseStartedAt(value) {
  if (!value) {
    return null;
  }

  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function normalizeMethod(method) {
  return String(method || "GET").toUpperCase();
}

function pruneExpiredResponses() {
  const cutoff = Date.now() - RESPONSE_TTL_MS;

  for (const [tabId, records] of capturedResponsesByTabId.entries()) {
    const nextRecords = records.filter((record) => record.startedAt >= cutoff);

    if (nextRecords.length) {
      capturedResponsesByTabId.set(tabId, nextRecords);
    } else {
      capturedResponsesByTabId.delete(tabId);
    }
  }
}

function safeCloseFilter(filter) {
  try {
    filter.close();
  } catch (error) {
    safeDisconnectFilter(filter);
  }
}

function safeDisconnectFilter(filter) {
  try {
    filter.disconnect();
  } catch (error) {
    // Ignore cleanup failures.
  }
}

function resetConsoleLogs(sender) {
  const tabId = sender && sender.tab ? sender.tab.id : null;
  const frameId = sender && Number.isInteger(sender.frameId) ? sender.frameId : null;

  if (!Number.isInteger(tabId) || frameId !== 0) {
    return;
  }

  consoleLogsByTabId.set(tabId, []);
}

function appendConsoleLog(sender, payload) {
  const tabId = sender && sender.tab ? sender.tab.id : null;
  if (!Number.isInteger(tabId) || !payload || typeof payload !== "object") {
    return;
  }

  const frameId = sender && Number.isInteger(sender.frameId) ? sender.frameId : 0;
  const records = consoleLogsByTabId.get(tabId) || [];
  records.push({
    frameId,
    level: typeof payload.level === "string" ? payload.level : "log",
    source: typeof payload.source === "string" ? payload.source : "console",
    text: typeof payload.text === "string" ? payload.text : "",
    timestamp: typeof payload.timestamp === "string" ? payload.timestamp : new Date().toISOString(),
    url: typeof payload.url === "string" ? payload.url : ""
  });

  if (records.length > MAX_CONSOLE_LOGS_PER_TAB) {
    records.splice(0, records.length - MAX_CONSOLE_LOGS_PER_TAB);
  }

  consoleLogsByTabId.set(tabId, records);
}

function getCapturedConsoleLogs(message) {
  const tabId = Number(message && message.tabId);
  if (!Number.isInteger(tabId)) {
    return {
      ok: false,
      error: "Missing inspected tab id."
    };
  }

  return {
    ok: true,
    entries: [...(consoleLogsByTabId.get(tabId) || [])]
  };
}

async function saveExportFiles(message) {
  if (!ext || !ext.downloads || typeof ext.downloads.download !== "function") {
    return {
      ok: false,
      error: "Downloads API is unavailable in the background script."
    };
  }

  const files = Array.isArray(message && message.files) ? message.files : [];
  if (!files.length) {
    return {
      ok: false,
      error: "No files were provided for export."
    };
  }

  for (const file of files) {
    await saveTextFile(file);
  }

  return { ok: true };
}

function saveTextFile(file) {
  const filename = String(file && file.filename || "").trim();
  const content = String(file && file.content || "");
  const mimeType = String(file && file.mimeType || "text/plain;charset=utf-8");

  if (!filename) {
    return Promise.reject(new Error("Export file is missing a filename."));
  }

  const descriptor = createDownloadDescriptor(content, mimeType);
  const cleanup = typeof descriptor.cleanup === "function" ? descriptor.cleanup : () => {};

  return downloadFromBackground({
    url: descriptor.url,
    filename,
    saveAs: false,
    conflictAction: "uniquify"
  }).finally(cleanup);
}

function downloadFromBackground(options) {
  if (globalThis.browser) {
    return ext.downloads.download(options);
  }

  return new Promise((resolve, reject) => {
    ext.downloads.download(options, (downloadId) => {
      if (ext.runtime && ext.runtime.lastError) {
        reject(new Error(ext.runtime.lastError.message));
        return;
      }

      resolve(downloadId);
    });
  });
}

function createDataUrl(content, mimeType) {
  const bytes = new TextEncoder().encode(content);
  let binary = "";
  const chunkSize = 0x8000;

  for (let index = 0; index < bytes.length; index += chunkSize) {
    const slice = bytes.subarray(index, index + chunkSize);
    binary += String.fromCharCode(...slice);
  }

  return `data:${mimeType};base64,${btoa(binary)}`;
}

function createDownloadDescriptor(content, mimeType) {
  if (
    typeof Blob === "function" &&
    globalThis.URL &&
    typeof globalThis.URL.createObjectURL === "function"
  ) {
    const blob = new Blob([content], { type: mimeType });
    const url = globalThis.URL.createObjectURL(blob);

    return {
      url,
      cleanup: () => {
        setTimeout(() => {
          try {
            globalThis.URL.revokeObjectURL(url);
          } catch (error) {
            // Ignore cleanup failures.
          }
        }, 60000);
      }
    };
  }

  return {
    url: createDataUrl(content, mimeType),
    cleanup: () => {}
  };
}
