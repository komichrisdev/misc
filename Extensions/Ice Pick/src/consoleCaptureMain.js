(function initConsoleCaptureMain() {
  if (globalThis.__ICE_PICK_CONSOLE_CAPTURE_ACTIVE__) {
    return;
  }

  globalThis.__ICE_PICK_CONSOLE_CAPTURE_ACTIVE__ = true;

  emitConsoleReset();

  const levels = ["log", "info", "warn", "error", "debug", "trace"];
  for (const level of levels) {
    wrapConsoleMethod(level);
  }

  globalThis.addEventListener("error", (event) => {
    const parts = [];
    if (event.message) {
      parts.push(event.message);
    }
    if (event.filename) {
      const location = `${event.filename}${event.lineno ? `:${event.lineno}` : ""}${event.colno ? `:${event.colno}` : ""}`;
      parts.push(location);
    }
    if (event.error && event.error.stack) {
      parts.push(event.error.stack);
    }

    emitConsoleEntry("error", parts.join("\n") || "Uncaught error", "window.onerror");
  }, true);

  globalThis.addEventListener("unhandledrejection", (event) => {
    emitConsoleEntry("error", `Unhandled rejection: ${serializeValue(event.reason)}`, "unhandledrejection");
  }, true);
})();

function wrapConsoleMethod(level) {
  const consoleRef = globalThis.console;
  if (!consoleRef || typeof consoleRef[level] !== "function") {
    return;
  }

  const original = consoleRef[level].bind(consoleRef);
  consoleRef[level] = function wrappedConsoleMethod(...args) {
    try {
      const serializedArgs = args.map((value) => serializeValue(value));
      const text = serializedArgs.join(" ");
      const source = level === "trace" ? "console.trace" : `console.${level}`;
      const stackText = level === "trace" ? captureTraceStack() : "";
      const fullText = stackText ? `${text}\n${stackText}`.trim() : text;
      emitConsoleEntry(level, fullText, source);
    } catch (error) {
      // Keep console behavior even if capture fails.
    }

    original(...args);
  };
}

function emitConsoleReset() {
  dispatchConsoleEvent("ice-pick-console-reset", {
    scope: globalThis.top === globalThis ? "top" : "frame",
    timestamp: new Date().toISOString(),
    url: safeHref()
  });
}

function emitConsoleEntry(level, text, source) {
  dispatchConsoleEvent("ice-pick-console-entry", {
    level,
    source,
    text,
    timestamp: new Date().toISOString(),
    url: safeHref(),
    scope: globalThis.top === globalThis ? "top" : "frame"
  });
}

function dispatchConsoleEvent(type, detail) {
  document.dispatchEvent(new CustomEvent(type, { detail }));
}

function safeHref() {
  try {
    return globalThis.location ? globalThis.location.href : "";
  } catch (error) {
    return "";
  }
}

function captureTraceStack() {
  try {
    const error = new Error();
    return error.stack ? String(error.stack).split("\n").slice(1).join("\n").trim() : "";
  } catch (error) {
    return "";
  }
}

function serializeValue(value) {
  if (typeof value === "string") {
    return value;
  }

  if (value === null) {
    return "null";
  }

  if (value === undefined) {
    return "undefined";
  }

  if (typeof value === "number" || typeof value === "boolean" || typeof value === "bigint") {
    return String(value);
  }

  if (typeof value === "function") {
    return `[Function ${value.name || "anonymous"}]`;
  }

  if (value instanceof Error) {
    return value.stack || `${value.name}: ${value.message}`;
  }

  if (typeof Element !== "undefined" && value instanceof Element) {
    const id = value.id ? `#${value.id}` : "";
    const className = typeof value.className === "string" && value.className.trim()
      ? `.${value.className.trim().split(/\s+/).join(".")}`
      : "";
    return `<${String(value.tagName || "element").toLowerCase()}${id}${className}>`;
  }

  try {
    return JSON.stringify(value, createCircularReplacer(), 2);
  } catch (error) {
    return String(value);
  }
}

function createCircularReplacer() {
  const seen = new WeakSet();

  return function circularReplacer(_key, value) {
    if (!value || typeof value !== "object") {
      return value;
    }

    if (seen.has(value)) {
      return "[Circular]";
    }

    seen.add(value);
    return value;
  };
}
