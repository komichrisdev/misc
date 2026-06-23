# Ice Pick

Ice Pick is a local-only DevTools extension for Chrome and Firefox. It watches completed network requests while its DevTools panel is open, matches requests with user-configurable rules, reads JSON response bodies, extracts configured JSON paths, and shows the extracted values in the panel.

Captured data is not sent anywhere. Rules and simple preferences are stored in local extension storage. Response bodies are read only for matching requests and are not stored permanently.

## Features

- User-configurable request filters by endpoint or file name
- Saved JSON paths per rule
- Extracted JSON values shown inside captured matches
- Shared codebase for Chrome and Firefox

## Load in Chrome

1. Open `chrome://extensions`.
2. Enable **Developer mode**.
3. Click **Load unpacked**.
4. Select this extension folder.
5. Open DevTools on the target tab.
6. Open the **Ice Pick** panel.
7. Reload the target page or app.

Chrome keeps using the DevTools request content API for response JSON reads.

## Test in Firefox

1. Install dependencies with `npm install`.
2. Run `npm run firefox:run`.
3. Firefox launches with a temporary build from `.firefox/`.
4. Open DevTools on the target tab.
5. Open the **Ice Pick** panel.
6. Reload the target page or app.
7. Confirm rules still match and extracted values still appear.

Useful commands:

- `npm run firefox:lint`
- `npm run firefox:build`

Firefox uses `browser.webRequest.filterResponseData()` in `background.js` to capture response bodies, guarded so Chrome never runs that path.

## Build Instructions for Mozilla Review

These steps reproduce the Firefox package submitted to AMO.

### Operating system and build environment

- Tested on Windows 10 Pro
- PowerShell `5.1.26100.8655`
- Node.js `24.17.0`
- npm `11.13.0`
- Local build dependency: `web-ext 10.4.0` installed from `package-lock.json`

### Install requirements

1. Install Node.js `24.17.0` or a compatible Node.js 24 release. npm is included with Node.js.
2. Open PowerShell in the project root.
3. Run `npm ci` to install the exact locked dependency versions from `package-lock.json`.

### Exact build steps

1. In the project root, run `npm ci`.
2. Run `npm run firefox:build`.
3. The packaged Firefox add-on is created at `web-ext-artifacts/ice_pick-1.0.27.zip`.

### Build script details

- The build entry point is the `firefox:build` script in `package.json`.
- `npm run firefox:build` first runs `node scripts/prepare-firefox.js`.
- `scripts/prepare-firefox.js` copies the readable source files into `.firefox-build/` and updates `manifest.json` there with Firefox-only permissions.
- After staging, `web-ext build --source-dir .firefox-build --artifacts-dir web-ext-artifacts --overwrite-dest` creates the final AMO package.

### Source code notes

- Source files are plain JavaScript, HTML, and CSS.
- No source files are transpiled, concatenated, minified, or obfuscated.
- No webpack, Babel, TypeScript, template engine, or code generation pipeline is used.
- The only build processing is the Firefox staging step in `scripts/prepare-firefox.js`, which copies files and adjusts Firefox manifest permissions.

## Manifest Notes

- Shared root manifest keeps Chrome support intact.
- Firefox build adds `webRequestBlocking` and `webRequestFilterResponse` through `scripts/prepare-firefox.js`.
- Shared permissions include `storage`, `webRequest`, and `host_permissions` for `<all_urls>`.
- `browser_specific_settings.gecko` uses the stable ID `ice-pick@komichris`.
- `data_collection_permissions.required` is set to `["none"]`.

## Example Rule

```json
{
  "id": "rule_init_game",
  "name": "Init Game",
  "enabled": true,
  "method": "POST",
  "matchType": "file",
  "matchValue": "initGame",
  "fields": [
    {
      "label": "User ID",
      "path": "payload.userData.userId"
    },
    {
      "label": "Experiment",
      "path": "payload.experimentId"
    }
  ]
}
```

For a URL ending in `/InitService/initGame`, use `matchType: "file"` and `matchValue: "initGame"`.

## Known Limitation

Ice Pick only captures requests while DevTools and the Ice Pick panel are open.
