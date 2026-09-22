# Central WhatsApp — primeira versão navegável

## Objetivo
Criar uma interface desktop em português do Brasil para centralizar contas simuladas, com tema escuro, navegação completa e persistência local, sem incorporar o WhatsApp Web e sem backend.

## O que será construído
- Estrutura de aplicativo ocupando toda a janela, otimizada para telas a partir de 1024 px.
- Barra lateral recolhível com fotos de perfil fictícias, fallback por iniciais, nome real do perfil, telefone, conexão, não lidas e total geral.
- Três contas iniciais (São Bento, Criatta e Pessoal), cada uma com conversas e mensagens próprias.
- Lista de conversas com busca, filtros, seleção e ação para marcar mensagens como lidas.
- Painel de conversa com histórico, composição e envio por botão ou Enter.
- Cabeçalho da conta com recarregar, silenciar e menu funcional para renomear, alterar cor, reordenar, desconectar e excluir.
- Cadastro de conta em duas etapas, incluindo simulação do futuro QR Code.
- Configurações funcionais para geral, notificações, privacidade e contas.
- Persistência de contas, configurações e última seleção no armazenamento local do dispositivo.
- Camada `desktopBridge` isolando as ações que futuramente serão ligadas ao Electron.

## Organização e acabamento
- Componentes menores e reutilizáveis para navegação, contas, conversas, mensagens, diálogos, indicadores e configurações.
- Tokens visuais centralizados: grafite, verde discreto, tipografia legível, bordas suaves, sombras contidas e movimentos rápidos.
- Ícones Lucide e controles acessíveis com rótulos e dicas de contexto.
- Comentário explícito indicando o ponto futuro de montagem do `WebContentsView`.
- Metadados próprios do aplicativo e revisão final em tela desktop, incluindo ações, persistência, textos e console.

## Limites desta etapa
- Nenhum backend, nuvem, API não oficial ou automação.
- Nenhum iframe ou tentativa de carregar `web.whatsapp.com`.
- A integração real com Electron e sessões independentes ficará preparada, mas não será implementada agora.
