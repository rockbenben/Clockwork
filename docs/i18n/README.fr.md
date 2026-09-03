<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Mettez en pilote automatique les tâches répétitives de votre PC**

Lancez vos applications automatiquement à l'ouverture de session · rappels programmés · une seule pression pour exécuter toute une routine

**[⬇ Télécharger pour Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, sans installation

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · **Français** · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![La liste de démarrage de Clockwork — une suite ordonnée d'étapes d'ouverture de session, chacune avec son type, son délai et ses conditions](../../assets/screenshot.png)

## Ce qu'il fait

- 🚀 **Liste de démarrage** — ouvre vos applications de tous les jours dans l'ordre à l'ouverture de session, avec délai, condition de jour et style de fenêtre par étape ; ferme, met au premier plan ou coupe le son en chemin. Les étapes peuvent aussi dépendre de l'état de la machine : seulement si une application tourne (ou pas), seulement sur secteur ou seulement sur batterie, seulement si un fichier ou un dossier existe.
- ⏰ **Tâches planifiées** — un rappel à l'heure, lu à voix haute si vous voulez, ou un groupe d'actions exécuté en silence. Cliquer sur **Oui** peut lancer un programme, ouvrir un fichier ou une URL, ou déclencher un groupe. Ou un événement peut déclencher à la place de l'horloge — au déverrouillage, au verrouillage, à la sortie de veille, après N minutes d'inactivité, au branchement ou débranchement du secteur, ou en cas de batterie faible. Besoin d'un rappel unique, tout de suite ? La zone de notification propose un **rappel rapide** — de 5 à 60 minutes, il sonne une fois puis se supprime. D'autres déclencheurs surveillent le matériel — changement d'écran, réseau qui revient ou qui tombe, clé USB branchée — et un dernier vous surveille, vous : il se déclenche après N minutes de travail sans pause pour vous rappeler de vous lever.
- 🎛️ **Groupes d'actions** — regroupez une routine (Concentration / Réunion / Clôture / Coucher…) et déclenchez-la depuis la barre d'état système, un **raccourci global**, la liste de démarrage ou une tâche planifiée. Modèles inclus. Les pas peuvent se transmettre des valeurs : **saisie de l’utilisateur, choix de l’utilisateur** et **récupérer le texte sélectionné** enregistrent chacun leur résultat dans une variable, que les pas suivants reprennent sous la forme `{nom}` dans une URL ou un texte — « demande-moi quoi chercher, puis cherche-le » tient en deux pas.
- 🧹 **Éléments de démarrage du système** — tout ce qui démarre automatiquement sur votre PC, dans une seule liste : désactivez ce dont vous n'avez pas besoin (désactivé, pas supprimé) ou récupérez-le dans votre propre liste.
- 🔌 **Ports** — chaque port TCP en écoute, avec le processus qui l'occupe et le projet d'où il vient : double-cliquez sur une ligne pour ouvrir `localhost:3000` dans le navigateur, clic droit pour libérer le port, ce qui termine tous les processus qui l'occupent et leurs processus enfants. Par défaut, seuls apparaissent les services lancés depuis vos propres répertoires de projet : le serveur de dev oublié n'est plus enseveli sous les applis de chat et les services système.
- ⚡ **Panneau rapide** — un raccourci (par défaut `Ctrl+Alt+Space`) ouvre une grille de tuiles là où se trouve déjà votre souris : vos propres actions, plus relancer la liste, arrêter, ne pas déranger et ouvrir la fenêtre. Cliquez une tuile, ou atteignez-la aux flèches et appuyez sur Entrée ; **Échap** ou un clic ailleurs la referme. Il figure aussi dans le menu de la zone de notification : effacer le raccourci désactive le raccourci, pas la fonction. Plutôt souris ? Activez **maintenir le bouton central** et il s'ouvre sans toucher au clavier ; le clic central normal continue de fonctionner. Les pages du panneau, c’est vous qui les créez et les rangez dans le gestionnaire de panneaux, et chaque tuile d’une page est une seule opération : verrouiller l’écran, couper le son, lancer une app, ouvrir une URL et « exécuter un groupe entier » se côtoient ainsi. Quand les pages se multiplient, classez-les : **la rangée du haut choisit une catégorie, la colonne de gauche liste les pages de cette catégorie**, et les deux se réordonnent par glisser-déposer ; sans catégorie, tout se retrouve dans *Non classé* et la rangée des catégories n’apparaît même pas. Vous ne l'utilisez pas du tout ? **Paramètres → Utiliser le panneau rapide** désactive la fonction elle-même, raccourci et bouton central compris.
- 🖱️ **Gestes de souris** — maintenez le **bouton droit**, dessinez un tracé, et l’action correspondante s’exécute. Huit directions, n’importe quel type de pas (y compris exécuter un groupe entier). Le tracé s’affiche à l’écran pendant que vous le dessinez et disparaît au relâchement. **Dix sont livrés prêts à l’emploi** (copier / coller / précédent / suivant / rechercher le texte sélectionné (↑↓) / aller tout en bas, plus les quatre diagonales pour réduire, agrandir, épingler au-dessus et fermer) — tels quels, ou à réécrire. Le prix, annoncé d’emblée : un seul geste actif suffit à accaparer le bouton droit, donc **le glisser-déposer au bouton droit demande une courte pause** — appuyez, restez immobile environ 0,2 s, puis faites glisser (glisser un fichier au bouton droit dans l’Explorateur, pivoter la vue dans une app 3D). Pour que le bouton droit ne soit pas touché du tout, la ligne de titre du gestionnaire de gestes porte un interrupteur général. Rien ne vous oblige à utiliser les gestes fournis : coupez cet interrupteur, laissez WGestures / StrokesPlus / Quicker faire le tracé et gardez les actions ici via `Clockwork.exe --run-group "Concentration"`. Ce commutateur figure aussi dans l'onglet **Paramètres** : **Utiliser les gestes de souris**.

> **Arrêtez à tout moment** — le bouton d'arrêt à droite de la barre d'onglets (visible uniquement pendant une exécution), zone de notification → **Arrêter les actions en cours**, ou le raccourci d'arrêt d'urgence global (par défaut `Ctrl+Alt+Q`). Les longues attentes sont coupées court, pas subies.

## Prérequis

| Aspect | Détail |
| --- | --- |
| **Système** | Windows 10 / 11, x64 |
| **Installation** | Aucune. Un seul `Clockwork.exe` portable — déposez-le dans n'importe quel dossier |
| **Droits admin** | Uniquement pour « Démarrer à l'ouverture de session » et pour les étapes que vous marquez **exécuter en administrateur** |
| **Vos réglages** | `clockwork.settings.json` à côté de l'exe (ou `%APPDATA%\Clockwork\` si ce dossier était en lecture seule au premier lancement ; ensuite elle reste où elle est) — rien ne quitte la machine |
| **Interface** | 18 langues et un thème clair / sombre. La langue suit Windows au premier lancement ; le thème démarre en sombre et peut suivre Windows |

**Limites.** Sans installateur, pas de mise à jour automatique — téléchargez le nouveau zip et remplacez l'exe. Les lanceurs en bac à sable bloquent l'envoi de touches, les actions de souris, les actions de fenêtre, activer-si-déjà-lancé et le volume (vous recevez un avertissement clair ; le simple « lancer un programme » fonctionne toujours). Le remappage de touches et l'expansion de texte restent hors périmètre — c'est le travail d'AutoHotkey.

## Prise en main

1. Téléchargez la dernière version depuis [Releases](https://github.com/rockbenben/Clockwork/releases) — deux builds, trois téléchargements — et déposez l'unique `Clockwork.exe` obtenu dans n'importe quel dossier.
   - **`Clockwork-<version>-win-x64.zip`** — runtime .NET inclus, tourne tel quel sur n'importe quel Windows 10/11. À prendre en cas de doute, ou si le PC est hors ligne ou verrouillé.
   - **`Clockwork-<version>-win-x64-needs-dotnet10.zip`** — exige le [runtime de bureau .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) installé. Installez-le une fois sur un PC connecté, ensuite chaque mise à jour ne pèse presque rien.
   - **`Clockwork.exe`** — le même build que le zip ci-dessus, sans le zip : cliquez, lancez, ou écrasez votre copie existante pour mettre à jour. Si le runtime manque, Windows en propose le téléchargement.
2. Double-cliquez dessus pour ouvrir la fenêtre des paramètres. Les exemples chargés sont tous **décochés** — rien ne s'exécute tant que vous ne les cochez pas.
3. Pour le lancer à chaque démarrage : dans l'onglet **Paramètres**, cochez **Démarrer à l'ouverture de session** (enregistre une tâche planifiée avec droits d'administrateur, donc pas de déluge d'invites UAC au démarrage).

Il reste ensuite dans la barre d'état système : double-cliquez sur l'icône pour ouvrir la fenêtre, et le bouton de fermeture ne fait que la masquer à nouveau. Pour quitter vraiment, utilisez **Quitter** dans le clic droit de la barre.

> [!IMPORTANT]
> **L'exe n'est pas signé**, donc SmartScreen affiche « Windows a protégé votre ordinateur » au premier lancement — cliquez sur **Informations complémentaires → Exécuter quand même**. Un antivirus peut aussi réagir : écrire des clés Run du registre et des tâches planifiées, c'est exactement le travail d'un gestionnaire de démarrage — et aussi ce que fait un logiciel malveillant ; de l'extérieur, rien ne les distingue. Si vous préférez ne pas l'accepter sur parole, [compilez-le vous-même](../../CONTRIBUTING.md) — même résultat, votre propre binaire. Chaque release inclut aussi un `SHA256SUMS.txt` et une attestation de build GitHub : `gh attestation verify <fichier> -R rockbenben/Clockwork` prouve que le téléchargement a été compilé par la CI de ce dépôt, pas sur l'ordinateur de quelqu'un.

**Guide complet** — chaque champ, chaque cas limite : [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Astuces

- **Double-cliquez sur une ligne pour la modifier**. Les chemins, processus et dates ne se saisissent pas à la main : **le bouton … en bout de ligne** ouvre le sélecteur correspondant (fichier, liste de processus avec recherche, date), et les raccourcis s'enregistrent en les pressant via **Capturer**.
- **Faites glisser une ligne pour la réordonner** — dans les trois listes et dans la liste des étapes de l'éditeur de groupe ; les boutons haut/bas fonctionnent toujours.
- **Essayez avant d'enregistrer** — **▶ Exécuter cette étape** et **▶ Exécuter le groupe** dans l'éditeur de groupe exécutent ce qui est actuellement à l'écran, et le bouton devient **■ Arrêter** pendant l'exécution.
- **Dupliquer** clone la tâche ou le groupe sélectionné juste en dessous — plus rapide que de refaire une ligne presque identique. **La suppression demande toujours confirmation**, partout.
- Double-cliquer sur `Clockwork.exe` ouvre seulement la fenêtre ; cela **n'**exécute **pas** à nouveau la liste de démarrage. Pour cela, utilisez **Réexécuter la liste de démarrage** de la barre d'état système.

## À propos du 365 Open Source Plan

Projet **#020** du [365 Open Source Plan](https://github.com/rockbenben/365opensource) — une personne + l'IA, plus de 300 projets open source en un an.

[Proposez votre idée →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
