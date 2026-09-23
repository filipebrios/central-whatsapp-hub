# MODUX — Guia obrigatório para agentes

Este arquivo é a fonte central de contexto técnico e histórico do MODUX. Todo agente que atuar no projeto deve lê-lo integralmente antes de analisar, editar, compilar ou publicar qualquer alteração.

> O projeto está conectado ao Lovable. Nunca reescreva histórico publicado: não use force push, rebase, amend ou squash em commits já enviados.

## Regra de manutenção

Em toda solicitação nova:

1. Leia este arquivo antes de agir.
2. Confira o estado real do código, da branch e das releases; não presuma que uma solicitação anterior foi concluída.
3. Preserve sessões, contas e dados do usuário sempre que possível.
4. Faça mudanças pequenas, rastreáveis e testáveis.
5. Compile e valide antes de declarar que algo está pronto.
6. Atualize as seções **Estado atual**, **Pendências** e **Registro de mudanças** no mesmo commit da alteração.
7. Nunca registre como concluído algo que ainda não foi compilado e testado.
8. Não mantenha dois caminhos concorrentes como se fossem o mesmo produto. Identifique claramente qual implementação gera cada instalador.

O registro deve permanecer objetivo. Detalhes extensos pertencem a commits, issues ou documentação específica, não a este arquivo.

## Objetivo do produto

O MODUX é um aplicativo desktop para centralizar várias contas independentes do WhatsApp Web em uma única interface. Cada conta deve possuir sessão, foto, nome, número, estado de conexão e contador de mensagens próprios.

Requisitos permanentes:

- Interface em português do Brasil.
- Identidade MODUX: Slate Deep `#0B132B`, Electric Cobalt `#1D4ED8`, Cyan `#06B6D4` e branco.
- Menu lateral recolhível com conta ativa claramente destacada.
- Foto, nome, telefone, conexão e mensagens não lidas por conta.
- Total de não lidas dentro do aplicativo e no ícone do sistema.
- Som e notificação de novas mensagens.
- Clique na notificação deve abrir a conta correspondente.
- Links clicados dentro das conversas devem abrir em janela externa/separada, sem substituir o WhatsApp da conta.
- Gerenciamento de contas: adicionar, renomear, escolher/buscar foto, reordenar, desconectar e remover.
- Limpeza de cache sem apagar cookies, sessões ou logins.
- Fechar a janela deve manter o aplicativo na bandeja quando essa opção estiver ativa.
- Inicialização automática opcional.
- Compatibilidade pretendida: Windows, macOS e Linux.
- WaSeller deve ser instalado somente após escolha explícita da conta. Nunca instalar automaticamente em todas as contas.

## Arquiteturas existentes

### 1. `webview2-prototype/` — versão Windows em produção

- Tecnologia: .NET 8, WinForms e Microsoft WebView2.
- Plataforma: somente Windows.
- Situação: foi a base das releases públicas `1.0.0` a `1.3.0`.
- Dados locais: `%LOCALAPPDATA%\CentralWhatsApp\WebView2Prototype`.
- Vantagem: WhatsApp e WaSeller funcionaram nos testes do usuário.
- Limitação: não pode gerar versões para macOS ou Linux.

### 2. `electron/` + `src/` — base multiplataforma em desenvolvimento

- Tecnologia: Electron, React, TanStack Start e `WebContentsView`.
- Sessões independentes: partições `persist:whatsapp-<id>`.
- Plataformas pretendidas: Windows, macOS e Linux.
- Situação: ainda não substituiu a versão WebView2 e não deve ser anunciada como pronta.
- Risco conhecido: WaSeller não possui suporte oficial a Electron; tratar como experimental e validar por plataforma.

## Estado atual confirmado

- Repositório público: `filipebrios/central-whatsapp-hub`.
- Branch de desenvolvimento e releases atuais: `codex/electron-windows-mvp`.
- Pull request existente: PR #1, mantido como rascunho; não mesclar sem solicitação explícita.
- Última release pública confirmada: `MODUX 1.3.0`.
- Candidata em preparação: `MODUX 1.4.0` para Windows, baseada diretamente na 1.3.0.
- A 1.4.0 só pode ser marcada como publicada após o workflow concluir e o instalador ser confirmado na release.
- A release 1.3.0 foi compilada a partir de `webview2-prototype/`, portanto continua exclusiva do Windows.
- Por decisão do usuário, qualquer versão externa atribuída ao Manus deve ser ignorada. A continuidade parte da release 1.3.0.
- Existem alterações locais ainda não publicadas iniciando a migração Electron. Elas não são uma release e não devem ser confundidas com a versão instalada.

## Funcionalidades já implementadas na versão WebView2

- Múltiplas contas com diretórios de sessão separados.
- Menu lateral expansível/recolhível.
- Contadores por conta obtidos do WhatsApp Web.
- Captura automática de foto com escolha manual como alternativa.
- Gerenciador de contas.
- Instalação manual do WaSeller somente na conta ativa.
- Busca automática da pasta do WaSeller em Chrome, Edge, Brave e Opera.
- Limpeza de cache imediata e complementar na próxima inicialização.
- Execução única, inicialização com Windows e permanência na bandeja.
- Instalador Inno Setup, atalhos e identidade MODUX.
- Logs em `%LOCALAPPDATA%\CentralWhatsApp\WebView2Prototype\modux.log`.
- Versão exibida no título a partir da 1.2.0.

## Pontos que não devem ser considerados resolvidos

- Indicador da conta ativa e contador na barra de tarefas da 1.3.0 não ficaram visíveis/funcionais para o usuário.
- Compatibilidade com macOS e Linux ainda não foi entregue.
- Captura automática da foto do próprio perfil continua dependente do DOM mutável do WhatsApp; a escolha manual é o fallback confiável.
- Som e notificações precisam de teste real com mensagem recebida.
- Alterações atribuídas ao Manus não estão no repositório conhecido.

## Pendências priorizadas

1. Compilar e validar a candidata 1.4.0 no Windows, preservando contas e sessões.
2. Confirmar que links das conversas abrem externamente e não substituem o WhatsApp.
3. Confirmar visualmente o cartão da conta ativa e o badge total na barra de tarefas.
4. Concluir a base Electron e gerar instaladores reais para Windows, macOS e Linux.
5. No macOS usar Dock badge; no Linux usar a integração disponível no ambiente.
6. Testar WhatsApp Web e WaSeller em cada plataforma antes da publicação.
7. Criar migração segura dos dados da versão WebView2 para a futura versão Electron, quando tecnicamente possível.

## Política de versões e publicação

- Correções da base WebView2 usam versões `1.x`.
- A primeira versão Electron realmente multiplataforma deve usar `2.0.0-beta` até concluir testes nas três plataformas.
- Cada release deve identificar claramente plataforma, arquitetura e estado beta/estável.
- Nunca publicar release somente porque o workflow iniciou. Aguarde conclusão bem-sucedida e confirme os assets.
- Não substituir ou apagar releases funcionais durante testes.

## Verificação mínima obrigatória

Antes de entregar uma versão:

- Compilação concluída sem erros.
- Instalador correspondente presente na release.
- Aplicativo inicia e mostra a interface.
- Troca entre pelo menos duas contas mantém sessões isoladas.
- Conta ativa aparece corretamente identificada.
- Contadores atualizam sem tocar som no primeiro carregamento.
- Link externo não substitui a página do WhatsApp.
- Fechar/restaurar pela bandeja funciona na plataforma suportada.
- Limpeza de cache preserva login.
- WaSeller, quando aplicável, permanece limitado à conta escolhida.

## Registro de mudanças

### 2026-09-23 — candidata MODUX 1.4.0

- Continuidade definida diretamente a partir da 1.3.0; alterações externas do Manus foram descartadas.
- Corrigido o CLSID usado pelo badge da barra de tarefas do Windows (`...344`).
- Criado cartão permanente “EM USO AGORA” com foto, nome e estado da conta ativa.
- Links externos passam a abrir no navegador padrão e não substituem o WhatsApp Web.
- Versão, instalador e workflow atualizados para 1.4.0.
- Estado: aguardando compilação bem-sucedida e confirmação do asset da release.

### 2026-09-23 — organização do contexto

- Criado este guia central para impedir perda de contexto e divergência entre implementações.
- Documentadas as duas arquiteturas, releases, funcionalidades, limitações e pendências reais.
- Registrado o requisito de abrir links em janela separada.
- Registrada a necessidade de auditar a versão externa produzida pelo Manus antes de integrá-la.

### 2026-09-23 — MODUX 1.3.0

- Adicionada tentativa de identificação da conta ativa no título/barra superior.
- Adicionada tentativa de badge de mensagens na barra de tarefas do Windows.
- Resultado reportado pelo usuário: recursos não apareceram como esperado; permanecem pendentes.

### 2026-09-23 — MODUX 1.2.0

- Atualização da identidade MODUX, versão no título, ícones e notificações.
- Remoção de atalhos antigos “Central WhatsApp”.

### 2026-09-23 — MODUX 1.1.0

- WaSeller passou a exigir instalação manual por conta.
- Adicionados limpeza de cache, logs e escolha manual de foto.

### 2026-09-23 — MODUX 1.0.0

- Renomeação de Central WhatsApp para MODUX.
- Aplicação inicial da marca e criação do instalador automático.
