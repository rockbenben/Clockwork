<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Mettez en pilote automatique les tâches répétitives de votre PC**

Lancez vos applications automatiquement à l'ouverture de session · rappels programmés · une seule pression pour exécuter toute une routine

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · **Français** · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Plan Open Source 365 #020 · Un outil de barre d'état système pour Windows : lanceur au démarrage · rappels · éléments de démarrage du système · groupes d'actions

![Clockwork](../assets/social-card.png)

Un petit outil de barre d'état système pour Windows qui prend en charge les tâches routinières du début de votre journée devant l'ordinateur :

- 🚀 **Liste de démarrage** — ouvre automatiquement vos applications de tous les jours à l'ouverture de session, dans l'ordre (droits d'administrateur par étape, délais, uniquement-certains-jours-de-la-semaine / uniquement-avant-N-heures, style de fenêtre, activer-si-déjà-en-cours, chemins de secours), et accomplit quelques corvées en chemin (fermer ou mettre au premier plan des fenêtres, envoyer des frappes de touches / du texte, régler le volume…).
- ⏰ **Rappels** — affiche un rappel à l'heure ; le lit à voix haute ; le répète par jour de la semaine / tous-les-N-jours / mensuellement ; ou le déclenche « à l'ouverture de session ». Cliquer sur **Oui** peut lancer un programme, ouvrir un fichier (par ex. de la musique) ou une URL, ou exécuter un groupe d'actions.
- 🧹 **Éléments de démarrage du système** — répertorie **tout ce qui démarre automatiquement sur votre PC** et désactive ce dont vous n'avez pas besoin (désactivé, pas supprimé — réactivez-le quand vous voulez). Un clic « récupère » un élément dans votre propre liste de démarrage.
- 🎛️ **Groupes d'actions** — regroupe une série d'actions dans un groupe réutilisable (Concentration / Réunion / Clôture / Coucher…) et déclenche-le d'un clic depuis la barre d'état système, un **raccourci global**, la liste de démarrage ou un rappel. Modèles intégrés inclus.

Sans installation, entièrement portable dans un dossier unique, tout se configure à la souris ; interface sombre, compatible haute résolution (high-DPI).

## Prérequis

- Windows 10 / 11 (x64)
- Rien à installer : un unique fichier autonome `Clockwork.exe` avec le runtime .NET intégré.

## Prise en main

1. Téléchargez le dernier `Clockwork.exe` depuis [Releases](https://github.com/rockbenben/Clockwork/releases) et déposez-le dans n'importe quel dossier (portable — mettez-le où vous voulez). Pour le compiler vous-même, voir **Pour les développeurs** ci-dessous.
2. Double-cliquez sur **`Clockwork.exe`** pour ouvrir la fenêtre des paramètres.
   - Au **premier lancement**, il charge une **configuration d'exemple** (illustrant démarrage / rappels / groupes d'actions) que vous pouvez adapter à la vôtre. Vos paramètres résident dans `clockwork.settings.json` à côté de l'exe — en local uniquement, jamais poussé sur le dépôt.
3. Pour le lancer à chaque démarrage : dans l'onglet **Paramètres**, cliquez sur **Démarrer à l'ouverture de session** (enregistre une tâche planifiée avec droits d'administrateur, donc pas de déluge d'invites UAC au démarrage).

> Il reste discret dans la barre d'état système. Double-cliquez sur l'icône de la barre pour ouvrir la fenêtre ; le bouton de fermeture de la fenêtre ne fait que la masquer dans la barre. Pour quitter vraiment, utilisez **Quitter** dans le clic droit de la barre.

## Capture d'écran

![Capture d'écran](../assets/screenshot.png)

## Les cinq onglets

### Liste de démarrage

Une **liste ordonnée d'étapes** exécutées de haut en bas à l'ouverture de session. Cliquez sur **Ajouter ▾** pour choisir un type ; ajoutez, supprimez et réorganisez librement ; chaque étape peut être activée/désactivée, dotée d'un **délai après l'étape**, d'un **nombre de répétitions** (la répéter N fois) et de conditions (**uniquement certains jours de la semaine / uniquement avant N heures**). Types d'étape :

- **Lancer un programme** — cible (**Parcourir…** pour choisir un fichier) / arguments / dossier de travail (laissez vide = dossier de la cible) / administrateur. La cible peut être un `.exe`, un document, un raccourci ou une URL ; un `.ps1` s'exécute via PowerShell. Avancé : **style de fenêtre** (réduite / agrandie / masquée), **activer si déjà en cours** (la mettre au premier plan au lieu de la relancer ; nom du processus via **Choisir…**), **chemins de secours** (un chemin complet par ligne ; le premier existant est utilisé — pratique quand les chemins d'installation diffèrent d'une machine à l'autre).
- **Envoyer des touches** — par ex. Win+D, Alt+K, Ctrl+Enter, F5 (**Capturer** pour enregistrer un raccourci en l'appuyant).
- **Envoyer du texte** — saisit une chaîne dans la fenêtre active (ou dans un **processus cible** choisi via **Choisir…**).
- **Volume** — couper / rétablir le son / régler le niveau.
- **Action de fenêtre** — par nom de processus (**Choisir…**, avec recherche) : fermer / réduire / agrandir / mettre-au-premier-plan / mettre-au-premier-plan-et-envoyer-des-touches ; les applications lentes peuvent **attendre jusqu'à N secondes l'apparition de la fenêtre**.
- **Commande système** — afficher le bureau / verrouiller / éteindre l'écran / vider la corbeille / effacer le presse-papiers / ouvrir les Paramètres / le Gestionnaire des tâches / capture d'écran / mettre en veille / mettre en veille prolongée / se déconnecter / redémarrer / arrêter (les trois dernières demandent d'abord confirmation).
- **Délai** — attend simplement N secondes avant l'étape suivante.
- **Groupe d'actions** — exécute un groupe d'actions défini ; fixez un nombre de répétitions pour répéter tout le groupe.

> **Délai de démarrage** (onglet Paramètres, au démarrage uniquement) : attend un nombre fixe de secondes après l'ouverture de session pour laisser passer la « tempête de démarrage » (contention disque/CPU de tous les programmes qui se lancent automatiquement) avant l'exécution de la liste ; une réexécution manuelle n'est pas concernée. Augmentez-le (0–600 s) si les choses démarrent trop tôt.

> **Arrêtez à tout moment** — barre d'état système → **Arrêter les actions en cours**, ou le **raccourci panique** global (défini dans l'onglet Paramètres ; par défaut `Ctrl+Alt+Q`). Ce qui est en cours s'arrête après l'action courante ; les longues attentes (délai de démarrage, attente d'une fenêtre) sont interrompues immédiatement.

### Rappels

Fixez une **heure** (ou passez à **à l'ouverture de session**), une **récurrence** (jours de la semaine / tous-les-N-jours / mensuel) et le **texte** ; éventuellement lisez-le à voix haute. Les rappels dotés d'une action **Sur-Oui** (lancer un programme / ouvrir un fichier / URL / exécuter un groupe d'actions) affichent une boîte de dialogue **Oui / Non** avec un bouton **Répéter plus tard** (par défaut 10 min, menu ▾ de 5–60 min) ; les autres apparaissent en glissant sous forme de **carte de rappel** dans le coin (se ferme d'elle-même après le nombre de secondes configuré, **0 = reste jusqu'à ce que vous la fermiez**). Vous pouvez aussi définir un **groupe d'actions silencieux** — exécute un groupe à l'heure dite sans aucune fenêtre.

Avancé : **fermeture automatique**, **relance insistante** (réapparaît toutes les N minutes jusqu'à une échéance), **délai après déclenchement + variation aléatoire**, **délai de grâce** (rattrape un déclenchement manqué à cause d'un bref arrêt/veille), **rattrapage si manqué** (se redéclenche une fois si une mise en veille prolongée/un arrêt l'a sauté) et une **date d'ancrage** pour tous-les-N-jours (**Choisir la date**). « Déclenché aujourd'hui » et « reporté jusqu'à » survivent aux redémarrages (`clockwork.state.json`), si bien qu'un report se maintient après un redémarrage et rien ne se déclenche deux fois.

Besoin de vous concentrer ou de participer à une réunion ? La barre d'état système propose **Suspendre les rappels pendant 1 / 2 / 4 heures** (Ne pas déranger) : tout (y compris les groupes silencieux) est supprimé et reprend automatiquement à la fin du délai.

### Éléments de démarrage du système

Répertorie **tout ce qui démarre automatiquement** (clés Run du registre, dossiers Démarrage, tâches planifiées). Décochez **Activer** pour désactiver un élément — **désactivé, pas supprimé ; recochez pour restaurer** (effet immédiat). Les éléments marqués **nécessite l'administrateur** invitent à relancer en mode élevé. Les éléments système / de stratégie / à usage unique (Run de stratégie de groupe, RunOnce, Winlogon, Active Setup) ne peuvent pas être basculés normalement et sont **masqués par défaut** — cochez **Afficher les éléments système / en lecture seule** pour les voir (grisés). **Récupérer dans la liste de démarrage** confie un élément à Clockwork (uniquement les clés Run du registre et les éléments du dossier Démarrage). Un **filtre** en haut recherche par nom / commande ; survolez une commande tronquée pour la lire en entier.

### Groupes d'actions

Regroupe des actions dans un groupe réutilisable. **Ajouter ▾** en démarre un à partir d'un **modèle intégré** (Concentration / Réunion / Clôture / Coucher / S'absenter / Capture d'écran) — ajustez les noms de processus et enregistrez. Un groupe **ne fait que définir des actions** ; déclenchez-le de quatre façons : depuis la barre d'état système (**Exécuter : <groupe>**), un **raccourci global**, comme une **étape de groupe d'actions** dans la liste de démarrage (au démarrage) ou depuis un rappel (**Sur-Oui / groupe silencieux**). Un groupe n'exécute qu'une seule copie à la fois ; une étape **message** peut servir de porte de confirmation (répondre **Non** interrompt le reste).

> **Raccourci global** — dans l'éditeur de groupe, cliquez sur la case du raccourci et appuyez sur un raccourci (p. ex. `Ctrl+Alt+F`) pour exécuter ce groupe depuis n'importe où, sans menu. Échap annule, Suppr efface. Les groupes désactivés libèrent leur combinaison ; les combinaisons réservées par le système (Alt+F4, Ctrl+Shift+Esc…) et les combinaisons déjà prises par un autre groupe ou par le raccourci panique sont refusées avec un avis.

### Paramètres

**Délai de démarrage** (0–600 s, au démarrage uniquement), **démarrer réduit dans la barre d'état système**, **raccourci panique** (cliquez sur la case et appuyez sur votre raccourci ; Échap annule, Suppr efface ; par défaut `Ctrl+Alt+Q`) et **langue de l'interface** (chinois simplifié, anglais, 日本語 et 15 autres — 18 au total ; changer de langue redémarre l'application pour l'appliquer).

## Astuces

- **Double-cliquez sur une ligne pour la modifier**. Pour remplir les chemins / processus / raccourcis / dates, pas besoin de tout taper à la main : **Parcourir…**, **Choisir…** (sélecteur de processus avec recherche), **Capturer** et **Choisir la date**.
- Double-cliquer sur `Clockwork.exe` ouvre seulement les paramètres — cela **n'**exécute **pas** immédiatement la liste de démarrage ; pour cela, utilisez **Réexécuter la liste de démarrage** de la barre d'état système.
- **Lancez-le normalement** (double-clic / barre d'état système / tâche planifiée). Certains lanceurs en bac à sable / à privilèges réduits bloquent les appels de bas niveau, de sorte que envoyer-des-touches / actions de fenêtre / activer-si-déjà-en-cours / envoyer-du-texte-à-un-processus / volume peuvent ne pas fonctionner (vous recevrez un avertissement clair ; le simple « lancer un programme » n'est pas affecté).
- Votre configuration est `clockwork.settings.json` (en local uniquement). Supprimez-la pour revenir à l'exemple. L'état des rappels est `clockwork.state.json` (également local ; suppression sans risque).
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
- **CI / releases** (GitHub Actions) : les builds de push / PR compilent et exécutent tous les tests sur un runner Windows ; pousser un tag `v*` (par ex. `v2.0.0`) compile, estampille la version du fichier à partir du tag, crée une Release GitHub et y attache `Clockwork.exe`.

## À propos du Plan Open Source 365

Il s'agit du projet #20 du [Plan Open Source 365](https://github.com/rockbenben/365opensource) — une personne + l'IA, plus de 300 projets open source en un an. [Soumettre une demande →](https://365.aishort.top/)

## Licence

[MIT](../LICENSE) © rockbenben
