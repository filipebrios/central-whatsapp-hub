const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("centralDesktop", {
  isDesktop: true,
  addAccount: (account) => ipcRenderer.invoke("whatsapp:add-account", account),
  removeAccount: (accountId) => ipcRenderer.invoke("whatsapp:remove-account", accountId),
  selectAccount: (accountId) => ipcRenderer.invoke("whatsapp:select-account", accountId),
  reloadAccount: (accountId) => ipcRenderer.invoke("whatsapp:reload-account", accountId),
  connectAccount: (accountId) => ipcRenderer.invoke("whatsapp:select-account", accountId),
  disconnectAccount: (accountId) => ipcRenderer.invoke("whatsapp:disconnect-account", accountId),
  installExtension: (accountId) => ipcRenderer.invoke("whatsapp:install-extension", accountId),
  setWhatsAppBounds: (bounds) => ipcRenderer.invoke("whatsapp:set-bounds", bounds),
  showNotification: (title, body) => ipcRenderer.invoke("app:notification", title, body),
  updateUnreadCount: (count) => ipcRenderer.invoke("app:set-badge", count),
  onUnread: (callback) => {
    const listener = (_event, payload) => callback(payload);
    ipcRenderer.on("whatsapp:unread", listener);
    return () => ipcRenderer.removeListener("whatsapp:unread", listener);
  },
});
