<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Metti in pilota automatico le parti ripetitive del tuo PC**

Avvia le tue app automaticamente all'accesso · promemoria a tempo · un tocco per eseguire un'intera routine

**[⬇ Scarica per Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portatile, senza installazione

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · **Italiano** · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![L'elenco di avvio di Clockwork — una sequenza ordinata di passi all'accesso, ciascuno con il proprio tipo, ritardo e condizioni](../../assets/screenshot.png)

## Cosa fa

- 🚀 **Elenco di avvio** — apre in ordine le tue app di tutti i giorni all'accesso, con ritardo, condizione sui giorni e stile della finestra per ogni passo; chiude, mette in primo piano o silenzia lungo il percorso. I passi possono anche dipendere da cosa sta facendo il computer: solo se un'app è in esecuzione (o non lo è), solo con l'alimentatore o solo a batteria, solo se un file o una cartella esiste.
- ⏰ **Attività pianificate** — un promemoria all'ora giusta, letto ad alta voce se vuoi, oppure un gruppo di azioni eseguito in silenzio. Cliccare **Sì** può avviare un programma, aprire un file o un URL, o lanciare un gruppo. Oppure può scattare un evento invece dell'orologio — allo sblocco, al blocco, al risveglio dalla sospensione, dopo N minuti di inattività, quando si collega o scollega l'alimentatore, o con la batteria scarica.
- 🧹 **Elementi di avvio del sistema** — tutto ciò che si avvia da solo sul tuo PC, in un unico elenco: disattiva ciò che non ti serve (disattivato, non eliminato) oppure prendilo in carico nel tuo elenco di avvio.
- 🎛️ **Gruppi di azioni** — raggruppa una routine (Concentrazione / Riunione / Chiusura / Prima di dormire…) e attivala dall'area di notifica, da una **scorciatoia globale**, dall'elenco di avvio o da un'attività pianificata. Modelli inclusi.

> **Ferma quando vuoi** — il pulsante di stop all'estremità destra della barra delle schede (compare solo mentre qualcosa è in esecuzione), area di notifica → **Ferma le azioni in corso**, oppure la scorciatoia di emergenza globale (predefinita `Ctrl+Alt+Q`). Le attese lunghe vengono troncate, non subite.

## Requisiti

| Aspetto | Dettaglio |
| --- | --- |
| **Sistema** | Windows 10 / 11, x64 |
| **Installazione** | Nessuna. Un unico `Clockwork.exe` portatile — mettilo in una cartella qualsiasi |
| **Amministratore** | Solo per «Avvia all'accesso» e per i passi che marchi **esegui come amministratore** |
| **Le tue impostazioni** | `clockwork.settings.json` accanto all'exe (o `%APPDATA%\Clockwork\` se quella cartella è di sola lettura) — nulla lascia la macchina |
| **Interfaccia** | 18 lingue, segue la lingua di Windows al primo avvio |

**Limiti.** Senza installazione non c'è aggiornamento automatico — scarica il nuovo zip e sostituisci l'exe. I launcher in sandbox bloccano invio tasti, azioni finestra, attiva-se-in-esecuzione e volume (riceverai un avviso chiaro; il semplice «avvia programma» funziona comunque). Rimappare i tasti ed espandere il testo restano fuori ambito — quello è il mestiere di AutoHotkey.

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
- Fare doppio clic su `Clockwork.exe` apre solo la finestra; **non** riesegue l'elenco di avvio. Per quello usa **Riesegui l'elenco di avvio** dall'area di notifica.

## Informazioni sul 365 Open Source Plan

Progetto **#020** del [365 Open Source Plan](https://github.com/rockbenben/365opensource) — una persona + l'IA, oltre 300 progetti open source in un anno.

[Proponi la tua idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
