<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Coloque as tarefas repetitivas do seu PC no piloto automático**

Abra seus apps automaticamente ao entrar · lembretes com hora marcada · um toque para executar uma rotina inteira

**[⬇ Baixar para Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portátil, sem instalador

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · **Português** · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![A lista de inicialização do Clockwork — uma sequência ordenada de etapas de login, cada uma com seu tipo, atraso e condições](../../assets/screenshot.png)

## O que ele faz

- 🚀 **Lista de inicialização** — abre em ordem os apps do dia a dia ao entrar, com atraso, condição de dia e estilo de janela por etapa; fecha, foca ou silencia coisas pelo caminho. Os passos também podem depender do que a máquina está a fazer: só enquanto uma aplicação corre (ou não), só na corrente ou só com bateria, só se um ficheiro ou pasta existir.
- ⏰ **Tarefas agendadas** — um lembrete na hora certa, falado em voz alta se você quiser, ou um grupo de ações executado em silêncio. Clicar em **Sim** pode executar um programa, abrir um arquivo ou URL, ou disparar um grupo. Ou deixe um evento disparar em vez do relógio — ao desbloquear, ao bloquear, ao retomar da suspensão, após N minutos inativo, ao ligar ou desligar o carregador, ou com a bateria fraca. Precisa de algo só uma vez, agora? Na bandeja há um **lembrete rápido**: de 5 a 60 minutos, toca uma vez e apaga-se sozinho.
- 🧹 **Itens de inicialização do sistema** — tudo o que inicia sozinho no seu PC, em uma lista: desligue o que não precisa (desativado, não excluído) ou assuma para a sua própria lista de inicialização.
- 🎛️ **Grupos de ações** — agrupe uma rotina (Foco / Reunião / Encerramento / Hora de dormir…) e dispare-a a partir da bandeja, de uma **tecla global**, da lista de inicialização ou de uma tarefa agendada. Modelos incluídos.

> **Pare quando quiser** — o botão de parada à direita da barra de abas (só aparece enquanto algo está rodando), bandeja → **Parar ações em execução**, ou a tecla de pânico global (padrão `Ctrl+Alt+Q`). Esperas longas são cortadas, não aguardadas.

## Requisitos

| Aspecto | Detalhe |
| --- | --- |
| **Sistema** | Windows 10 / 11, x64 |
| **Instalação** | Nenhuma. Um único `Clockwork.exe` portátil — coloque em qualquer pasta |
| **Administrador** | Só para «Iniciar ao entrar» e para as etapas que você marcar como **executar como administrador** |
| **Suas configurações** | `clockwork.settings.json` ao lado do exe (ou `%APPDATA%\Clockwork\` se essa pasta for somente leitura) — nada sai da máquina |
| **Interface** | 18 idiomas, seguindo o idioma do Windows no primeiro uso |

**Limites.** Sem instalador não há atualização automática — baixe o zip novo e substitua o exe. Lançadores em sandbox bloqueiam envio de teclas, ações de rato, ações de janela, ativar-se-já-estiver-aberto e volume (você recebe um aviso claro; o simples «executar programa» continua funcionando). Remapear teclas e expandir texto ficam fora do escopo — isso é trabalho do AutoHotkey.

## Como começar

1. Baixe a versão mais recente em [Releases](https://github.com/rockbenben/Clockwork/releases) — duas compilações, três downloads — e coloque o único `Clockwork.exe` que sobra em qualquer pasta.
   - **`Clockwork-<versão>-win-x64.zip`** — runtime do .NET incluído, roda como está em qualquer Windows 10/11. Escolha este na dúvida, ou se o PC estiver offline ou restrito.
   - **`Clockwork-<versão>-win-x64-needs-dotnet10.zip`** — exige o [runtime de desktop do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) instalado. Instale-o uma vez num PC com internet e cada atualização depois disso é um download mínimo.
   - **`Clockwork.exe`** — a mesma compilação do zip acima, sem zip em volta: clique e execute, ou sobreponha à sua cópia atual para atualizar. Se faltar o runtime, o Windows oferece o download.
2. Dê um duplo clique para abrir a janela de configurações. Os exemplos carregados vêm todos **desmarcados** — nada é executado até você marcar.
3. Para executá-lo a cada inicialização: na aba **Configurações**, marque **Iniciar ao entrar** (registra uma tarefa agendada com direitos de administrador, evitando uma enxurrada de avisos do UAC na inicialização).

Depois ele fica na bandeja: duplo clique no ícone para abrir a janela, e o botão de fechar apenas a oculta de novo. Para sair de verdade, use **Sair** no menu de contexto da bandeja.

> [!IMPORTANT]
> **O exe não é assinado**, então o SmartScreen mostra «O Windows protegeu o seu PC» no primeiro uso — clique em **Mais informações → Executar assim mesmo**. Algum antivírus também pode alertar: gravar chaves Run do registro e tarefas agendadas é exatamente o que um gerenciador de inicialização faz — e também o que o malware faz; de fora não dá para distinguir. Se preferir não aceitar por confiança, [compile você mesmo](../../CONTRIBUTING.md) — mesmo resultado, binário seu. Cada release inclui também um `SHA256SUMS.txt` e uma atestação de build do GitHub: `gh attestation verify <arquivo> -R rockbenben/Clockwork` comprova que o download foi compilado pela CI deste repositório, não no notebook de alguém.

**Guia completo** — cada campo, cada caso limite: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Dicas

- **Dê um duplo clique em uma linha para editá-la.** Caminhos, processos e datas não precisam ser digitados: **o botão … no fim da linha** abre o seletor correspondente (arquivo, lista de processos com busca, data), e os atalhos são gravados pressionando-os com **Capturar**.
- **Arraste uma linha para reordená-la** — nas três listas e na lista de etapas do editor de grupos; os botões de subir/descer continuam funcionando.
- **Teste antes de salvar** — **▶ Executar este passo** e **▶ Executar grupo** do editor executam o que está na tela no momento, e o botão vira **■ Parar** enquanto roda.
- **Duplicar** clona a tarefa ou o grupo selecionado logo abaixo — mais rápido que refazer um quase idêntico. **Excluir sempre pede confirmação**, em todo lugar.
- Dar um duplo clique em `Clockwork.exe` só abre a janela; **não** reexecuta a lista de inicialização. Para isso use **Reexecutar lista de inicialização** na bandeja.

## Sobre o Plano 365 Open Source

Projeto **#020** do [Plano 365 Open Source](https://github.com/rockbenben/365opensource) — uma pessoa + IA, mais de 300 projetos open-source em um ano.

[Envie sua ideia →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
