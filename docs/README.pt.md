<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Coloque as tarefas repetitivas do seu PC no piloto automático**

Abra seus apps automaticamente ao entrar · lembretes com hora marcada · um toque para executar uma rotina inteira

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · **Português** · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Plano Open-Source 365 #020 · Uma ferramenta de bandeja do Windows: lançador de inicialização · lembretes · itens de inicialização do sistema · grupos de ações

![Clockwork](../assets/social-card.png)

Uma pequena ferramenta de bandeja do Windows que cuida das partes rotineiras de começar o seu dia no computador:

- 🚀 **Lista de inicialização** — abre automaticamente os apps do dia a dia ao entrar, em ordem (direitos de administrador por etapa, atrasos, apenas-em-certos-dias-da-semana / apenas-antes-das-N-horas, estilo de janela, ativar-se-já-estiver-aberto, caminhos alternativos) e faz algumas tarefas pelo caminho (fechar ou focar janelas, enviar teclas / texto, ajustar o volume…).
- ⏰ **Lembretes** — exibe um lembrete na hora certa; fala em voz alta; repete por dia da semana / a-cada-N-dias / mensalmente; ou dispara "ao entrar". Clicar em **Sim** pode executar um programa, abrir um arquivo (por exemplo, uma música) ou uma URL, ou executar um grupo de ações.
- 🧹 **Itens de inicialização do sistema** — lista **tudo no seu PC que inicia automaticamente** e desliga o que você não precisa (desativado, não excluído — reative quando quiser). Um clique "assume" um item, passando-o para a sua própria lista de inicialização.
- 🎛️ **Grupos de ações** — agrupe uma série de ações em um grupo reutilizável (Foco / Reunião / Encerramento / Hora de dormir…) e dispare-o com um clique a partir da bandeja, de uma **tecla global**, da lista de inicialização ou de um lembrete. Modelos prontos incluídos.

Sem instalação, totalmente portátil em uma única pasta, tudo configurável com o mouse; interface escura, com suporte a alta resolução (high-DPI).

## Requisitos

- Windows 10 / 11 (x64)
- Nada a instalar: um `Clockwork.exe` autônomo de arquivo único com o runtime .NET embutido.

## Como começar

1. Baixe o `Clockwork.exe` mais recente em [Releases](https://github.com/rockbenben/Clockwork/releases) e coloque-o em qualquer pasta (portátil — ponha onde quiser). Para compilá-lo você mesmo, veja **Para desenvolvedores** abaixo.
2. Dê um duplo clique em **`Clockwork.exe`** para abrir a janela de configurações.
   - No **primeiro uso** ele carrega uma **configuração de exemplo** (demonstrando inicialização / lembretes / grupos de ações) para você adaptar à sua realidade. Suas configurações ficam em `clockwork.settings.json` ao lado do exe — só locais, nunca versionadas.
3. Para executá-lo a cada inicialização: na aba **Configurações**, clique em **Iniciar ao entrar** (registra uma tarefa agendada com direitos de administrador, evitando uma enxurrada de avisos do UAC na inicialização).

> Ele fica quietinho na bandeja. Dê um duplo clique no ícone da bandeja para abrir a janela; o botão de fechar da janela apenas a oculta na bandeja. Para sair de verdade, use **Sair** no menu de contexto da bandeja.

## Captura de tela

![Screenshot](../assets/screenshot.png)

## As cinco abas

### Lista de inicialização

Uma **lista ordenada de etapas** executadas de cima para baixo ao entrar. Clique em **Adicionar ▾** para escolher um tipo; adicione/remova/reordene à vontade; cada etapa pode ser ativada/desativada, receber um **atraso pós-etapa**, uma **contagem de repetições** (repeti-la N vezes) e condições (**apenas em certos dias da semana / apenas antes das N horas**). Tipos de etapa:

- **Executar programa** — alvo (**Procurar…** para escolher um arquivo) / argumentos / diretório de trabalho (deixe em branco = pasta do alvo) / administrador. O alvo pode ser um `.exe`, documento, atalho ou URL; um `.ps1` é executado via PowerShell. Avançado: **estilo de janela** (minimizada / maximizada / oculta), **ativar se já estiver em execução** (traz para frente em vez de reabrir; nome do processo via **Escolher…**), **caminhos alternativos** (um caminho completo por linha; o primeiro que existir é usado — útil quando os caminhos de instalação diferem entre máquinas).
- **Enviar teclas** — por exemplo, Win+D, Alt+K, Ctrl+Enter, F5 (**Capturar** para registrar um atalho pressionando-o).
- **Enviar texto** — digita uma cadeia de caracteres na janela em foco (ou em um **processo alvo** escolhido via **Escolher…**).
- **Volume** — silenciar / dessilenciar / definir o nível.
- **Ação de janela** — por nome de processo (**Escolher…**, com busca): fechar / minimizar / maximizar / trazer-para-frente / trazer-para-frente-e-enviar-teclas; apps lentos podem **esperar até N segundos até a janela aparecer**.
- **Comando de sistema** — mostrar a área de trabalho / bloquear / desligar o monitor / esvaziar a lixeira / limpar a área de transferência / abrir as Configurações / o Gerenciador de Tarefas / captura de tela / suspender / hibernar / sair da conta / reiniciar / desligar (os três últimos pedem confirmação antes).
- **Atraso** — apenas espera N segundos antes da próxima etapa.
- **Grupo de ações** — executa um grupo de ações definido; defina uma contagem de repetições para repetir o grupo inteiro.

> **Atraso de inicialização** (aba Configurações, apenas no boot): espera um número fixo de segundos após o login para que a "tempestade de login" (disputa de disco/CPU de todo autostart) passe antes de a lista rodar; uma reexecução manual não é afetada. Aumente-o (0–600 s) se as coisas começarem cedo demais.

> **Pare a qualquer momento** — bandeja → **Parar ações em execução**, ou a **tecla de pânico** global (definida na aba Configurações; padrão `Ctrl+Alt+Q`). O que estiver em execução para após a ação atual; esperas longas (atraso de inicialização, aguardar uma janela) são interrompidas imediatamente.

### Lembretes

Defina uma **hora** (ou mude para **ao entrar**), uma **recorrência** (dias da semana / a-cada-N-dias / mensal) e o **texto**; opcionalmente fale-o em voz alta. Lembretes com uma ação **Ao-Sim** (executar programa / abrir arquivo / URL / executar grupo de ações) exibem um diálogo **Sim / Não** com um botão **Adiar** (padrão 10 min, menu ▾ de 5–60 min); os demais deslizam como um **cartão de lembrete** no canto (fecha automaticamente após os segundos configurados, **0 = permanece até você dispensá-lo**). Você também pode definir um **grupo de ações silencioso** — executar um grupo na hora certa sem nenhum pop-up.

Avançado: **fechamento automático**, **insistência repetida** (reaparece a cada N minutos até um prazo), **atraso pós-disparo + variação aleatória**, **tolerância** (recupera um disparo perdido por um breve desligamento/suspensão), **recuperar se perdido** (dispara uma vez mais depois que a hibernação/o desligamento o pulou) e uma **data-âncora** para a cada N dias (**Escolher data**). "Disparado hoje" e "adiado até" sobrevivem a reinicializações (`clockwork.state.json`), então um adiamento persiste após reiniciar e nada dispara em duplicidade.

Precisa se concentrar ou entrar em uma reunião? A bandeja oferece **Pausar lembretes por 1 / 2 / 4 horas** (Não Perturbe): tudo (inclusive grupos silenciosos) é suprimido e retomado automaticamente quando o tempo acaba.

### Itens de inicialização do sistema

Lista **tudo que inicia automaticamente** (chaves Run do registro, pastas de Inicialização, tarefas agendadas). Desmarque **Ativar** para desligar um item — **desativado, não excluído; marque de novo para restaurar** (efeito imediato). Itens marcados como **precisa de administrador** pedem para reiniciar com elevação. Itens de sistema / política / de uso único (Run de Política de Grupo, RunOnce, Winlogon, Active Setup) não podem ser alternados normalmente e ficam **ocultos por padrão** — marque **Mostrar itens de sistema / somente leitura** para vê-los (esmaecidos). **Assumir na lista de inicialização** entrega um item ao Clockwork (apenas chaves Run do registro e itens da pasta de Inicialização). Um **filtro** no topo busca por nome / comando; passe o mouse sobre um comando truncado para lê-lo por inteiro.

### Grupos de ações

Agrupe ações em um grupo reutilizável. **Adicionar ▾** inicia um a partir de um **modelo pronto** (Foco / Reunião / Encerramento / Hora de dormir / Ausência / Captura de tela) — ajuste os nomes dos processos e salve. Um grupo **apenas define ações**; dispare-o de quatro formas: pela bandeja (**Executar: <grupo>**), uma **tecla global**, como uma **etapa de grupo de ações** na lista de inicialização (no boot) ou por um lembrete (**Ao-Sim / grupo silencioso**). Um grupo executa apenas uma cópia por vez; uma etapa de **mensagem** pode funcionar como uma barreira de confirmação (responder **Não** aborta o restante).

> **Tecla global** — no editor de grupos, clique na caixa da tecla e pressione um atalho (ex.: `Ctrl+Alt+F`) para executar esse grupo de qualquer lugar, sem menu. Esc cancela, Delete limpa. Grupos desativados liberam sua combinação; combinações reservadas pelo sistema (Alt+F4, Ctrl+Shift+Esc…) e combinações já ocupadas por outro grupo ou pela tecla de pânico são recusadas com um aviso.

### Configurações

**Atraso de inicialização** (0–600 s, apenas no boot), **iniciar minimizado na bandeja**, **tecla de pânico** (clique na caixa e pressione seu atalho; Esc cancela, Delete limpa; padrão `Ctrl+Alt+Q`) e **idioma da interface** (chinês simplificado, inglês, 日本語 e mais 15 — 18 no total; a troca reinicia o app para aplicar).

## Dicas

- **Dê um duplo clique em uma linha para editá-la.** Ao preencher caminhos / processos / atalhos / datas você não precisa digitar manualmente: **Procurar…**, **Escolher…** (seletor de processos com busca), **Capturar** e **Escolher data**.
- Dar um duplo clique em `Clockwork.exe` só abre as configurações — **não** executa imediatamente a lista de inicialização; para isso use **Reexecutar lista de inicialização** na bandeja.
- **Inicie-o normalmente** (duplo clique / bandeja / tarefa agendada). Alguns lançadores de sandbox / com privilégios reduzidos bloqueiam chamadas de baixo nível, então envio de teclas / ações de janela / ativar-se-já-estiver-aberto / enviar-texto-a-processo / volume podem não funcionar (você receberá um aviso claro; o simples "executar programa" não é afetado).
- Sua configuração é o `clockwork.settings.json` (só local). Exclua-o para redefinir ao exemplo. O estado dos lembretes é o `clockwork.state.json` (também local; seguro para excluir).
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
- **CI / lançamentos** (GitHub Actions): pushes / PRs compilam e executam todos os testes em um runner Windows; enviar uma tag `v*` (por exemplo, `v2.0.0`) compila, carimba a versão do arquivo a partir da tag, cria um GitHub Release e anexa o `Clockwork.exe`.

## Sobre o Plano Open-Source 365

Este é o projeto #20 do [Plano Open-Source 365](https://github.com/rockbenben/365opensource) — uma pessoa + IA, mais de 300 projetos open-source em um ano. [Enviar um pedido →](https://365.aishort.top/)

## Licença

[MIT](../LICENSE) © rockbenben
