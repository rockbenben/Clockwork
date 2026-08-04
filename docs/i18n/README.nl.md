<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Zet de repetitieve taken van je pc op de automatische piloot**

Start je apps automatisch bij het aanmelden · herinneringen op tijd · met één tik een hele routine uitvoeren

**[⬇ Downloaden voor Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, geen installatie

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · **Nederlands** · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Een Windows-systeemvaktool: opstartlanceerder · herinneringen · opstartitems van het systeem · actiegroepen

![Clockwork](../../assets/social-card.png)

Een kleine Windows-systeemvaktool die de routineklusjes afhandelt waarmee je je dag achter de computer begint:

- 🚀 **Opstartlijst** — opent automatisch je dagelijkse apps bij het aanmelden, op volgorde (beheerdersrechten per stap, vertragingen, alleen-op-bepaalde-weekdagen / alleen-vóór-N-uur, vensterstijl, activeren-indien-actief, terugvalpaden) en doet onderweg wat klusjes (vensters sluiten of naar de voorgrond halen, toetsaanslagen / tekst versturen, volume instellen…).
- ⏰ **Geplande taken** — laat een herinnering op tijd verschijnen; leest hem hardop voor; herhaalt per weekdag / om-de-N-dagen / maandelijks; of triggert «bij het aanmelden». Op **Ja** klikken kan een programma starten, een bestand (bijv. muziek) of een URL openen, of een actiegroep uitvoeren. Ondersteunt ook interval-uitvoering en eenmalige planning.
- 🧹 **Opstartitems van het systeem** — toont **alles op je pc dat automatisch opstart** en schakelt uit wat je niet nodig hebt (uitgeschakeld, niet verwijderd — zet het wanneer je wilt weer aan). Met één klik «neem je een item over» in je eigen opstartlijst.
- 🎛️ **Actiegroepen** — bundel een reeks acties in een herbruikbare groep (Focus / Vergadering / Afronden / Bedtijd…) en trigger die met één klik vanuit het systeemvak, een **globale sneltoets**, de opstartlijst of een herinnering. Ingebouwde sjablonen inbegrepen.

Geen installatie, volledig draagbaar in één map, alles met de muis in te stellen; donkere interface, geschikt voor hoge resolutie (high-DPI).

> 📖 **Volledige handleiding:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Vereisten

- Windows 10 / 11 (x64)
- Niets te installeren: één op zichzelf staand `Clockwork.exe`-bestand met de .NET-runtime ingebouwd.

## Aan de slag

1. Download de nieuwste `Clockwork-<versie>.zip` van [Releases](https://github.com/rockbenben/Clockwork/releases) en pak hem uit — daarin zit één `Clockwork.exe`; zet hem in een willekeurige map (draagbaar — zet hem waar je wilt). Om hem zelf te bouwen, zie **Voor ontwikkelaars** hieronder.
2. Dubbelklik op **`Clockwork.exe`** om het instellingenvenster te openen.
   - Bij de **eerste keer starten** laadt hij een paar **voorbeelden** in de opstartlijst en de herinneringen, zodat je die naar je eigen situatie kunt aanpassen — ze staan allemaal uit, dus er draait niets tot je ze zelf aanvinkt. Het tabblad **Actiegroepen** begint ook met twee kant-en-klare groepen (Even weg / Afronden · Einde werkdag) — die staan *wel* aangevinkt, omdat een groep nooit vanzelf afgaat; hij draait alleen wanneer jij hem triggert. Je instellingen staan in `clockwork.settings.json` naast de exe — alleen lokaal, nooit vastgelegd in de repository.
3. Om hem bij elke keer opstarten uit te voeren: klik op het tabblad **Instellingen** op **Starten bij aanmelden** (registreert een geplande taak met beheerdersrechten, dus geen stortvloed aan UAC-meldingen bij het opstarten).

> Hij zit rustig in het systeemvak. Dubbelklik op het systeemvakpictogram om het venster te openen; de sluitknop van het venster verbergt het alleen in het systeemvak. Echt afsluiten doe je via **Afsluiten** in het rechtsklikmenu van het systeemvak.

> **Een waarschuwing bij de eerste start is normaal.** De exe is niet ondertekend, dus SmartScreen toont «Windows heeft uw pc beschermd» — klik op **Meer informatie → Toch uitvoeren**. Ook een virusscanner kan aanslaan: Run-sleutels in het register en geplande taken schrijven is precies wat een opstartbeheerder doet — en ook wat malware doet; van buitenaf zijn ze niet te onderscheiden. Wil je dat niet op vertrouwen aannemen, bouw hem dan zelf via **Voor ontwikkelaars** hieronder: zelfde resultaat, je eigen binary.

## Schermafbeelding

![Schermafbeelding](../../assets/screenshot.png)

## De vijf tabbladen

Vijf tabbladen; elk veld wordt stuk voor stuk uitgelegd in de [volledige handleiding](../USAGE.md).

- **Opstartlijst** — stappen lopen bij het aanmelden van boven naar beneden. Typen: programma starten · toetsen sturen · tekst sturen · volume · vensteractie · systeemopdracht · actiegroep · wachttijd · bericht. Elke stap heeft een wachttijd erna, een aantal herhalingen en voorwaarden (alleen op bepaalde weekdagen / alleen vóór N uur); programma's daarnaast beheerdersrechten, vensterstijl, activeren-als-actief en alternatieve paden.
- **Geplande taken** — een tijd (of "bij aanmelden") × een herhaling (weekdag / elke N dagen / maandelijks / eenmalig) × één actie: een herinnering (Ja/Nee-venster met uitstel, of een kaart in de hoek, desgewenst voorgelezen) of een stil uitgevoerde actiegroep. Plus intervalruns, herhaald aandringen, inhalen van een gemiste trigger en Niet storen vanuit het systeemvak.
- **Systeem-opstartitems** — alles wat op je pc automatisch start (Run-sleutels in het register, Opstartmappen, geplande taken): uitzetten (uitgeschakeld, niet verwijderd), overnemen in je eigen opstartlijst of definitief verwijderen.
- **Actiegroepen** — een herbruikbare bundel acties, gestart vanuit het systeemvak, een **globale sneltoets** (nogmaals drukken annuleert die run), een stap in de opstartlijst of een geplande taak. Een groep kan zichzelf in zijn geheel herhalen en naar andere groepen verwijzen (kringverwijzingen worden bij opslaan geweigerd); een **bericht**-stap houdt de rest tegen met Ja / Nee.
- **Instellingen** — opstartvertraging (0–600 s, alleen bij opstarten), geminimaliseerd in het systeemvak starten, starten bij aanmelden, noodstop-sneltoets, UI-taal (18 stuks), configuratie exporteren / importeren.

> **Stop wanneer je wilt** — de **stopknop** rechts in de tabbalk (alleen zichtbaar terwijl er iets loopt), systeemvak → **Lopende acties stoppen**, of de globale **noodstop-sneltoets** (standaard `Ctrl+Alt+Q`). Lange wachttijden (opstartvertraging, wachten op een venster) worden meteen afgebroken.

## Tips

- **Dubbelklik op een rij om die te bewerken**. Bij het invullen van paden / processen / sneltoetsen / datums hoef je niet met de hand te typen: **Bladeren…**, **Kiezen…** (doorzoekbare proceskiezer), **Vastleggen** en **Datum kiezen**.
- **Sleep een rij om de volgorde te wijzigen** — in alle drie de lijsten (opstartlijst, geplande taken, actiegroepen) en in de stappenlijst van de groepseditor; de omhoog/omlaag-knoppen werken nog steeds.
- **Test het vóór het opslaan** — de groepseditor heeft **▶ Deze stap uitvoeren** en **▶ Groep uitvoeren**, beide voeren uit wat er nu op het scherm staat. Tijdens het uitvoeren verandert de knop in **■ Stoppen**, en het sluiten van de editor stopt het ook.
- **Dupliceren** (tabbladen Geplande taken / Actiegroepen) kloont de geselecteerde rij er direct onder — sneller dan een bijna identieke opnieuw opbouwen; een gedupliceerde groep krijgt de naam «… (kopie)».
- **Verwijderen vraagt altijd eerst om bevestiging**, overal — rijen in lijsten, stappen in de groepseditor en opstartitems van het systeem.
- Dubbelklikken op `Clockwork.exe` opent alleen de instellingen — het voert **niet** meteen de opstartlijst uit; gebruik daarvoor **Opstartlijst opnieuw uitvoeren** in het systeemvak.
- **Start hem op de normale manier** (dubbelklik / systeemvak / geplande taak). Sommige sandbox- / verlaagde-rechten-lanceerders blokkeren aanroepen op laag niveau, dus toetsen-versturen / vensteracties / activeren-indien-actief / tekst-naar-proces-versturen / volume werken mogelijk niet (je krijgt een duidelijke melding; het gewone «programma starten» wordt niet beïnvloed).
- Je configuratie is `clockwork.settings.json` (alleen lokaal). Verwijder hem om terug te zetten naar het voorbeeld. De taakstatus is `clockwork.state.json` (ook lokaal; veilig te verwijderen).
- Een `.ahk`-stap toevoegen vereist dat AutoHotkey geïnstalleerd is. Globale sneltoetsen / tekstuitbreiding vallen buiten de scope — dat is de kracht van AutoHotkey.

## Voor ontwikkelaars

C#/.NET WPF; broncode in `app/` (vereist de .NET 10-SDK). Lagen: `Core/` pure logica · `Native/` Win32-interop · `Engine/` uitvoering · `ViewModels/` + `Views/` UI · `I18n/` + `Resources/` lokalisatie (neutraal = Chinese bron, één `Strings.<code>.resx`-satelliet per taal).

- Tests uitvoeren (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- De op zichzelf staande enkelbestands-exe bouwen (single-file / self-contained / compressie zijn ingesteld in de csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Uitvoer: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / releases** (GitHub Actions): push- / PR-builds compileren en draaien alle tests op een Windows-runner; het pushen van een `v*`-tag (bijv. `v2.0.0`) bouwt, stempelt de bestandsversie uit de tag, maakt een GitHub-Release en voegt `Clockwork-<tag>.zip` toe (met daarin `Clockwork.exe`).

## Over het 365 Open-Source Plan

Project **#020** van het [365 Open-Source Plan](https://github.com/rockbenben/365opensource) — één persoon + AI, 300+ opensourceprojecten in een jaar.

[Een verzoek indienen →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)