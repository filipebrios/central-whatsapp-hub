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
- Branch WebView2 com Agenda em manutenção: `feature/modux-agenda-sidebar-v17` (base `codex/electron-windows-mvp`); base Electron continua separada.
- Pull request existente: PR #1, mantido como rascunho; não mesclar sem solicitação explícita.
- Última release beta Windows **validada visualmente pelo usuário**: `MODUX 1.8.1`, com Google iCal em leitura, ~220 eventos de outubro mostrados no MODUX em 20/10/2026. Não equivale a testes automáticos de cor nem sincronização bidirecional.
- Último instalador beta testado visualmente: `MODUX-Setup-1.8.1.exe` na release `modux-v1.8.1-beta1` (Windows x64).
- Página pública oficial de downloads: `https://modux-downloads.filipebrios.chatgpt.site`.
- A 1.4.0 foi compilada com sucesso pelo workflow Windows e publicada na release `modux-v1.4.0`.
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

- Usuário reportou em 06/10 que o rascunho recarrega o WhatsApp na mesma janela e aparece na conta da empresa. A seleção 1.6.0 podia usar conta ativa/primeira. Candidata 1.6.1 exige vínculo explícito e ignora a preferência antiga; validar escolha Criatta, abertura a frio/bandeja e envio manual no Windows.

1. Testar em uso real no Windows se links das conversas abrem externamente e não substituem o WhatsApp.
2. Confirmar visualmente o cartão da conta ativa e o badge total na barra de tarefas.
3. Concluir a base Electron e gerar instaladores reais para Windows, macOS e Linux.
4. No macOS usar Dock badge; no Linux usar a integração disponível no ambiente.
5. Testar WhatsApp Web e WaSeller em cada plataforma antes da publicação.
6. Criar migração segura dos dados da versão WebView2 para a futura versão Electron, quando tecnicamente possível.

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

## Próxima release consolidada: MODUX 1.9.0 (escopo registrado em 20/10/2026)

- **Fonte do escopo**: https://github.com/filipebrios/central-whatsapp-hub/issues/5
- Pedido explícito do usuário: **não gerar novos instaladores a cada pequena alteração**. Desenvolver e testar internamente as etapas, entregando um único instalador consolidado ao final, quando integradas.
- Google iCal atual = **somente leitura**; em 1.9.0 priorizar OAuth 2.0 oficial Google Calendar API para editar eventos do Google sem duplicar registros do Supabase, incluindo IDs/etags, recorrências e conflitos.
- Incluir operação completa, cores por serviço, sessões multicliente e painel de pendências, com ações do WaSeller submetidas ao usuário, **sem declarar envios autônomos fora do expediente com PC desligado**.
- Por último redesenhar visual do **aplicativo inteiro** com inspiração Apple, espaço, cantos arredondados, tipografia licenciada Windows, ícones consistentes, preservando WebView2 e todas as sessões atuais.
- Release 1.9.0 ainda é **planejamento**, sem integração Google API, build nem instalador publicados. Validação por etapa é interna; validação geral com usuário só após integração.

### MODUX 1.9.0 — execução iniciada em branch isolada (20/10/2026)

- Branch `feature/modux-1.9-consolidated`, exclusiva para a próxima release consolidada, derivada da versão com leitura iCal e cores. **Não usar fluxo da 1.8.2 para publicar versões intermediárias**.
- `GoogleDesktopOAuth.cs`: fluxo Google OAuth 2.0 para aplicativo de computador, PKCE, state e callback de loopback no navegador padrão; importa JSON do Google Cloud selecionado pelo usuário, guarda credenciais/tokens cifrados com DPAPI CurrentUser no Windows. Nunca pedir senhas, arquivo JSON ou tokens no chat.
- `GoogleCalendarApi.cs`: leitura oficial de calendários com permissão writer/owner e eventos; criação/edição via PATCH protegido por ETag/If-Match, conferindo antes se houve alteração de terceiros; exclusão só após confirmação explícita; sem envio silencioso de WhatsApp e sem notificações intencionais a convidados; Google pode ter comportamentos próprios de notificação.
- `GoogleCalendarManageDialog.cs`, `GoogleCalendarEventDialog.cs`, `AgendaPanel.cs`: conexão e escolha da agenda; edição pelo duplo clique nos eventos obtidos da API; botão de criação no Google; o iCal mantém modo somente leitura. Modificação de evento recorrente deve ser feita somente na instância selecionada e multi-dia permanece bloqueado até suporte seguro.
- `tests/GoogleCalendarCore`: valida parsing de eventos, fuso, duração e JSON de atualização, **sem chamar o Google real**.
- `.github/workflows/validate-modux-19.yml`: faz testes e `dotnet build` Windows mas **não gera instalador nem release**. Checar sempre conclusão da execução mais recente, porque o usuário quer somente um instalador consolidado no final.
- **Estado: implementação parcial em testes internos, ainda NÃO autorizada em uma conta Google real.** OAuth Desktop exige que o responsável configure credenciais no Google Cloud e autorize o acesso. Não declarar sincronização bidirecional entregue. Pendentes reconciliação Supabase ↔ Google, edição operativa completa, regras de recorrência, fila de mensagens e redesign Apple-like.

## MODUX 1.7.0 — Agenda São Bento (candidata, 16/10/2026)

- Atalho verde Agenda São Bento abaixo dos WhatsApps, com menu recolhível. Não criar abas horizontais.
- Painel Agenda com login Supabase Auth, RLS e consulta/cadastro inicial de serviços.
- Não alterar sessões WhatsApp/WaSeller, nem ligar automações de envio.
- Usuário administrador ainda precisa ser convidado/vinculado no Supabase antes do login.
- GitHub Actions run #38968209127 concluiu com sucesso em 16/10/2026: testes ComposeRequest, compilação .NET, Inno Setup, artefato e release beta (modux-v1.7.0-beta1). Instalador: MODUX-Setup-1.7.0.exe. **Validação real de sessões e WaSeller no Windows ainda pendente.**
- Falha do CRM WaSeller ainda pendente de diagnóstico.

### MODUX 1.7.1 — senha por código de recuperação (16/10/2026)

- `AgendaPasswordDialog.cs` adiciona definição/recuperação de senha diretamente no MODUX por OTP enviado ao e-mail (Supabase Auth `/recover`, `/verify` tipo `recovery`, `/user`).
- Exige que **Authentication → Emails → Reset Password** contenha `{{ .Token }}` para o usuário receber o código; não requer site `localhost:3000`.
- Senha, código e token ficam apenas na memória do aplicativo e são enviados diretamente ao Supabase por HTTPS. Não precisam ser compartilhados com a equipe de desenvolvimento.
- Windows: GitHub Actions run **38971710042** concluiu com sucesso (testes, compilação e instalador Inno Setup), release beta `modux-v1.7.1-beta1` publicada com `MODUX-Setup-1.7.1.exe`.
- **Teste funcional no computador do usuário e recuperação efetiva de senha ainda pendentes**. Não presumir que SMTP/OTP funciona antes de validar.
- Sessões existentes do WhatsApp, WaSeller, Google Agenda e CRM não foram alteradas.

### MODUX 1.8.0 — agenda mensal e formulário ampliado (20/10/2026)

- Acrescentado `AgendaMonthView.cs`: calendário mensal clicável, navegação entre meses e prévia de até duas visitas por dia. Não modifica compromissos; lê dados autorizados por RLS.
- `AgendaPanel.cs`: consulta por Hoje/7 dias/Mês, filtragem por clique no dia, coloração de situações e abertura de novo cadastro no dia selecionado. Mantém acesso Supabase Auth.
- `AgendaAppointmentDialog.cs`: PF/PJ, cliente, responsável, WhatsApp, endereço, e-mail opcional, múltiplos serviços ou serviço customizado, data, hora, duração padrão de 60 min, técnico e observações. Salva dados no Supabase sem disparar WhatsApp nem criar evento no Google.
- A tabela `agenda_service_types` ainda está vazia: catálogo do formulário é local nesta candidata, sem sincronização de configuração entre usuários. Próxima etapa é transferir os serviços para o banco.
- Google Calendar não é sincronizado, compromissos da extensão antiga não foram importados, e CRM WaSeller segue pendente. Não substituir versão 1.7.1 estável sem testar a beta no Windows.
- GitHub Actions run **#39433058396** concluída com sucesso em 20/10/2026: teste ComposeRequest, compilação .NET, instalador Inno Setup, artefato e release beta `modux-v1.8.0-beta1` com `MODUX-Setup-1.8.0.exe`. **Validação visual e teste dos novos formulários no Windows ainda pendentes.**

### MODUX 1.8.1 — leitura Google Agenda por endereço secreto iCal (20/10/2026)

- `GoogleCalendarFeed.cs`: assinatura **somente leitura** de um endereço secreto HTTPS do Google Agenda fornecido pelo usuário diretamente no MODUX; validado host Google, URL cifrada localmente com DPAPI de usuário Windows. Não salvar nem copiar URL ao GitHub, Supabase, logs ou chat.
- Eventos existentes, incluindo recorrências, são expandidos via Ical.Net e mostrados no mesmo calendário e tabela, diferenciados pela situação `Google • leitura`, sem criar/importar registros no Supabase nem duplicar automaticamente.
- Botão **Conectar Google Agenda** no módulo Agenda, com campo oculto para inserir a URL; atualização ao login, ao mudar período e a cada 15 minutos enquanto painel estiver ativo e PC ligado.
- Sem OAuth e sem sincronização bidirecional; alterações no Google ficam apenas no Google. A assinatura privada é específica de um calendário e não deve ser publicada. A revogação é realizada no próprio Google Agenda.
- WaSeller no MODUX continua dependente do computador ligado; respostas fora de expediente quando PC desligado exigem resposta de ausência no aplicativo Android ou serviço oficial em nuvem. Nenhum novo envio automático implementado.
- Correção de compatibilidade do Ical.Net 5.2.3 concluída e GitHub Actions run **#39436070492** finalizado com sucesso: testes ComposeRequest, compilação .NET Windows, Inno Setup e release `modux-v1.8.1-beta1` com asset `MODUX-Setup-1.8.1.exe`. **Usuário confirmou visualmente leitura bem-sucedida dos eventos de outubro em 20/10/2026**. Google continua somente leitura.

### MODUX 1.8.2 — cores de serviços (20/10/2026, candidata)

- `AgendaServiceColors.cs`: classificação determinística por nomes de serviço, descrição do evento ou ID de serviço já registrado no Supabase. Prioridade explícita para cancelado/reagendar. Compromissos desconhecidos permanecem cinza; nunca atribuir cores inventadas aos não reconhecidos.
- Preserva paleta definida pela São Bento: Desinsetização Cobalto `#039BE5`; Desratização Mirtilo `#3F51B5`; Orçamento Banana `#F6BF26`; Confirmar Manjericão `#0B8043`; Cancelado/reagendar Tomate `#D50000`; Internos/curso Abóbora `#F4511E`.
- Acrescenta cores locais auxiliares para descupinização, reservatório, caixa de gordura, sanitização e capina química. Essas cores são propostas e poderão ser configuradas posteriormente.
- `AgendaMonthView.cs` exibe faixa individual colorida por compromisso, suporte a mais de um serviço no dia, tooltip e legenda. `AgendaPanel.cs` mostra o serviço inferido na tabela com célula colorida, em vez de identificar tudo como apenas “Google Agenda”.
- **A fonte iCal não garante trazer cor individual de evento do Google**: a paleta é inferida dos serviços e aplicada só ao MODUX; a cor do Google original nunca é alterada. Os eventos existentes são apenas lidos, sem salvar ou duplicar no Supabase.
- Testes automatizados de classificação e hexadecimais em `tests/AgendaServiceColors/` **passaram**. GitHub Actions **#39440133345** concluído com sucesso em 20/10/2026: testes, compilação Windows, Inno Setup e release beta `modux-v1.8.2-beta1` com `MODUX-Setup-1.8.2.exe`. **Teste visual e conferência da classificação com eventos reais ainda pendentes.**

### 2026-10-21 — etapa 1 de 8: proteções de eventos e testes de regressão

- Decisão do usuário: trabalho em **etapas sequenciais, com verificação explícita e passagem para a próxima somente após evidência**. Continuam proibidas publicações de instaladores intermediários. Checklist em https://github.com/filipebrios/central-whatsapp-hub/issues/5.
- Auditoria de `GoogleCalendarApi.cs` identificou que eventos temporizados atravessando a meia-noite não eram marcados `SpansMultipleDays`, apesar do bloqueio de edição de eventos multidiários. Corrigida a marcação comparando datas locais no fuso `America/Sao_Paulo`.
- `GoogleEventSnapshot.Parse` agora recusa evento dia inteiro com fim exclusivo anterior/igual ao início e eventos temporizados com término anterior/igual ao início.
- `tests/GoogleCalendarCore/Program.cs` expandido com recorrência isolada, virada da meia-noite, dia inteiro de vários dias, inversão de horários, dados incompletos e título inválido, sem acessar agenda verdadeira.
- Apenas código de desenvolvimento e testes; verificar conclusão da execução GitHub Actions mais recente **antes de marcar a etapa como concluída**.
- Próxima etapa: conexão OAuth oficial em uma conta autorizada/teste controlado; credenciais e consentimento Google são bloqueadores explícitos para validação real e não podem ser simulados como êxito.

### 2026-10-22 — etapa 2/8: Google Cloud configurado e trava de escrita isolada

- Usuário confirmou configuração manual do projeto Google Cloud `MODUX - Agenda Sao Bento`: Calendar API ativa, app OAuth tipo Desktop com JSON baixado **somente no computador dele**, testador `agendamento.saobento@gmail.com`, escopos `calendar.calendarlist.readonly` e `calendar.events`. **Nunca copiar credenciais, segredos ou tokens ao GitHub/chat.** O segredo anterior apareceu em um print e o usuário informou substituição das credenciais.
- `GoogleCalendarWriteGuard.cs`: piloto permite Create/Update/Delete exclusivamente quando o calendário autorizado tem nome exato `MODUX - TESTES` e ID secundário `@group.calendar.google.com`. Recusa a agenda principal, inclusive se renomeada com o nome de testes.
- `GoogleDesktopOAuth` aplica a guarda ao selecionar agenda e expõe `CanWriteSelectedCalendar`; `GoogleCalendarApi` aplica `DemandSafeWrite()` antes de cada método de escrita; `AgendaPanel` e `GoogleCalendarManageDialog` deixam explícita a restrição e desabilitam controles de escrita na agenda operacional.
- Testes `GoogleCalendarCore` ampliados para cobrir guarda positiva, autorização ausente, ID principal, ID inválido e nomes que não correspondem exatamente. CI [run #39791746318](https://github.com/filipebrios/central-whatsapp-hub/actions/runs/39791746318) concluiu com sucesso: parser/segurança Google, cores, protocolo de mensagens e compilação Windows, sem instalador.
- **Ainda não está concluída a etapa 2:** validação do consentimento OAuth e operações num calendário Google **real de testes** exige execução no computador do usuário. Não há instalador 1.9 publicado; não anunciar integração operacional validada.
- O Google informa que refresh tokens de aplicativo Externo em modo Testando e com permissões de Calendar expiram em cerca de 7 dias; tratar antes de liberar uso contínuo. Documentação: https://developers.google.com/identity/protocols/oauth2/

### 2026-10-22 — evento de teste preparado e verificação de leitura no MODUX

- Usuário confirmou por captura do Google Agenda a agenda secundária `MODUX - TESTES` marcada em amarelo e um compromisso de teste em 23/10 por volta das 10h, com título truncado `TESTE MODUX - DES…`. A captura NÃO confirma ainda ID interno de calendário nem OAuth/API.
- `GoogleCalendarManageDialog.cs`: botão `4. Verificar evento de teste` só fica habilitado após OAuth e seleção autorizada da agenda secundária de testes. Consulta em modo **somente leitura** a data 23/10/2026 via Google Calendar API e procura um título contendo `TESTE MODUX`, sem modificar eventos, sem tocar na agenda real.
- Etapa 2 continua **parcial** até teste real de autorização do navegador e leitura do evento pelo MODUX no Windows. Não publicar beta intermediária; relatório CI interno deve ser consultado para confirmar compilação.
- Nenhum segredo OAuth, token, print de credencial nem convite de calendário com dados reais deve ser enviado no chat.

## Registro de mudanças

### 2026-09-23 — candidata MODUX 1.5.2

- Identificada pela captura do usuário a exceção `Collection was modified; enumeration operation may not execute` ao alternar contas.
- A causa foi a alteração do dicionário de navegadores durante leituras assíncronas dos contadores.
- As enumerações de navegadores e contas agora trabalham sobre cópias estáveis; atualização periódica e inicialização também registram falhas sem derrubar o aplicativo.
- Estado: compilação Windows concluída com sucesso, instalador publicado e página oficial atualizada; aguardando teste funcional do usuário.

### 2026-09-23 — candidata MODUX 1.5.1

- O badge da barra de tarefas passou a usar círculo vermelho e mostrar a soma das não lidas de todas as contas.
- O badge é reaplicado depois que a janela aparece e ao restaurar o MODUX da bandeja, evitando a tentativa precoce antes de o botão existir no Windows.
- Estado: compilação Windows concluída com sucesso e instalador publicado; aguardando teste visual do badge pelo usuário.

### 2026-09-23 — página oficial de downloads

- Publicada uma página responsiva com identidade MODUX, download direto da versão Windows e estado real das futuras versões para macOS e Linux.
- O site não anuncia pacotes multiplataforma como disponíveis antes de serem validados.
- Endereço: `https://modux-downloads.filipebrios.chatgpt.site`.

### 2026-09-23 — candidata MODUX 1.5.0

- O cartão “EM USO AGORA” passou a abrir um seletor visual com foto, nome, não lidas e marcação da conta ativa.
- Criada tela de Preferências com tema claro/escuro/sistema, fonte, escala da interface e zoom do WhatsApp.
- Preferências persistem em `preferences.json` sem alterar sessões, contas ou dados do WaSeller.
- Estado: compilação Windows concluída com sucesso e instalador publicado; aguardando teste funcional do usuário.

### 2026-09-23 — candidata MODUX 1.4.0

- Continuidade definida diretamente a partir da 1.3.0; alterações externas do Manus foram descartadas.
- Corrigido o CLSID usado pelo badge da barra de tarefas do Windows (`...344`).
- Criado cartão permanente “EM USO AGORA” com foto, nome e estado da conta ativa.
- Links externos passam a abrir no navegador padrão e não substituem o WhatsApp Web.
- Versão, instalador e workflow atualizados para 1.4.0.
- Estado: workflow concluído com sucesso e instalador publicado; aguardando teste funcional do usuário no Windows.

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


### 2026-10-06 — integração Criatta (MODUX 1.6.0)

- Instalador registra modux://compose por usuário. Parser aceita somente source=criatta, telefone brasileiro e texto limitado; não abre URLs arbitrárias nem envia mensagens.
- Pipe local CurrentUserOnly encaminha o link à instância existente; inicialização guarda os rascunhos até carregar contas. Prévia pede conta e confirmação, lembra a última escolha em criatta-account.txt. Sessões existentes são preservadas.
- Testes de protocolo em tests/ComposeRequest; workflow compila WinForms e instalador Windows. Workflows #41 e #42 compilaram aplicativo e instalador com sucesso; release modux-v1.6.0 publicada com MODUX-Setup-1.6.0.exe. Criatta 1.6.1 publicada. Aguardando validação real no Windows com as contas do usuário.


### 2026-10-06 — candidata MODUX 1.6.1: conta explícita da Criatta

- Seleção não usa conta ativa/primeira. Sem vínculo válido, botão fica desabilitado até escolher a conta. Rótulo e botão mostram a conta escolhida.
- Novo criatta-account-v2.txt exige confirmar o vínculo na primeira utilização após atualizar; preferência antiga preservada, mas não reutilizada. Sessões/contas não são apagadas.
- Abertura a frio com rascunho não carrega primeiro a conta da empresa. Navegar para o rascunho ainda pode recarregar a página do WhatsApp; nenhuma nova instância nem envio automático foi adicionado.
- Testes de seleção cobrem vínculo ausente, vazio, removido, válido e reordenação. Estado: testes de protocolo/seleção, compilação Windows e instalador concluídos nos workflows 37487645902 e 37488050185; MODUX-Setup-1.6.1.exe publicado na release modux-v1.6.1. Uso real pendente.
