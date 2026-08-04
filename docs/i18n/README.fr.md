<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Mettez en pilote automatique les tâches répétitives de votre PC**

Lancez vos applications automatiquement à l'ouverture de session · rappels programmés · une seule pression pour exécuter toute une routine

**[⬇ Télécharger pour Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, sans installation

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · **Français** · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Un outil de barre d'état système pour Windows : lanceur au démarrage · rappels · éléments de démarrage du système · groupes d'actions

![Clockwork](../../assets/social-card.png)

Un petit outil de barre d'état système pour Windows qui prend en charge les tâches routinières du début de votre journée devant l'ordinateur :

- 🚀 **Liste de démarrage** — ouvre automatiquement vos applications de tous les jours à l'ouverture de session, dans l'ordre (droits d'administrateur par étape, délais, uniquement-certains-jours-de-la-semaine / uniquement-avant-N-heures, style de fenêtre, activer-si-déjà-en-cours, chemins de secours), et accomplit quelques corvées en chemin (fermer ou mettre au premier plan des fenêtres, envoyer des frappes de touches / du texte, régler le volume…).
- ⏰ **Tâches planifiées** — affiche un rappel à l'heure ; le lit à voix haute ; le répète par jour de la semaine / tous-les-N-jours / mensuellement ; ou le déclenche « à l'ouverture de session ». Cliquer sur **Oui** peut lancer un programme, ouvrir un fichier (par ex. de la musique) ou une URL, ou exécuter un groupe d'actions. Prend aussi en charge les exécutions par intervalle et la planification en une seule fois.
- 🧹 **Éléments de démarrage du système** — répertorie **tout ce qui démarre automatiquement sur votre PC** et désactive ce dont vous n'avez pas besoin (désactivé, pas supprimé — réactivez-le quand vous voulez). Un clic « récupère » un élément dans votre propre liste de démarrage.
- 🎛️ **Groupes d'actions** — regroupe une série d'actions dans un groupe réutilisable (Concentration / Réunion / Clôture / Coucher…) et déclenche-le d'un clic depuis la barre d'état système, un **raccourci global**, la liste de démarrage ou un rappel. Modèles intégrés inclus.

Sans installation, entièrement portable dans un dossier unique, tout se configure à la souris ; interface sombre, compatible haute résolution (high-DPI).

> 📖 **Guide complet :** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Prérequis

- Windows 10 / 11 (x64)
- Rien à installer : un unique fichier autonome `Clockwork.exe` avec le runtime .NET intégré.

## Prise en main

1. Téléchargez le dernier `Clockwork-<version>.zip` depuis [Releases](https://github.com/rockbenben/Clockwork/releases) et décompressez-le — à l'intérieur se trouve un unique `Clockwork.exe` ; déposez-le dans n'importe quel dossier (portable — mettez-le où vous voulez). Pour le compiler vous-même, voir **Pour les développeurs** ci-dessous.
2. Double-cliquez sur **`Clockwork.exe`** pour ouvrir la fenêtre des paramètres.
   - Au **premier lancement**, il charge quelques **exemples** dans la liste de démarrage et les rappels, que vous pouvez adapter aux vôtres — tous sont décochés au départ, donc rien ne s'exécute tant que vous ne les cochez pas. L'onglet **Groupes d'actions** démarre lui aussi avec deux groupes prêts à l'emploi (Absent un instant / Fin de journée) — ceux-là sont *cochés*, car un groupe ne se déclenche jamais tout seul ; il ne s'exécute que lorsque vous le déclenchez. Vos paramètres résident dans `clockwork.settings.json` à côté de l'exe — en local uniquement, jamais poussé sur le dépôt.
3. Pour le lancer à chaque démarrage : dans l'onglet **Paramètres**, cliquez sur **Démarrer à l'ouverture de session** (enregistre une tâche planifiée avec droits d'administrateur, donc pas de déluge d'invites UAC au démarrage).

> Il reste discret dans la barre d'état système. Double-cliquez sur l'icône de la barre pour ouvrir la fenêtre ; le bouton de fermeture de la fenêtre ne fait que la masquer dans la barre. Pour quitter vraiment, utilisez **Quitter** dans le clic droit de la barre.

> **Un avertissement au premier lancement, c'est normal.** L'exe n'est pas signé, donc SmartScreen affiche « Windows a protégé votre ordinateur » — cliquez sur **Informations complémentaires → Exécuter quand même**. Un antivirus peut aussi réagir : écrire des clés Run du registre et des tâches planifiées, c'est exactement le travail d'un gestionnaire de démarrage — et aussi ce que fait un logiciel malveillant ; de l'extérieur, rien ne les distingue. Si vous préférez ne pas l'accepter sur parole, compilez-le vous-même via **Pour les développeurs** ci-dessous : même résultat, votre propre binaire.

## Capture d'écran

![Capture d'écran](../../assets/screenshot.png)

## Les cinq onglets

Cinq onglets ; chaque champ est détaillé dans le [guide complet](../USAGE.md).

- **Liste de démarrage** — les étapes s'exécutent de haut en bas à l'ouverture de session. Types : lancer un programme · envoyer des touches · envoyer du texte · volume · action de fenêtre · commande système · groupe d'actions · délai · message. Chaque étape accepte un délai après exécution, un nombre de répétitions et des conditions (certains jours seulement / seulement avant N heures) ; les programmes ont en plus les droits admin, le style de fenêtre, activer-si-déjà-lancé et les chemins de secours.
- **Tâches planifiées** — une heure (ou « à l'ouverture de session ») × une récurrence (jour de la semaine / tous les N jours / mensuel / une seule fois) × une action : un rappel (boîte Oui/Non avec report, ou une carte dans le coin, lecture à voix haute possible) ou un groupe d'actions exécuté en silence. S'y ajoutent les exécutions par intervalle, les relances répétées, le rattrapage d'un déclenchement manqué et le mode Ne pas déranger depuis la zone de notification.
- **Éléments de démarrage du système** — tout ce qui démarre automatiquement sur votre PC (clés Run du registre, dossiers Démarrage, tâches planifiées) : le désactiver (désactivé, pas supprimé), le reprendre dans votre propre liste de démarrage ou le supprimer définitivement.
- **Groupes d'actions** — un lot d'actions réutilisable, déclenché depuis la zone de notification, un **raccourci global** (appuyez à nouveau pour annuler cette exécution), une étape de la liste de démarrage ou une tâche planifiée. Un groupe peut se répéter entièrement et référencer d'autres groupes (les références circulaires sont refusées à l'enregistrement) ; une étape **message** bloque la suite avec Oui / Non.
- **Paramètres** — délai de démarrage (0–600 s, au démarrage uniquement), démarrer réduit dans la zone de notification, lancer à l'ouverture de session, raccourci d'arrêt d'urgence, langue de l'interface (18), exporter / importer la configuration.

> **Arrêtez à tout moment** — le **bouton d'arrêt** à droite de la barre d'onglets (visible uniquement pendant une exécution), zone de notification → **Arrêter les actions en cours**, ou le **raccourci d'arrêt d'urgence** global (par défaut `Ctrl+Alt+Q`). Les longues attentes (délai de démarrage, attente d'une fenêtre) sont interrompues immédiatement.

## Astuces

- **Double-cliquez sur une ligne pour la modifier**. Pour remplir les chemins / processus / raccourcis / dates, pas besoin de tout taper à la main : **Parcourir…**, **Choisir…** (sélecteur de processus avec recherche), **Capturer** et **Choisir la date**.
- **Faites glisser une ligne pour la réordonner** — dans les trois listes (liste de démarrage, tâches planifiées, groupes d'actions) et dans la liste des étapes de l'éditeur de groupe ; les boutons haut/bas fonctionnent toujours.
- **Essayez avant d'enregistrer** — l'éditeur de groupe propose **▶ Exécuter cette étape** et **▶ Exécuter le groupe**, qui exécutent tous deux ce qui est actuellement à l'écran. Pendant l'exécution, le bouton devient **■ Arrêter**, et fermer l'éditeur l'arrête aussi.
- **Dupliquer** (onglets Tâches planifiées / Groupes d'actions) clone la ligne sélectionnée juste en dessous — plus rapide que de refaire une ligne presque identique ; un groupe dupliqué est nommé « … (copie) ».
- **La suppression demande toujours confirmation**, partout — lignes des listes, étapes dans l'éditeur de groupe et éléments de démarrage du système.
- Double-cliquer sur `Clockwork.exe` ouvre seulement les paramètres — cela **n'**exécute **pas** immédiatement la liste de démarrage ; pour cela, utilisez **Réexécuter la liste de démarrage** de la barre d'état système.
- **Lancez-le normalement** (double-clic / barre d'état système / tâche planifiée). Certains lanceurs en bac à sable / à privilèges réduits bloquent les appels de bas niveau, de sorte que envoyer-des-touches / actions de fenêtre / activer-si-déjà-en-cours / envoyer-du-texte-à-un-processus / volume peuvent ne pas fonctionner (vous recevrez un avertissement clair ; le simple « lancer un programme » n'est pas affecté).
- Votre configuration est `clockwork.settings.json` (en local uniquement). Supprimez-la pour revenir à l'exemple. L'état des tâches est `clockwork.state.json` (également local ; suppression sans risque).
- Ajouter une étape `.ahk` nécessite l'installation d'AutoHotkey. Les raccourcis globaux / l'expansion de texte sont hors du périmètre — c'est là qu'AutoHotkey excelle.

## Pour les développeurs

C#/.NET WPF ; source dans `app/` (nécessite le SDK .NET 10). Couches : `Core/` logique pure · `Native/` interop Win32 · `Engine/` exécution · `ViewModels/` + `Views/` interface · `I18n/` + `Resources/` localisation (neutre = source en chinois, un satellite `Strings.<code>.resx` par langue).

- Exécuter les tests (xUnit) :
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Compiler l'exe autonome à fichier unique (single-file / self-contained / compression sont définis dans le csproj) :
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Sortie : `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / releases** (GitHub Actions) : les builds de push / PR compilent et exécutent tous les tests sur un runner Windows ; pousser un tag `v*` (par ex. `v2.0.0`) compile, estampille la version du fichier à partir du tag, crée une Release GitHub et y attache `Clockwork-<tag>.zip` (contenant `Clockwork.exe`).

## À propos du 365 Open Source Plan

Projet **#020** du [365 Open Source Plan](https://github.com/rockbenben/365opensource) — une personne + l'IA, plus de 300 projets open source en un an.

[Proposez votre idée →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)