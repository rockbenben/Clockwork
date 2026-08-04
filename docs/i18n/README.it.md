<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Metti in pilota automatico le parti ripetitive del tuo PC**

Avvia le tue app automaticamente all'accesso · promemoria a tempo · un tocco per eseguire un'intera routine

**[⬇ Scarica per Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portatile, senza installazione

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · **Italiano** · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Uno strumento da area di notifica per Windows: avvio automatico · promemoria · elementi di avvio del sistema · gruppi di azioni

![Clockwork](../../assets/social-card.png)

Un piccolo strumento da area di notifica per Windows che si occupa delle parti di routine dell'inizio della tua giornata al computer:

- 🚀 **Elenco di avvio** — apre automaticamente le tue app di tutti i giorni all'accesso, in ordine (diritti di amministratore per singolo passaggio, ritardi, solo-in-certi-giorni-della-settimana / solo-prima-delle-N, stile della finestra, attiva-se-in-esecuzione, percorsi di ripiego) e sbriga qualche faccenda lungo il percorso (chiudere o mettere in primo piano finestre, inviare combinazioni di tasti / testo, impostare il volume…).
- ⏰ **Attività pianificate** — mostra un promemoria all'ora giusta; lo legge ad alta voce; lo ripete per giorno della settimana / ogni-N-giorni / mensilmente; oppure lo attiva «all'accesso». Cliccando **Sì** puoi avviare un programma, aprire un file (ad es. musica) o un URL, oppure eseguire un gruppo di azioni. Supporta anche l'esecuzione a intervalli e la pianificazione una tantum.
- 🧹 **Elementi di avvio del sistema** — elenca **tutto ciò che si avvia automaticamente sul tuo PC** e disattiva ciò che non ti serve (disattivato, non eliminato — riattivalo quando vuoi). Con un clic «prendi in carico» un elemento nel tuo elenco di avvio.
- 🎛️ **Gruppi di azioni** — raggruppa una serie di azioni in un gruppo riutilizzabile (Concentrazione / Riunione / Chiusura / Prima di dormire…) e attivalo con un clic dall'area di notifica, da una **scorciatoia globale**, dall'elenco di avvio o da un promemoria. Modelli integrati inclusi.

Nessuna installazione, completamente portatile in un'unica cartella, tutto configurabile con il mouse; interfaccia scura, compatibile con l'alta risoluzione (high-DPI).

> 📖 **Guida completa:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Requisiti

- Windows 10 / 11 (x64)
- Niente da installare: un unico file autonomo `Clockwork.exe` con il runtime .NET incorporato.

## Per iniziare

1. Scarica l'ultimo `Clockwork-<versione>.zip` dalle [Releases](https://github.com/rockbenben/Clockwork/releases) e decomprimilo — all'interno c'è un unico `Clockwork.exe`; mettilo in una cartella qualsiasi (portatile — mettilo dove vuoi). Per compilarlo tu stesso, vedi **Per gli sviluppatori** più sotto.
2. Fai doppio clic su **`Clockwork.exe`** per aprire la finestra delle impostazioni.
   - Al **primo avvio** carica qualche **esempio** nell'elenco di avvio e nei promemoria, così puoi adattarli ai tuoi — sono tutti deselezionati all'inizio, quindi non viene eseguito nulla finché non li spunti. La scheda **Gruppi di azioni** parte anche con due gruppi già pronti all'uso (Mi allontano un attimo / Fine giornata) — questi sono *già spuntati*, perché un gruppo non si avvia mai da solo; viene eseguito solo quando lo attivi tu. Le tue impostazioni risiedono in `clockwork.settings.json` accanto all'exe — solo in locale, mai inviato al repository.
3. Per eseguirlo a ogni accensione: nella scheda **Impostazioni**, clicca su **Avvia all'accesso** (registra un'operazione pianificata con diritti di amministratore, così niente valanga di richieste UAC all'avvio).

> Se ne sta tranquillo nell'area di notifica. Fai doppio clic sull'icona nell'area di notifica per aprire la finestra; il pulsante di chiusura della finestra la nasconde solo nell'area di notifica. Per uscire davvero, usa **Esci** dal clic destro sull'area di notifica.

> **Al primo avvio compare un avviso: è normale.** L'exe non è firmato, quindi SmartScreen mostra «Windows ha protetto il PC» — fai clic su **Ulteriori informazioni → Esegui comunque**. Anche un antivirus può segnalarlo: scrivere chiavi Run del registro e attività pianificate è esattamente ciò che fa un gestore dell'avvio — ed è anche ciò che fa il malware; dall'esterno non si distinguono. Se preferisci non accettarlo sulla fiducia, compilalo tu seguendo **Per gli sviluppatori** più sotto: stesso risultato, binario tuo.

## Schermata

![Schermata](../../assets/screenshot.png)

## Le cinque schede

Cinque schede; ogni campo è spiegato nella [guida completa](../USAGE.md).

- **Elenco di avvio** — i passi vengono eseguiti dall'alto in basso all'accesso. Tipi: avvia programma · invia tasti · invia testo · volume · azione finestra · comando di sistema · gruppo di azioni · attesa · messaggio. Ogni passo ha un'attesa successiva, un numero di ripetizioni e condizioni (solo in certi giorni / solo prima delle N); i programmi anche diritti di amministratore, stile della finestra, attiva-se-già-in-esecuzione e percorsi alternativi.
- **Attività pianificate** — un orario (o «all'accesso») × una ricorrenza (giorno della settimana / ogni N giorni / mensile / una sola volta) × un'azione: un promemoria (finestra Sì/No con posticipo, oppure una scheda nell'angolo, con lettura vocale opzionale) o un gruppo di azioni eseguito in silenzio. In più esecuzioni a intervalli, solleciti ripetuti, recupero di uno scatto perso e Non disturbare dall'area di notifica.
- **Elementi di avvio del sistema** — tutto ciò che si avvia da solo sul PC (chiavi Run del registro, cartelle Esecuzione automatica, attività pianificate): disattivarlo (disabilitato, non eliminato), portarlo nel tuo elenco di avvio o eliminarlo per sempre.
- **Gruppi di azioni** — un pacchetto riutilizzabile di azioni, avviato dall'area di notifica, da una **scorciatoia globale** (premila di nuovo per annullare quell'esecuzione), da un passo dell'elenco di avvio o da un'attività pianificata. Un gruppo può ripetersi per intero e referenziare altri gruppi (i riferimenti circolari vengono rifiutati al salvataggio); un passo **messaggio** blocca il resto con Sì / No.
- **Impostazioni** — ritardo di avvio (0–600 s, solo all'avvio), avvio ridotto nell'area di notifica, avvio all'accesso, scorciatoia di emergenza, lingua dell'interfaccia (18), esporta / importa configurazione.

> **Ferma quando vuoi** — il **pulsante di stop** all'estremità destra della barra delle schede (compare solo mentre qualcosa è in esecuzione), area di notifica → **Ferma le azioni in corso**, oppure la **scorciatoia di emergenza** globale (predefinita `Ctrl+Alt+Q`). Le attese lunghe (ritardo di avvio, attesa di una finestra) vengono interrotte subito.

## Suggerimenti

- **Fai doppio clic su una riga per modificarla**. Quando compili percorsi / processi / scorciatoie / date non devi digitare a mano: **Sfoglia…**, **Scegli…** (selettore di processi con ricerca), **Cattura** e **Scegli data**.
- **Trascina una riga per riordinarla** — in tutti e tre gli elenchi (elenco di avvio, attività pianificate, gruppi di azioni) e nell'elenco dei passi dell'editor del gruppo; i pulsanti su/giù continuano a funzionare.
- **Provalo prima di salvare** — l'editor del gruppo ha **▶ Esegui questo passo** e **▶ Esegui gruppo**, entrambi eseguono ciò che è in questo momento sullo schermo. Durante l'esecuzione il pulsante diventa **■ Ferma**, e anche chiudere l'editor la ferma.
- **Duplica** (schede Attività pianificate / Gruppi di azioni) clona la riga selezionata subito sotto di essa — più veloce che ricostruirne una quasi identica; un gruppo duplicato si chiama «… (copia)».
- **L'eliminazione chiede sempre conferma**, ovunque — righe degli elenchi, passaggi nell'editor del gruppo ed elementi di avvio del sistema.
- Fare doppio clic su `Clockwork.exe` apre solo le impostazioni — **non** esegue subito l'elenco di avvio; per quello usa **Riesegui l'elenco di avvio** dall'area di notifica.
- **Avvialo normalmente** (doppio clic / area di notifica / operazione pianificata). Alcuni launcher in sandbox / a privilegi ridotti bloccano le chiamate di basso livello, quindi invia-tasti / azioni finestra / attiva-se-in-esecuzione / invia-testo-a-processo / volume potrebbero non funzionare (riceverai un avviso chiaro; il semplice «avvia programma» non ne è influenzato).
- La tua configurazione è `clockwork.settings.json` (solo in locale). Eliminala per ripristinare l'esempio. Lo stato delle attività è `clockwork.state.json` (anch'esso locale; eliminabile senza problemi).
- Aggiungere un passaggio `.ahk` richiede l'installazione di AutoHotkey. Le scorciatoie globali / l'espansione di testo sono fuori ambito — quello è il punto forte di AutoHotkey.

## Per gli sviluppatori

C#/.NET WPF; sorgente in `app/` (richiede l'SDK .NET 10). Livelli: `Core/` logica pura · `Native/` interoperabilità Win32 · `Engine/` esecuzione · `ViewModels/` + `Views/` interfaccia · `I18n/` + `Resources/` localizzazione (neutrale = sorgente in cinese, un satellite `Strings.<code>.resx` per lingua).

- Eseguire i test (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Compilare l'exe autonomo a file singolo (single-file / self-contained / compressione sono impostati nel csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Output: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / releases** (GitHub Actions): le build di push / PR compilano ed eseguono tutti i test su un runner Windows; il push di un tag `v*` (ad es. `v2.0.0`) compila, marca la versione del file dal tag, crea una Release GitHub e vi allega `Clockwork-<tag>.zip` (contenente `Clockwork.exe`).

## Informazioni sul 365 Open Source Plan

Progetto **#020** del [365 Open Source Plan](https://github.com/rockbenben/365opensource) — una persona + l'IA, oltre 300 progetti open source in un anno.

[Proponi la tua idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)