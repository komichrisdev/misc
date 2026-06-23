const fs = require("fs");
const path = require("path");

const rootDir = path.resolve(__dirname, "..");
const firefoxDir = path.join(rootDir, ".firefox-build");
const sharedEntries = [
  "assets",
  "src",
  "background.js",
  "devtools.html",
  "devtools.js",
  "manifest.json",
  "panel.css",
  "panel.html",
  "panel.js"
];
const firefoxOnlyPermissions = ["webRequestBlocking", "webRequestFilterResponse"];

prepareFirefoxSource();

function prepareFirefoxSource() {
  fs.mkdirSync(firefoxDir, { recursive: true });
  clearDirectory(firefoxDir);

  for (const entry of sharedEntries) {
    copySharedEntry(entry);
  }

  const manifestPath = path.join(firefoxDir, "manifest.json");
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));

  manifest.permissions = dedupeValues([
    ...(Array.isArray(manifest.permissions) ? manifest.permissions : []),
    ...firefoxOnlyPermissions
  ]);

  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  process.stdout.write(`Prepared Firefox source at ${firefoxDir}\n`);
}

function copySharedEntry(entry) {
  const sourcePath = path.join(rootDir, entry);
  const targetPath = path.join(firefoxDir, entry);
  const stats = fs.statSync(sourcePath);

  if (stats.isDirectory()) {
    fs.cpSync(sourcePath, targetPath, { recursive: true, force: true });
    return;
  }

  fs.mkdirSync(path.dirname(targetPath), { recursive: true });
  fs.copyFileSync(sourcePath, targetPath);
}

function clearDirectory(directoryPath) {
  for (const entry of fs.readdirSync(directoryPath)) {
    try {
      fs.rmSync(path.join(directoryPath, entry), {
        recursive: true,
        force: true,
        maxRetries: 5,
        retryDelay: 100
      });
    } catch (error) {
      // Some synced Windows folders hold transient locks. Copying shared
      // entries with force still refreshes the staged build content.
    }
  }
}

function dedupeValues(values) {
  return [...new Set(values)];
}
