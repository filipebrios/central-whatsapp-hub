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

- 26/10: CI #40371916359 aprovou C#/Node, página móvel e smoke Windows (grade 1028×155 nos dois temas). Capturas clara/escura conferidas internamente; refinados contraste da legenda e formato brasileiro de data. Convites e links permanecem sem envio real; página prévia sbento PR #26 compilou e GET autenticado retornou 200.

- 26/10: refinamento nativo de aparência em validação: temas alcançam Agenda/formulários, botões arredondados, tabelas e calendário; mantém DOM/sessões/WebView2/WaSeller. Novo smoke Windows sem login/credenciais prepara capturas clara/escura. Não declarar revisão visual concluída antes de executar e conferir.

- 26/10: links/rascunhos passaram nos testes C#/Node e compilação Windows CI #40369817886. Página preparada em sbento PR #26, aguardando validação/publicação; não declarar link público entregue. Convites Google em validação interna: ação separada, padrão Não, If-Match e preservação dos convidados; nenhum convite real enviado.

- 26/10: handler de resposta pública passou em cinco testes Node (origem/método, limites, consulta sem consumo, nota zero, não vazamento). Edge `agenda-response` v1 ativa com autenticação própria por capacidade; teste HTTP inválido retornou 400/no-store. Página e integração de rascunho no cliente em validação de CI; publicação da página ainda pendente, sem link real enviado.

- 26/10: ativação operacional e deduplicação iCal passaram no CI #40350635367. Backend de respostas públicas aprovado em testes SQL com rollback: token SHA-256, validade, uso único, horário exato, NPS e isolamento. Migração `20261026172943_modux_public_responses` aplicada; zero tokens/fixtures reais persistidos. Página pública e integração do cliente ainda em implementação; nenhum link foi enviado e nenhum instalador novo foi criado.

- 26/10: busca/filtros/CSV/checkpoints passaram no CI #40348718438. Implementação da ativação operacional no aplicativo principal está em validação: usuário deve escolher e confirmar o ID concreto do calendário no próprio Windows. Configurações antigas não herdam aprovação; verificador isolado continua limitado a MODUX - TESTES. Nenhuma agenda operacional real foi ativada ou alterada por estas ferramentas.
- O ambiente local ficou indisponível nesta etapa; continuidade pelo conector GitHub e CI Windows. Mudanças de desenvolvimento são salvas no repositório, sem prometer trabalho entre mensagens.

- 26/10: painel de pendências/fila manual/NPS e separação São Bento/Criatta passaram no CI #40347646462. Busca/filtros/CSV/checkpoints locais cifrados do período carregado em validação interna; sem novo instalador.

- 26/10: catálogo compartilhado passou nos testes/compilação no CI #40344124246. Rotina manual de pendências/lembretes/NPS em integração: fila compartilhada idempotente, rascunho separado de envio confirmado pelo operador, conta São Bento independente da Criatta. Migrações `20261026150043`/`20261026151259` aplicadas e testes SQL com rollback aprovados; cliente Windows aguardando CI.

- 26/10: edição operacional passou nos testes/compilação Windows no CI #40343259988. Catálogo compartilhado inicial aplicado e conferido: 12 definições (10 serviços ativos + 2 categorias de legenda), seis cores históricas preservadas. Configuração de cores/rótulos por administrador e leitura compartilhada em validação.

- 26/10: reconciliação vinculada/cancelamento passaram no CI #40342399727. Edição operacional PF/PJ em implementação: PATCH condicionado a empresa/ID/revisão; horários vinculados preservados, alteração pelo editor Google. Aguardar novo CI e validação integrada da UI.

- 26/10: criação integrada recuperável e cancelamento OAuth passaram nos testes/compilação Windows no CI #40341480502. Reconciliação de horários/ETags e cancelamento vinculado em integração; migração remota `20261026143956_modux_google_calendar_reconciliation` e testes SQL com rollback aprovados. Não há novo instalador nem escrita na agenda operacional liberada.

- 26/10: usuário concluiu no Windows a autorização OAuth, leitura do evento preparado e criação, edição e exclusão de um evento descartável no calendário secundário **MODUX - TESTES**, usando o verificador do CI #40318275302. Etapa 2 aprovada para o piloto; CRUD básico real confirmado. Isso não autoriza gravações na agenda operacional nem comprova recorrências/séries, convites ou sincronização Supabase.
- 26/10: vínculo explícito Google/Supabase em implementação na branch 1.9. Migração remota `20261026142854_modux_google_calendar_links` aplicada: unicidade por evento/atendimento/calendário, RPC invoker com revisão/lock, RLS e privilégios mínimos. Testes transacionais de idempotência, conflito, revisão, isolamento e anonimato passaram com rollback integral (0 fixtures/vínculos persistidos). Código de vínculo/projeção passou nos testes C# e compilação Windows no CI #40340412410. Criação integrada recuperável em implementação; não há novo instalador.

- 26/10: preparado verificador Google portátil independente em `tools/GoogleCalendarDiagnostic`, usando os mesmos componentes OAuth/API/formulários da 1.9. Armazena tokens na pasta própria `GoogleOAuthDiagnostic`, sem abrir sessões WhatsApp/WaSeller nem alterar o aplicativo instalado. Não é instalador nem release 1.9. CI #40318275302 concluiu testes, compilação Windows e verificador autocontido; artefato #12647269936 disponível até 09/11/2026. Execução real de OAuth e CRUD básico confirmada pelo usuário em 26/10; demais fluxos ainda pendentes.

- 26/10: a correção da verificação OAuth passou nos testes e na compilação Windows (CI #40314893833). Testes HTTP de CRUD/ETag e compilação também passaram no CI #40317911528, sem autenticar conta real.
- 26/10: retomada na branch 1.9; verificação OAuth permite escolher a data do evento fictício e bloqueia troca/desconexão durante a consulta. Compilação pendente de CI; nenhum instalador novo.

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

- Concluir página/handler e integração dos links de confirmação/NPS no rascunho manual; convites Google, revisão visual e instalador consolidado. Backend aprovado não equivale a fluxo público entregue.

- Validar CI da ativação operacional por ID/consentimento e fallback iCal por identidade; manter escrita real desativada até a ação explícita do operador no aplicativo. Concluir links públicos/convites, validação visual e empacotamento único.

- Validar CI de busca/filtros/CSV e checkpoints; concluir links públicos/convites controlados, refinamento visual e empacotamento consolidado. Validação real da nova UI será na rodada única de feedback.

- Aguardar CI de pendências/fila manual/NPS e integração com conta São Bento; depois concluir busca/filtros, refinamento visual, backup/exportação e instalador consolidado.
- Links públicos de confirmação/cancelamento e convites Google controlados ainda não estão entregues. Modelos atuais de confirmação solicitam resposta manual; não anunciar clique de confirmação nem disparos automáticos.
- Link exato de avaliações Google não foi recuperado com certeza; administrador pode configurar a URL oficial no próprio painel, sem link inventado ou de outra empresa.

- Validar CI do catálogo compartilhado e configuração de cores; depois concluir filtros/painel operacional, preparação de mensagens, redesign e empacotamento único. Configuração visual real e fluxo completo Windows serão testados na entrega consolidada.

- Validar CI da edição operacional; concluir catálogo/cores compartilhados, busca/filtros, painel e mensagens preparadas, redesign e instalador consolidado. Não confundir fases Google/Supabase já testadas com entrega completa.

- Aguardar CI da reconciliação vinculada no cliente: horários/duração por revisão+ETag, cancelamento preservando cadastro e recuperação explícita de pendências. A validação integrada de UI permanece para a entrega consolidada.

- Validar CI da criação integrada recuperável (ID Google derivado do atendimento, propriedades privadas de origem, consulta antes de criar, recuperação de disputa 409 sem sobrescrever). Depois concluir reconciliação de datas/ETags e edição operacional.

- Consolidar vínculo Google/Supabase no aplicativo principal e validar a UI na entrega integrada. O piloto OAuth + CRUD básico no calendário de testes já foi confirmado pelo usuário; não pedir repetição sem falha nova.
- Concluir reconciliação de revisões/ETags, criação operacional integrada, edição de dados PF/PJ, recorrência e convites, rotinas de mensagens com revisão humana e redesign geral.
- Manter guarda de escrita na agenda operacional até uma seleção/ativação explícita; aprovação dos testes não equivale a autorização de mudanças em clientes reais.

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

### 2026-10-22 — separar a Agenda operacional dos eventos de teste na mesma tela

- `AgendaPanel.cs` alterado na branch `feature/modux-1.9-consolidated`: antes o código `if (googleOAuth.CalendarId is not null) ... else if (googleFeed.HasUrl)` escondia TODOS os compromissos reais do iCal quando uma agenda de teste OAuth era conectada.
- Agora o MODUX carrega **ambas as fontes independentemente**, desde que autorizadas: calendário operacional privado iCal permanece `Google • leitura`; somente calendário secundário `MODUX - TESTES` entra como `Google • TESTES`, identificado como editável e com seleção de cor de linha distinta.
- Contadores e status informam separadamente locais, iCal operacional e Google TESTES. Os dados continuam sem importação/duplicação no Supabase e sem alteração no Google operacional.
- Código `AgendaPanel` commit `18c5036`; GitHub Actions run [#39797773408](https://github.com/filipebrios/central-whatsapp-hub/actions/runs/39797773408) foi disparado para testes de parser, cores, protocolo e compilação Windows. **Não assumir sucesso antes da conclusão.**
- Próxima validação humana: usuário deverá abrir autorização no MODUX com JSON OAuth local e confirmar leitura do evento fictício com o botão de verificação. Esta prova real ainda não ocorreu; usuário pediu não publicar instaladores intermediários.

### 2026-10-22 — prevenção de criação duplicada Google

- Confirmação da GitHub Actions [#39797773408](https://github.com/filipebrios/central-whatsapp-hub/actions/runs/39797773408): integração visual de Google iCal operacional + calendário secundário MODUX - TESTES, testes automáticos e compilação Windows **passaram**. Não equivale a autorização OAuth com conta real.
- `GoogleEventDraft.CreateRequestId` agora gera identificador estável por instância de rascunho, prefixo `modu` + UUID hexadecimal, obedecendo caracteres permitidos pela Google Calendar Events.insert. `GoogleCalendarApi.Create` envia esse identificador em `id` para reduzir risco de repetição no mesmo rascunho.
- Quando ocorre erro de rede/timeout após POST, o aplicativo informa que o Google **pode** ter salvo o evento e manda consultar o calendário de testes antes de repetir. Nunca reenviar automaticamente operações de criação ambíguas.
- Testes `tests/GoogleCalendarCore` incluem 1000 IDs criados sem duplicatas e com caracteres base32hex válidos. GitHub Actions [#39805612341](https://github.com/filipebrios/central-whatsapp-hub/actions/runs/39805612341) **concluiu com sucesso**: testes Google, paleta de cores, protocolo e compilação Windows, sem gerar instalador.
- Este código ainda **não garante deduplicação entre duas sessões ou novos rascunhos**; a prevenção definitiva depende de IDs consistentes relacionados à entidade no Supabase, etapa 4, e validação real da API no Windows.
- Não gerar instaladores até a release consolidada e não autorizar escrita no calendário operacional antes de validar explicitamente.

## Registro de mudanças

### 2026-10-26 — revisão final do fluxo de mensagem

- Confirmações/lembretes consultam vínculo atual e evento Google antes de preparar rascunho/link. Calendário diferente, múltiplos vínculos, ETag/horário/duração divergentes ou evento excluído exigem Conferir vínculo e atualizar a fila. Consulta não escreve no Google e NPS usa conclusão operacional.
- Legenda usa texto neutro legível com amostra da cor, preservando faixas/semântica; data curta padronizada em dd/MM/yyyy. Capturas anteriores de claro/escuro foram inspecionadas.
- Edge v2 mantém corpo limitado (16 KiB) e aceita notas Unicode de até 4000 caracteres; cinco testes Node locais passaram, incluindo Unicode. Sem credenciais administrativas no cliente.
- Empacotamento preparado testa instalação/desinstalação com perfil fictício e confere assets baixados/SHA-256 antes de publicar a beta; ainda aguarda manifesto explícito. CI da revisão pendente.


### 2026-10-26 — espaço útil da Agenda

- Smoke encontrou grade sem altura após a barra reunir todas as ações. Barra principal passa a conter período/atualizar/cadastro/pendências e Mais ações; edição/vínculo/convite/configurações permanecem disponíveis em menu com habilitação do controle original.
- Calendário ajusta altura em janelas menores, preservando área para a grade. Smoke imprime dimensões e guarda captura mesmo em falha para investigação. Aguardar novo CI antes de afirmar layout validado.


### 2026-10-26 — preparação do pacote único

- Workflow de empacotamento 1.9 preparado, mas só é acionado por manifesto de release explícito ou despacho manual. Reutiliza validação C#/Node/navegador/smoke antes de gerar instalador autocontido; confere versão/remoção de dados, gera SHA-256 e cria release em rascunho para verificação de assets.
- Instalador mantém AppId/diretório da instalação/perfis e passa a criar atalhos com nome MODUX. Notas identificam Windows x64 beta e limites de recorrência/multidia, envio manual, cancelamento solicitado e checkpoints parciais. Nenhum manifesto de release foi criado nesta alteração; nenhum instalador novo gerado.


### 2026-10-26 — tema do contêiner integrado

- Smoke Windows detectou que UserControl exige tratamento próprio além de Panel. Tema passa a alcançar o contêiner AgendaPanel, mantendo aplicação aos controles internos. Testes de calendário/convites/protocolo e compilação anteriores passaram; repetir smoke antes de empacotar.


### 2026-10-26 — aparência nativa e smoke Windows

- Camada visual aplica paleta clara/escura aos controles Windows e formulários abertos, com foco nativo, botões arredondados, campos/tabelas de contraste e cores semânticas preservadas. Controles WebView2 são excluídos; nenhuma mudança em DOM, perfis ou extensão.
- Calendário usa superfícies/seleção/texto do tema mantendo faixas de serviços. Renderização descarta células antigas para não reter controles a cada recarga.
- Smoke em Windows instancia painel real sem autenticar ou abrir WhatsApp, alterna temas, confere espaço da grade e preservação da cor de serviço; captura clara/escura como artefatos internos. Implementação aguardando CI e inspeção visual, sem instalador.


### 2026-10-26 — navegação entre links da página

- Teste de navegador encontrou reutilização do mesmo documento ao trocar somente fragmento. A página recarrega ao receber outro fragmento para consultar o novo link; teste abre documento novo para cada cenário, evitando confundir navegação de âncora com carregamento inicial. Aguardar CI; página segue sem publicação de produção.


### 2026-10-26 — correção do teste de convites

- CI identificou colisão do parâmetro `_` da lambda com descarte `out _` em teste HTTP. Renomeado o resultado local do TryGetProperty; falha era de compilação do teste, sem envio a calendário real. Aguardar nova execução.


### 2026-10-26 — teste integrado da página móvel

- Preparado teste Playwright da página real com API simulada: fragmento removido, conteúdo via textContent, responsividade 390 px, confirmação, cancelamento com voltar, NPS zero/nota obrigatória e links encerrados. CI instala navegador em diretório isolado, sem alterar dependências do aplicativo.
- Navegador não pôde ser instalado localmente por download truncado; aguardar resultado do job remoto antes de considerar a página validada. PR sbento #26 permanece rascunho e sem publicação de produção.


### 2026-10-26 — convites Google explícitos

- API lê evento/ETag e preserva lista completa de convidados, respostas e campos editáveis antes de acrescentar apenas o e-mail escolhido. Recusa lista omitida, revisão antiga e endereço composto; participante já existente não gera PATCH/reenvio.
- `sendUpdates=all` restrito à ação separada Convidar por e-mail; formulário identifica evento e destinatário, informa notificação aos convidados existentes e exige confirmação com padrão Não. Edição/criação comum continuam sem convite intencional.
- If-Match protege alteração concorrente; timeout não provoca repetição automática e informa conferência no Google. Testes isolados de preservação/idempotência/guarda/lista incompleta/rede em CI; sem envio de convite real.


### 2026-10-26 — API e página de resposta pública

- Handler limita origem, tamanho do corpo, operações e campos; consulta não consome token, respostas contêm somente serviço/data/hora ou situação, sem contatos/identidade/credenciais. Service role permanece no ambiente Edge; nenhuma chave administrativa no Windows ou HTML.
- Página independente recebe capacidade por fragmento (fora de logs de URL/referrer), remove fragmento do histórico, usa textContent e CSP, sem analytics/dependências externas. Exige ação explícita para confirmar/cancelar e nota selecionada para NPS; não promete exclusão automática do Google.
- Rascunho confirmação/NPS emite capacidade autenticada pela revisão atual e acrescenta URL; preparação continua separada de envio manual. Testes Node passaram; teste HTTP negativo na Edge v1 retornou 400 com no-store. Integração C# aguardando CI e página aguardando publicação verificada.


### 2026-10-26 — capacidades de resposta do cliente

- Migração criada com Supabase CLI e alinhada ao histórico remoto `20261026172943`. Guarda somente SHA-256 de token aleatório de 256 bits; identidade, validade e snapshots definidos no servidor. Emissão exige membro ativo e revisão atual; consumo reservado a service_role, sem RPC anônima nem segredo no aplicativo.
- Confirmação exige horário ainda vigente. Cancelamento é solicitação para equipe (`reagendar`), interrompe rascunhos de confirmação/lembrete e não afirma excluir o evento Google. Pós-venda exige sete dias desde conclusão e nota ainda ausente; resposta de uso único não sobrescreve nota registrada.
- Testes transacionais cobriram consulta sem consumo/contatos, validade, repetição, horário alterado, nota inválida, tipos cruzados, RLS e ACL. Rollback confirmou zero tokens/fixtures persistidos. Página/Edge/clientes ainda pendentes; sem envio real ou instalador.


### 2026-10-26 — ativação operacional explícita e fallback por identidade

- CI #40348718438 aprovou filtros/CSV e compilação Windows, junto com regressões do pacote integrado.
- OAuth do aplicativo principal aceita calendário operacional somente após escolha do ID concreto e confirmação explícita (padrão Não). A aprovação fica cifrada no Windows e vinculada ao ID; alias primary, conta não autorizada ou outro ID não satisfazem a guarda.
- O verificador portátil usa modo diagnóstico e continua recusando ativação operacional na UI e na camada OAuth/API, mesmo com um arquivo de configuração incompatível.
- Nova autorização não herda refresh token nem aprovação de calendário anteriores; renovação interrompe se a conexão foi trocada/desconectada durante a consulta.
- Quando iCal e API representam o mesmo ID de calendário, API prevalece após leitura bem-sucedida; se a API falhar, iCal permanece leitura. Calendários diferentes continuam visíveis separadamente. URL secreta não é exposta nem copiada.
- Formulários indicam o calendário selecionado e criação integrada; confirmações Google usam padrão Não. Nenhum evento legado é importado ou recolorido automaticamente.
- Testes adicionais cobrem ID aprovado, ausência de autorização, alias principal e identidade do endereço iCal. Aguardando CI, sem novo instalador e sem ativar agenda real.

### 2026-10-26 — busca, filtros, exportação e checkpoints

- CI #40347646462 aprovou painel operacional, fila/NPS, origem São Bento separada, regressões de protocolo e compilação Windows.
- Grade/calendário filtram por texto (sem distinguir acento/caixa), situação, origem e serviço; clique no dia restringe a grade preservando visão do mês. Períodos Hoje/Semana/Mês mantidos. Carregamento local paginado elimina corte silencioso em 500 atendimentos.
- CSV exporta somente linhas visíveis, com UTF-8/BOM, escape de delimitador/aspas/quebras e neutralização de fórmulas em dados importados.
- Após consulta bem-sucedida, salva checkpoint DPAPI CurrentUser dos dados operacionais/vínculos do período carregado, um por dia/período, no máximo 60. Não inclui tokens OAuth/Supabase nem URL privada iCal e não é backup completo do banco/Google. Falha de gravação é informada; não impede leitura da agenda.
- Navegação por período/calendário desabilitada durante consulta evita mostrar mês novo com resposta antiga. Testes C# de filtros/CSV e compilação aguardando CI; nenhum instalador intermediário.

### 2026-10-26 — pendências e comunicação manual compartilhada

- CI #40344124246 aprovou catálogo compartilhado, cores/contraste/CAS, regressões e compilação Windows.
- `AgendaWorkflow`/`AgendaWorkflowDialog` consultam revisões, contatos faltantes, confirmações hoje/amanhã, lembretes D-1/D0 e NPS após sete dias da conclusão real (janela de 90 dias). Nota fica não informada até registro explícito; convite Google fica disponível conforme regra >=8 e modelo/link configurado por administrador.
- Fila `agenda_message_queue` usa ocorrência estável e RPC invoker/RLS com revisão do atendimento. Preparar não significa enviar; `enviada_manual` exige confirmação do operador. Registro guarda quem preparou e quem declarou atendimento/envio, com horários do servidor.
- Migrações de fila/consistência aplicadas: rascunhos de horário antigo ou serviço cancelado são cancelados, envios já registrados preservados; retorno NPS encerra pendência correspondente. Modelos e cores recebem versão/autoria do servidor, mantendo CAS.
- Testes SQL com rollback aprovaram repetição sem duplicatas, envio manual separado, preservação de histórico enviado, invalidação por reagendamento/cancelamento e retorno NPS. Zero registros de mensagens reais criados/enviados pelos testes.
- Rascunhos São Bento usam origem `saobento` e preferência `saobento-account-v1.txt`, sem reutilizar conta da Criatta. O operador escolhe a conta, revisa/edita texto e prepara na sessão existente; envio permanece manual. Perfil, WaSeller e navegação externa preservados.
- Edição operacional consulta vínculos em qualquer calendário antes de liberar horário; formulário registra data real de conclusão. Avaliações de nota baixa continuam acessíveis em Mostrar tratadas.
- Aguardando CI Windows desta alteração. Sem instalador intermediário; sem Meta API, robô 24h ou envio autônomo.

### 2026-10-26 — catálogo e cores compartilhados

- CI #40343259988 aprovou edição operacional com CAS, regressões e compilação Windows.
- Catálogo `agenda_service_types` preenchido por migração `20261026145133`, sem sobrescrever valores existentes: 10 serviços ativos e categorias de cancelado/não identificado para legenda. Consulta confirmou as seis cores históricas exatas; auxiliares mantêm cores propostas já usadas pelo MODUX.
- `AgendaServiceCatalog` carrega apenas a empresa autorizada e aplica cores/rótulos ao formulário, grade e legendas. Administrador pode editar rótulo/cor com CAS por updated_at; RLS admin existente permanece como autorização real. Não altera cores no Google.
- Contraste de texto calculado por luminância; logout limpa catálogo e legenda da empresa. Formulário usa catálogo compartilhado e preserva dados históricos de serviço quando não editados.
- Renovação Supabase serializada para evitar duas trocas concorrentes do mesmo refresh token entre atualização periódica e diálogo de configuração.
- Testes C# cobrem escopo, cores, contraste, prioridade de cancelado e conflito de configuração. Aguardando CI; nenhum novo instalador.

### 2026-10-26 — edição operacional e proteção do horário vinculado

- CI #40342399727 aprovou testes de reconciliação/cancelamento, regressões e compilação Windows.
- `AgendaAppointmentStore` edita por PATCH com empresa/ID/revisão e retorno de representação; zero linhas é conflito, nunca motivo para forçar sobrescrita. Não altera identidade nem autoria. Ao concluir serviço registra data de conclusão para futura rotina de pós-venda.
- Formulário PF/PJ reaproveitado para edição de contatos, serviços, equipe, observações e situação local. Preserva IDs/rótulo originais de serviços não reconhecidos quando não editados. Horário de atendimento vinculado fica bloqueado no formulário local; é alterado pela ação Google e reconciliado.
- `Editar atendimento` disponível somente em linha com cadastro operacional. Edição local não cria/altera eventos nem envia mensagens; campos Google permanecem em fluxo explícito separado.
- Testes C# verificam CAS/escopo, proteção de horário vinculado, identidade imutável no payload, conclusão e detecção de revisão antiga. Compilação desta etapa aguardando CI; sem novo instalador.

### 2026-10-26 — reconciliação otimista e cancelamento vinculado

- CI #40341480502 aprovou identidade estável/criação recuperável, regressões Google/vínculos/cores/protocolo, compilação Windows e ferramenta isolada.
- RPC `agenda_reconcile_google_event`, invoker/RLS, confere ETag do vínculo e revisão do atendimento sob lock no mesmo sentido da associação. Atualiza somente data/hora/duração ou status cancelado; preserva contatos, serviços, equipe, observações e auditoria. Retentativas já concluídas não incrementam revisão.
- Privilégios de UPDATE nos vínculos limitados às colunas event_etag/sync_status; identidade permanece imutável. Migração criada por CLI e alinhada à versão remota `20261026143956`.
- Testes SQL transacionais passaram para mudança de horário, ETag/revisão antigos, preservação de contatos/serviços, cancelamento idempotente, isolamento e anonimato. Rollback removeu todos os fixtures (0 registros persistidos). Advisors sem novos avisos.
- Cliente passa a obter a identidade real retornada pelo RPC de vínculo e reconciliar o horário após criação/edição confirmada. Exclusão Google marca o atendimento vinculado como cancelado após confirmação explícita, preservando cadastro. Falha parcial é informada como pendência e pode ser revisada em `Conferir vínculo`; evento ausente exige conferência humana antes de cancelar.
- Barra de ações ajusta linhas à largura da janela para manter os controles acessíveis. Testes C#/compilação desta alteração aguardando CI; nenhum novo instalador.

### 2026-10-26 — criação integrada recuperável e cancelamento OAuth

- Vínculo/projeção por IDs e testes do cliente Supabase passaram no CI #40340412410, junto com regressões e compilação Windows.
- Formulário de atendimento oferece criação no Google de testes quando conectado. O atendimento é salvo primeiro com UUID explícito; o evento usa ID `modu` + UUID do atendimento e propriedades privadas de empresa/atendimento. `EnsureOperationalEvent` consulta antes de criar, recupera disputa 409 por leitura e recusa origem divergente ou evento excluído (410), sem reenvio cego após timeout.
- Se Google ou vínculo falhar após salvar o atendimento, a interface informa pendência e permite `Completar Google` no mesmo cadastro, reaproveitando a identidade; nunca manda cadastrar tudo de novo nem anuncia sincronização concluída sem vínculo confirmado.
- Acrescentado botão `Cancelar autorização` e cancelamento ao fechar a janela enquanto aguarda navegador, corrigindo espera sem saída relatada no piloto. Não foi identificada causa comprovada para o 404 inicial; o usuário conseguiu autorizar depois.
- Novos testes cobrem identidade estável entre rascunhos, propriedade de origem, recuperação de evento existente/disputa, evento excluído e falha ambígua. Aguardando CI desta alteração; sem instalador intermediário e sem escrita operacional liberada.

### 2026-10-26 — OAuth real aprovado e vínculo Google/Supabase

- Usuário confirmou autorização, leitura, criação, edição e exclusão reais no verificador de testes; registrado o fechamento da etapa 2 para esse piloto.
- `AgendaCalendarLinks.cs` contém cliente RLS/RPC e projeção por IDs exatos, sem associação por nome/telefone/data. A UI oferece vínculo explícito com atendimento existente, conferindo ETag Google e revisão Supabase antes de gravar.
- Linhas vinculadas são exibidas uma única vez com data/hora Google e contatos/serviços operacionais. Atendimentos com mudança de mês são buscados pelo ID. Vínculos sem evento/cadastro ou ETag divergente permanecem visíveis como pendência; nenhuma importação/recoloração/regravação Google automática.
- Migração criada pela CLI, alinhada à versão remota `20261026142854`. Teste `tests/Database/calendar-links.sql` passou no banco em transação com rollback: associação repetida, dois atendimentos por evento, dois eventos por atendimento, revisão antiga, isolamento entre empresas, identidade imutável e acesso anônimo negado. Dados reais não foram importados nem editados.
- Advisors de segurança sem novos avisos após a migração (aviso pré-existente de proteção de senha vazada permanece). Testes C# de projeção/RPC/paginação e compilação Windows aguardando CI; não há novo instalador.

### 2026-10-26 — verificador portátil para desbloquear o teste real OAuth

- Criada ferramenta WinForms separada com a implementação Google compartilhada da 1.9, contendo conexão, consulta por dia e CRUD confirmado de eventos fictícios. Guarda do calendário de testes se aplica também antes da leitura nesta ferramenta.
- Credenciais/tokens cifrados em `%LOCALAPPDATA%\CentralWhatsApp\GoogleOAuthDiagnostic`; construtor padrão do MODUX mantém `WebView2Prototype`. A ferramenta não referencia WebView2, Supabase, banco operacional ou contas WhatsApp.
- CI prepara executável Windows x64 autocontido como artefato interno de 14 dias, com instruções. Não cria instalador, release pública nem substitui 1.8.2. Validação real continua dependente da execução pelo responsável.
- Diálogo OAuth bloqueia fechamento/troca de conta durante operações pendentes e protege também o carregamento inicial de calendários contra ações concorrentes.
- Importação OAuth rejeita JSON sem objeto installed/client_id com mensagem controlada em vez de KeyNotFoundException.
- CI #40318275302 (commit c222952) passou: testes HTTP/Google, cores, protocolo, compilação MODUX Windows e publicação do artefato interno #12647269936 (ZIP de 66.227.457 bytes). Nenhum instalador/release nova criado. Nenhuma autenticação real declarada concluída.

### 2026-10-26 — testes HTTP isolados do cliente Google Calendar

- `GoogleCalendarApi` aceita autorização e HttpClient injetáveis internamente; o construtor de produção continua usando OAuth Desktop e transporte real. `GoogleDesktopOAuth` implementa a interface mínima explicitamente, sem expor tokens à UI.
- Testes executam os métodos reais de criação, edição e exclusão com transporte simulado: verificam método/destino, Bearer fictício, ID estável no POST, If-Match, conflitos antes e depois da leitura, bloqueio antes de HTTP, erros 401/403/500 e falha de rede/timeout sem reenvio.
- Nenhuma credencial, evento real ou envio de WhatsApp envolvido. Testes e compilação Windows passaram no CI #40317911528; etapa 2 continua parcial e sem instalador intermediário.

### 2026-10-26 — retomada e diagnóstico OAuth reutilizável

- Retomada a implementação correta WebView2 na branch `feature/modux-1.9-consolidated`; main contém outra base e não representa o progresso da versão Windows.
- A data da verificação de leitura deixou de ser fixa: seletor mantém 23/10 como valor inicial para o evento preparado, permitindo testes posteriores sem recompilar.
- Importação, seleção de calendário, autorização e desconexão ficam bloqueadas durante consulta/autorização, evitando mudanças de conta ou calendário no meio da verificação.
- Código aguardando compilação Windows via CI; não equivale a OAuth real validado, não avança etapa 2 e não publica instalador intermediário.

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
