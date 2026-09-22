const { app, BrowserWindow, WebContentsView, ipcMain, session, shell, Notification, Menu, dialog } = require("electron");
const { spawn } = require("node:child_process");
const path = require("node:path");
const fs = require("node:fs");

let mainWindow;
let rendererServer;
let activeAccountId = null;
let panelBounds = { x: 260, y: 74, width: 1024, height: 700 };
const accountViews = new Map();
const profileSyncTimers = new Map();
const chromeVersion = process.versions.chrome;
const chromeMajor = chromeVersion.split(".")[0];
const extensionConfigPath = () => path.join(app.getPath("userData"), "account-extensions.json");

function readExtensionConfig() {
  try { return JSON.parse(fs.readFileSync(extensionConfigPath(), "utf8")); } catch { return {}; }
}

function writeExtensionConfig(config) {
  fs.writeFileSync(extensionConfigPath(), JSON.stringify(config, null, 2), "utf8");
}

async function loadAccountExtension(accountId, extensionPath) {
  const accountSession = session.fromPartition(`persist:whatsapp-${safeAccountId(accountId)}`);
  const manifestPath = path.join(extensionPath, "manifest.json");
  if (!fs.existsSync(manifestPath)) throw new Error("A pasta escolhida não contém o manifest.json do WaSeller.");
  const extension = accountSession.extensions?.loadExtension
    ? await accountSession.extensions.loadExtension(extensionPath)
    : await accountSession.loadExtension(extensionPath);
  if (extension.id !== "illemhbijpiebjfilfmgebahaakajkpe") {
    const remover = accountSession.extensions?.removeExtension ?? accountSession.removeExtension?.bind(accountSession);
    remover?.(extension.id);
    throw new Error("A pasta escolhida não pertence à extensão oficial WaSeller.");
  }
  return extension;
}

const chromeUserAgent = `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${chromeVersion} Safari/537.36`;

function safeAccountId(value) {
  return String(value || "").replace(/[^a-zA-Z0-9_-]/g, "-").slice(0, 80);
}

function applyBounds(view) {
  if (!view || !mainWindow || mainWindow.isDestroyed()) return;
  const [windowWidth, windowHeight] = mainWindow.getContentSize();
  view.setBounds({
    x: Math.max(0, Math.round(panelBounds.x)),
    y: Math.max(0, Math.round(panelBounds.y)),
    width: Math.max(1, Math.min(Math.round(panelBounds.width), windowWidth)),
    height: Math.max(1, Math.min(Math.round(panelBounds.height), windowHeight)),
  });
}

function setVisibleAccount(accountId) {
  activeAccountId = accountId;
  for (const [id, view] of accountViews) {
    view.setVisible(id === accountId);
    if (id === accountId) applyBounds(view);
  }
}

async function syncAccountProfile(accountId, view) {
  if (view.webContents.isDestroyed()) return;
  try {
    const profile = await view.webContents.executeJavaScript(`(async () => {
      const markers = Array.from(document.querySelectorAll("span, div")).filter((element) => /\\((você|voce|you)\\)/i.test((element.textContent || "").trim()));
      for (const marker of markers) {
        let row = marker;
        for (let level = 0; level < 7 && row; level += 1, row = row.parentElement) {
          const text = (row.innerText || "").trim();
          const phone = text.match(/(?:\\+?55\\s*)?(?:\\(?\\d{2}\\)?\\s*)?9?\\d{4}[-\\s]?\\d{4}/)?.[0];
          const image = row.querySelector("img");
          if (!phone || !image) continue;
          const nameLine = text.split("\\n").map(line => line.trim()).find(line => /\\((você|voce|you)\\)/i.test(line));
          const profileName = nameLine?.replace(/\\s*\\((você|voce|you)\\)\\s*/i, "").trim();
          let photoUrl = "";
          try {
            const canvas = document.createElement("canvas");
            canvas.width = image.naturalWidth || 256;
            canvas.height = image.naturalHeight || 256;
            canvas.getContext("2d").drawImage(image, 0, 0, canvas.width, canvas.height);
            photoUrl = canvas.toDataURL("image/png");
          } catch {
            photoUrl = image.src || "";
            if (photoUrl.startsWith("blob:")) {
              try {
                const blob = await fetch(photoUrl).then(response => response.blob());
                photoUrl = await new Promise((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.onerror = reject; reader.readAsDataURL(blob); });
              } catch { photoUrl = ""; }
            }
          }
          const avatarRect = image.getBoundingClientRect();
          if (profileName) return { profileName, phoneNumber: phone, photoUrl, avatarBounds: { x: avatarRect.x, y: avatarRect.y, width: avatarRect.width, height: avatarRect.height } };
        }
      }
      const navigationAvatar = Array.from(document.querySelectorAll("img")).map(image => ({ image, rect: image.getBoundingClientRect() })).find(({ rect }) => rect.x >= 8 && rect.x < 70 && rect.y > window.innerHeight - 90 && rect.width >= 20 && rect.width <= 60);
      if (navigationAvatar) return { avatarBounds: { x: navigationAvatar.rect.x, y: navigationAvatar.rect.y, width: navigationAvatar.rect.width, height: navigationAvatar.rect.height } };
      return null;
    })()`);
    if (profile?.avatarBounds && !profile.photoUrl) {
      const bounds = profile.avatarBounds;
      const image = await view.webContents.capturePage({ x: Math.max(0, Math.round(bounds.x)), y: Math.max(0, Math.round(bounds.y)), width: Math.max(1, Math.round(bounds.width)), height: Math.max(1, Math.round(bounds.height)) });
      profile.photoUrl = image.toDataURL();
    }
    if (profile && (profile.profileName || profile.photoUrl)) mainWindow?.webContents.send("whatsapp:profile", { accountId, ...profile });
  } catch (error) {
    console.warn(`[profile-sync:${accountId}]`, error.message);
  }
}

function scheduleProfileSync(accountId, view) {
  clearInterval(profileSyncTimers.get(accountId));
  setTimeout(() => void syncAccountProfile(accountId, view), 5000);
  profileSyncTimers.set(accountId, setInterval(() => void syncAccountProfile(accountId, view), 30000));
}

function createAccountView(accountId) {
  const id = safeAccountId(accountId);
  if (!id) throw new Error("Identificador de conta inválido.");
  if (accountViews.has(id)) return accountViews.get(id);

  const accountSession = session.fromPartition(`persist:whatsapp-${id}`);
  accountSession.setUserAgent(chromeUserAgent, "pt-BR");
  accountSession.webRequest.onBeforeSendHeaders((details, callback) => {
    details.requestHeaders["User-Agent"] = chromeUserAgent;
    details.requestHeaders["sec-ch-ua"] = `"Google Chrome";v="${chromeMajor}", "Chromium";v="${chromeMajor}", "Not_A Brand";v="99"`;
    details.requestHeaders["sec-ch-ua-mobile"] = "?0";
    details.requestHeaders["sec-ch-ua-platform"] = '"Windows"';
    callback({ requestHeaders: details.requestHeaders });
  });
  accountSession.setPermissionRequestHandler((webContents, permission, callback) => {
    const allowedOrigin = webContents.getURL().startsWith("https://web.whatsapp.com/");
    callback(allowedOrigin && ["media", "notifications", "clipboard-sanitized-write"].includes(permission));
  });

  const view = new WebContentsView({
    webPreferences: {
      session: accountSession,
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
      spellcheck: true,
    },
  });

  view.webContents.setUserAgent(chromeUserAgent);
  view.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith("https://")) shell.openExternal(url);
    return { action: "deny" };
  });
  view.webContents.on("did-finish-load", () => scheduleProfileSync(id, view));
  view.webContents.on("page-title-updated", (_event, title) => {
    const match = title.match(/^\((\d+)\)/);
    mainWindow?.webContents.send("whatsapp:unread", { accountId: id, count: match ? Number(match[1]) : 0 });
  });
  view.webContents.on("did-fail-load", (_event, code, description, url, isMainFrame) => {
    if (isMainFrame) mainWindow?.webContents.send("whatsapp:error", { accountId: id, code, description, url });
  });

  mainWindow.contentView.addChildView(view);
  accountViews.set(id, view);
  applyBounds(view);
  view.setVisible(false);
  view.webContents.loadURL("https://web.whatsapp.com/");
  return view;
}

function removeAccountView(accountId) {
  const id = safeAccountId(accountId);
  const view = accountViews.get(id);
  if (!view) return;
  mainWindow?.contentView.removeChildView(view);
  view.webContents.close();
  accountViews.delete(id);
  clearInterval(profileSyncTimers.get(id));
  profileSyncTimers.delete(id);
  if (activeAccountId === id) activeAccountId = null;
}

function startRendererServer() {
  const serverEntry = path.join(process.resourcesPath, "app-output", "server", "index.mjs");
  const port = 43127;
  rendererServer = spawn(process.execPath, [serverEntry], {
    env: { ...process.env, ELECTRON_RUN_AS_NODE: "1", HOST: "127.0.0.1", PORT: String(port), NODE_ENV: "production" },
    stdio: "ignore",
    windowsHide: true,
  });
  return new Promise((resolve, reject) => {
    const startedAt = Date.now();
    const check = () => {
      const request = require("node:http").get(`http://127.0.0.1:${port}/`, response => {
        response.resume();
        resolve(port);
      });
      request.on("error", () => {
        if (rendererServer?.exitCode !== null) return reject(new Error("Servidor da interface encerrou antes de iniciar."));
        if (Date.now() - startedAt > 15000) return reject(new Error("Tempo esgotado ao iniciar a interface."));
        setTimeout(check, 200);
      });
    };
    check();
  });
}

async function createMainWindow() {
  mainWindow = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 1024,
    minHeight: 640,
    title: "Central WhatsApp",
    backgroundColor: "#111827",
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  mainWindow.on("resize", () => {
    const active = accountViews.get(activeAccountId);
    if (active) applyBounds(active);
  });
  mainWindow.on("closed", () => {
    for (const view of accountViews.values()) view.webContents.close();
    accountViews.clear();
    mainWindow = null;
  });
  mainWindow.once("ready-to-show", () => mainWindow.show());

  if (!app.isPackaged) {
    await mainWindow.loadURL(process.env.ELECTRON_RENDERER_URL || "http://127.0.0.1:3000");
  } else {
    const port = await startRendererServer();
    await mainWindow.loadURL(`http://127.0.0.1:${port}`);
  }
}

app.whenReady().then(async () => {
  app.userAgentFallback = chromeUserAgent;
  Menu.setApplicationMenu(null);
  ipcMain.handle("whatsapp:select-account", (_event, accountId) => {
    const id = safeAccountId(accountId);
    createAccountView(id);
    setVisibleAccount(id);
    return true;
  });
  ipcMain.handle("whatsapp:add-account", (_event, account) => {
    const id = safeAccountId(account?.id);
    createAccountView(id);
    setVisibleAccount(id);
    return true;
  });
  ipcMain.handle("whatsapp:remove-account", (_event, accountId) => removeAccountView(accountId));
  ipcMain.handle("whatsapp:reload-account", (_event, accountId) => accountViews.get(safeAccountId(accountId))?.webContents.reload());
  ipcMain.handle("whatsapp:disconnect-account", async (_event, accountId) => {
    const id = safeAccountId(accountId);
    removeAccountView(id);
    await session.fromPartition(`persist:whatsapp-${id}`).clearStorageData();
    return true;
  });
  ipcMain.handle("whatsapp:set-visible", (_event, visible) => {
    const active = accountViews.get(activeAccountId);
    if (active) active.setVisible(Boolean(visible));
    return true;
  });
  ipcMain.handle("whatsapp:set-bounds", (_event, bounds) => {
    panelBounds = bounds;
    const active = accountViews.get(activeAccountId);
    if (active) applyBounds(active);
  });
  ipcMain.handle("app:notification", (_event, title, body) => {
    if (Notification.isSupported()) new Notification({ title, body }).show();
  });
  ipcMain.handle("app:set-badge", (_event, count) => {
    if (process.platform === "darwin" || process.platform === "linux") app.setBadgeCount(Number(count) || 0);
  });
  await createMainWindow();
});

app.on("window-all-closed", () => {
  rendererServer?.kill();
  if (process.platform !== "darwin") app.quit();
});
