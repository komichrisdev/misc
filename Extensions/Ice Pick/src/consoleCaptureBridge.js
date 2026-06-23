(function initConsoleCaptureBridge() {
  const ext = globalThis.browser ?? globalThis.chrome;

  if (!ext || !ext.runtime || typeof ext.runtime.sendMessage !== "function") {
    return;
  }

  document.addEventListener("ice-pick-console-reset", (event) => {
    const detail = sanitizeDetail(event.detail);
    if (!detail || detail.scope !== "top") {
      return;
    }

    sendMessage({
      type: "ice-pick:console-reset",
      payload: detail
    });
  });

  document.addEventListener("ice-pick-console-entry", (event) => {
    const detail = sanitizeDetail(event.detail);
    if (!detail) {
      return;
    }

    sendMessage({
      type: "ice-pick:console-entry",
      payload: detail
    });
  });
})();

function sendMessage(message) {
  const ext = globalThis.browser ?? globalThis.chrome;
  if (!ext || !ext.runtime || typeof ext.runtime.sendMessage !== "function") {
    return;
  }

  try {
    const result = ext.runtime.sendMessage(message);
    if (result && typeof result.catch === "function") {
      result.catch(() => {});
    }
  } catch (error) {
    // Ignore logging bridge failures.
  }
}

function sanitizeDetail(detail) {
  if (!detail || typeof detail !== "object") {
    return null;
  }

  return {
    level: typeof detail.level === "string" ? detail.level : "log",
    source: typeof detail.source === "string" ? detail.source : "console",
    text: typeof detail.text === "string" ? detail.text : "",
    url: typeof detail.url === "string" ? detail.url : "",
    timestamp: typeof detail.timestamp === "string" ? detail.timestamp : new Date().toISOString(),
    scope: typeof detail.scope === "string" ? detail.scope : "frame"
  };
}
