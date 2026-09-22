import { createFileRoute } from "@tanstack/react-router";
import { CentralWhatsAppApp } from "@/features/central-whatsapp/CentralWhatsAppApp";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: [
      { title: "Central WhatsApp — Suas contas em um só lugar" },
      { name: "description", content: "Interface desktop para organizar e alternar entre várias contas de atendimento." },
      { property: "og:title", content: "Central WhatsApp" },
      { property: "og:description", content: "Organize suas contas e conversas em uma única janela." },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
    ],
  }),
  component: Index,
});

function Index() {
  return <CentralWhatsAppApp />;
}
