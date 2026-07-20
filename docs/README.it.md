<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Metti in pilota automatico le parti ripetitive del tuo PC**

Avvia le tue app automaticamente all'accesso · promemoria a tempo · un tocco per eseguire un'intera routine

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · **Italiano** · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Piano Open Source 365 #020 · Uno strumento da area di notifica per Windows: avvio automatico · promemoria · elementi di avvio del sistema · gruppi di azioni

![Clockwork](../assets/social-card.png)

Un piccolo strumento da area di notifica per Windows che si occupa delle parti di routine dell'inizio della tua giornata al computer:

- 🚀 **Elenco di avvio** — apre automaticamente le tue app di tutti i giorni all'accesso, in ordine (diritti di amministratore per singolo passaggio, ritardi, solo-in-certi-giorni-della-settimana / solo-prima-delle-N, stile della finestra, attiva-se-in-esecuzione, percorsi di ripiego) e sbriga qualche faccenda lungo il percorso (chiudere o mettere in primo piano finestre, inviare combinazioni di tasti / testo, impostare il volume…).
- ⏰ **Promemoria** — mostra un promemoria all'ora giusta; lo legge ad alta voce; lo ripete per giorno della settimana / ogni-N-giorni / mensilmente; oppure lo attiva «all'accesso». Cliccando **Sì** puoi avviare un programma, aprire un file (ad es. musica) o un URL, oppure eseguire un gruppo di azioni.
- 🧹 **Elementi di avvio del sistema** — elenca **tutto ciò che si avvia automaticamente sul tuo PC** e disattiva ciò che non ti serve (disattivato, non eliminato — riattivalo quando vuoi). Con un clic «prendi in carico» un elemento nel tuo elenco di avvio.
- 🎛️ **Gruppi di azioni** — raggruppa una serie di azioni in un gruppo riutilizzabile (Concentrazione / Riunione / Chiusura / Prima di dormire…) e attivalo con un clic dall'area di notifica, da una **scorciatoia globale**, dall'elenco di avvio o da un promemoria. Modelli integrati inclusi.

Nessuna installazione, completamente portatile in un'unica cartella, tutto configurabile con il mouse; interfaccia scura, compatibile con l'alta risoluzione (high-DPI).

> 📖 **Guida completa:** [English](USAGE.md) · [中文](USAGE.zh-CN.md)

## Requisiti

- Windows 10 / 11 (x64)
- Niente da installare: un unico file autonomo `Clockwork.exe` con il runtime .NET incorporato.

## Per iniziare

1. Scarica l'ultimo `Clockwork-<versione>.zip` dalle [Releases](https://github.com/rockbenben/Clockwork/releases) e decomprimilo — all'interno c'è un unico `Clockwork.exe`; mettilo in una cartella qualsiasi (portatile — mettilo dove vuoi). Per compilarlo tu stesso, vedi **Per gli sviluppatori** più sotto.
2. Fai doppio clic su **`Clockwork.exe`** per aprire la finestra delle impostazioni.
   - Al **primo avvio** carica una **configurazione di esempio** (che mostra avvio / promemoria / gruppi di azioni) così puoi adattarla alla tua. Le tue impostazioni risiedono in `clockwork.settings.json` accanto all'exe — solo in locale, mai inviato al repository.
3. Per eseguirlo a ogni accensione: nella scheda **Impostazioni**, clicca su **Avvia all'accesso** (registra un'operazione pianificata con diritti di amministratore, così niente valanga di richieste UAC all'avvio).

> Se ne sta tranquillo nell'area di notifica. Fai doppio clic sull'icona nell'area di notifica per aprire la finestra; il pulsante di chiusura della finestra la nasconde solo nell'area di notifica. Per uscire davvero, usa **Esci** dal clic destro sull'area di notifica.

## Schermata

![Schermata](../assets/screenshot.png)

## Le cinque schede

### Elenco di avvio

Un **elenco ordinato di passaggi** eseguiti dall'alto verso il basso all'accesso. Clicca su **Aggiungi ▾** per scegliere un tipo; aggiungi, rimuovi e riordina liberamente; ogni passaggio può essere abilitato/disabilitato, dotato di un **ritardo dopo il passaggio**, di un **numero di ripetizioni** (ripeterlo N volte) e di condizioni (**solo in certi giorni della settimana / solo prima delle N**). Tipi di passaggio:

- **Avvia programma** — destinazione (**Sfoglia…** per scegliere un file) / argomenti / cartella di lavoro (lascia vuoto = cartella della destinazione) / amministratore. La destinazione può essere un `.exe`, un documento, un collegamento o un URL; un `.ps1` viene eseguito tramite PowerShell. Avanzate: **stile della finestra** (ridotta a icona / ingrandita / nascosta), **attiva se già in esecuzione** (portala in primo piano invece di riavviarla; nome del processo tramite **Scegli…**), **percorsi di ripiego** (un percorso completo per riga; viene usato il primo esistente — comodo quando i percorsi di installazione variano da una macchina all'altra).
- **Invia tasti** — ad es. Win+D, Alt+K, Ctrl+Enter, F5 (**Cattura** per registrare una scorciatoia premendola).
- **Invia testo** — digita una stringa nella finestra attiva (o in un **processo di destinazione** scelto tramite **Scegli…**).
- **Volume** — disattiva / riattiva l'audio / imposta il livello.
- **Azione finestra** — per nome del processo (**Scegli…**, con ricerca): chiudi / riduci a icona / ingrandisci / porta-in-primo-piano / porta-in-primo-piano-e-invia-tasti; le app lente possono **attendere fino a N secondi la comparsa della finestra**.
- **Comando di sistema** — mostra il desktop / blocca / spegni il monitor / svuota il cestino / cancella gli appunti / apri Impostazioni / Gestione attività / schermata / sospendi / ibernazione / disconnetti / riavvia / arresta (le ultime tre chiedono prima conferma).
- **Ritardo** — aspetta semplicemente N secondi prima del passaggio successivo.
- **Gruppo di azioni** — esegue un gruppo di azioni definito; imposta un numero di ripetizioni per ripetere l'intero gruppo.

> **Ritardo di avvio** (scheda Impostazioni, solo all'accensione): aspetta un numero fisso di secondi dopo l'accesso in modo che la «tempesta di avvio» (contesa di disco/CPU di tutti i programmi che partono automaticamente) sia passata prima che l'elenco venga eseguito; una riesecuzione manuale non ne è influenzata. Aumentalo (0–600 s) se le cose partono troppo presto.

> **Ferma quando vuoi** — area di notifica → **Ferma le azioni in esecuzione**, oppure la **scorciatoia di emergenza** globale (impostata nella scheda Impostazioni; predefinita `Ctrl+Alt+Q`). Ciò che è in esecuzione si ferma dopo l'azione corrente; le lunghe attese (ritardo di avvio, attesa di una finestra) vengono interrotte immediatamente.

### Promemoria

Imposta un'**ora** (o passa a **all'accesso**), una **ricorrenza** (giorni della settimana / ogni-N-giorni / mensile) e il **testo**; facoltativamente leggilo ad alta voce. I promemoria con un'azione **Al-Sì** (avvia programma / apri file / URL / esegui gruppo di azioni) mostrano una finestra di dialogo **Sì / No** con un pulsante **Posticipa** (predefinito 10 min, menu ▾ da 5–60 min); gli altri scivolano dentro come una **scheda promemoria** nell'angolo (si chiude da sola dopo i secondi configurati, **0 = resta finché non la chiudi**). Puoi anche impostare un **gruppo di azioni silenzioso** — esegue un gruppo all'ora stabilita senza alcun popup.

Avanzate: **chiusura automatica**, **insistenza ripetuta** (ricompare ogni N minuti fino a una scadenza), **ritardo dopo l'attivazione + variazione casuale**, **tolleranza** (recupera un'attivazione persa a causa di un breve spegnimento/sospensione), **recupera se mancato** (si riattiva una volta se l'ibernazione/lo spegnimento l'ha saltato) e una **data di riferimento** per ogni-N-giorni (**Scegli data**). «Attivato oggi» e «posticipato fino a» sopravvivono ai riavvii (`clockwork.state.json`), così un rinvio si mantiene dopo un riavvio e niente si attiva due volte.

Hai bisogno di concentrarti o di partecipare a una riunione? L'area di notifica offre **Sospendi i promemoria per 1 / 2 / 4 ore** (Non disturbare): tutto (compresi i gruppi silenziosi) viene soppresso e riprende automaticamente allo scadere del tempo.

### Elementi di avvio del sistema

Elenca **tutto ciò che si avvia automaticamente** (chiavi Run del registro, cartelle Esecuzione automatica, operazioni pianificate). Deseleziona **Abilita** per disattivare un elemento — **disattivato, non eliminato; riseleziona per ripristinare** (ha effetto immediato). Gli elementi contrassegnati come **richiede l'amministratore** chiedono di riavviare con privilegi elevati. Gli elementi di sistema / criterio / una tantum (Run di Criteri di gruppo, RunOnce, Winlogon, Active Setup) non possono essere toccati e sono **nascosti per impostazione predefinita** — spunta **Mostra elementi di sistema / di sola lettura** per vederli (in grigio). Clicca con il tasto destro su una riga per **Assumi nell'elenco di avvio** (affida l'elemento a Clockwork; solo chiavi Run del registro ed elementi della cartella Esecuzione automatica) oppure **Elimina dal sistema** (rimuove la voce per sempre — chiede prima conferma e non si può annullare; deselezionare la casella è l'opzione reversibile). Un **filtro** in alto cerca per nome / comando; passa il mouse su un comando troncato per leggerlo per intero.

### Gruppi di azioni

Raggruppa azioni in un gruppo riutilizzabile. **Aggiungi ▾** ne avvia uno da un **modello integrato** (Concentrazione / Riunione / Chiusura / Prima di dormire / Allontanarsi / Schermata) — modifica i nomi dei processi e salva. Un gruppo **definisce solo azioni**; attivalo in quattro modi: dall'area di notifica (**Esegui: <gruppo>**), una **scorciatoia globale**, come un **passaggio di gruppo di azioni** nell'elenco di avvio (all'accensione) o da un promemoria (**Al-Sì / gruppo silenzioso**). Un gruppo esegue una sola copia alla volta; un passaggio **messaggio** può fungere da barriera di conferma (rispondere **No** interrompe il resto).

> **Scorciatoia globale** — nell'editor del gruppo, clicca sulla casella della scorciatoia e premi una combinazione (es. `Ctrl+Alt+F`) per eseguire quel gruppo da qualsiasi punto, senza menu. Esc annulla, Canc cancella. I gruppi disabilitati rilasciano la loro combinazione; le combinazioni riservate dal sistema (Alt+F4, Ctrl+Shift+Esc…) e le combinazioni già occupate da un altro gruppo o dalla scorciatoia di emergenza vengono rifiutate con un avviso.

### Impostazioni

**Ritardo di avvio** (0–600 s, solo all'accensione), **avvia ridotto a icona nell'area di notifica**, **scorciatoia di emergenza** (clicca sulla casella e premi la tua scorciatoia; Esc annulla, Canc cancella; predefinita `Ctrl+Alt+Q`) e **lingua dell'interfaccia** (cinese semplificato, inglese, 日本語 e altre 15 — 18 in totale; cambiarla riavvia l'applicazione per applicarla).

**Esporta configurazione / Importa configurazione** — sposta tutta la tua configurazione su un altro PC o tienine un backup. L'esportazione scrive una copia di `clockwork.settings.json` dove preferisci; l'importazione sostituisce **tutto** (elenco di avvio / promemoria / gruppi di azioni / impostazioni), quindi chiede prima conferma, esegue il backup della configurazione attuale in `clockwork.settings.json.bak` e riavvia l'applicazione per applicarla.

## Suggerimenti

- **Fai doppio clic su una riga per modificarla**. Quando compili percorsi / processi / scorciatoie / date non devi digitare a mano: **Sfoglia…**, **Scegli…** (selettore di processi con ricerca), **Cattura** e **Scegli data**.
- **Duplica** (schede Promemoria / Gruppi di azioni) clona la riga selezionata subito sotto di essa — più veloce che ricostruirne una quasi identica; un gruppo duplicato si chiama «… (copia)».
- **L'eliminazione chiede sempre conferma**, ovunque — righe degli elenchi, passaggi nell'editor del gruppo ed elementi di avvio del sistema.
- Fare doppio clic su `Clockwork.exe` apre solo le impostazioni — **non** esegue subito l'elenco di avvio; per quello usa **Riesegui l'elenco di avvio** dall'area di notifica.
- **Avvialo normalmente** (doppio clic / area di notifica / operazione pianificata). Alcuni launcher in sandbox / a privilegi ridotti bloccano le chiamate di basso livello, quindi invia-tasti / azioni finestra / attiva-se-in-esecuzione / invia-testo-a-processo / volume potrebbero non funzionare (riceverai un avviso chiaro; il semplice «avvia programma» non ne è influenzato).
- La tua configurazione è `clockwork.settings.json` (solo in locale). Eliminala per ripristinare l'esempio. Lo stato dei promemoria è `clockwork.state.json` (anch'esso locale; eliminabile senza problemi).
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

## Informazioni sul Piano Open Source 365

Questo è il progetto #20 del [Piano Open Source 365](https://github.com/rockbenben/365opensource) — una persona + IA, oltre 300 progetti open source in un anno. [Invia una richiesta →](https://365.aishort.top/)

## Licenza

[MIT](../LICENSE) © rockbenben
