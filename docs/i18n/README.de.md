<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Schalte die wiederkehrenden Aufgaben deines PCs auf Autopilot**

Apps beim Anmelden automatisch starten · zeitgesteuerte Erinnerungen · eine ganze Routine per Fingertipp ausführen

**[⬇ Für Windows herunterladen](https://github.com/rockbenben/Clockwork/releases/latest)** — portabel, ohne Installation

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · **Deutsch** · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![Die Startliste von Clockwork — eine geordnete Folge von Anmeldeschritten, jeder mit eigenem Typ, eigener Verzögerung und eigenen Bedingungen](../../assets/screenshot.png)

## Was es kann

- 🚀 **Startliste** — öffnet beim Anmelden deine alltäglichen Apps der Reihe nach, mit Verzögerung, Wochentagsbedingung und Fensterstil pro Schritt; schließt, fokussiert oder schaltet unterwegs etwas stumm. Schritte lassen sich auch daran knüpfen, was der Rechner gerade tut: nur wenn ein Programm läuft (oder nicht), nur am Netzteil oder nur im Akkubetrieb, nur wenn eine Datei oder ein Ordner existiert.
- ⏰ **Geplante Aufgaben** — eine Erinnerung zur rechten Zeit, auf Wunsch vorgelesen, oder eine still ausgeführte Aktionsgruppe. Ein Klick auf **Ja** startet ein Programm, öffnet eine Datei oder URL oder löst eine Gruppe aus. Oder statt der Uhr löst ein Ereignis aus — beim Entsperren, beim Sperren, beim Aufwachen, nach N Minuten Inaktivität, beim Anstecken oder Abziehen des Netzteils oder bei niedrigem Akkustand. Nur einmal schnell etwas? Im Tray gibt es eine **Schnellerinnerung** — 5 bis 60 Minuten, meldet sich einmal und löscht sich selbst. Weitere Auslöser beobachten die Hardware – ein Anzeigewechsel, das Netzwerk, das zurückkommt oder abbricht, ein eingestecktes USB-Laufwerk – und einer beobachtet Sie: Er meldet sich nach N Minuten ununterbrochener Arbeit, damit Sie aufstehen.
- 🎛️ **Aktionsgruppen** — eine Routine bündeln (Fokus / Meeting / Feierabend / Schlafenszeit …) und aus dem Tray, per **globalem Hotkey**, aus der Startliste oder einer geplanten Aufgabe auslösen. Vorlagen inklusive. Schritte können Werte weiterreichen: **Benutzereingabe, Benutzerauswahl** und **markierten Text holen** legen ihr Ergebnis jeweils in einer Variablen ab, spätere Schritte greifen es in einer URL oder einem Text als `{Name}` wieder auf – erst fragen, wonach gesucht werden soll, dann danach suchen, sind zwei Schritte.
- 🧹 **System-Autostart-Einträge** — alles, was auf deinem PC automatisch startet, in einer Liste: abschalten, was du nicht brauchst (deaktiviert, nicht gelöscht), oder in die eigene Startliste übernehmen.
- 🔌 **Ports** — jeder TCP-Port, auf dem gerade gelauscht wird, samt dem Prozess, der ihn hält, und dem Projekt, aus dem er stammt: Doppelklick auf eine Zeile öffnet `localhost:3000` im Browser, Rechtsklick gibt den Port frei und beendet dabei jeden belegenden Prozess samt Unterprozessen. Standardmäßig erscheinen nur Dienste, die aus deinen eigenen Projektverzeichnissen gestartet wurden — der vergessene Dev-Server liegt damit nicht unter Chat-Apps und Systemdiensten begraben.
- ⚡ **Schnellpanel** — eine Taste (Standard `Ctrl+Alt+Space`) öffnet ein Kachelraster genau dort, wo die Maus ohnehin steht: Ihre eigenen Aktionen, dazu Startliste erneut ausführen, Stoppen, Nicht stören und Fenster öffnen. Kachel anklicken oder mit den Pfeiltasten ansteuern und Enter drücken; **Esc** oder ein Klick daneben schließt es. Es steht auch im Infobereichsmenü – die Taste zu löschen schaltet also die Tastenkombination ab, nicht die Funktion. Lieber mit der Maus? Schalten Sie **mittlere Taste halten** ein und es geht ganz ohne Tastatur – ein normaler Mittelklick funktioniert weiterhin. Panel-Seiten legen Sie im Panel-Manager selbst an und ordnen sie dort auch; jede Kachel darauf ist ein einzelner Vorgang – Bildschirm sperren, stummschalten, App starten, URL öffnen und eine ganze Aktionsgruppe ausführen stehen damit nebeneinander. Sind es viele Seiten geworden, gruppieren Sie sie: **die obere Reihe wählt eine Kategorie, die linke Reihe listet deren Seiten**, beide Reihen sortieren Sie per Ziehen; ohne Kategorien liegt alles unter *Nicht kategorisiert* und die Kategoriereihe erscheint gar nicht erst. Gar nicht in Gebrauch? **Einstellungen → Schnellpanel verwenden** schaltet die Funktion selbst ab – Tastenkürzel und mittlere Maustaste zusammen.
- 🖱️ **Mausgesten** — rechte Taste halten, eine Spur zeichnen, und die passende Aktion läuft. Acht Richtungen, jede Schrittart (auch das Ausführen einer ganzen Aktionsgruppe). Die Spur wird beim Zeichnen auf dem Bildschirm mitgezeichnet und verschwindet beim Loslassen. **Zehn sind ab Installation einsatzbereit** (Kopieren / Einfügen / Zurück / Vorwärts / markierten Text suchen (↑↓) / ans Ende springen, dazu die vier Diagonalen für Minimieren, Maximieren, Immer-im-Vordergrund und Schließen) – so nutzbar, wie sie sind, oder nach Belieben umgeschrieben. Der Preis, gleich vorweg: eine aktive Geste genügt, damit die rechte Taste übernommen wird, **Rechts-Ziehen braucht also erst eine kurze Pause** – drücken, rund 0,2 s stillhalten, dann ziehen (eine Datei im Explorer rechts ziehen, in einer 3D-Anwendung die Ansicht drehen). Soll die rechte Taste ganz unangetastet bleiben, gibt es in der Titelzeile des Gesten-Managers einen Hauptschalter. Die mitgelieferten Gesten sind auch keine Pflicht: Schalter aus, WGestures / StrokesPlus / Quicker zeichnen lassen und die Aktionen weiterhin hier halten – per `Clockwork.exe --run-group "Fokus"`. Diesen Schalter gibt es auch im Tab **Einstellungen**: **Mausgesten verwenden**.

> **Jederzeit stoppen** — die Stopp-Schaltfläche rechts in der Registerleiste (erscheint nur, während etwas läuft), Tray → **Laufende Aktionen stoppen** oder der globale Notfall-Hotkey (Standard `Ctrl+Alt+Q`). Lange Wartezeiten werden abgekürzt, nicht abgewartet.

## Voraussetzungen

| Aspekt | Detail |
| --- | --- |
| **System** | Windows 10 / 11, x64 |
| **Installation** | Keine. Eine einzelne portable `Clockwork.exe` — leg sie in einen beliebigen Ordner |
| **Adminrechte** | Nur für „Beim Anmelden starten“ und für Schritte, die du als **als Administrator ausführen** markierst |
| **Deine Einstellungen** | `clockwork.settings.json` neben der exe (oder `%APPDATA%\Clockwork\`, falls dieser Ordner beim ersten Start schreibgeschützt war; danach bleibt sie, wo sie gelandet ist) — nichts verlässt den Rechner |
| **Oberfläche** | 18 Sprachen sowie helles / dunkles Design. Die Sprache folgt beim ersten Start Windows; das Design startet dunkel und kann Windows folgen |

**Grenzen.** Ohne Installer gibt es kein Auto-Update — lade das neue Zip und ersetze die exe. Launcher mit Sandbox blockieren Tasteneingaben, Mausaktionen, Fensteraktionen, Aktivieren-falls-läuft und Lautstärke (du bekommst einen klaren Hinweis; das schlichte „Programm starten“ funktioniert weiterhin). Tastenbelegung ändern und Textbausteine bleiben außerhalb des Umfangs — das ist AutoHotkeys Aufgabe.

## Erste Schritte

1. Lade die neueste Version aus den [Releases](https://github.com/rockbenben/Clockwork/releases) herunter — zwei Builds, drei Downloads — und leg die einzelne `Clockwork.exe`, die am Ende übrig bleibt, in einen beliebigen Ordner.
   - **`Clockwork-<Version>-win-x64.zip`** — .NET-Laufzeit enthalten, läuft auf jedem Windows 10/11 sofort. Nimm die im Zweifel, oder wenn der Rechner offline oder gesperrt ist.
   - **`Clockwork-<Version>-win-x64-needs-dotnet10.zip`** — braucht die installierte [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Einmal auf einem Rechner mit Internet installieren, danach ist jedes Update ein kleiner Download.
   - **`Clockwork.exe`** — derselbe Build wie im Zip darüber, nur ohne Zip drumherum: anklicken, starten, oder über die vorhandene Datei kopieren zum Aktualisieren. Fehlt die Laufzeit, bietet Windows den Download an.
2. Doppelklicke sie, um das Einstellungsfenster zu öffnen. Die geladenen Beispiele sind alle **nicht angehakt** — es läuft nichts, bis du es selbst aktivierst.
3. Für jeden Start: hake auf dem Tab **Einstellungen** **Beim Anmelden starten** an (registriert eine geplante Aufgabe mit Administratorrechten, sodass beim Start keine Flut von UAC-Abfragen erscheint).

Danach sitzt es im Tray: Doppelklick auf das Symbol öffnet das Fenster, und die Schließen-Schaltfläche des Fensters blendet es nur wieder aus. Wirklich beenden über den Rechtsklick im Tray → **Beenden**.

> [!IMPORTANT]
> **Die exe ist nicht signiert**, daher zeigt SmartScreen beim ersten Start „Der Computer wurde durch Windows geschützt“ — klicke auf **Weitere Informationen → Trotzdem ausführen**. Auch Virenscanner können anschlagen: Registry-Run-Schlüssel und geplante Aufgaben zu schreiben ist genau das, was ein Autostart-Manager tut und zugleich das, was Schadsoftware tut; von außen ist das nicht zu unterscheiden. Wer das nicht auf Vertrauen hin hinnehmen will, [baut es selbst](../../CONTRIBUTING.md) — gleiches Ergebnis, eigene Binärdatei. Jedes Release enthält außerdem eine `SHA256SUMS.txt` und eine GitHub-Build-Attestierung: `gh attestation verify <Datei> -R rockbenben/Clockwork` belegt, dass ein Download von der CI dieses Repositories gebaut wurde — nicht auf irgendeinem Laptop.

**Vollständige Anleitung** — jedes Feld, jeder Sonderfall: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Tipps

- **Doppelklicke eine Zeile zum Bearbeiten**. Pfade, Prozesse und Daten musst du nicht tippen: **die … Schaltfläche am Zeilenende** öffnet den passenden Auswahldialog (Datei, durchsuchbare Prozessliste, Datum), und Kürzel nimmst du mit **Aufzeichnen** einfach durch Drücken auf.
- **Ziehe eine Zeile, um sie umzusortieren** — in allen drei Listen und in der Schrittliste des Gruppeneditors; die Hoch-/Runter-Schaltflächen funktionieren weiterhin.
- **Teste es vor dem Speichern** — **▶ Diesen Schritt ausführen** und **▶ Gruppe ausführen** im Gruppeneditor führen aus, was gerade auf dem Bildschirm steht, und die Schaltfläche wird währenddessen zu **■ Stopp**.
- **Duplizieren** klont die ausgewählte Aufgabe oder Gruppe direkt darunter — schneller, als eine fast identische neu aufzubauen. **Löschen fragt immer zuerst nach**, überall.
- Ein Doppelklick auf `Clockwork.exe` öffnet nur das Fenster; er führt die Startliste **nicht** erneut aus. Nutze dafür **Startliste erneut ausführen** im Tray.

## Über den 365 Open Source Plan

Projekt **#020** des [365 Open Source Plan](https://github.com/rockbenben/365opensource) — eine Person + KI, über 300 Open-Source-Projekte in einem Jahr.

[Reiche deine Idee ein →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
