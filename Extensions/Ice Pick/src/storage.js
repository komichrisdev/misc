import { storageGet, storageSet } from "./browserApi.js";
import { normalizeRules } from "./rules.js";

export const SAMPLE_RULE = {
  id: "sample_init_game",
  name: "Sample Init Game",
  enabled: true,
  method: "POST",
  matchType: "file",
  matchValue: "initGame",
  fields: [
    {
      label: "User ID",
      path: "payload.userData.userId"
    },
    {
      label: "Experiment",
      path: "payload.experimentId"
    }
  ]
};

export const DEFAULT_PREFS = {
  paused: false,
  maxResults: 500
};

const RULES_KEY = "networkSniffer.rules";
const PREFS_KEY = "networkSniffer.prefs";
const SEEDED_KEY = "networkSniffer.seeded";

export async function loadRules() {
  const data = await storageGet({
    [RULES_KEY]: null,
    [SEEDED_KEY]: false
  });

  if (!data[SEEDED_KEY] || !Array.isArray(data[RULES_KEY])) {
    const rules = normalizeRules([SAMPLE_RULE]);
    await saveRules(rules);
    await storageSet({ [SEEDED_KEY]: true });
    return rules;
  }

  return normalizeRules(data[RULES_KEY]);
}

export async function saveRules(rules) {
  await storageSet({ [RULES_KEY]: normalizeRules(rules) });
}

export async function loadPrefs() {
  const data = await storageGet({ [PREFS_KEY]: DEFAULT_PREFS });
  return normalizePrefs(data[PREFS_KEY]);
}

export async function savePrefs(prefs) {
  await storageSet({ [PREFS_KEY]: normalizePrefs(prefs) });
}

function normalizePrefs(prefs) {
  const raw = prefs || {};

  return {
    paused: Boolean(raw.paused),
    maxResults: DEFAULT_PREFS.maxResults
  };
}
