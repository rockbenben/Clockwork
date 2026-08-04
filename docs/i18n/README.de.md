<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Schalte die wiederkehrenden Aufgaben deines PCs auf Autopilot**

Apps beim Anmelden automatisch starten · zeitgesteuerte Erinnerungen · eine ganze Routine per Fingertipp ausführen

**[⬇ Für Windows herunterladen](https://github.com/rockbenben/Clockwork/releases/latest)** — portabel, ohne Installation

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · **Deutsch** · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Ein Windows-Tray-Tool: Autostart-Launcher · Erinnerungen · System-Autostart-Einträge · Aktionsgruppen

![Clockwork](../../assets/social-card.png)

Ein kleines Windows-Tray-Tool, das sich um die Routineteile deines Tagesstarts am Computer kümmert:

- 🚀 **Startliste** — öffnet beim Anmelden deine alltäglichen Apps automatisch und der Reihe nach (pro Schritt Administratorrechte, Verzögerungen, nur an bestimmten Wochentagen / nur vor einer bestimmten Uhrzeit, Fensterstil, Aktivieren-falls-läuft, Ausweichpfade) und erledigt unterwegs ein paar Kleinigkeiten (Fenster schließen oder fokussieren, Tasteneingaben / Text senden, Lautstärke einstellen …).
- ⏰ **Geplante Aufgaben** — blenden pünktlich eine Erinnerung ein; sprechen sie vor; wiederholen nach Wochentag / alle N Tage / monatlich; oder werden „beim Anmelden” ausgelöst. Ein Klick auf **Ja** kann ein Programm ausführen, eine Datei (z. B. Musik) oder eine URL öffnen oder eine Aktionsgruppe ausführen. Unterstützt außerdem Intervallausführungen und die einmalige Ausführung.
- 🧹 **System-Autostart-Einträge** — listet **alles auf deinem PC auf, das automatisch startet**, und schaltet ab, was du nicht brauchst (deaktiviert, nicht gelöscht — jederzeit zurückschaltbar). Ein Klick „übernimmt“ einen Eintrag in deine eigene Startliste.
- 🎛️ **Aktionsgruppen** — bündeln eine Reihe von Aktionen zu einer wiederverwendbaren Gruppe (Fokus / Meeting / Feierabend / Schlafenszeit …) und lösen sie mit einem Klick aus dem Tray, einem **globalen Hotkey**, der Startliste oder einer Erinnerung aus. Integrierte Vorlagen inklusive.

Keine Installation, vollständig portabel in einem einzigen Ordner, alles per Maus konfigurierbar; dunkle Oberfläche, High-DPI-tauglich.

> 📖 **Vollständige Anleitung:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Voraussetzungen

- Windows 10 / 11 (x64)
- Nichts zu installieren: eine eigenständige Einzeldatei `Clockwork.exe` mit gebündelter .NET-Laufzeit.

## Erste Schritte

1. Lade die neueste `Clockwork-<Version>.zip` aus den [Releases](https://github.com/rockbenben/Clockwork/releases) herunter und entpacke sie — darin steckt eine einzelne `Clockwork.exe`; lege sie in einen beliebigen Ordner (portabel — leg sie ab, wo du willst). Zum Selbstbauen siehe **Für Entwickler** weiter unten.
2. Doppelklicke **`Clockwork.exe`**, um das Einstellungsfenster zu öffnen.
   - Beim **ersten Start** werden ein paar **Beispiele** in der Startliste und den Erinnerungen geladen, die du an deine Bedürfnisse anpassen kannst — alle sind zunächst nicht angehakt, es läuft also nichts, bis du es selbst aktivierst. Der Tab **Aktionsgruppen** startet außerdem mit zwei einsatzbereiten Gruppen (Kurz weg / Feierabend) — die sind *angehakt*, denn eine Gruppe läuft nie von allein; sie läuft nur, wenn du sie auslöst. Deine Einstellungen liegen in `clockwork.settings.json` neben der exe — nur lokal, wird nie eingecheckt.
3. Um es bei jedem Start auszuführen: klicke auf dem Tab **Einstellungen** auf **Beim Anmelden starten** (registriert eine geplante Aufgabe mit Administratorrechten, sodass beim Start keine Flut von UAC-Abfragen erscheint).

> Es sitzt still im Tray. Doppelklicke das Tray-Symbol, um das Fenster zu öffnen; die Schließen-Schaltfläche des Fensters blendet es nur in den Tray aus. Wirklich beenden über den Rechtsklick im Tray → **Beenden**.

> **Beim ersten Start kommt eine Warnung — das ist normal.** Die exe ist nicht signiert, daher zeigt SmartScreen „Der Computer wurde durch Windows geschützt“ — klicken Sie auf **Weitere Informationen → Trotzdem ausführen**. Auch Virenscanner können anschlagen: Registry-Run-Schlüssel und geplante Aufgaben zu schreiben ist genau das, was ein Autostart-Manager tut — und zugleich das, was Schadsoftware tut; von außen ist das nicht zu unterscheiden. Wer das nicht auf Vertrauen hin hinnehmen möchte, baut sich die Anwendung nach **Für Entwickler** unten selbst — gleiches Ergebnis, eigene Binärdatei.

## Screenshot

![Screenshot](../../assets/screenshot.png)

## Die fünf Tabs

Fünf Tabs; jedes Feld wird im [vollständigen Handbuch](../USAGE.md) einzeln erklärt.

- **Startliste** — Schritte laufen beim Anmelden von oben nach unten. Typen: Programm starten · Tasten senden · Text senden · Lautstärke · Fensteraktion · Systembefehl · Aktionsgruppe · Verzögerung · Nachricht. Jeder Schritt hat eine Verzögerung danach, eine Wiederholungszahl und Bedingungen (nur an bestimmten Wochentagen / nur vor N Uhr); Programme zusätzlich Adminrechte, Fensterstil, Aktivieren-falls-läuft und Fallback-Pfade.
- **Geplante Aufgaben** — eine Uhrzeit (oder „beim Anmelden") × eine Wiederholung (Wochentag / alle N Tage / monatlich / einmalig) × eine Aktion: eine Erinnerung (Ja/Nein-Dialog mit Schlummern oder eine Karte in der Ecke, auf Wunsch vorgelesen) oder eine still ausgeführte Aktionsgruppe. Dazu Intervallläufe, wiederholtes Nachhaken, Nachholen verpasster Auslösungen und Nicht-stören aus dem Tray.
- **System-Autostart-Einträge** — alles, was auf deinem PC automatisch startet (Registry-Run-Schlüssel, Autostart-Ordner, geplante Aufgaben): abschalten (deaktiviert, nicht gelöscht), in die eigene Startliste übernehmen oder endgültig löschen.
- **Aktionsgruppen** — ein wiederverwendbares Bündel von Aktionen, ausgelöst aus dem Tray, per **globalem Hotkey** (erneut drücken bricht den Lauf ab), als Schritt in der Startliste oder aus einer geplanten Aufgabe. Eine Gruppe kann sich als Ganzes wiederholen und andere Gruppen referenzieren (Ringverweise werden beim Speichern abgelehnt); ein **Nachrichten**-Schritt hält den Rest mit Ja / Nein auf.
- **Einstellungen** — Startverzögerung (0–600 s, nur beim Start), minimiert in den Tray starten, beim Anmelden starten, Notfall-Hotkey, UI-Sprache (18 Stück), Konfiguration exportieren / importieren.

> **Jederzeit stoppen** — die **Stopp-Schaltfläche** rechts in der Registerleiste (erscheint nur, während etwas läuft), Tray → **Laufende Aktionen stoppen** oder der globale **Notfall-Hotkey** (Standard `Ctrl+Alt+Q`). Lange Wartezeiten (Startverzögerung, Warten auf ein Fenster) werden sofort unterbrochen.

## Tipps

- **Doppelklicke eine Zeile zum Bearbeiten**. Beim Ausfüllen von Pfaden / Prozessen / Kürzeln / Daten musst du nicht von Hand tippen: **Durchsuchen…**, **Auswählen…** (durchsuchbarer Prozess-Picker), **Aufzeichnen** und **Datum auswählen**.
- **Ziehe eine Zeile, um sie umzusortieren** — in allen drei Listen (Startliste, Geplante Aufgaben, Aktionsgruppen) und in der Schrittliste des Gruppeneditors; die Hoch-/Runter-Schaltflächen funktionieren weiterhin.
- **Teste es vor dem Speichern** — der Gruppeneditor hat **▶ Diesen Schritt ausführen** und **▶ Gruppe ausführen**; beide führen aus, was gerade im Editor steht. Während des Laufs wird die Schaltfläche zu **■ Stopp**, und das Schließen des Editors stoppt ihn ebenfalls.
- **Duplizieren** (Tabs „Geplante Aufgaben“ / „Aktionsgruppen“) klont die ausgewählte Zeile direkt darunter — schneller, als eine fast identische neu aufzubauen; eine duplizierte Gruppe heißt „… (Kopie)“.
- **Löschen fragt immer zuerst nach**, überall — Listenzeilen, Schritte im Gruppeneditor und System-Autostart-Einträge.
- Ein Doppelklick auf `Clockwork.exe` öffnet nur die Einstellungen — er führt die Startliste **nicht** sofort aus; nutze dafür **Startliste erneut ausführen** im Tray.
- **Starte es normal** (Doppelklick / Tray / geplante Aufgabe). Manche Sandbox- / Launcher mit reduzierten Rechten blockieren Low-Level-Aufrufe, sodass Tasten senden / Fensteraktionen / Aktivieren-falls-läuft / Text-an-Prozess-senden / Lautstärke möglicherweise nicht funktionieren (du bekommst einen klaren Hinweis; das schlichte „Programm starten“ ist nicht betroffen).
- Deine Konfiguration ist `clockwork.settings.json` (nur lokal). Lösche sie, um auf das Beispiel zurückzusetzen. Der Aufgabenstatus ist `clockwork.state.json` (ebenfalls lokal; kann gefahrlos gelöscht werden).
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

## Über den 365 Open Source Plan

Projekt **#020** des [365 Open Source Plan](https://github.com/rockbenben/365opensource) — eine Person + KI, über 300 Open-Source-Projekte in einem Jahr.

[Reiche deine Idee ein →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)