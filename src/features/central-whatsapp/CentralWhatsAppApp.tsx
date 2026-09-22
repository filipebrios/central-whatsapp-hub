import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import {
  Archive, Bell, BellOff, Check, CheckCheck, ChevronLeft, ChevronRight, CircleHelp,
  EllipsisVertical, LockKeyhole, MessageCircleMore, Mic, MonitorCog, Paperclip, Plus,
  QrCode, RefreshCw, Search, Send, Settings as SettingsIcon, ShieldCheck, Smile, Star,
  Trash2, UserRound, Volume2, Wifi, X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import { desktopBridge } from "@/services/desktopBridge";
import { initialAccounts, initialSettings } from "./mock-data";
import type { Account, Conversation, Settings } from "./types";

const ACCOUNTS_KEY = "central-whatsapp:accounts:v1";
const SETTINGS_KEY = "central-whatsapp:settings:v1";
const ACTIVE_KEY = "central-whatsapp:active-account";
const colors = ["emerald", "violet", "blue", "rose", "amber"];
const colorClass: Record<string, string> = { emerald: "bg-avatar-emerald", violet: "bg-avatar-violet", blue: "bg-avatar-blue", rose: "bg-avatar-rose", amber: "bg-avatar-amber" };

function initials(name: string) { return name.split(" ").filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase(); }
function now() { return new Intl.DateTimeFormat("pt-BR", { hour: "2-digit", minute: "2-digit" }).format(new Date()); }

function ProfileAvatar({ account, size = "md" }: { account: Account; size?: "sm" | "md" | "lg" }) {
  const [failed, setFailed] = useState(false);
  const sizeClass = size === "lg" ? "size-12" : size === "sm" ? "size-9" : "size-11";
  return (
    <div className={cn("relative shrink-0 overflow-hidden rounded-full ring-1 ring-border", sizeClass, colorClass[account.accountColor] ?? "bg-primary")}>
      {account.photoUrl && !failed ? <img src={account.photoUrl} alt={`Foto de ${account.profileName}`} width={816} height={816} loading="lazy" onError={() => setFailed(true)} className="size-full object-cover" /> : <span className="flex size-full items-center justify-center text-xs font-bold text-avatar-foreground">{initials(account.profileName)}</span>}
    </div>
  );
}

function UnreadBadge({ count }: { count: number }) { return count > 0 ? <span className="flex min-w-5 h-5 items-center justify-center rounded-full bg-primary px-1.5 text-[11px] font-bold text-primary-foreground">{count > 99 ? "99+" : count}</span> : null; }

function AccountSidebar({ accounts, activeId, expanded, totalUnread, onSelect, onAdd, onSettings, onToggle }: { accounts: Account[]; activeId: string; expanded: boolean; totalUnread: number; onSelect: (id: string) => void; onAdd: () => void; onSettings: () => void; onToggle: () => void }) {
  return <aside className={cn("flex h-screen shrink-0 flex-col border-r border-sidebar-border bg-sidebar transition-[width] duration-200", expanded ? "w-[260px]" : "w-[82px]")}>
    <div className={cn("flex h-[74px] items-center border-b border-sidebar-border px-4", expanded ? "justify-between" : "justify-center")}>
      <div className="relative flex items-center gap-3"><div className="grid size-10 place-items-center rounded-lg bg-primary text-primary-foreground shadow-action"><MessageCircleMore className="size-5" /></div>{expanded && <div><p className="text-sm font-semibold text-sidebar-foreground">Central WhatsApp</p><p className="flex items-center gap-1.5 text-[11px] text-status"><Wifi className="size-3" /> {accounts.filter(a => a.connectionStatus === "connected").length} contas online</p></div>} {!expanded && totalUnread > 0 && <span className="absolute -right-2 -top-2 flex size-5 items-center justify-center rounded-full bg-primary text-[10px] font-bold text-primary-foreground">{totalUnread}</span>}</div>
      {expanded && <UnreadBadge count={totalUnread} />}
    </div>
    <div className="flex-1 space-y-2 overflow-y-auto p-3">
      {accounts.map(account => <Tooltip key={account.id}><TooltipTrigger asChild><button type="button" onClick={() => onSelect(account.id)} className={cn("relative flex w-full items-center rounded-lg border transition-all", expanded ? "gap-3 p-2.5 text-left" : "justify-center p-2", activeId === account.id ? "border-sidebar-primary/40 bg-sidebar-accent shadow-inset-accent" : "border-transparent hover:bg-sidebar-accent/60")}>
        <div className="relative"><ProfileAvatar account={account} size={expanded ? "lg" : "md"} />{account.connectionStatus === "connected" && <span className="absolute bottom-0 right-0 size-3 rounded-full border-2 border-sidebar bg-status" />}</div>
        {expanded && <div className="min-w-0 flex-1"><div className="flex items-center justify-between gap-2"><p className="truncate text-sm font-semibold text-sidebar-foreground">{account.profileName}</p><UnreadBadge count={account.unreadCount} /></div><p className="mt-0.5 truncate text-xs text-sidebar-muted">{account.phoneNumber}</p><p className="mt-1 flex items-center gap-1 text-[10px] text-status"><span className="size-1.5 rounded-full bg-status" />{account.connectionStatus === "connected" ? "Conectado" : "Desconectado"}</p></div>}
        {!expanded && account.unreadCount > 0 && <span className="absolute right-0 top-0 flex min-w-5 h-5 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-bold text-primary-foreground">{account.unreadCount}</span>}
      </button></TooltipTrigger>{!expanded && <TooltipContent side="right"><p className="font-medium">{account.profileName}</p><p className="opacity-70">{account.phoneNumber}</p></TooltipContent>}</Tooltip>)}
    </div>
    <div className="space-y-1 border-t border-sidebar-border p-3">
      <SidebarAction icon={Plus} label="Adicionar conta" expanded={expanded} onClick={onAdd} />
      <SidebarAction icon={SettingsIcon} label="Configurações" expanded={expanded} onClick={onSettings} />
      <SidebarAction icon={expanded ? ChevronLeft : ChevronRight} label={expanded ? "Recolher barra" : "Expandir barra"} expanded={expanded} onClick={onToggle} />
    </div>
  </aside>;
}

function SidebarAction({ icon: Icon, label, expanded, onClick }: { icon: typeof Plus; label: string; expanded: boolean; onClick: () => void }) {
  return <Tooltip><TooltipTrigger asChild><Button type="button" variant="ghost" onClick={onClick} className={cn("w-full text-sidebar-muted hover:text-sidebar-foreground", expanded ? "justify-start" : "px-0")}><Icon />{expanded && <span>{label}</span>}</Button></TooltipTrigger>{!expanded && <TooltipContent side="right">{label}</TooltipContent>}</Tooltip>;
}

function ActiveAccountHeader({ account, onAction }: { account: Account; onAction: (action: string) => void }) {
  return <header className="flex h-[74px] shrink-0 items-center justify-between border-b border-border bg-surface px-5">
    <div className="flex min-w-0 items-center gap-3"><ProfileAvatar account={account} /><div className="min-w-0"><div className="flex items-center gap-2"><h1 className="truncate text-sm font-semibold">{account.profileName}</h1>{account.accountLabel && <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">{account.accountLabel}</span>}</div><p className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">{account.phoneNumber}<span>•</span><span className={account.connectionStatus === "connected" ? "text-status" : "text-destructive"}>{account.connectionStatus === "connected" ? "Conectado" : "Desconectado"}</span></p></div></div>
    <div className="flex items-center gap-1">
      <Tooltip><TooltipTrigger asChild><Button variant="ghost" size="icon" aria-label="Recarregar conta" onClick={() => onAction("reload")}><RefreshCw /></Button></TooltipTrigger><TooltipContent>Recarregar conta</TooltipContent></Tooltip>
      <Tooltip><TooltipTrigger asChild><Button variant="ghost" size="icon" aria-label={account.muted ? "Ativar notificações" : "Silenciar notificações"} onClick={() => onAction("mute")}>{account.muted ? <BellOff /> : <Bell />}</Button></TooltipTrigger><TooltipContent>{account.muted ? "Ativar notificações" : "Silenciar notificações"}</TooltipContent></Tooltip>
      <DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" aria-label="Mais opções"><EllipsisVertical /></Button></DropdownMenuTrigger><DropdownMenuContent align="end" className="w-52"><DropdownMenuItem onSelect={() => onAction("rename")}>Renomear conta</DropdownMenuItem><DropdownMenuItem onSelect={() => onAction("color")}>Alterar cor</DropdownMenuItem><DropdownMenuItem onSelect={() => onAction("reorder")}>Reordenar contas</DropdownMenuItem><DropdownMenuSeparator /><DropdownMenuItem onSelect={() => onAction("disconnect")}>{account.connectionStatus === "connected" ? "Desconectar" : "Conectar"}</DropdownMenuItem><DropdownMenuItem className="text-destructive focus:text-destructive" onSelect={() => onAction("delete")}><Trash2 />Excluir conta</DropdownMenuItem></DropdownMenuContent></DropdownMenu>
    </div>
  </header>;
}

function ConversationList({ conversations, selectedId, onSelect }: { conversations: Conversation[]; selectedId: string | undefined; onSelect: (id: string) => void }) {
  const [query, setQuery] = useState(""); const [filter, setFilter] = useState<"all" | "unread" | "favorites">("all");
  const filtered = conversations.filter(c => c.name.toLowerCase().includes(query.toLowerCase()) && (filter === "all" || filter === "unread" && c.unread > 0 || filter === "favorites" && c.favorite));
  return <section className="flex w-[340px] shrink-0 flex-col border-r border-border bg-panel">
    <div className="border-b border-border p-4"><div className="flex items-center justify-between"><h2 className="text-base font-semibold">Conversas</h2><Button variant="ghost" size="icon" aria-label="Arquivadas"><Archive /></Button></div><div className="relative mt-3"><Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"/><Input value={query} onChange={e => setQuery(e.target.value)} placeholder="Pesquisar conversas" className="h-9 bg-muted pl-9" /></div><div className="mt-3 flex gap-1">{([['all','Todas'],['unread','Não lidas'],['favorites','Favoritas']] as const).map(([key,label]) => <Button key={key} size="sm" variant={filter === key ? "default" : "ghost"} onClick={() => setFilter(key)}>{label}</Button>)}</div></div>
    <div className="flex-1 overflow-y-auto">{filtered.map(conversation => <button key={conversation.id} type="button" onClick={() => onSelect(conversation.id)} className={cn("flex w-full gap-3 border-b border-border/60 px-4 py-3 text-left transition-colors hover:bg-accent/50", selectedId === conversation.id && "bg-accent")}><div className="grid size-11 shrink-0 place-items-center rounded-full bg-muted text-xs font-semibold text-muted-foreground">{conversation.initials}</div><div className="min-w-0 flex-1"><div className="flex items-center justify-between gap-2"><p className="truncate text-sm font-medium">{conversation.name}</p><span className={cn("text-[11px]", conversation.unread ? "text-primary" : "text-muted-foreground")}>{conversation.time}</span></div><div className="mt-1 flex items-center justify-between gap-2"><p className="truncate text-xs text-muted-foreground">{conversation.preview}</p><UnreadBadge count={conversation.unread} /></div></div></button>)}{filtered.length === 0 && <div className="px-6 py-12 text-center text-sm text-muted-foreground"><Search className="mx-auto mb-3 size-5"/>Nenhuma conversa encontrada.</div>}</div>
  </section>;
}

function ChatPanel({ conversation, onSend, onMarkRead }: { conversation: Conversation | undefined; onSend: (text: string) => void; onMarkRead: () => void }) {
  const [text, setText] = useState("");
  if (!conversation) return <section className="grid flex-1 place-items-center bg-chat"><div className="text-center text-muted-foreground"><MessageCircleMore className="mx-auto mb-3 size-8"/><p>Selecione uma conversa para começar.</p></div></section>;
  const submit = (event: FormEvent) => { event.preventDefault(); const clean = text.trim(); if (!clean) return; onSend(clean); setText(""); };
  return <section className="flex min-w-0 flex-1 flex-col bg-chat">
    <div className="flex h-[66px] shrink-0 items-center justify-between border-b border-border bg-surface px-5"><div className="flex items-center gap-3"><div className="relative grid size-10 place-items-center rounded-full bg-muted text-xs font-semibold">{conversation.initials}{conversation.online && <span className="absolute bottom-0 right-0 size-2.5 rounded-full border-2 border-surface bg-status" />}</div><div><h2 className="text-sm font-semibold">{conversation.name}</h2><p className="text-xs text-muted-foreground">{conversation.online ? "online agora" : "visto recentemente"}</p></div></div>{conversation.unread > 0 && <Button size="sm" variant="outline" onClick={onMarkRead}><CheckCheck />Marcar como lida</Button>}</div>
    {/* Futuramente, o WebContentsView da sessão ativa ocupará esta região. */}
    <div className="flex-1 overflow-y-auto px-[8%] py-6"><div className="mx-auto mb-5 w-fit rounded-full bg-muted/80 px-3 py-1 text-[10px] font-medium text-muted-foreground">HOJE</div><div className="mx-auto max-w-3xl space-y-2">{conversation.messages.map(message => <div key={message.id} className={cn("flex", message.outgoing ? "justify-end" : "justify-start")}><div className={cn("max-w-[72%] rounded-lg px-3.5 py-2.5 shadow-message", message.outgoing ? "rounded-br-sm bg-message-out" : "rounded-bl-sm bg-message-in")}><p className="text-sm leading-relaxed">{message.text}</p><div className="mt-1 flex items-center justify-end gap-1 text-[10px] text-muted-foreground"><span>{message.time}</span>{message.outgoing && <CheckCheck className="size-3 text-receipt" />}</div></div></div>)}</div></div>
    <form onSubmit={submit} className="flex items-end gap-2 border-t border-border bg-surface p-3"><Button type="button" variant="ghost" size="icon" aria-label="Emoji" onClick={() => setText(current => `${current} 😊`)}><Smile /></Button><Button type="button" variant="ghost" size="icon" aria-label="Anexar arquivo" onClick={() => setText(current => `${current} [arquivo simulado]`)}><Paperclip /></Button><Input value={text} onChange={e => setText(e.target.value)} placeholder="Digite uma mensagem" aria-label="Mensagem" className="h-10 flex-1 bg-muted"/><Button type="button" variant="ghost" size="icon" aria-label="Gravar áudio" onClick={() => setText(current => `${current} [áudio simulado]`)}><Mic /></Button><Button type="submit" size="icon" aria-label="Enviar mensagem" disabled={!text.trim()}><Send /></Button></form>
  </section>;
}

function DesktopWhatsAppPanel({ accountId }: { accountId: string }) {
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const panel = panelRef.current;
    if (!panel) return;
    const syncBounds = () => {
      const rect = panel.getBoundingClientRect();
      desktopBridge.setWhatsAppBounds({ x: rect.x, y: rect.y, width: rect.width, height: rect.height });
    };
    syncBounds();
    const observer = new ResizeObserver(syncBounds);
    observer.observe(panel);
    window.addEventListener("resize", syncBounds);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", syncBounds);
    };
  }, []);

  useEffect(() => {
    desktopBridge.selectAccount(accountId);
  }, [accountId]);

  return <section ref={panelRef} className="relative min-w-0 flex-1 overflow-hidden bg-chat">
    <div className="grid h-full place-items-center text-sm text-muted-foreground">
      Carregando WhatsApp Web…
    </div>
  </section>;
}

function AddAccountDialog({ open, onOpenChange, onAdd }: { open: boolean; onOpenChange: (open: boolean) => void; onAdd: (account: Account) => void }) {
  const [step, setStep] = useState(1); const [name, setName] = useState(""); const [phone, setPhone] = useState(""); const [color, setColor] = useState("emerald"); const [avatarInitials, setAvatarInitials] = useState("");
  const reset = () => { setStep(1); setName(""); setPhone(""); setColor("emerald"); setAvatarInitials(""); };
  const connect = () => { const profileName = name.trim(); const account: Account = { id: `account-${Date.now()}`, profileName, phoneNumber: phone.trim(), accountColor: color, connectionStatus: "connected", unreadCount: 0, conversations: [{ id: `welcome-${Date.now()}`, name: "Conversa de exemplo", initials: avatarInitials || "CE", preview: "Conta conectada com sucesso.", time: now(), unread: 0, favorite: false, messages: [{ id: "1", text: "Conta conectada com sucesso. Esta conversa é uma simulação.", time: now(), outgoing: false }] }] }; onAdd(account); reset(); onOpenChange(false); };
  return <Dialog open={open} onOpenChange={value => { onOpenChange(value); if (!value) reset(); }}><DialogContent><DialogHeader><DialogTitle>Adicionar nova conta</DialogTitle><DialogDescription>Etapa {step} de 2 — {step === 1 ? "Identificação da conta" : "Vincular dispositivo"}</DialogDescription></DialogHeader>{step === 1 ? <div className="space-y-4"><Field label="Nome do perfil"><Input value={name} onChange={e => setName(e.target.value)} placeholder="Ex.: Loja Centro" autoFocus /></Field><Field label="Número de telefone"><Input value={phone} onChange={e => setPhone(e.target.value)} placeholder="+55 00 00000-0000" /></Field><Field label="Iniciais"><Input value={avatarInitials} onChange={e => setAvatarInitials(e.target.value.slice(0, 2).toUpperCase())} placeholder={initials(name || "Nova Conta")} /></Field><Field label="Cor da conta"><div className="flex gap-2">{colors.map(item => <button key={item} type="button" aria-label={`Selecionar cor ${item}`} onClick={() => setColor(item)} className={cn("size-8 rounded-full border-2", colorClass[item], color === item ? "border-foreground" : "border-transparent")}><span className="sr-only">{item}</span></button>)}</div></Field></div> : <div className="py-2"><div className="mx-auto grid aspect-square w-48 place-items-center rounded-lg border border-dashed border-primary/40 bg-muted"><QrCode className="size-20 text-muted-foreground" /></div><p className="mx-auto mt-5 max-w-sm text-center text-sm leading-relaxed text-muted-foreground">Na versão desktop, o QR Code do WhatsApp aparecerá aqui. Use o celular da conta desejada para vincular este dispositivo.</p></div>}<DialogFooter>{step === 1 ? <><Button variant="ghost" onClick={() => onOpenChange(false)}>Cancelar</Button><Button disabled={!name.trim() || !phone.trim()} onClick={() => setStep(2)}>Continuar</Button></> : <><Button variant="ghost" onClick={() => onOpenChange(false)}>Cancelar</Button><Button variant="outline" onClick={() => setStep(1)}>Voltar</Button><Button onClick={connect}><Wifi />Simular conexão</Button></>}</DialogFooter></DialogContent></Dialog>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) { return <label className="block"><span className="mb-1.5 block text-xs font-medium text-muted-foreground">{label}</span>{children}</label>; }
function SettingRow({ label, description, checked, onChange }: { label: string; description: string; checked: boolean; onChange: (value: boolean) => void }) { return <div className="flex items-center justify-between gap-5 py-3"><div><p className="text-sm font-medium">{label}</p><p className="mt-0.5 text-xs text-muted-foreground">{description}</p></div><Switch checked={checked} onCheckedChange={onChange} /></div>; }

function SettingsDialog({ open, onOpenChange, settings, onSettings, accounts, onRename, onColor, onRemove, onMove, onClear }: { open: boolean; onOpenChange: (o: boolean) => void; settings: Settings; onSettings: (s: Settings) => void; accounts: Account[]; onRename: (id: string) => void; onColor: (id: string) => void; onRemove: (id: string) => void; onMove: (id: string) => void; onClear: () => void }) {
  const [section, setSection] = useState<"geral" | "notificacoes" | "privacidade" | "contas">("geral"); const update = (key: keyof Settings, value: boolean | string) => onSettings({ ...settings, [key]: value });
  const sections = [{ id: "geral", label: "Geral", icon: MonitorCog }, { id: "notificacoes", label: "Notificações", icon: Bell }, { id: "privacidade", label: "Privacidade", icon: ShieldCheck }, { id: "contas", label: "Contas", icon: UserRound }] as const;
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent className="max-w-3xl gap-0 overflow-hidden p-0"><DialogHeader className="border-b border-border p-5"><DialogTitle>Configurações</DialogTitle><DialogDescription>Personalize o funcionamento do Central WhatsApp.</DialogDescription></DialogHeader><div className="grid min-h-[480px] grid-cols-[180px_1fr]"><nav className="border-r border-border bg-panel p-3">{sections.map(item => <Button key={item.id} variant={section === item.id ? "secondary" : "ghost"} onClick={() => setSection(item.id)} className="mb-1 w-full justify-start"><item.icon />{item.label}</Button>)}</nav><div className="max-h-[520px] overflow-y-auto p-6"><h3 className="mb-4 text-xs font-bold uppercase text-muted-foreground">{sections.find(s => s.id === section)?.label}</h3>
    {section === "geral" && <div className="divide-y divide-border"><SettingRow label="Iniciar com o Windows" description="Abrir o aplicativo ao iniciar o computador." checked={settings.startWithWindows} onChange={v => update("startWithWindows", v)} /><SettingRow label="Manter em segundo plano" description="Continuar recebendo atualizações ao fechar a janela." checked={settings.keepInBackground} onChange={v => update("keepInBackground", v)} /><SettingRow label="Abrir na última conta" description="Retomar a conta usada mais recentemente." checked={settings.openLastAccount} onChange={v => update("openLastAccount", v)} /><Field label="Tema"><div className="flex gap-2">{([['dark','Escuro'],['light','Claro'],['system','Automático']] as const).map(([value,label]) => <Button key={value} size="sm" variant={settings.theme === value ? "default" : "outline"} onClick={() => update("theme", value)}>{label}</Button>)}</div></Field><div className="mt-5"><Field label="Idioma"><Input value="Português do Brasil" disabled /></Field></div></div>}
    {section === "notificacoes" && <div className="divide-y divide-border"><SettingRow label="Ativar notificações" description="Receber alertas de novas mensagens." checked={settings.notifications} onChange={v => update("notifications", v)} /><SettingRow label="Mostrar nome da conta" description="Identificar a conta que recebeu a mensagem." checked={settings.showAccountName} onChange={v => update("showAccountName", v)} /><SettingRow label="Reproduzir som" description="Tocar um aviso ao receber mensagens." checked={settings.playSound} onChange={v => update("playSound", v)} /><SettingRow label="Exibir contador total" description="Somar mensagens não lidas de todas as contas." checked={settings.showTotalUnread} onChange={v => update("showTotalUnread", v)} /></div>}
    {section === "privacidade" && <div className="divide-y divide-border"><SettingRow label="Bloquear com senha" description="Solicitar senha ao abrir o aplicativo." checked={settings.lockWithPassword} onChange={v => update("lockWithPassword", v)} /><SettingRow label="Ocultar prévia das mensagens" description="Não mostrar o conteúdo nas notificações." checked={settings.hideMessagePreview} onChange={v => update("hideMessagePreview", v)} /><div className="pt-5"><Button variant="destructive" onClick={onClear}><Trash2 />Limpar dados locais</Button></div></div>}
    {section === "contas" && <div className="space-y-2">{accounts.map(account => <div key={account.id} className="flex items-center gap-3 rounded-lg border border-border bg-card p-3"><ProfileAvatar account={account} /><div className="min-w-0 flex-1"><p className="truncate text-sm font-medium">{account.profileName}</p><p className="text-xs text-muted-foreground">{account.phoneNumber}</p></div><DropdownMenu><DropdownMenuTrigger asChild><Button size="icon" variant="ghost" aria-label={`Opções de ${account.profileName}`}><EllipsisVertical /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem onSelect={() => onRename(account.id)}>Renomear</DropdownMenuItem><DropdownMenuItem onSelect={() => onColor(account.id)}>Alterar cor</DropdownMenuItem><DropdownMenuItem onSelect={() => onMove(account.id)}>Reordenar</DropdownMenuItem><DropdownMenuSeparator/><DropdownMenuItem className="text-destructive focus:text-destructive" onSelect={() => onRemove(account.id)}>Remover</DropdownMenuItem></DropdownMenuContent></DropdownMenu></div>)}</div>}
  </div></div></DialogContent></Dialog>;
}

function SimpleEditDialog({ mode, account, onClose, onSave }: { mode: "rename" | "color" | null; account: Account | undefined; onClose: () => void; onSave: (value: string) => void }) {
  const [value, setValue] = useState(""); useEffect(() => { if (mode === "rename" && account) setValue(account.profileName); if (mode === "color" && account) setValue(account.accountColor); }, [mode, account]);
  return <Dialog open={Boolean(mode)} onOpenChange={open => { if (!open) onClose(); }}><DialogContent><DialogHeader><DialogTitle>{mode === "rename" ? "Renomear perfil" : "Alterar cor da conta"}</DialogTitle><DialogDescription>{mode === "rename" ? "Atualize o nome exibido para este perfil." : "Escolha a cor usada quando não houver foto."}</DialogDescription></DialogHeader>{mode === "rename" ? <Input value={value} onChange={e => setValue(e.target.value)} autoFocus /> : <div className="flex gap-3 py-3">{colors.map(item => <button key={item} type="button" aria-label={`Cor ${item}`} onClick={() => setValue(item)} className={cn("size-10 rounded-full border-2", colorClass[item], value === item ? "border-foreground" : "border-transparent")} />)}</div>}<DialogFooter><Button variant="ghost" onClick={onClose}>Cancelar</Button><Button onClick={() => { if (value.trim()) onSave(value.trim()); }}>Salvar</Button></DialogFooter></DialogContent></Dialog>;
}

export function CentralWhatsAppApp() {
  const firstAccountId = initialAccounts.at(0)?.id ?? "";
  const [accounts, setAccounts] = useState<Account[]>(initialAccounts); const [settings, setSettings] = useState<Settings>(initialSettings); const [activeId, setActiveId] = useState(firstAccountId); const [selectedConversation, setSelectedConversation] = useState<Record<string, string>>({}); const [expanded, setExpanded] = useState(true); const [addOpen, setAddOpen] = useState(false); const [settingsOpen, setSettingsOpen] = useState(false); const [edit, setEdit] = useState<{ mode: "rename" | "color"; id: string } | null>(null); const [hydrated, setHydrated] = useState(false);
  useEffect(() => { try { const savedAccounts = localStorage.getItem(ACCOUNTS_KEY); const savedSettings = localStorage.getItem(SETTINGS_KEY); const savedActive = localStorage.getItem(ACTIVE_KEY); if (savedAccounts) setAccounts(JSON.parse(savedAccounts)); if (savedSettings) setSettings(JSON.parse(savedSettings)); if (savedActive) setActiveId(savedActive); } catch { console.warn("Não foi possível carregar as configurações locais."); } finally { setHydrated(true); } }, []);
  useEffect(() => {
    const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
    document.documentElement.classList.toggle("dark", settings.theme === "dark" || (settings.theme === "system" && prefersDark));
  }, [settings.theme]);
  useEffect(() => { if (!hydrated) return; localStorage.setItem(ACCOUNTS_KEY, JSON.stringify(accounts)); localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); localStorage.setItem(ACTIVE_KEY, activeId); desktopBridge.updateUnreadCount(accounts.reduce((sum, a) => sum + a.unreadCount, 0)); }, [accounts, settings, activeId, hydrated]);
  const active = accounts.find(a => a.id === activeId) ?? accounts[0];
  useEffect(() => { const firstConversation = active?.conversations.at(0); if (active && !selectedConversation[active.id] && firstConversation) setSelectedConversation(current => ({ ...current, [active.id]: firstConversation.id })); }, [active, selectedConversation]);
  const conversation = active ? active.conversations.find(c => c.id === selectedConversation[active.id]) : undefined; const totalUnread = useMemo(() => accounts.reduce((sum, account) => sum + account.unreadCount, 0), [accounts]);
  if (!active) return <div className="grid h-screen place-items-center bg-background"><div className="text-center"><MessageCircleMore className="mx-auto mb-3 size-10 text-primary"/><h1 className="text-lg font-semibold">Nenhuma conta cadastrada</h1><p className="mt-1 text-sm text-muted-foreground">Adicione uma conta para começar.</p><Button className="mt-5" onClick={() => setAddOpen(true)}><Plus />Adicionar conta</Button><AddAccountDialog open={addOpen} onOpenChange={setAddOpen} onAdd={account => { setAccounts([account]); setActiveId(account.id); }} /></div></div>;
  const updateAccount = (id: string, updater: (account: Account) => Account) => setAccounts(current => current.map(a => a.id === id ? updater(a) : a));
  const remove = (id: string) => { if (!window.confirm("Excluir esta conta simulada?")) return; desktopBridge.removeAccount(id); const remaining = accounts.filter(a => a.id !== id); setAccounts(remaining); if (id === activeId && remaining[0]) setActiveId(remaining[0].id); };
  const reorder = (id: string) => { const index = accounts.findIndex(a => a.id === id); if (index <= 0) return; const reordered = [...accounts]; const previous = reordered[index - 1]; const current = reordered[index]; if (!previous || !current) return; reordered[index - 1] = current; reordered[index] = previous; setAccounts(reordered); };
  const handleAction = (action: string) => { if (action === "reload") { desktopBridge.reloadAccount(active.id); } else if (action === "mute") updateAccount(active.id, a => ({ ...a, muted: !a.muted })); else if (action === "rename" || action === "color") setEdit({ mode: action, id: active.id }); else if (action === "reorder") reorder(active.id); else if (action === "delete") remove(active.id); else if (action === "disconnect") { const connected = active.connectionStatus === "connected"; updateAccount(active.id, a => ({ ...a, connectionStatus: connected ? "disconnected" : "connected" })); connected ? desktopBridge.disconnectAccount(active.id) : desktopBridge.connectAccount(active.id); } };
  const markRead = () => { if (!conversation) return; updateAccount(active.id, account => { const removed = account.conversations.find(c => c.id === conversation.id)?.unread ?? 0; return { ...account, unreadCount: Math.max(0, account.unreadCount - removed), conversations: account.conversations.map(c => c.id === conversation.id ? { ...c, unread: 0 } : c) }; }); };
  const send = (text: string) => { if (!conversation) return; updateAccount(active.id, account => ({ ...account, conversations: account.conversations.map(c => c.id === conversation.id ? { ...c, preview: text, time: now(), messages: [...c.messages, { id: crypto.randomUUID(), text, time: now(), outgoing: true }] } : c) })); desktopBridge.showNotification(active.profileName, "Mensagem simulada enviada"); };
  return <TooltipProvider delayDuration={250}><div className="flex h-screen min-w-[1024px] overflow-hidden bg-background text-foreground"><AccountSidebar accounts={accounts} activeId={active.id} expanded={expanded} totalUnread={settings.showTotalUnread ? totalUnread : 0} onSelect={id => { setActiveId(id); desktopBridge.selectAccount(id); }} onAdd={() => setAddOpen(true)} onSettings={() => setSettingsOpen(true)} onToggle={() => setExpanded(v => !v)} /><main className="flex min-w-0 flex-1 flex-col"><ActiveAccountHeader account={active} onAction={handleAction} /><div className="flex min-h-0 flex-1">{desktopBridge.isDesktop ? <DesktopWhatsAppPanel accountId={active.id} /> : <><ConversationList conversations={active.conversations} selectedId={conversation?.id} onSelect={id => { setSelectedConversation(current => ({ ...current, [active.id]: id })); }} /><ChatPanel conversation={conversation} onSend={send} onMarkRead={markRead} /></>}</div></main></div><AddAccountDialog open={addOpen} onOpenChange={setAddOpen} onAdd={account => { setAccounts(current => [...current, account]); setActiveId(account.id); desktopBridge.addAccount(account); }} /><SettingsDialog open={settingsOpen} onOpenChange={setSettingsOpen} settings={settings} onSettings={setSettings} accounts={accounts} onRename={id => setEdit({ mode: "rename", id })} onColor={id => setEdit({ mode: "color", id })} onRemove={remove} onMove={reorder} onClear={() => { if (window.confirm("Limpar contas e configurações locais e restaurar os dados simulados?")) { localStorage.clear(); setAccounts(initialAccounts); setSettings(initialSettings); setActiveId(firstAccountId); } }} /><SimpleEditDialog mode={edit?.mode ?? null} account={accounts.find(a => a.id === edit?.id)} onClose={() => setEdit(null)} onSave={value => { if (!edit) return; updateAccount(edit.id, a => edit.mode === "rename" ? { ...a, profileName: value } : { ...a, accountColor: value }); setEdit(null); }} /></TooltipProvider>;
}