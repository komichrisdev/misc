export const ext = globalThis.browser ?? globalThis.chrome;

const usesPromiseApi = typeof globalThis.browser !== "undefined";
const isFirefoxRuntime = typeof globalThis.browser !== "undefined";

function runtimeError() {
  return ext.runtime && ext.runtime.lastError ? new Error(ext.runtime.lastError.message) : null;
}

export function storageGet(keys) {
  if (usesPromiseApi) {
    return ext.storage.local.get(keys);
  }

  return new Promise((resolve, reject) => {
    ext.storage.local.get(keys, (result) => {
      const error = runtimeError();
      if (error) {
        reject(error);
        return;
      }
      resolve(result || {});
    });
  });
}

export function storageSet(values) {
  if (usesPromiseApi) {
    return ext.storage.local.set(values);
  }

  return new Promise((resolve, reject) => {
    ext.storage.local.set(values, () => {
      const error = runtimeError();
      if (error) {
        reject(error);
        return;
      }
      resolve();
    });
  });
}

export function createDevtoolsPanel(title, iconPath, pagePath) {
  if (usesPromiseApi) {
    return ext.devtools.panels.create(title, iconPath, pagePath);
  }

  return new Promise((resolve, reject) => {
    try {
      ext.devtools.panels.create(title, iconPath, pagePath, (panel) => {
        const error = runtimeError();
        if (error) {
          reject(error);
          return;
        }
        resolve(panel);
      });
    } catch (error) {
      reject(error);
    }
  });
}

export function getDevtoolsNetwork() {
  return ext && ext.devtools ? ext.devtools.network || null : null;
}

export function getHarLog() {
  const networkApi = getDevtoolsNetwork();
  if (!networkApi || typeof networkApi.getHAR !== "function") {
    return Promise.reject(new Error("HAR export is unavailable."));
  }

  if (networkApi.getHAR.length === 0) {
    return Promise.resolve(networkApi.getHAR());
  }

  return new Promise((resolve, reject) => {
    try {
      networkApi.getHAR((harLog) => {
        const error = runtimeError();
        if (error) {
          reject(error);
          return;
        }
        resolve(harLog);
      });
    } catch (error) {
      reject(error);
    }
  });
}

export function getInspectedTabId() {
  return ext && ext.devtools && ext.devtools.inspectedWindow
    ? ext.devtools.inspectedWindow.tabId ?? null
    : null;
}

export function isFirefoxBrowser() {
  return isFirefoxRuntime;
}

export function sendRuntimeMessage(message) {
  if (!ext || !ext.runtime || typeof ext.runtime.sendMessage !== "function") {
    return Promise.reject(new Error("Runtime messaging API is unavailable."));
  }

  if (usesPromiseApi) {
    return ext.runtime.sendMessage(message);
  }

  return new Promise((resolve, reject) => {
    ext.runtime.sendMessage(message, (response) => {
      const error = runtimeError();
      if (error) {
        reject(error);
        return;
      }
      resolve(response);
    });
  });
}

export function downloadBlob({ blob, filename, saveAs = false, conflictAction = "uniquify" }) {
  if (!ext || !ext.downloads || typeof ext.downloads.download !== "function") {
    return Promise.reject(new Error("Downloads API is unavailable."));
  }

  const objectUrl = URL.createObjectURL(blob);
  const cleanup = () => {
    setTimeout(() => {
      URL.revokeObjectURL(objectUrl);
    }, 60000);
  };

  if (usesPromiseApi) {
    return ext.downloads.download({
      url: objectUrl,
      filename,
      saveAs,
      conflictAction
    }).finally(cleanup);
  }

  return new Promise((resolve, reject) => {
    ext.downloads.download({
      url: objectUrl,
      filename,
      saveAs,
      conflictAction
    }, (downloadId) => {
      cleanup();
      const error = runtimeError();
      if (error) {
        reject(error);
        return;
      }
      resolve(downloadId);
    });
  });
}

export function getRequestContent(request) {
  if (!request || typeof request.getContent !== "function") {
    return Promise.reject(new Error("Request content API is unavailable."));
  }

  if (request.getContent.length === 0) {
    return Promise.resolve(request.getContent()).then((result) => {
      if (Array.isArray(result)) {
        return {
          content: result[0],
          encoding: result[1]
        };
      }

      return result;
    });
  }

  return new Promise((resolve, reject) => {
    try {
      request.getContent((content, encoding) => {
        const error = runtimeError();
        if (error) {
          reject(error);
          return;
        }
        resolve({ content, encoding });
      });
    } catch (error) {
      reject(error);
    }
  });
}
