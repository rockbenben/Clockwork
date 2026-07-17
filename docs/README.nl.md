<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Zet de repetitieve taken van je pc op de automatische piloot**

Start je apps automatisch bij het aanmelden · herinneringen op tijd · met één tik een hele routine uitvoeren

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · **Nederlands** · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> 365 Open-Source Plan #020 · Een Windows-systeemvaktool: opstartlanceerder · herinneringen · opstartitems van het systeem · actiegroepen

![Clockwork](../assets/social-card.png)

Een kleine Windows-systeemvaktool die de routineklusjes afhandelt waarmee je je dag achter de computer begint:

- 🚀 **Opstartlijst** — opent automatisch je dagelijkse apps bij het aanmelden, op volgorde (beheerdersrechten per stap, vertragingen, alleen-op-bepaalde-weekdagen / alleen-vóór-N-uur, vensterstijl, activeren-indien-actief, terugvalpaden) en doet onderweg wat klusjes (vensters sluiten of naar de voorgrond halen, toetsaanslagen / tekst versturen, volume instellen…).
- ⏰ **Herinneringen** — laat een herinnering op tijd verschijnen; leest hem hardop voor; herhaalt per weekdag / om-de-N-dagen / maandelijks; of triggert «bij het aanmelden». Op **Ja** klikken kan een programma starten, een bestand (bijv. muziek) of een URL openen, of een actiegroep uitvoeren.
- 🧹 **Opstartitems van het systeem** — toont **alles op je pc dat automatisch opstart** en schakelt uit wat je niet nodig hebt (uitgeschakeld, niet verwijderd — zet het wanneer je wilt weer aan). Met één klik «neem je een item over» in je eigen opstartlijst.
- 🎛️ **Actiegroepen** — bundel een reeks acties in een herbruikbare groep (Focus / Vergadering / Afronden / Bedtijd…) en trigger die met één klik vanuit het systeemvak, de opstartlijst of een herinnering. Ingebouwde sjablonen inbegrepen.

Geen installatie, volledig draagbaar in één map, alles met de muis in te stellen; donkere interface, geschikt voor hoge resolutie (high-DPI).

## Vereisten

- Windows 10 / 11 (x64)
- Niets te installeren: één op zichzelf staand `Clockwork.exe`-bestand met de .NET-runtime ingebouwd.

## Aan de slag

1. Download de nieuwste `Clockwork.exe` van [Releases](https://github.com/rockbenben/Clockwork/releases) en zet hem in een willekeurige map (draagbaar — zet hem waar je wilt). Om hem zelf te bouwen, zie **Voor ontwikkelaars** hieronder.
2. Dubbelklik op **`Clockwork.exe`** om het instellingenvenster te openen.
   - Bij de **eerste keer starten** laadt hij een **voorbeeldconfiguratie** (die opstart / herinneringen / actiegroepen demonstreert) zodat je die naar je eigen situatie kunt aanpassen. Je instellingen staan in `clockwork.settings.json` naast de exe — alleen lokaal, nooit vastgelegd in de repository.
3. Om hem bij elke keer opstarten uit te voeren: klik op het tabblad **Instellingen** op **Starten bij aanmelden** (registreert een geplande taak met beheerdersrechten, dus geen stortvloed aan UAC-meldingen bij het opstarten).

> Hij zit rustig in het systeemvak. Dubbelklik op het systeemvakpictogram om het venster te openen; de sluitknop van het venster verbergt het alleen in het systeemvak. Echt afsluiten doe je via **Afsluiten** in het rechtsklikmenu van het systeemvak.

## Schermafbeelding

![Schermafbeelding](../assets/screenshot.png)

## De vijf tabbladen

### Opstartlijst
Een **geordende lijst met stappen** die bij het aanmelden van boven naar beneden worden uitgevoerd. Klik op **Toevoegen ▾** om een type te kiezen; vrij toevoegen, verwijderen en herordenen; elke stap kan worden in-/uitgeschakeld, een **vertraging na de stap**, een **aantal herhalingen** (N keer herhalen) en voorwaarden (**alleen op bepaalde weekdagen / alleen vóór N uur**) krijgen. Staptypen:

- **Programma starten** — doel (**Bladeren…** om een bestand te kiezen) / argumenten / werkmap (leeg laten = map van het doel) / beheerder. Het doel kan een `.exe`, document, snelkoppeling of URL zijn; een `.ps1` draait via PowerShell. Geavanceerd: **vensterstijl** (geminimaliseerd / gemaximaliseerd / verborgen), **activeren indien al actief** (haal het naar de voorgrond in plaats van het opnieuw te starten; procesnaam via **Kiezen…**), **terugvalpaden** (één volledig pad per regel; het eerste bestaande wordt gebruikt — handig wanneer installatiepaden per machine verschillen).
- **Toetsen versturen** — bijv. Win+D, Alt+K, Ctrl+Enter, F5 (**Vastleggen** om een sneltoets op te nemen door hem in te drukken).
- **Tekst versturen** — typt een tekenreeks in het actieve venster (of in een gekozen **doelproces** via **Kiezen…**).
- **Volume** — dempen / dempen opheffen / niveau instellen.
- **Vensteractie** — op procesnaam (**Kiezen…**, doorzoekbaar): sluiten / minimaliseren / maximaliseren / naar-voorgrond / naar-voorgrond-en-toetsen-versturen; trage apps kunnen **tot N seconden wachten tot het venster verschijnt**.
- **Systeemopdracht** — bureaublad weergeven / vergrendelen / monitor uitzetten / prullenbak legen / klembord wissen / Instellingen openen / Taakbeheer / schermafbeelding / slaapstand / sluimerstand / afmelden / opnieuw opstarten / afsluiten (de laatste drie vragen eerst om bevestiging).
- **Vertraging** — wacht gewoon N seconden vóór de volgende stap.
- **Actiegroep** — voert een gedefinieerde actiegroep uit; stel een aantal herhalingen in om de hele groep te herhalen.

> **Opstartvertraging** (tabblad Instellingen, alleen bij opstarten): wacht een vast aantal seconden na het aanmelden zodat de «opstartstorm» (schijf-/CPU-belasting van alles dat automatisch opstart) voorbij is voordat de lijst wordt uitgevoerd; een handmatige herstart wordt niet beïnvloed. Verhoog het (0–600 s) als dingen te vroeg starten.

> **Stop op elk moment** — systeemvak → **Actieve acties stoppen**, of de globale **paniek-sneltoets** (ingesteld op het tabblad Instellingen; standaard `Ctrl+Alt+Q`). Wat er draait stopt na de huidige actie; lange wachttijden (opstartvertraging, wachten op een venster) worden onmiddellijk onderbroken.

### Herinneringen
Stel een **tijd** in (of schakel over naar **bij het aanmelden**), een **herhaling** (weekdagen / om-de-N-dagen / maandelijks) en de **tekst**; lees hem eventueel hardop voor. Herinneringen met een **Bij-Ja**-actie (programma starten / bestand openen / URL / actiegroep uitvoeren) tonen een **Ja / Nee**-dialoogvenster met een knop **Uitstellen** (standaard 10 min, ▾-menu 5–60 min); de rest schuift als een **herinneringskaart** in de hoek naar binnen (sluit vanzelf na het ingestelde aantal seconden, **0 = blijft staan tot je hem sluit**). Je kunt ook een **stille actiegroep** instellen — voert een groep op tijd uit zonder enige pop-up.

Geavanceerd: **automatisch sluiten**, **herhaald aandringen** (verschijnt om de N minuten opnieuw tot een deadline), **vertraging na trigger + willekeurige spreiding**, **respijt** (haalt een trigger in die door een korte afsluiting/slaapstand is gemist), **inhalen indien gemist** (vuurt eenmaal opnieuw als sluimerstand/afsluiting hem heeft overgeslagen) en een **ankerdatum** voor om-de-N-dagen (**Datum kiezen**). «Vandaag gevuurd» en «uitgesteld tot» overleven herstarts (`clockwork.state.json`), zodat een uitstel een herstart overbrugt en niets dubbel vuurt.

Moet je je concentreren of een vergadering bijwonen? Het systeemvak biedt **Herinneringen pauzeren gedurende 1 / 2 / 4 uur** (Niet storen): alles (inclusief stille groepen) wordt onderdrukt en hervat automatisch wanneer de tijd om is.

### Opstartitems van het systeem
Toont **alles dat automatisch opstart** (Run-sleutels in het register, Opstartmappen, geplande taken). Vink **Inschakelen** uit om een item uit te schakelen — **uitgeschakeld, niet verwijderd; opnieuw aanvinken om te herstellen** (heeft direct effect). Items die als **vereist beheerder** zijn gemarkeerd, vragen om verhoogd opnieuw te starten. Systeem- / beleids- / eenmalige items (Groepsbeleid-Run, RunOnce, Winlogon, Active Setup) kunnen niet normaal worden omgeschakeld en zijn **standaard verborgen** — vink **Systeem- / alleen-lezen-items weergeven** aan om ze te bekijken (grijs weergegeven). **Overnemen in opstartlijst** draagt een item over aan Clockwork (alleen Run-sleutels in het register en items in de Opstartmap). Een **filter** bovenaan zoekt op naam / opdracht; beweeg de muis over een afgekapte opdracht om hem volledig te lezen.

### Actiegroepen
Bundel acties in een herbruikbare groep. **Toevoegen ▾** start er een op basis van een **ingebouwd sjabloon** (Focus / Vergadering / Afronden / Bedtijd / Even weg / Schermafbeelding) — pas de procesnamen aan en sla op. Een groep **definieert alleen acties**; trigger hem op drie manieren: vanuit het systeemvak (**Uitvoeren: <groep>**), als een **actiegroep-stap** in de opstartlijst (bij het opstarten) of vanuit een herinnering (**Bij-Ja / stille groep**). Een groep draait telkens maar één kopie tegelijk; een **bericht**-stap kan als bevestigingspoort dienen (met **Nee** antwoorden breekt de rest af).

### Instellingen
**Opstartvertraging** (0–600 s, alleen bij opstarten), **geminimaliseerd naar het systeemvak starten**, **paniek-sneltoets** (klik op het vak en druk je sneltoets in; Esc annuleert, Delete wist; standaard `Ctrl+Alt+Q`) en **UI-taal** (Vereenvoudigd Chinees, Engels, 日本語 en 15 meer — 18 in totaal; wisselen herstart de app om het toe te passen).

## Tips

- **Dubbelklik op een rij om die te bewerken**. Bij het invullen van paden / processen / sneltoetsen / datums hoef je niet met de hand te typen: **Bladeren…**, **Kiezen…** (doorzoekbare proceskiezer), **Vastleggen** en **Datum kiezen**.
- Dubbelklikken op `Clockwork.exe` opent alleen de instellingen — het voert **niet** meteen de opstartlijst uit; gebruik daarvoor **Opstartlijst opnieuw uitvoeren** in het systeemvak.
- **Start hem op de normale manier** (dubbelklik / systeemvak / geplande taak). Sommige sandbox- / verlaagde-rechten-lanceerders blokkeren aanroepen op laag niveau, dus toetsen-versturen / vensteracties / activeren-indien-actief / tekst-naar-proces-versturen / volume werken mogelijk niet (je krijgt een duidelijke melding; het gewone «programma starten» wordt niet beïnvloed).
- Je configuratie is `clockwork.settings.json` (alleen lokaal). Verwijder hem om terug te zetten naar het voorbeeld. De herinneringsstatus is `clockwork.state.json` (ook lokaal; veilig te verwijderen).
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
- **CI / releases** (GitHub Actions): push- / PR-builds compileren en draaien alle tests op een Windows-runner; het pushen van een `v*`-tag (bijv. `v2.0.0`) bouwt, stempelt de bestandsversie uit de tag, maakt een GitHub-Release en voegt `Clockwork.exe` toe.

## Over het 365 Open-Source Plan

Dit is project #20 van het [365 Open-Source Plan](https://github.com/rockbenben/365opensource) — één persoon + AI, 300+ opensourceprojecten in een jaar. [Een verzoek indienen →](https://my.feishu.cn/share/base/form/shrcnI6y7rrmlSjbzkYXh6sjmzb)

## Licentie

[MIT](../LICENSE) © rockbenben
