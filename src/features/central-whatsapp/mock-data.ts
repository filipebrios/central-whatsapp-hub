import type { Account, Settings } from "./types";

export const initialSettings: Settings = {
  startWithWindows: true,
  keepInBackground: true,
  openLastAccount: true,
  theme: "dark",
  notifications: true,
  showAccountName: true,
  playSound: false,
  showTotalUnread: true,
  lockWithPassword: false,
  hideMessagePreview: false,
};

export const initialAccounts: Account[] = [];
