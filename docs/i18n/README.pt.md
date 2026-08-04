<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Coloque as tarefas repetitivas do seu PC no piloto automático**

Abra seus apps automaticamente ao entrar · lembretes com hora marcada · um toque para executar uma rotina inteira

**[⬇ Baixar para Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portátil, sem instalador

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · **Português** · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Uma ferramenta de bandeja do Windows: lançador de inicialização · lembretes · itens de inicialização do sistema · grupos de ações

![Clockwork](../../assets/social-card.png)

Uma pequena ferramenta de bandeja do Windows que cuida das partes rotineiras de começar o seu dia no computador:

- 🚀 **Lista de inicialização** — abre automaticamente os apps do dia a dia ao entrar, em ordem (direitos de administrador por etapa, atrasos, apenas-em-certos-dias-da-semana / apenas-antes-das-N-horas, estilo de janela, ativar-se-já-estiver-aberto, caminhos alternativos) e faz algumas tarefas pelo caminho (fechar ou focar janelas, enviar teclas / texto, ajustar o volume…).
- ⏰ **Tarefas agendadas** — exibe um lembrete na hora certa; fala em voz alta; repete por dia da semana / a-cada-N-dias / mensalmente; ou dispara "ao entrar". Clicar em **Sim** pode executar um programa, abrir um arquivo (por exemplo, uma música) ou uma URL, ou executar um grupo de ações. Também oferece suporte a execuções por intervalo e agendamento de execução única.
- 🧹 **Itens de inicialização do sistema** — lista **tudo no seu PC que inicia automaticamente** e desliga o que você não precisa (desativado, não excluído — reative quando quiser). Um clique "assume" um item, passando-o para a sua própria lista de inicialização.
- 🎛️ **Grupos de ações** — agrupe uma série de ações em um grupo reutilizável (Foco / Reunião / Encerramento / Hora de dormir…) e dispare-o com um clique a partir da bandeja, de uma **tecla global**, da lista de inicialização ou de um lembrete. Modelos prontos incluídos.

Sem instalação, totalmente portátil em uma única pasta, tudo configurável com o mouse; interface escura, com suporte a alta resolução (high-DPI).

> 📖 **Guia completo:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Requisitos

- Windows 10 / 11 (x64)
- Nada a instalar: um `Clockwork.exe` autônomo de arquivo único com o runtime .NET embutido.

## Como começar

1. Baixe o `Clockwork-<versão>.zip` mais recente em [Releases](https://github.com/rockbenben/Clockwork/releases) e descompacte-o — dentro há um único `Clockwork.exe`; coloque-o em qualquer pasta (portátil — ponha onde quiser). Para compilá-lo você mesmo, veja **Para desenvolvedores** abaixo.
2. Dê um duplo clique em **`Clockwork.exe`** para abrir a janela de configurações.
   - No **primeiro uso** ele carrega alguns **exemplos** na lista de inicialização e nos lembretes para você adaptar à sua realidade — todos vêm desmarcados, então nada é executado até você marcar. A aba **Grupos de ações** também começa com dois grupos prontos para rodar (Ausente um momento / Encerrar o dia) — esses já vêm *marcados*, porque um grupo nunca dispara sozinho; ele só roda quando você o aciona. Suas configurações ficam em `clockwork.settings.json` ao lado do exe — só locais, nunca versionadas.
3. Para executá-lo a cada inicialização: na aba **Configurações**, clique em **Iniciar ao entrar** (registra uma tarefa agendada com direitos de administrador, evitando uma enxurrada de avisos do UAC na inicialização).

> Ele fica quietinho na bandeja. Dê um duplo clique no ícone da bandeja para abrir a janela; o botão de fechar da janela apenas a oculta na bandeja. Para sair de verdade, use **Sair** no menu de contexto da bandeja.

> **Um aviso no primeiro uso é normal.** O exe não é assinado, então o SmartScreen mostra «O Windows protegeu o seu PC» — clique em **Mais informações → Executar assim mesmo**. Algum antivírus também pode alertar: gravar chaves Run do registo e tarefas agendadas é exatamente o que um gestor de arranque faz — e também o que o malware faz; de fora não dá para distinguir. Se preferir não aceitar por confiança, compile você mesmo seguindo **Para programadores** abaixo: mesmo resultado, binário seu.

## Captura de tela

![Screenshot](../../assets/screenshot.png)

## As cinco abas

Cinco abas; cada campo é explicado no [guia completo](../USAGE.md).

- **Lista de inicialização** — os passos rodam de cima para baixo no login. Tipos: executar programa · enviar teclas · enviar texto · volume · ação de janela · comando do sistema · grupo de ações · espera · mensagem. Cada passo tem espera posterior, número de repetições e condições (só em certos dias / só antes das N horas); programas ainda têm admin, estilo de janela, ativar-se-já-em-execução e caminhos alternativos.
- **Tarefas agendadas** — um horário (ou "no login") × uma recorrência (dia da semana / a cada N dias / mensal / uma vez) × uma ação: um lembrete (caixa Sim/Não com adiar, ou um cartão no canto, com leitura em voz alta opcional) ou um grupo de ações executado em silêncio. Além disso, execuções por intervalo, insistência repetida, recuperação de disparo perdido e Não perturbe pela bandeja.
- **Itens de inicialização do sistema** — tudo o que inicia sozinho no seu PC (chaves Run do registro, pastas de Inicialização, tarefas agendadas): desligar (desativado, não excluído), assumir para a sua própria lista de inicialização ou apagar de vez.
- **Grupos de ações** — um pacote reutilizável de ações, disparado pela bandeja, por uma **tecla global** (pressione de novo para cancelar aquela execução), por um passo da lista de inicialização ou por uma tarefa agendada. Um grupo pode repetir por inteiro e referenciar outros grupos (referências circulares são rejeitadas ao salvar); um passo de **mensagem** barra o restante com Sim / Não.
- **Configurações** — atraso de inicialização (0–600 s, só no boot), iniciar minimizado na bandeja, iniciar no login, tecla de pânico, idioma da interface (18), exportar / importar configuração.

> **Pare quando quiser** — o **botão de parada** à direita da barra de abas (só aparece enquanto algo está rodando), bandeja → **Parar ações em execução**, ou a **tecla de pânico** global (padrão `Ctrl+Alt+Q`). Esperas longas (atraso de inicialização, esperar uma janela) são interrompidas na hora.

## Dicas

- **Dê um duplo clique em uma linha para editá-la.** Ao preencher caminhos / processos / atalhos / datas você não precisa digitar manualmente: **Procurar…**, **Escolher…** (seletor de processos com busca), **Capturar** e **Escolher data**.
- **Arraste uma linha para reordená-la** — nas três listas (lista de inicialização, tarefas agendadas, grupos de ações) e na lista de etapas do editor de grupos; os botões de subir/descer continuam funcionando.
- **Teste antes de salvar** — o editor de grupos tem **▶ Executar este passo** e **▶ Executar grupo**, ambos executando o que está na tela no momento. Durante a execução o botão vira **■ Parar**, e fechar o editor também a interrompe.
- **Duplicar** (abas Tarefas agendadas / Grupos de ações) clona a linha selecionada logo abaixo dela — mais rápido que refazer uma quase idêntica; um grupo duplicado recebe o nome "… (cópia)".
- **Excluir sempre pede confirmação**, em todo lugar — linhas das listas, etapas dentro do editor de grupos e itens de inicialização do sistema.
- Dar um duplo clique em `Clockwork.exe` só abre as configurações — **não** executa imediatamente a lista de inicialização; para isso use **Reexecutar lista de inicialização** na bandeja.
- **Inicie-o normalmente** (duplo clique / bandeja / tarefa agendada). Alguns lançadores de sandbox / com privilégios reduzidos bloqueiam chamadas de baixo nível, então envio de teclas / ações de janela / ativar-se-já-estiver-aberto / enviar-texto-a-processo / volume podem não funcionar (você receberá um aviso claro; o simples "executar programa" não é afetado).
- Sua configuração é o `clockwork.settings.json` (só local). Exclua-o para redefinir ao exemplo. O estado das tarefas é o `clockwork.state.json` (também local; seguro para excluir).
- Adicionar uma etapa `.ahk` requer o AutoHotkey instalado. Teclas de atalho globais / expansão de texto estão fora do escopo — essa é a força do AutoHotkey.

## Para desenvolvedores

C#/.NET WPF; código-fonte em `app/` (requer o SDK do .NET 10). Camadas: `Core/` lógica pura · `Native/` interop Win32 · `Engine/` execução · `ViewModels/` + `Views/` interface · `I18n/` + `Resources/` localização (neutro = fonte em chinês, um satélite `Strings.<code>.resx` por idioma).

- Executar os testes (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Compilar o exe autônomo de arquivo único (arquivo único / autônomo / compressão são definidos no csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Saída: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / lançamentos** (GitHub Actions): pushes / PRs compilam e executam todos os testes em um runner Windows; enviar uma tag `v*` (por exemplo, `v2.0.0`) compila, carimba a versão do arquivo a partir da tag, cria um GitHub Release e anexa o `Clockwork-<tag>.zip` (contendo o `Clockwork.exe`).

## Sobre o Plano 365 Open Source

Projeto **#020** do [Plano 365 Open Source](https://github.com/rockbenben/365opensource) — uma pessoa + IA, mais de 300 projetos open-source em um ano.

[Envie sua ideia →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)