<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Zet de repetitieve taken van je pc op de automatische piloot**

Start je apps automatisch bij het aanmelden · herinneringen op tijd · met één tik een hele routine uitvoeren

**[⬇ Downloaden voor Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, geen installatie

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · **Nederlands** · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![De opstartlijst van Clockwork — een geordende reeks aanmeldstappen, elk met een eigen type, vertraging en voorwaarden](../../assets/screenshot.png)

## Wat het doet

- 🚀 **Opstartlijst** — opent je dagelijkse apps op volgorde bij het aanmelden, met per stap een vertraging, weekdagvoorwaarde en vensterstijl; sluit, focust of dempt onderweg dingen. Stappen kunnen ook afhangen van wat de machine doet: alleen terwijl een app draait (of juist niet), alleen op netstroom of alleen op accu, alleen als een bestand of map bestaat.
- ⏰ **Geplande taken** — een herinnering op tijd, desgewenst voorgelezen, of een stil uitgevoerde actiegroep. Op **Ja** klikken kan een programma starten, een bestand of URL openen, of een groep afvuren. Of laat een gebeurtenis het starten in plaats van de klok — bij ontgrendelen, bij vergrendelen, bij ontwaken uit slaapstand, na N minuten inactiviteit, bij het aansluiten of loskoppelen van de lader, of bij een lage accu. Even iets eenmaligs? In het systeemvak zit een **snelle herinnering** — 5 tot 60 minuten, gaat één keer af en verwijdert zichzelf. Nog meer triggers letten op de hardware — een weergavewijziging, het netwerk dat terugkomt of wegvalt, een aangesloten USB-stick — en één let op jou: die gaat af na N minuten onafgebroken werken, zodat je eraan denkt op te staan.
- 🎛️ **Actiegroepen** — bundel een routine (Focus / Vergadering / Afronden / Bedtijd…) en vuur die af vanuit het systeemvak, een **globale sneltoets**, de opstartlijst of een geplande taak. Sjablonen inbegrepen. Stappen kunnen waarden doorgeven: **gebruikersinvoer, gebruikerskeuze** en **geselecteerde tekst ophalen** leggen hun resultaat elk in een variabele vast, en latere stappen halen die als `{naam}` aan in een URL of een stuk tekst — «vraag me waarop ik wil zoeken en zoek daar dan op» zijn twee stappen.
- 🧹 **Opstartitems van het systeem** — alles op je pc dat automatisch opstart, in één lijst: schakel uit wat je niet nodig hebt (uitgeschakeld, niet verwijderd) of neem het over in je eigen opstartlijst.
- 🔌 **Poorten** — elke TCP-poort waarop wordt geluisterd, naast het proces dat hem bezet en het project waar hij vandaan komt: dubbelklik op een rij om `localhost:3000` in de browser te openen, rechtsklik om de poort vrij te geven, waarbij elk proces dat hem bezet met zijn onderliggende processen wordt beëindigd. Standaard verschijnen alleen diensten die vanuit je eigen projectmappen zijn gestart, zodat de vergeten dev-server niet bedolven raakt onder chat-apps en systeemservices.
- ⚡ **Snelpaneel** — één sneltoets (standaard `Ctrl+Alt+Space`) opent een tegelraster precies waar je muis al staat: je eigen acties, plus startlijst opnieuw uitvoeren, stoppen, niet storen en venster openen. Klik een tegel, of ga er met de pijltjestoetsen heen en druk op Enter; **Esc** of een klik ernaast sluit het. Het staat ook in het systeemvakmenu, dus de sneltoets wissen schakelt de sneltoets uit, niet de functie. Liever de muis? Zet **middelste knop vasthouden** aan en het opent zonder toetsenbord — een normale middelklik blijft werken. Paneelpagina's maak en orden je zelf in de paneelbeheerder, en elke tegel op een pagina is één handeling — scherm vergrendelen, dempen, een app starten, een URL openen en «een hele actiegroep uitvoeren» staan zo naast elkaar. Worden het veel pagina's, groepeer ze dan: **de bovenste rij kiest een categorie, de linkerkolom toont de pagina's van die categorie**, en beide sorteer je door te slepen; zonder categorieën staat alles onder *Zonder categorie* en verschijnt de categorierij helemaal niet. Gebruik je het helemaal niet? **Instellingen → Snelpaneel gebruiken** zet de functie zelf uit, sneltoets en middelste muisknop samen.
- 🖱️ **Muisgebaren** — houd de **rechterknop** ingedrukt, teken een streek en de bijbehorende actie wordt uitgevoerd. Acht richtingen en elk staptype (ook een hele actiegroep uitvoeren). De streek wordt tijdens het tekenen op het scherm getoond en verdwijnt bij het loslaten. **Er zitten er tien kant-en-klaar in** (kopiëren / plakken / terug / vooruit / geselecteerde tekst zoeken (↑↓) / naar onderen springen, plus de vier diagonalen voor minimaliseren, maximaliseren, altijd-op-voorgrond en sluiten) — zo te gebruiken, of naar smaak te herschrijven. De prijs, meteen gezegd: één ingeschakeld gebaar is genoeg om de rechterknop over te nemen, dus **rechts slepen vraagt eerst om een korte pauze** — indrukken, ongeveer 0,2 s stil houden, dan slepen (een bestand met de rechterknop slepen in Verkenner, de camera draaien in een 3D-app). Wil je dat de rechterknop helemaal met rust wordt gelaten, dan staat er in de titelbalk van het gebarenbeheer een hoofdschakelaar. Je hoeft de meegeleverde gebaren ook niet te gebruiken: zet die schakelaar uit, laat WGestures / StrokesPlus / Quicker het tekenen doen en houd de acties hier via `Clockwork.exe --run-group "Focus"`. Die schakelaar staat ook op het tabblad **Instellingen**: **Muisgebaren gebruiken**.

> **Stop wanneer je wilt** — de stopknop rechts in de tabbalk (alleen zichtbaar terwijl er iets loopt), systeemvak → **Lopende acties stoppen**, of de globale noodstop-sneltoets (standaard `Ctrl+Alt+Q`). Lange wachttijden worden afgekapt, niet uitgezeten.

## Vereisten

| Aspect | Detail |
| --- | --- |
| **Systeem** | Windows 10 / 11, x64 |
| **Installatie** | Geen. Eén portable `Clockwork.exe` — zet hem in een willekeurige map |
| **Beheerdersrechten** | Alleen voor «Starten bij aanmelden» en voor stappen die je markeert als **als administrator uitvoeren** |
| **Jouw instellingen** | `clockwork.settings.json` naast de exe (of `%APPDATA%\Clockwork\` als die map bij de eerste start alleen-lezen was; daarna blijft hij waar hij terechtkwam) — er verlaat niets de machine |
| **Interface** | 18 talen en een licht / donker thema. De taal volgt bij de eerste start Windows; het thema begint donker en kan Windows volgen |

**Beperkingen.** Geen installatie betekent ook geen automatische update — pak de nieuwe zip en vervang de exe. Sandbox-lanceerders blokkeren toetsen-versturen, muisacties, vensteracties, activeren-indien-actief en volume (je krijgt een duidelijke melding; het gewone «programma starten» werkt gewoon). Toetsen hertoewijzen en tekstuitbreiding vallen buiten de scope — dat is het werk van AutoHotkey.

## Aan de slag

1. Download de nieuwste versie van [Releases](https://github.com/rockbenben/Clockwork/releases) — twee builds, drie downloads — en zet de enkele `Clockwork.exe` die je overhoudt in een willekeurige map.
   - **`Clockwork-<versie>-win-x64.zip`** — .NET-runtime inbegrepen, draait zo op elke Windows 10/11. Neem deze bij twijfel, of als de pc offline of dichtgetimmerd is.
   - **`Clockwork-<versie>-win-x64-needs-dotnet10.zip`** — vereist een geïnstalleerde [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Installeer die één keer op een pc met internet en daarna is elke update een minieme download.
   - **`Clockwork.exe`** — dezelfde build als de zip hierboven, zonder zip eromheen: klikken en draaien, of over je bestaande kopie zetten om bij te werken. Ontbreekt de runtime, dan biedt Windows de download aan.
2. Dubbelklik erop om het instellingenvenster te openen. De voorbeelden die hij laadt staan allemaal **uit** — er draait niets tot je ze zelf aanvinkt.
3. Om hem bij elke keer opstarten uit te voeren: vink op het tabblad **Instellingen** **Starten bij aanmelden** aan (registreert een geplande taak met beheerdersrechten, dus geen stortvloed aan UAC-meldingen bij het opstarten).

Daarna zit hij in het systeemvak: dubbelklik op het pictogram om het venster te openen, en de sluitknop verbergt het alleen weer. Echt afsluiten doe je via **Afsluiten** in het rechtsklikmenu van het systeemvak.

> [!IMPORTANT]
> **De exe is niet ondertekend**, dus SmartScreen toont bij de eerste start «Windows heeft uw pc beschermd» — klik op **Meer informatie → Toch uitvoeren**. Ook een virusscanner kan aanslaan: Run-sleutels in het register en geplande taken schrijven is precies wat een opstartbeheerder doet — en ook wat malware doet; van buitenaf zijn ze niet te onderscheiden. Wil je dat niet op vertrouwen aannemen, [bouw hem dan zelf](../../CONTRIBUTING.md) — zelfde resultaat, je eigen binary. Elke release bevat ook een `SHA256SUMS.txt` en een GitHub-buildattestatie: `gh attestation verify <bestand> -R rockbenben/Clockwork` bewijst dat een download door de CI van deze repository is gebouwd — niet op iemands laptop.

**Volledige handleiding** — elk veld, elk randgeval: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Tips

- **Dubbelklik op een rij om die te bewerken**. Paden, processen en datums hoef je niet te typen: **de … knop aan het eind van de rij** opent de bijbehorende kiezer (bestand, doorzoekbare proceslijst, datum), en sneltoetsen neem je op door ze in te drukken via **Opnemen**.
- **Sleep een rij om de volgorde te wijzigen** — in alle drie de lijsten en in de stappenlijst van de groepseditor; de omhoog/omlaag-knoppen werken nog steeds.
- **Test het vóór het opslaan** — **▶ Deze stap uitvoeren** en **▶ Groep uitvoeren** in de groepseditor voeren uit wat er nu op het scherm staat, en de knop verandert ondertussen in **■ Stoppen**.
- **Dupliceren** kloont de geselecteerde taak of groep er direct onder — sneller dan een bijna identieke opnieuw opbouwen. **Verwijderen vraagt altijd eerst om bevestiging**, overal.
- Dubbelklikken op `Clockwork.exe` opent alleen het venster; het voert de opstartlijst **niet** opnieuw uit. Gebruik daarvoor **Opstartlijst opnieuw uitvoeren** in het systeemvak.

## Over het 365 Open-Source Plan

Project **#020** van het [365 Open-Source Plan](https://github.com/rockbenben/365opensource) — één persoon + AI, 300+ opensourceprojecten in een jaar.

[Een verzoek indienen →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
