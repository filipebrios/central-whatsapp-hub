MODUX 1.9.0 beta para Windows x64, baseado em .NET 8 / WinForms / WebView2.

Inclui Google Calendar com OAuth local protegido, vínculo explícito com atendimento compartilhado, criação recuperável por ID, proteção contra conflitos de revisão/ETag, cadastro PF/PJ, serviços/cores compartilhados, filtros e CSV, fila manual de confirmações/lembretes/pós-venda, links exclusivos de confirmação e NPS e convite Google confirmado pelo operador. Temas claro/escuro revisados nos controles nativos.

Atualiza a instalação existente e mantém o diretório de perfis WhatsApp/WaSeller. Não migra para Electron nem apaga os dados locais. Na primeira conexão Google do aplicativo principal, importe o JSON Desktop já existente no seu computador, autorize no navegador e escolha/confirme o calendário; o diagnóstico isolado usa armazenamento separado.

Limites desta beta:
- WhatsApp é sempre revisado e enviado manualmente na conta explicitamente escolhida. Não há robô 24h com PC desligado.
- Pedido de cancelamento pelo cliente entra para revisão da equipe e interrompe lembretes pendentes; não apaga automaticamente evento Google.
- Eventos recorrentes são tratados pela ocorrência selecionada. Séries completas e edição de eventos de vários dias continuam pelo Google Agenda.
- Checkpoints cifrados são dos períodos carregados, não backup completo do banco/Google nem restauração automática.
- Link de avaliações Google deve ser configurado pelo administrador com a URL oficial da São Bento.
- OAuth externo em modo de teste pode exigir nova autorização conforme regras do Google.
- A validação real de sessões WhatsApp, WaSeller, notificações e fluxo integrado no computador do usuário acontece nesta rodada única de feedback.

Acompanhe as restrições e o estado de validação no repositório. Não contém credenciais Google ou chaves administrativas do Supabase.
