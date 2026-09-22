import type { Account } from "@/features/central-whatsapp/types";

type Bounds = { x: number; y: number; width: number; height: number };
type DesktopApi = {
  isDesktop: true;
  addAccount: (account: Account) => Promise<boolean>;
  removeAccount: (accountId: string) => Promise<void>;
  selectAccount: (accountId: string) => Promise<boolean>;
  reloadAccount: (accountId: string) => Promise<void>;
  connectAccount: (accountId: string) => Promise<boolean>;
  disconnectAccount: (accountId: string) => Promise<boolean>;
  installExtension: (accountId: string) => Promise<{ canceled: boolean; id?: string; name?: string; version?: string }>;
  installExtension: (accountId: string) => api()?.installExtension(accountId),
  setWhatsAppBounds: (bounds: Bounds) => Promise<void>;
  showNotification: (title: string, body: string) => Promise<void>;
  updateUnreadCount: (count: number) => Promise<void>;
  onUnread: (callback: (payload: { accountId: string; count: number }) => void) => () => void;
};

declare global {
  interface Window {
    centralDesktop?: DesktopApi;
  }
}

const api = () => window.centralDesktop;

export const desktopBridge = {
  isDesktop: Boolean(typeof window !== "undefined" && window.centralDesktop?.isDesktop),
  addAccount: (account: Account) => api()?.addAccount(account) ?? console.info("[desktopBridge] Conta adicionada", account.id),
  removeAccount: (accountId: string) => api()?.removeAccount(accountId) ?? console.info("[desktopBridge] Conta removida", accountId),
  selectAccount: (accountId: string) => api()?.selectAccount(accountId) ?? console.info("[desktopBridge] Conta selecionada", accountId),
  reloadAccount: (accountId: string) => api()?.reloadAccount(accountId) ?? console.info("[desktopBridge] Conta recarregada", accountId),
  connectAccount: (accountId: string) => api()?.connectAccount(accountId) ?? console.info("[desktopBridge] Conta conectada", accountId),
  disconnectAccount: (accountId: string) => api()?.disconnectAccount(accountId) ?? console.info("[desktopBridge] Conta desconectada", accountId),
  setWhatsAppBounds: (bounds: Bounds) => api()?.setWhatsAppBounds(bounds),
  showNotification: (title: string, body: string) => api()?.showNotification(title, body) ?? console.info("[desktopBridge] Notificação", title, body),
  updateUnreadCount: (count: number) => api()?.updateUnreadCount(count) ?? console.info("[desktopBridge] Não lidas", count),
  onUnread: (callback: (payload: { accountId: string; count: number }) => void) => api()?.onUnread(callback) ?? (() => undefined),
};
