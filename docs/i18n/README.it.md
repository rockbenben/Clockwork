<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Metti in pilota automatico le parti ripetitive del tuo PC**

Avvia le tue app automaticamente all'accesso · promemoria a tempo · un tocco per eseguire un'intera routine

**[⬇ Scarica per Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portatile, senza installazione

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · **Italiano** · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![L'elenco di avvio di Clockwork — una sequenza ordinata di passi all'accesso, ciascuno con il proprio tipo, ritardo e condizioni](../../assets/screenshot.png)

## Cosa fa

- 🚀 **Elenco di avvio** — apre in ordine le tue app di tutti i giorni all'accesso, con ritardo, condizione sui giorni e stile della finestra per ogni passo; chiude, mette in primo piano o silenzia lungo il percorso. I passi possono anche dipendere da cosa sta facendo il computer: solo se un'app è in esecuzione (o non lo è), solo con l'alimentatore o solo a batteria, solo se un file o una cartella esiste.
- ⏰ **Attività pianificate** — un promemoria all'ora giusta, letto ad alta voce se vuoi, oppure un gruppo di azioni eseguito in silenzio. Cliccare **Sì** può avviare un programma, aprire un file o un URL, o lanciare un gruppo. Oppure può scattare un evento invece dell'orologio — allo sblocco, al blocco, al risveglio dalla sospensione, dopo N minuti di inattività, quando si collega o scollega l'alimentatore, o con la batteria scarica. Ti serve solo una volta, subito? Nella barra c'è un **promemoria rapido**: da 5 a 60 minuti, suona una volta e si cancella da solo. Altri trigger guardano l'hardware — un cambio di schermo, la rete che torna o che cade, una chiavetta USB inserita — e uno guarda te: scatta dopo N minuti di lavoro senza pause, così ti ricordi di alzarti.
- 🎛️ **Gruppi di azioni** — raggruppa una routine (Concentrazione / Riunione / Chiusura / Prima di dormire…) e attivala dall'area di notifica, da una **scorciatoia globale**, dall'elenco di avvio o da un'attività pianificata. Modelli inclusi. I passi possono passarsi valori: **input dell’utente, scelta dell’utente** e **ottieni il testo selezionato** salvano ciascuno il proprio risultato in una variabile, e i passi successivi la richiamano come `{nome}` dentro un URL o un testo — «chiedimi cosa cercare, poi cercalo» sta in due passi.
- 🧹 **Elementi di avvio del sistema** — tutto ciò che si avvia da solo sul tuo PC, in un unico elenco: disattiva ciò che non ti serve (disattivato, non eliminato) oppure prendilo in carico nel tuo elenco di avvio.
- 🔌 **Porte** — ogni porta TCP in ascolto, accanto al processo che la occupa e al progetto da cui proviene: fai doppio clic su una riga per aprire `localhost:3000` nel browser, tasto destro per liberare la porta, terminando tutti i processi che la occupano insieme ai loro processi figli. Per impostazione predefinita compaiono solo i servizi avviati dalle tue cartelle di progetto, così il dev server che hai dimenticato di chiudere non resta sepolto sotto app di chat e servizi di sistema.
- ⚡ **Pannello rapido** — un tasto (predefinito `Ctrl+Alt+Space`) apre una griglia di riquadri proprio dove si trova già il mouse: le tue azioni, più riesegui l'elenco, ferma, non disturbare e apri la finestra. Clicca un riquadro, oppure raggiungilo con le frecce e premi Invio; **Esc** o un clic altrove lo chiude. C'è anche nel menu dell'area di notifica, quindi cancellare il tasto spegne la scorciatoia, non la funzione. Preferisci il mouse? Attiva **tieni premuto il tasto centrale** e si apre senza toccare la tastiera; il clic centrale normale continua a funzionare. Le pagine del pannello le crei e le disponi tu nel gestore dei pannelli, e ogni casella di una pagina è una singola operazione: blocca schermo, muto, avvia un’app, apri un URL e «esegui un intero gruppo» stanno così fianco a fianco. Quando le pagine diventano tante, raggruppale: **la riga in alto sceglie una categoria, la colonna a sinistra elenca le pagine di quella categoria**, e le riordini entrambe trascinando; senza categorie finisce tutto in *Senza categoria* e la riga delle categorie non compare nemmeno. Non lo usi affatto? **Impostazioni → Usa il pannello rapido** disattiva la funzione stessa, scorciatoia e tasto centrale insieme.
- 🖱️ **Gesti del mouse** — tieni premuto il **tasto destro**, disegna un tratto e parte l’azione corrispondente. Otto direzioni e qualsiasi tipo di passo (anche eseguire un intero gruppo). Il tratto viene disegnato sullo schermo mentre lo fai e sparisce quando rilasci. **Dieci sono pronti all’uso appena installato** (copia / incolla / indietro / avanti / cerca il testo selezionato (↑↓) / vai in fondo, più le quattro diagonali per ridurre a icona, ingrandire, tenere in primo piano e chiudere): usali così o riscrivili. Il prezzo, detto subito: basta un gesto attivo perché il tasto destro venga preso, quindi **il trascinamento col tasto destro richiede prima una breve pausa** — premi, resta fermo circa 0,2 s, poi trascina (trascinare un file col tasto destro in Esplora file, ruotare la vista in un’app 3D). Se preferisci che il tasto destro non venga toccato affatto, la riga del titolo del gestore dei gesti ha un interruttore generale. Non sei obbligato a usare quelli inclusi: spegni quell’interruttore, lascia disegnare a WGestures / StrokesPlus / Quicker e tieni qui le azioni con `Clockwork.exe --run-group "Concentrazione"`. Quell'interruttore c'è anche nella scheda **Impostazioni**: **Usa i gesti del mouse**.

> **Ferma quando vuoi** — il pulsante di stop all'estremità destra della barra delle schede (compare solo mentre qualcosa è in esecuzione), area di notifica → **Ferma le azioni in corso**, oppure la scorciatoia di emergenza globale (predefinita `Ctrl+Alt+Q`). Le attese lunghe vengono troncate, non subite.

## Requisiti

| Aspetto | Dettaglio |
| --- | --- |
| **Sistema** | Windows 10 / 11, x64 |
| **Installazione** | Nessuna. Un unico `Clockwork.exe` portatile — mettilo in una cartella qualsiasi |
| **Amministratore** | Solo per «Avvia all'accesso» e per i passi che marchi **esegui come amministratore** |
| **Le tue impostazioni** | `clockwork.settings.json` accanto all'exe (o `%APPDATA%\Clockwork\` se quella cartella era di sola lettura al primo avvio; poi resta dov'è finita) — nulla lascia la macchina |
| **Interfaccia** | 18 lingue e tema chiaro / scuro. La lingua segue Windows al primo avvio; il tema parte scuro e può seguire Windows |

**Limiti.** Senza installazione non c'è aggiornamento automatico — scarica il nuovo zip e sostituisci l'exe. I launcher in sandbox bloccano invio tasti, azioni del mouse, azioni finestra, attiva-se-in-esecuzione e volume (riceverai un avviso chiaro; il semplice «avvia programma» funziona comunque). Rimappare i tasti ed espandere il testo restano fuori ambito — quello è il mestiere di AutoHotkey.

## Per iniziare

1. Scarica l'ultima versione dalle [Releases](https://github.com/rockbenben/Clockwork/releases) — due build, tre download — e metti l'unico `Clockwork.exe` che ti resta in una cartella qualsiasi.
   - **`Clockwork-<versione>-win-x64.zip`** — runtime .NET incluso, gira così com'è su qualsiasi Windows 10/11. Scegli questo nel dubbio, o se il PC è offline o bloccato.
   - **`Clockwork-<versione>-win-x64-needs-dotnet10.zip`** — richiede il [runtime desktop di .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) installato. Installalo una volta su un PC con internet e ogni aggiornamento successivo pesa pochissimo.
   - **`Clockwork.exe`** — la stessa build dello zip qui sopra, senza zip attorno: cliccalo ed è avviato, oppure sovrascrivi la copia esistente per aggiornare. Se manca il runtime, Windows ne propone il download.
2. Fai doppio clic per aprire la finestra delle impostazioni. Gli esempi che carica sono tutti **deselezionati** — non viene eseguito nulla finché non li spunti tu.
3. Per eseguirlo a ogni accensione: nella scheda **Impostazioni** spunta **Avvia all'accesso** (registra un'operazione pianificata con diritti di amministratore, così niente valanga di richieste UAC all'avvio).

Poi se ne sta nell'area di notifica: doppio clic sull'icona per aprire la finestra, e il pulsante di chiusura la nasconde soltanto. Per uscire davvero, usa **Esci** dal clic destro sull'area di notifica.

> [!IMPORTANT]
> **L'exe non è firmato**, quindi al primo avvio SmartScreen mostra «Windows ha protetto il PC» — fai clic su **Ulteriori informazioni → Esegui comunque**. Anche un antivirus può segnalarlo: scrivere chiavi Run del registro e attività pianificate è esattamente ciò che fa un gestore dell'avvio — ed è anche ciò che fa il malware; dall'esterno non si distinguono. Se preferisci non accettarlo sulla fiducia, [compilalo tu](../../CONTRIBUTING.md) — stesso risultato, binario tuo. Ogni release include anche un `SHA256SUMS.txt` e un'attestazione di build GitHub: `gh attestation verify <file> -R rockbenben/Clockwork` dimostra che il download è stato compilato dalla CI di questo repository, non sul portatile di qualcuno.

**Guida completa** — ogni campo, ogni caso limite: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Suggerimenti

- **Fai doppio clic su una riga per modificarla**. Percorsi, processi e date non vanno digitati: **il pulsante … a fine riga** apre il selettore giusto (file, elenco processi con ricerca, data), e le scorciatoie si registrano premendole con **Cattura**.
- **Trascina una riga per riordinarla** — in tutti e tre gli elenchi e nell'elenco dei passi dell'editor del gruppo; i pulsanti su/giù continuano a funzionare.
- **Provalo prima di salvare** — **▶ Esegui questo passo** e **▶ Esegui gruppo** dell'editor eseguono ciò che è in questo momento sullo schermo, e durante l'esecuzione il pulsante diventa **■ Ferma**.
- **Duplica** clona l'attività o il gruppo selezionato subito sotto — più veloce che ricostruirne uno quasi identico. **L'eliminazione chiede sempre conferma**, ovunque.
- Fare doppio clic su `Clockwork.exe` apre solo la finestra; **non** riesegue l'elenco di avvio. Per quello usa **Riesegui elenco di avvio** dall'area di notifica.

## Informazioni sul 365 Open Source Plan

Progetto **#020** del [365 Open Source Plan](https://github.com/rockbenben/365opensource) — una persona + l'IA, oltre 300 progetti open source in un anno.

[Proponi la tua idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
