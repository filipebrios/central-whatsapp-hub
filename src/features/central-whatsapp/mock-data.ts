import saoBentoPhoto from "@/assets/profile-sao-bento.jpg";
import criattaPhoto from "@/assets/profile-criatta.jpg";
import pessoalPhoto from "@/assets/profile-pessoal.jpg";
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

export const initialAccounts: Account[] = [
  {
    id: "sao-bento",
    photoUrl: saoBentoPhoto,
    profileName: "Padaria São Bento",
    accountLabel: "São Bento",
    phoneNumber: "+55 35 0000-0000",
    accountColor: "emerald",
    connectionStatus: "connected",
    unreadCount: 7,
    conversations: [
      { id: "sb-1", name: "Mariana Alves", initials: "MA", preview: "Pode separar dois pães italianos?", time: "10:42", unread: 4, favorite: true, online: true, messages: [
        { id: "1", text: "Bom dia! Vocês ainda têm pão italiano?", time: "10:38", outgoing: false },
        { id: "2", text: "Bom dia, Mariana! Temos sim, acabaram de sair do forno.", time: "10:40", outgoing: true },
        { id: "3", text: "Pode separar dois pães italianos?", time: "10:42", outgoing: false },
      ]},
      { id: "sb-2", name: "Restaurante Ipê", initials: "RI", preview: "Pedido confirmado para amanhã.", time: "09:18", unread: 2, favorite: true, messages: [{ id: "1", text: "Pedido confirmado para amanhã. Obrigado!", time: "09:18", outgoing: false }] },
      { id: "sb-3", name: "Camila Ribeiro", initials: "CR", preview: "Qual o horário de atendimento?", time: "Ontem", unread: 1, favorite: false, messages: [{ id: "1", text: "Qual o horário de atendimento aos domingos?", time: "Ontem", outgoing: false }] },
      { id: "sb-4", name: "Fornecedor Minas", initials: "FM", preview: "A entrega saiu para o destino.", time: "Ontem", unread: 0, favorite: false, messages: [{ id: "1", text: "A entrega saiu para o destino.", time: "Ontem", outgoing: false }] },
    ],
  },
  {
    id: "criatta",
    photoUrl: criattaPhoto,
    profileName: "Criatta Estúdio",
    accountLabel: "Criatta",
    phoneNumber: "+55 11 98888-2410",
    accountColor: "violet",
    connectionStatus: "connected",
    unreadCount: 3,
    conversations: [
      { id: "cr-1", name: "Lucas — Orion", initials: "LO", preview: "Aprovamos a segunda proposta!", time: "11:03", unread: 2, favorite: true, online: true, messages: [
        { id: "1", text: "Oi! Revisamos as opções de identidade.", time: "10:57", outgoing: false },
        { id: "2", text: "Ótimo, Lucas. Qual direção funcionou melhor?", time: "11:00", outgoing: true },
        { id: "3", text: "Aprovamos a segunda proposta!", time: "11:03", outgoing: false },
      ]},
      { id: "cr-2", name: "Equipe Criativa", initials: "EC", preview: "Reunião movida para as 15h.", time: "10:16", unread: 1, favorite: false, messages: [{ id: "1", text: "Pessoal, reunião movida para as 15h.", time: "10:16", outgoing: false }] },
      { id: "cr-3", name: "Fernanda Melo", initials: "FM", preview: "Enviei os arquivos finais.", time: "Seg", unread: 0, favorite: true, messages: [{ id: "1", text: "Enviei os arquivos finais na pasta do projeto.", time: "Seg", outgoing: false }] },
    ],
  },
  {
    id: "pessoal",
    photoUrl: pessoalPhoto,
    profileName: "Filipe Rios",
    accountLabel: "Pessoal",
    phoneNumber: "+55 35 99999-2026",
    accountColor: "blue",
    connectionStatus: "connected",
    unreadCount: 0,
    conversations: [
      { id: "pe-1", name: "Família", initials: "FA", preview: "Combinado, domingo ao meio-dia.", time: "09:32", unread: 0, favorite: true, messages: [
        { id: "1", text: "Vamos almoçar juntos neste domingo?", time: "09:20", outgoing: false },
        { id: "2", text: "Combinado, domingo ao meio-dia.", time: "09:32", outgoing: true },
      ]},
      { id: "pe-2", name: "Rafael Costa", initials: "RC", preview: "Depois te mando as fotos.", time: "Ontem", unread: 0, favorite: false, messages: [{ id: "1", text: "Depois te mando as fotos da viagem.", time: "Ontem", outgoing: false }] },
      { id: "pe-3", name: "Clube de Corrida", initials: "CC", preview: "Treino confirmado às 6h30.", time: "Sex", unread: 0, favorite: false, messages: [{ id: "1", text: "Treino confirmado às 6h30 no parque.", time: "Sex", outgoing: false }] },
    ],
  },
];