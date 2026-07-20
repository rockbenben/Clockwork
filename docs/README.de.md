<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Schalte die wiederkehrenden Aufgaben deines PCs auf Autopilot**

Apps beim Anmelden automatisch starten · zeitgesteuerte Erinnerungen · eine ganze Routine per Fingertipp ausführen

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · **Deutsch** · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> 365-Open-Source-Plan #020 · Ein Windows-Tray-Tool: Autostart-Launcher · Erinnerungen · System-Autostart-Einträge · Aktionsgruppen

![Clockwork](../assets/social-card.png)

Ein kleines Windows-Tray-Tool, das sich um die Routineteile deines Tagesstarts am Computer kümmert:

- 🚀 **Startliste** — öffnet beim Anmelden deine alltäglichen Apps automatisch und der Reihe nach (pro Schritt Administratorrechte, Verzögerungen, nur an bestimmten Wochentagen / nur vor einer bestimmten Uhrzeit, Fensterstil, Aktivieren-falls-läuft, Ausweichpfade) und erledigt unterwegs ein paar Kleinigkeiten (Fenster schließen oder fokussieren, Tasteneingaben / Text senden, Lautstärke einstellen …).
- ⏰ **Erinnerungen** — blenden pünktlich eine Erinnerung ein; sprechen sie vor; wiederholen nach Wochentag / alle N Tage / monatlich; oder werden „beim Anmelden“ ausgelöst. Ein Klick auf **Ja** kann ein Programm ausführen, eine Datei (z. B. Musik) oder eine URL öffnen oder eine Aktionsgruppe ausführen.
- 🧹 **System-Autostart-Einträge** — listet **alles auf deinem PC auf, das automatisch startet**, und schaltet ab, was du nicht brauchst (deaktiviert, nicht gelöscht — jederzeit zurückschaltbar). Ein Klick „übernimmt“ einen Eintrag in deine eigene Startliste.
- 🎛️ **Aktionsgruppen** — bündeln eine Reihe von Aktionen zu einer wiederverwendbaren Gruppe (Fokus / Meeting / Feierabend / Schlafenszeit …) und lösen sie mit einem Klick aus dem Tray, einem **globalen Hotkey**, der Startliste oder einer Erinnerung aus. Integrierte Vorlagen inklusive.

Keine Installation, vollständig portabel in einem einzigen Ordner, alles per Maus konfigurierbar; dunkle Oberfläche, High-DPI-tauglich.

> 📖 **Vollständige Anleitung:** [English](USAGE.md) · [中文](USAGE.zh-CN.md)

## Voraussetzungen

- Windows 10 / 11 (x64)
- Nichts zu installieren: eine eigenständige Einzeldatei `Clockwork.exe` mit gebündelter .NET-Laufzeit.

## Erste Schritte

1. Lade die neueste `Clockwork-<Version>.zip` aus den [Releases](https://github.com/rockbenben/Clockwork/releases) herunter und entpacke sie — darin steckt eine einzelne `Clockwork.exe`; lege sie in einen beliebigen Ordner (portabel — leg sie ab, wo du willst). Zum Selbstbauen siehe **Für Entwickler** weiter unten.
2. Doppelklicke **`Clockwork.exe`**, um das Einstellungsfenster zu öffnen.
   - Beim **ersten Start** wird eine **Beispielkonfiguration** geladen (die Autostart / Erinnerungen / Aktionsgruppen demonstriert), damit du sie an deine Bedürfnisse anpassen kannst. Deine Einstellungen liegen in `clockwork.settings.json` neben der exe — nur lokal, wird nie eingecheckt.
3. Um es bei jedem Start auszuführen: klicke auf dem Tab **Einstellungen** auf **Beim Anmelden starten** (registriert eine geplante Aufgabe mit Administratorrechten, sodass beim Start keine Flut von UAC-Abfragen erscheint).

> Es sitzt still im Tray. Doppelklicke das Tray-Symbol, um das Fenster zu öffnen; die Schließen-Schaltfläche des Fensters blendet es nur in den Tray aus. Wirklich beenden über den Rechtsklick im Tray → **Beenden**.

## Screenshot

![Screenshot](../assets/screenshot.png)

## Die fünf Tabs

### Startliste

Eine **geordnete Liste von Schritten**, die beim Anmelden von oben nach unten ausgeführt wird. Klicke auf **Hinzufügen ▾**, um einen Typ zu wählen; frei hinzufügen / entfernen / umsortieren; jeder Schritt lässt sich aktivieren/deaktivieren, mit einer **Verzögerung nach dem Schritt**, einer **Wiederholungszahl** (N-mal wiederholen) und Bedingungen (**nur an bestimmten Wochentagen / nur vor einer bestimmten Uhrzeit**) versehen. Schritttypen:

- **Programm starten** — Ziel (**Durchsuchen…**, um eine Datei zu wählen) / Argumente / Arbeitsverzeichnis (leer lassen = Ordner des Ziels) / Administrator. Das Ziel kann eine `.exe`, ein Dokument, eine Verknüpfung oder eine URL sein; eine `.ps1` läuft über PowerShell. Erweitert: **Fensterstil** (minimiert / maximiert / ausgeblendet), **aktivieren, falls bereits läuft** (nach vorn holen statt neu zu starten; Prozessname über **Auswählen…**), **Ausweichpfade** (ein vollständiger Pfad pro Zeile; der erste vorhandene wird verwendet — praktisch, wenn sich Installationspfade zwischen Rechnern unterscheiden).
- **Tasten senden** — z. B. Win+D, Alt+K, Ctrl+Enter, F5 (**Aufzeichnen**, um ein Tastenkürzel durch Drücken festzuhalten).
- **Text senden** — tippt eine Zeichenfolge in das fokussierte Fenster (oder einen über **Auswählen…** gewählten **Zielprozess**).
- **Lautstärke** — stummschalten / Stummschaltung aufheben / Pegel einstellen.
- **Fensteraktion** — nach Prozessname (**Auswählen…**, durchsuchbar): schließen / minimieren / maximieren / nach vorn holen / nach vorn holen und Tasten senden; langsame Apps können **bis zu N Sekunden warten, bis das Fenster erscheint**.
- **Systembefehl** — Desktop anzeigen / sperren / Monitor ausschalten / Papierkorb leeren / Zwischenablage löschen / Einstellungen öffnen / Task-Manager / Screenshot / Energie sparen / Ruhezustand / abmelden / neu starten / herunterfahren (die letzten drei fragen zuerst nach).
- **Verzögerung** — einfach N Sekunden warten, bevor der nächste Schritt kommt.
- **Aktionsgruppe** — führt eine definierte Aktionsgruppe aus; mit einer Wiederholungszahl lässt sich die ganze Gruppe wiederholen.

> **Startverzögerung** (Tab „Einstellungen“, nur beim Start): wartet nach dem Anmelden eine feste Anzahl Sekunden, damit der „Anmeldesturm“ (Datenträger-/CPU-Konkurrenz durch alle Autostarts) vorbei ist, bevor die Liste läuft; ein manueller Neustart der Liste ist davon nicht betroffen. Erhöhe sie (0–600 s), wenn Dinge zu früh starten.

> **Jederzeit stoppen** — Tray → **Laufende Aktionen stoppen**, oder das globale **Notfall-Hotkey** (auf dem Tab „Einstellungen“ festgelegt; Standard `Ctrl+Alt+Q`). Was gerade läuft, stoppt nach der aktuellen Aktion; lange Wartezeiten (Startverzögerung, Warten auf ein Fenster) werden sofort unterbrochen.

### Erinnerungen

Lege eine **Zeit** fest (oder wechsle zu **beim Anmelden**), eine **Wiederholung** (Wochentage / alle N Tage / monatlich) und den **Text**; optional laut vorlesen. Erinnerungen mit einer **Bei-Ja-Aktion** (Programm ausführen / Datei öffnen / URL / Aktionsgruppe ausführen) blenden ein **Ja / Nein**-Dialogfeld mit einer **Schlummern**-Schaltfläche ein (Standard 10 Min., ▾-Menü 5–60 Min.); die übrigen gleiten als **Erinnerungskarte** in die Ecke (schließt nach den konfigurierten Sekunden automatisch, **0 = bleibt, bis du sie schließt**). Du kannst auch eine **stille Aktionsgruppe** einstellen — eine Gruppe pünktlich ohne Popup ausführen.

Erweitert: **automatisch schließen**, **wiederholtes Nörgeln** (alle N Minuten bis zu einem Stichtag erneut einblenden), **Verzögerung nach Auslösung + zufälliger Jitter**, **Kulanz** (eine durch kurzes Herunterfahren/Ruhezustand verpasste Auslösung nachholen), **nachholen, wenn verpasst** (einmal erneut auslösen, nachdem Ruhezustand/Herunterfahren es übersprungen hat) und ein **Ankerdatum** für alle N Tage (**Datum auswählen**). „Heute ausgelöst“ und „geschlummert bis“ überleben Neustarts (`clockwork.state.json`), sodass ein Schlummern über einen Neustart hinweg erhalten bleibt und nichts doppelt auslöst.

Musst du dich konzentrieren oder in ein Meeting? Der Tray bietet **Erinnerungen für 1 / 2 / 4 Stunden pausieren** (Nicht stören): alles (auch stille Gruppen) wird unterdrückt und nach Ablauf der Zeit automatisch fortgesetzt.

### System-Autostart-Einträge

Listet **alles auf, das automatisch startet** (Registry-Run-Schlüssel, Autostart-Ordner, geplante Aufgaben). Entferne das Häkchen bei **Aktiviert**, um einen Eintrag abzuschalten — **deaktiviert, nicht gelöscht; erneut anhaken zum Wiederherstellen** (wirkt sofort). Als **benötigt Administrator** markierte Einträge fordern zum erhöhten Neustart auf. System- / Richtlinien- / einmalige Einträge (Gruppenrichtlinien-Run, RunOnce, Winlogon, Active Setup) lassen sich nicht anfassen und sind **standardmäßig ausgeblendet** — setze ein Häkchen bei **System- / schreibgeschützte Einträge anzeigen**, um sie zu sehen (ausgegraut). Ein Rechtsklick auf eine Zeile bietet **In Startliste übernehmen** (übergibt den Eintrag an Clockwork; nur Registry-Run-Schlüssel und Autostart-Ordner-Einträge) oder **Aus dem System löschen** (entfernt den Eintrag endgültig — fragt vorher nach und lässt sich nicht rückgängig machen; das Häkchen zu entfernen ist die umkehrbare Variante). Ein **Filter** oben durchsucht nach Name / Befehl; fahre über einen abgeschnittenen Befehl, um ihn vollständig zu lesen.

### Aktionsgruppen

Bündle Aktionen zu einer wiederverwendbaren Gruppe. **Hinzufügen ▾** beginnt eine aus einer **integrierten Vorlage** (Fokus / Meeting / Feierabend / Schlafenszeit / Kurz weg / Screenshot) — passe die Prozessnamen an und speichere. Eine Gruppe **definiert nur Aktionen**; löse sie auf vier Wegen aus: aus dem Tray (**Ausführen: <Gruppe>**), einem **globalen Hotkey**, als **Aktionsgruppen-Schritt** in der Startliste (beim Start) oder aus einer Erinnerung (**Bei-Ja / stille Gruppe**). Eine Gruppe läuft immer nur in einer Kopie zugleich; ein **Nachrichten**-Schritt kann als Bestätigungssperre dienen (die Antwort **Nein** bricht den Rest ab).

> **Globales Hotkey** — klicke im Gruppeneditor in das Hotkey-Feld und drücke ein Kürzel (z. B. `Ctrl+Alt+F`), um diese Gruppe von überall auszuführen, ganz ohne Menü. Esc bricht ab, Entf löscht. Deaktivierte Gruppen geben ihre Kombination frei; systemreservierte Kombinationen (Alt+F4, Ctrl+Shift+Esc…) und Kombinationen, die bereits von einer anderen Gruppe oder dem Notfall-Hotkey belegt sind, werden mit einem Hinweis abgelehnt.

### Einstellungen

**Startverzögerung** (0–600 s, nur beim Start), **minimiert in den Tray starten**, **Notfall-Hotkey** (klicke in das Feld und drücke dein Kürzel; Esc bricht ab, Entf löscht; Standard `Ctrl+Alt+Q`) und **UI-Sprache** (vereinfachtes Chinesisch, English, 日本語 und 15 weitere — 18 insgesamt; ein Wechsel startet die App zur Übernahme neu).

**Konfiguration exportieren / Konfiguration importieren** — verschiebe deine gesamte Einrichtung auf einen anderen PC oder bewahre ein Backup auf. Der Export schreibt eine Kopie von `clockwork.settings.json` an einen beliebigen Ort; der Import ersetzt **alles** (Startliste / Erinnerungen / Aktionsgruppen / Einstellungen), fragt daher zuerst nach, sichert die aktuelle Konfiguration nach `clockwork.settings.json.bak` und startet die App zur Übernahme neu.

## Tipps

- **Doppelklicke eine Zeile zum Bearbeiten**. Beim Ausfüllen von Pfaden / Prozessen / Kürzeln / Daten musst du nicht von Hand tippen: **Durchsuchen…**, **Auswählen…** (durchsuchbarer Prozess-Picker), **Aufzeichnen** und **Datum auswählen**.
- **Duplizieren** (Tabs „Erinnerungen“ / „Aktionsgruppen“) klont die ausgewählte Zeile direkt darunter — schneller, als eine fast identische neu aufzubauen; eine duplizierte Gruppe heißt „… (Kopie)“.
- **Löschen fragt immer zuerst nach**, überall — Listenzeilen, Schritte im Gruppeneditor und System-Autostart-Einträge.
- Ein Doppelklick auf `Clockwork.exe` öffnet nur die Einstellungen — er führt die Startliste **nicht** sofort aus; nutze dafür **Startliste erneut ausführen** im Tray.
- **Starte es normal** (Doppelklick / Tray / geplante Aufgabe). Manche Sandbox- / Launcher mit reduzierten Rechten blockieren Low-Level-Aufrufe, sodass Tasten senden / Fensteraktionen / Aktivieren-falls-läuft / Text-an-Prozess-senden / Lautstärke möglicherweise nicht funktionieren (du bekommst einen klaren Hinweis; das schlichte „Programm starten“ ist nicht betroffen).
- Deine Konfiguration ist `clockwork.settings.json` (nur lokal). Lösche sie, um auf das Beispiel zurückzusetzen. Der Erinnerungsstatus ist `clockwork.state.json` (ebenfalls lokal; kann gefahrlos gelöscht werden).
- Das Hinzufügen eines `.ahk`-Schritts erfordert eine installierte AutoHotkey. Globale Hotkeys / Textexpansion sind außerhalb des Umfangs — das ist die Stärke von AutoHotkey.

## Für Entwickler

C#/.NET WPF; Quelltext in `app/` (benötigt das .NET-10-SDK). Schichten: `Core/` reine Logik · `Native/` Win32-Interop · `Engine/` Ausführung · `ViewModels/` + `Views/` UI · `I18n/` + `Resources/` Lokalisierung (neutral = chinesische Quelle, ein `Strings.<code>.resx`-Satellit pro Sprache).

- Tests ausführen (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Die eigenständige Einzeldatei-exe bauen (Einzeldatei / eigenständig / Komprimierung sind in der csproj gesetzt):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Ausgabe: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / Releases** (GitHub Actions): Push / PR baut und führt alle Tests auf einem Windows-Runner aus; das Pushen eines `v*`-Tags (z. B. `v2.0.0`) baut, prägt die Dateiversion aus dem Tag ein, erstellt ein GitHub Release und hängt `Clockwork-<Tag>.zip` (enthält `Clockwork.exe`) an.

## Über den 365-Open-Source-Plan

Dies ist Projekt Nr. 20 des [365-Open-Source-Plans](https://github.com/rockbenben/365opensource) — eine Person + KI, 300+ Open-Source-Projekte in einem Jahr. [Einen Wunsch einreichen →](https://365.aishort.top/)

## Lizenz

[MIT](../LICENSE) © rockbenben
