const { app, BrowserWindow, WebContentsView, ipcMain, session, shell, Notification, Menu } = require("electron");
const { spawn } = require("node:child_process");
const path = require("node:path");

let mainWindow;
let rendererServer;
let activeAccountId = null;
let panelBounds = { x: 260, y: 74, width: 1024, height: 700 };
const accountViews = new Map();
const chromeVersion = process.versions.chrome;
const chromeMajor = chromeVersion.split(".")[0];
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
