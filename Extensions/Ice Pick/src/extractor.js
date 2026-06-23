export function getByPath(obj, path) {
  if (!path || typeof path !== "string") {
    return undefined;
  }

  const parts = toPathParts(path);
  let current = obj;

  for (const part of parts) {
    if (current === null || current === undefined) {
      return undefined;
    }

    current = readPathPart(current, part);
    if (current === undefined) {
      return undefined;
    }
  }

  return current;
}

export function extractFields(json, fields) {
  const extracted = {};
  const missingPaths = [];
  const missingLabels = [];

  for (const field of Array.isArray(fields) ? fields : []) {
    const baseLabel = field.label || field.path || "Value";
    const label = createUniqueLabel(baseLabel, field.path, extracted);
    const resolved = resolveFieldValue(json, field.path);

    if (resolved.value === undefined) {
      extracted[label] = null;
      missingPaths.push(field.path || label);
      missingLabels.push(label);
      continue;
    }

    extracted[label] = expandSelectedValue(json, resolved.path, resolved.value);
  }

  return { extracted, missingPaths, missingLabels };
}

function createUniqueLabel(label, path, extracted) {
  if (!Object.prototype.hasOwnProperty.call(extracted, label)) {
    return label;
  }

  const suffix = deriveLabelSuffix(path);
  const pathLabel = suffix ? `${label} (${suffix})` : `${label} (${path || "field"})`;
  if (!Object.prototype.hasOwnProperty.call(extracted, pathLabel)) {
    return pathLabel;
  }

  let counter = 2;
  let nextLabel = `${pathLabel} ${counter}`;
  while (Object.prototype.hasOwnProperty.call(extracted, nextLabel)) {
    counter += 1;
    nextLabel = `${pathLabel} ${counter}`;
  }

  return nextLabel;
}

function deriveLabelSuffix(path) {
  const parts = toPathParts(path);
  const last = parts[parts.length - 1] || "";
  const beforeLast = parts[parts.length - 2] || "";

  if (/^\d+$/.test(beforeLast)) {
    return beforeLast;
  }

  return last && last !== path ? last : String(path || "");
}

function resolveFieldValue(json, path) {
  const candidates = buildPathCandidates(json, path);

  for (const candidate of candidates) {
    const value = getByPath(json, candidate);
    if (value !== undefined) {
      return { path: candidate, value };
    }
  }

  const nestedValue = findValueInDescendants(json, buildSuffixCandidates(candidates));
  if (nestedValue !== undefined) {
    return { path, value: nestedValue };
  }

  return { path, value: undefined };
}

function expandSelectedValue(json, path, value) {
  const parts = toPathParts(path);
  const lastPart = parts[parts.length - 1] || "";

  if (lastPart !== "panelName" || value === null || typeof value === "object") {
    return value;
  }

  const parentPath = parts.slice(0, -1).join(".");
  const parentValue = parentPath ? getByPath(json, parentPath) : undefined;

  if (!parentValue || typeof parentValue !== "object" || Array.isArray(parentValue)) {
    return value;
  }

  return clonePlainValue(parentValue);
}

function buildPathCandidates(json, path) {
  const rawPath = String(path || "").trim();
  if (!rawPath) {
    return [];
  }

  const candidates = [rawPath];
  const hasPayload = getByPath(json, "payload") !== undefined;

  if (rawPath.startsWith("payload.")) {
    candidates.push(rawPath.slice("payload.".length));
  } else if (hasPayload) {
    candidates.push(`payload.${rawPath}`);
  }

  return [...new Set(candidates)];
}

function buildSuffixCandidates(candidates) {
  const suffixes = [];

  for (const candidate of candidates) {
    const parts = toPathParts(candidate);

    for (let index = 0; index < parts.length; index += 1) {
      suffixes.push(parts.slice(index).join("."));
    }
  }

  return [...new Set(suffixes.filter(Boolean))];
}

function findValueInDescendants(root, candidates) {
  if (!Array.isArray(candidates) || !candidates.length) {
    return undefined;
  }

  const stack = [root];
  const seen = new WeakSet();

  while (stack.length) {
    const current = stack.pop();
    const searchable = maybeParseJsonValue(current) ?? current;

    if (!isSearchableObject(searchable) || seen.has(searchable)) {
      continue;
    }

    seen.add(searchable);

    for (const candidate of candidates) {
      const value = getByPath(searchable, candidate);
      if (value !== undefined) {
        return value;
      }
    }

    for (const child of Object.values(searchable)) {
      stack.push(child);
    }
  }

  return undefined;
}

function maybeParseJsonValue(value) {
  if (typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();
  if (!trimmed || !/^[\[{]/.test(trimmed)) {
    return null;
  }

  try {
    return JSON.parse(trimmed);
  } catch (error) {
    return null;
  }
}

function isSearchableObject(value) {
  return Boolean(value) && typeof value === "object";
}

function readPathPart(current, part) {
  if (Array.isArray(current) && /^\d+$/.test(part)) {
    const index = Number(part);
    return Object.prototype.hasOwnProperty.call(current, index) ? current[index] : undefined;
  }

  if (!isSearchableObject(current)) {
    return undefined;
  }

  if (Object.prototype.hasOwnProperty.call(current, part)) {
    return current[part];
  }

  const match = Object.keys(current).find((key) => key.toLowerCase() === String(part).toLowerCase());
  return match === undefined ? undefined : current[match];
}

function toPathParts(path) {
  return String(path || "")
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .filter(Boolean);
}

function clonePlainValue(value) {
  if (value === null || typeof value !== "object") {
    return value;
  }

  if (Array.isArray(value)) {
    return value.map(clonePlainValue);
  }

  return Object.fromEntries(
    Object.entries(value).map(([key, childValue]) => [key, clonePlainValue(childValue)])
  );
}
