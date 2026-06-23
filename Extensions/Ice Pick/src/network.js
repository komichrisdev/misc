import {
  getDevtoolsNetwork,
  getInspectedTabId,
  getRequestContent,
  isFirefoxBrowser,
  sendRuntimeMessage
} from "./browserApi.js";
import { extractFields } from "./extractor.js";
import { deriveRequestMeta, findMatchingRules } from "./rules.js";

let resultCounter = 0;
let requestCounter = 0;
const FIREFOX_CAPTURE_RETRY_DELAYS_MS = [50, 125, 250];

export function subscribeToNetworkRequests({ getRules, onMatch, onError, isPaused, onRequestSeen }) {
  const networkApi = getDevtoolsNetwork();

  if (!networkApi || !networkApi.onRequestFinished) {
    throw new Error("devtools.network is unavailable.");
  }

  const listener = (request) => {
    handleRequest(request, { getRules, onMatch, onError, isPaused, onRequestSeen }).catch((error) => {
      if (typeof onError === "function") {
        onError(error);
      }
    });
  };

  networkApi.onRequestFinished.addListener(listener);

  return () => {
    networkApi.onRequestFinished.removeListener(listener);
  };
}

async function handleRequest(request, handlers) {
  const meta = deriveRequestMeta(request.request && request.request.url, request.request && request.request.method);
  const base = createBaseResult(request, meta);

  if (typeof handlers.onRequestSeen === "function") {
    handlers.onRequestSeen({
      ...base,
      id: createRequestId(),
      request
    });
  }

  if (handlers.isPaused && handlers.isPaused()) {
    return;
  }

  const rules = typeof handlers.getRules === "function" ? handlers.getRules() : [];
  const matches = findMatchingRules(meta, rules);

  if (!matches.length) {
    return;
  }

  try {
    const parsedJson = await readRequestJson(request);
    for (const rule of matches) {
      const { extracted, missingPaths, missingLabels } = extractFields(parsedJson, rule.fields);
      handlers.onMatch({
        ...base,
        id: createResultId(),
        ruleId: rule.id,
        ruleName: rule.name,
        sourceJson: parsedJson,
        extracted,
        missingPaths,
        missingLabels,
        parseError: null
      });
    }
  } catch (error) {
    for (const rule of matches) {
      handlers.onMatch({
        ...base,
        id: createResultId(),
        ruleId: rule.id,
        ruleName: rule.name,
        extracted: {},
        missingPaths: [],
        parseError: `Could not read response body: ${error.message || error}`
      });
    }
  }
}

function createBaseResult(request, meta) {
  const response = request.response || {};
  const content = response.content || {};

  return {
    timestamp: new Date().toISOString(),
    method: meta.method,
    file: meta.file,
    url: meta.fullUrl,
    status: Number.isFinite(response.status) ? response.status : null,
    mimeType: content.mimeType || ""
  };
}

function normalizeContentResult(contentResult) {
  if (contentResult && typeof contentResult === "object" && Object.prototype.hasOwnProperty.call(contentResult, "content")) {
    return contentResult.content || "";
  }

  return contentResult || "";
}

export async function readRequestJson(request) {
  const errors = [];
  let contentResult;

  try {
    if (isFirefoxBrowser()) {
      contentResult = await readFirefoxCapturedContent(request);
    }
  } catch (error) {
    errors.push(error.message || String(error));
  }

  if (contentResult === undefined) {
    try {
      contentResult = await getRequestContent(request);
    } catch (error) {
      errors.push(`Could not read response body: ${error.message || error}`);
    }
  }

  if (contentResult === undefined) {
    throw new Error(errors[0] || "Could not read response body.");
  }

  const content = normalizeContentResult(contentResult);

  try {
    return JSON.parse(content);
  } catch (error) {
    const prefix = errors.length ? `${errors.join(" | ")} | ` : "";
    throw new Error(`${prefix}JSON parse failed: ${error.message || error}`);
  }
}

async function readFirefoxCapturedContent(request) {
  const tabId = getInspectedTabId();
  if (!Number.isInteger(tabId)) {
    throw new Error("Firefox response capture is missing the inspected tab id.");
  }

  const requestInfo = request && request.request ? request.request : {};
  let lastError = "No Firefox response capture found.";

  for (let attempt = 0; attempt <= FIREFOX_CAPTURE_RETRY_DELAYS_MS.length; attempt += 1) {
    const response = await sendRuntimeMessage({
      type: "ice-pick:get-response-body",
      tabId,
      url: requestInfo.url || "",
      method: requestInfo.method || "",
      startedDateTime: request && request.startedDateTime ? request.startedDateTime : null
    });

    if (response && response.ok === true) {
      if (response.truncated) {
        throw new Error("Firefox response capture was truncated.");
      }

      return response.content;
    }

    lastError = response && response.error ? response.error : lastError;

    const retryDelay = FIREFOX_CAPTURE_RETRY_DELAYS_MS[attempt];
    if (retryDelay !== undefined) {
      await delay(retryDelay);
    }
  }

  throw new Error(lastError);
}

function createResultId() {
  resultCounter += 1;
  return `result_${Date.now()}_${resultCounter}`;
}

function createRequestId() {
  requestCounter += 1;
  return `request_${Date.now()}_${requestCounter}`;
}

function delay(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}
