const SUPPORTED_METHODS = new Set(["ANY", "GET", "POST", "PUT", "PATCH", "DELETE"]);
const SUPPORTED_MATCH_TYPES = new Set(["file", "urlContains", "pathContains", "fullUrl"]);

export function deriveRequestMeta(url, method) {
  const fullUrl = String(url || "");
  const normalizedMethod = String(method || "").toUpperCase();
  const fallbackPathname = getBestEffortPathname(fullUrl);
  let hostname = "";
  let pathname = fallbackPathname;

  try {
    const parsed = new URL(fullUrl);
    hostname = parsed.hostname;
    pathname = parsed.pathname || fallbackPathname;
  } catch (error) {
    hostname = getBestEffortHostname(fullUrl);
  }

  return {
    fullUrl,
    hostname,
    pathname,
    file: getFinalPathSegment(pathname),
    method: normalizedMethod
  };
}

export function matchesRule(meta, rule) {
  if (!rule || rule.enabled === false) {
    return false;
  }

  const method = normalizeMethod(rule.method);
  if (method !== "ANY" && method !== meta.method) {
    return false;
  }

  const matchType = SUPPORTED_MATCH_TYPES.has(rule.matchType) ? rule.matchType : "file";
  const matchValue = String(rule.matchValue || "");

  if (!matchValue) {
    return false;
  }

  switch (matchType) {
    case "file":
      return meta.file === matchValue;
    case "urlContains":
      return meta.fullUrl.includes(matchValue);
    case "pathContains":
      return meta.pathname.includes(matchValue);
    case "fullUrl":
      return meta.fullUrl === matchValue;
    default:
      return false;
  }
}

export function findMatchingRules(meta, rules) {
  return (Array.isArray(rules) ? rules : []).filter((rule) => matchesRule(meta, rule));
}

export function normalizeRule(rule) {
  const raw = rule || {};
  return {
    id: raw.id || `rule_${Date.now()}_${Math.random().toString(16).slice(2)}`,
    name: String(raw.name || "Untitled rule"),
    enabled: raw.enabled === undefined ? true : Boolean(raw.enabled),
    method: normalizeMethod(raw.method),
    matchType: SUPPORTED_MATCH_TYPES.has(raw.matchType) ? raw.matchType : "file",
    matchValue: String(raw.matchValue || ""),
    fields: normalizeFields(raw.fields)
  };
}

export function normalizeRules(rules) {
  return (Array.isArray(rules) ? rules : []).map(normalizeRule);
}

function normalizeMethod(method) {
  const normalized = String(method || "ANY").toUpperCase();
  return SUPPORTED_METHODS.has(normalized) ? normalized : "ANY";
}

function normalizeFields(fields) {
  return (Array.isArray(fields) ? fields : []).map((field) => ({
    label: String(field.label || field.path || "Value"),
    path: String(field.path || "")
  }));
}

function getFinalPathSegment(pathname) {
  const cleanPath = String(pathname || "").split("?")[0].split("#")[0].replace(/\/+$/, "");
  const parts = cleanPath.split("/").filter(Boolean);
  return parts.length ? decodeSafely(parts[parts.length - 1]) : "";
}

function getBestEffortPathname(fullUrl) {
  const withoutQuery = String(fullUrl || "").split("?")[0].split("#")[0];
  const protocolIndex = withoutQuery.indexOf("://");

  if (protocolIndex === -1) {
    return withoutQuery.startsWith("/") ? withoutQuery : `/${withoutQuery}`;
  }

  const firstSlash = withoutQuery.indexOf("/", protocolIndex + 3);
  return firstSlash === -1 ? "/" : withoutQuery.slice(firstSlash);
}

function getBestEffortHostname(fullUrl) {
  const value = String(fullUrl || "");
  const protocolIndex = value.indexOf("://");
  if (protocolIndex === -1) {
    return "";
  }

  const rest = value.slice(protocolIndex + 3);
  return rest.split(/[/?#]/)[0] || "";
}

function decodeSafely(value) {
  try {
    return decodeURIComponent(value);
  } catch (error) {
    return value;
  }
}
