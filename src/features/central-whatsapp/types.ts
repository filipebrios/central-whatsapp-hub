export type ConnectionStatus = "connected" | "disconnected";

export type Message = {
  id: string;
  text: string;
  time: string;
  outgoing: boolean;
};

export type Conversation = {
  id: string;
  name: string;
  initials: string;
  preview: string;
  time: string;
  unread: number;
  favorite: boolean;
  online?: boolean;
  messages: Message[];
};

export type Account = {
  id: string;
  photoUrl?: string;
  profileName: string;
  phoneNumber: string;
  accountColor: string;
  connectionStatus: ConnectionStatus;
  unreadCount: number;
  accountLabel?: string;
  muted?: boolean;
  conversations: Conversation[];
};

export type Settings = {
  startWithWindows: boolean;
  keepInBackground: boolean;
  openLastAccount: boolean;
  theme: "dark" | "light" | "system";
  notifications: boolean;
  showAccountName: boolean;
  playSound: boolean;
  showTotalUnread: boolean;
  lockWithPassword: boolean;
  hideMessagePreview: boolean;
};