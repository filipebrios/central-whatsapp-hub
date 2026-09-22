# Central WhatsApp Hub

Quero criar a primeira versão de um aplicativo desktop para centralizar várias contas do WhatsApp Web em uma única janela, semelhante ao Franz ou Rambox.

O nome provisório do projeto será “Central WhatsApp”.

IMPORTANTE: nesta primeira etapa, crie somente a interface funcional com dados simulados. Não tente incorporar o site web.whatsapp.com usando iframe, pois posteriormente o projeto será exportado para o GitHub e integrado ao Electron, usando WebContentsView e sessões persistentes independentes.

TECNOLOGIAS E ESTRUTURA

React

TypeScript

Vite

Tailwind CSS

Componentes shadcn/ui

Lucide Icons

Código organizado, modular e preparado para futura integração com Electron

Interface totalmente em português do Brasil

Não criar backend nesta primeira etapa

Não utilizar Supabase nesta primeira etapa

Armazenar configurações simuladas no localStorage

Não implementar automações, disparos ou integração não oficial com a API do WhatsApp

OBJETIVO DO APLICATIVO

O aplicativo deverá permitir que o usuário cadastre diversas contas de WhatsApp e alterne entre elas por uma barra lateral.

Cada conta será futuramente carregada em uma sessão isolada do Electron. Por enquanto, mostre uma simulação visual do WhatsApp para validar o design e a navegação.

LAYOUT GERAL

A aplicação deverá ocupar 100% da tela e ter três áreas principais:

Barra lateral estreita com as contas.

Área principal representando a conta selecionada.

Painel opcional de configurações.

BARRA LATERAL DE CONTAS

Criar uma barra lateral vertical fixa do lado esquerdo, com aproximadamente 82 pixels de largura.

No topo, colocar:

Logotipo provisório do aplicativo.

Indicador geral de conexão.

Abaixo, mostrar as contas cadastradas em botões verticais.

Criar inicialmente três contas simuladas:

São Bento

Cor verde-escuro

Iniciais “SB”

Status conectado

7 mensagens não lidas

Criatta

Cor roxa

Iniciais “CR”

Status conectado

3 mensagens não lidas

Pessoal

Cor azul

Iniciais “FR”

Status conectado

Nenhuma mensagem não lida

Cada botão de conta deverá mostrar:

Avatar ou iniciais.

Pequeno indicador verde quando estiver conectada.

Contador de mensagens não lidas.

Tooltip com o nome da conta.

Destaque visual evidente para a conta selecionada.

Ao clicar em uma conta, alterar a conta ativa e atualizar toda a área principal com os dados simulados correspondentes.

No final da barra lateral, adicionar:

Botão “Adicionar conta”, com ícone de mais.

Botão de configurações.

Botão para recolher ou expandir a barra lateral.

Quando expandida, a barra deverá mostrar também o nome de cada conta.

CABEÇALHO DA CONTA ATIVA

Na parte superior da área principal, criar um cabeçalho com:

Avatar da conta.

Nome da conta ativa.

Número de telefone simulado.

Status “Conectado”.

Botão “Recarregar conta”.

Botão “Silenciar notificações”.

Menu de três pontos.

No menu de três pontos, incluir:

Renomear conta.

Alterar cor.

Reordenar contas.

Desconectar.

Excluir conta.

Essas funções podem ser simuladas, mas os botões e modais precisam funcionar visualmente.

ÁREA PRINCIPAL

Criar uma simulação elegante da interface do WhatsApp Web, sem copiar logotipos ou elementos protegidos de forma literal.

A área deve conter:

Coluna de conversas do lado esquerdo.

Campo de pesquisa.

Filtros “Todas”, “Não lidas” e “Favoritas”.

Lista simulada de contatos.

Nome do contato.

Prévia da última mensagem.

Horário.

Contador de mensagens não lidas.

Área da conversa selecionada do lado direito.

Cabeçalho do contato.

Histórico de mensagens simuladas.

Campo para digitar mensagem.

Botões de emoji, anexo, áudio e enviar.

Crie conversas simuladas diferentes para cada uma das três contas, para que a troca pela barra lateral seja perceptível.

As mensagens digitadas devem poder ser adicionadas à conversa simulada quando o usuário clicar em enviar ou pressionar Enter.

ADICIONAR NOVA CONTA

Ao clicar em “Adicionar conta”, abrir um modal com duas etapas.

Etapa 1:

Nome de identificação da conta.

Número de telefone.

Escolha de uma cor.

Iniciais ou avatar.

Botão “Continuar”.

Etapa 2:

Mostrar uma área representando o futuro QR Code, com o texto:

“Na versão desktop, o QR Code do WhatsApp aparecerá aqui. Use o celular da conta desejada para vincular este dispositivo.”

Adicionar os botões:

“Simular conexão”

“Voltar”

“Cancelar”

Ao clicar em “Simular conexão”, adicionar a nova conta à barra lateral e salvá-la no localStorage.

CONFIGURAÇÕES

Criar um painel ou modal de configurações com as seções:

GERAL

Iniciar aplicativo junto com o Windows.

Manter o aplicativo em segundo plano.

Abrir na última conta utilizada.

Tema claro, escuro ou automático.

Idioma português do Brasil.

NOTIFICAÇÕES

Ativar notificações.

Mostrar o nome da conta na notificação.

Reproduzir som.

Exibir contador total de mensagens não lidas.

PRIVACIDADE

Bloquear o aplicativo com senha.

Ocultar prévia das mensagens nas notificações.

Limpar dados locais.

CONTAS

Listar as contas cadastradas.

Renomear.

Alterar cor.

Reordenar.

Remover.

Os controles devem funcionar utilizando estado local e localStorage.

INDICADOR GERAL DE MENSAGENS

Exibir no topo ou na barra lateral a soma total das mensagens não lidas de todas as contas.

Quando o usuário selecionar uma conversa, permitir marcar suas mensagens como lidas e atualizar os contadores.

ESTILO VISUAL

Quero um design profissional, moderno, limpo e adequado para uso empresarial diário.

Características:

Aparência de aplicativo desktop, não de página institucional.

Tema escuro como padrão.

Paleta predominantemente grafite.

Verde discreto como cor de ação.

Cantos suavemente arredondados.

Sombras discretas.

Boa legibilidade.

Animações rápidas e sutis.

Evitar excesso de gradientes.

Evitar elementos grandes ou espaços desperdiçados.

Priorizar produtividade e leitura rápida.

Interface otimizada inicialmente para telas de computador.

Usar a altura e a largura completas da janela.

Responsividade mínima para resoluções a partir de 1024 pixels.

COMPONENTES SUGERIDOS

Organize o código em componentes semelhantes a:

AppShell

AccountSidebar

AccountButton

ActiveAccountHeader

ConversationList

ConversationItem

ChatPanel

MessageBubble

MessageComposer

AddAccountDialog

AccountSettingsDialog

GeneralSettingsDialog

UnreadBadge

ConnectionStatus

PREPARAÇÃO PARA ELECTRON

Crie uma camada de abstração para as ações que futuramente dependerão do Electron.

Crie um arquivo de serviço ou interface chamado desktopBridge contendo métodos provisórios como:

addAccount()

removeAccount()

selectAccount()

reloadAccount()

connectAccount()

disconnectAccount()

showNotification()

updateUnreadCount()

Nesta etapa, esses métodos podem somente atualizar o estado local ou escrever mensagens no console.

Não utilize diretamente APIs do Electron nos componentes React. Toda comunicação futura deverá passar pelo desktopBridge.

Deixe um comentário claro no código indicando onde futuramente será exibido o WebContentsView correspondente à conta selecionada.

RESULTADO ESPERADO

Entregar uma interface navegável e visualmente finalizada em que seja possível:

Alternar entre três contas simuladas.

Ver conversas diferentes em cada conta.

Enviar mensagens simuladas.

Adicionar uma nova conta.

Editar e remover contas.

Visualizar e alterar configurações.

Ver contadores individuais e o total de mensagens não lidas.

Manter configurações e contas simuladas após atualizar a página.

Deixar o projeto organizado para exportação ao GitHub e posterior integração com Electron.

Antes de finalizar, revise se não existem botões sem ação, erros no console, textos em inglês ou áreas quebradas no layout.

Faça a seguinte alteração na barra lateral de contas:

Cada conta conectada deve exibir a foto de perfil utilizada naquela conta do WhatsApp.

Ao lado da foto, mostrar:

Nome do perfil no WhatsApp.

Número de telefone completo, abaixo do nome.

Indicador de conexão.

Contador de mensagens não lidas.

Exemplo:

Foto do perfil
São Bento
+55 35 0000-0000

A foto deve ser circular, ter boa visibilidade e ser o principal elemento de identificação da conta.

Quando a barra lateral estiver recolhida:

Mostrar somente a foto do perfil.

Manter o indicador de conexão.

Manter o contador de mensagens não lidas.

Ao passar o mouse sobre a foto, mostrar um tooltip com o nome do perfil e o número.

Quando a barra lateral estiver expandida:

Mostrar a foto do perfil.

Mostrar o nome do perfil ao lado.

Mostrar o número logo abaixo do nome, em tamanho menor.

Manter os indicadores de conexão e mensagens não lidas.

Se a conta não possuir foto de perfil ou se a imagem ainda não tiver sido carregada:

Mostrar um avatar com as iniciais do nome.

Utilizar a cor escolhida para a conta como fundo do avatar.

Nesta versão simulada, utilize fotos ou avatares fictícios. Na futura integração com o Electron, as informações deverão ser obtidas da sessão conectada do WhatsApp Web.

Atualize também os dados simulados das contas para esta estrutura:

photoUrl

profileName

phoneNumber

accountColor

connectionStatus

unreadCount

O nome exibido deve representar o nome real do perfil conectado no WhatsApp, e não apenas um apelido interno do aplicativo. Se necessário, mantenha separadamente um campo opcional chamado accountLabel para o usuário identificar a finalidade da conta, como “São Bento”, “Criatta” ou “Pessoal”.

Não altere o restante da estrutura e das funcionalidades que já foram criadas.

This project was built with [Lovable](https://lovable.dev).

## Build with Lovable

Continue developing this project in the [Lovable editor](https://lovable.dev/projects/88803988-707e-4b61-9f78-9362705aceaf).

- **Ship faster**: describe what you want to build and Lovable handles the code.
- **Stay in sync**: every change made in Lovable is committed straight to this repository.
- **Full ownership**: this code is yours. Push to `main` on GitHub and your changes sync back into Lovable, ready for your next prompt.

## Development

Prefer working locally? You need Node.js and npm — [install with nvm](https://github.com/nvm-sh/nvm#installing-and-updating).

```sh
git clone <this-repository-url>
cd <repository-name>
npm i
npm run dev
```
