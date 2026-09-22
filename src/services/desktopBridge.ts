import type { Account } from "@/features/central-whatsapp/types";

// Contrato único para a futura integração com IPC, WebContentsView e sessões Electron.
export const desktopBridge = {
  addAccount: (account: Account) => console.info("[desktopBridge] Conta adicionada", account.id),
  removeAccount: (accountId: string) => console.info("[desktopBridge] Conta removida", accountId),
  selectAccount: (accountId: string) => console.info("[desktopBridge] Conta selecionada", accountId),
  reloadAccount: (accountId: string) => console.info("[desktopBridge] Conta recarregada", accountId),
  connectAccount: (accountId: string) => console.info("[desktopBridge] Conta conectada", accountId),
  disconnectAccount: (accountId: string) => console.info("[desktopBridge] Conta desconectada", accountId),
  showNotification: (title: string, body: string) => console.info("[desktopBridge] Notificação", title, body),
  updateUnreadCount: (count: number) => console.info("[desktopBridge] Não lidas", count),
};