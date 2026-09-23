# BestCrush / DofusSharp

> Outil compagnon pour **Dofus** permettant de suivre les prix du marché, calculer la rentabilité du concassage et analyser les runes réellement obtenues.

**BestCrush** est développé dans un fork de [DofusSharp](https://github.com/DofusSharp/DofusSharp), un ensemble de bibliothèques et d'applications C# autour de Dofus.

Cette version de BestCrush ajoute une gestion locale du marché, une capture réseau passive des données Dofus, des overlays Windows et un suivi réel des résultats de concassage. L'OCR est désormais réservé à la lecture explicite d'une infobulle d'équipement avec `F8`.

---

## Télécharger BestCrush

La dernière version prête à l'emploi est disponible dans les **Releases GitHub** :

**[Télécharger la dernière release](https://github.com/Pagalom/DofusSharp/releases/latest)**

Pour une release Windows :

1. Télécharger l'archive `BestCrush-*-win-x64.zip`.
2. Extraire **entièrement** l'archive dans un dossier.
3. Lancer `BestCrush.exe`.

> Ne lancez pas directement `BestCrush.exe` depuis l'archive ZIP.

### Prérequis

- Windows 10 ou Windows 11 64 bits.
- Microsoft Edge WebView2 Runtime.
- **Npcap**, installé séparément par l'utilisateur depuis le site officiel.
- Dofus lancé en mode fenêtré ou dans une configuration permettant à BestCrush de détecter sa fenêtre.

BestCrush **n'intègre ni ne redistribue Npcap**. Au lancement, l'application vérifie sa présence ; s'il manque, un bandeau permet d'ouvrir la page officielle de téléchargement puis de revérifier l'installation.

Les releases self-contained incluent le runtime .NET et les composants Windows App SDK nécessaires.  
WebView2 est généralement déjà installé sur les versions récentes de Windows.

---

# Fonctionnalités

## Marché local

BestCrush maintient une base de prix **locale et propre à chaque serveur**.

Les données gérées comprennent notamment :

- prix des runes ;
- prix des ressources ;
- prix des équipements ;
- coefficients de brisage.

Les prix peuvent provenir :

- d'une saisie manuelle ;
- d'une capture automatique en jeu.

Une valeur peut également être volontairement vidée : une cellule vide signifie **donnée non renseignée**, et non `0 kama`.

### Lots HDV

Pour les ressources et les runes, BestCrush peut enregistrer plusieurs tailles de lots :

- x1 ;
- x10 ;
- x100 ;
- x1000 lorsque disponible.

Ces données servent ensuite aux calculs de craft et de valorisation.

---

## Coût réel d'un craft

BestCrush ne calcule pas seulement un prix unitaire théorique.

Pour chaque ingrédient, il cherche le **montant minimum réellement nécessaire pour acheter suffisamment de ressources**, en combinant les tailles de lots disponibles.

Exemple :

```text
Besoin : 42 unités

x1  =   911 K
x10 = 7 934 K
x100 = 89 992 K
```

La meilleure combinaison est :

```text
4 × x10 = 31 736 K
2 × x1  =  1 822 K

Total = 33 558 K
```

Le surplus éventuel d'un lot est payé en totalité : le calcul représente donc les **kamas réellement à dépenser à l'HDV**.

Un équipement peut lui-même être utilisé comme ingrédient d'une recette. Dans ce cas, BestCrush le conserve comme **équipement** et utilise son **prix local d'achat x1** dans le coût du craft parent. Son propre coût de craft n'est pas substitué automatiquement, car BestCrush ne peut pas savoir si le joueur possède le métier, le niveau ou les ressources nécessaires pour le fabriquer.

---

## Rentabilité du concassage

Pour un équipement sélectionné, BestCrush peut afficher :

- le coefficient de brisage ;
- la valeur estimée des runes ;
- le prix d'achat de l'équipement ;
- le coût réel du craft ;
- le bénéfice estimé ;
- le rendement estimé.

Les scénarios peuvent tenir compte des caractéristiques de l'équipement et des runes correspondantes.

Une donnée nécessaire manquante rend la catégorie concernée **rouge**, afin de ne pas présenter un résultat partiel comme totalement fiable.

### Couleur des données

Les couleurs de l'overlay permettent d'identifier rapidement l'état des informations :

- 🟢 donnée locale récente ;
- 🟠 donnée locale vieillissante ;
- 🔴 donnée ancienne ou donnée nécessaire manquante ;
- 🔵 coefficient initial provenant de DoFocus.

---

## Décote et seuils de rentabilité

Une décote configurable, fixée à **5 % par défaut**, peut être appliquée à la valeur théorique des runes afin d'obtenir une estimation plus prudente.

BestCrush calcule également :

- le prix maximum acceptable pour l'achat d'un équipement ;
- le coût maximal acceptable de son craft ;
- le coefficient minimum nécessaire ;
- le bénéfice et le rendement selon le ROI cible configuré.

Les valeurs d'une session de concassage sont figées avec la décote utilisée au moment de la session.

---

## Recherche et filtres avancés

La page principale permet notamment de filtrer les équipements selon :

- leur nom, leur niveau et leur type ;
- les runes qu'ils peuvent produire ;
- la présence de toutes les runes sélectionnées ou d'au moins une ;
- un ROI minimum ;
- un bénéfice minimum ;
- la rentabilité à l'achat, au craft ou selon le meilleur scénario ;
- la complétude et la fraîcheur des données ;
- la disponibilité d'un coefficient.

Les configurations de recherche peuvent être enregistrées sous forme de presets.

---

## Historique et analyses de marché

BestCrush conserve l'historique temporel des prix des runes, ressources et équipements pour chaque serveur.

La page **Historique** comprend les vues :

- Ressources ;
- Items ;
- Runes ;
- Concassages ;
- Analyses.

L'onglet **Analyses** permet de créer plusieurs séries simultanément, y compris plusieurs variantes du même élément.

Chaque série peut définir :

- le type d'objet et l'élément ;
- la taille de lot ;
- le prix unitaire ou le prix du lot ;
- la médiane, la moyenne, le premier quartile ou le troisième quartile ;
- les données capturées en jeu, manuelles ou toutes sources ;
- une valeur brute ou une transformation en Base 100.

Lorsque plusieurs tailles de lots sont regroupées, les prix sont toujours convertis en prix unitaire avant l'agrégation.

La **Base 100 est utilisée par défaut** afin de comparer facilement les évolutions relatives d'éléments ayant des prix très différents.

Les analyses proposent également :

- des périodes de 24 heures, 7 jours, 30 jours ou tout l'historique ;
- des presets réutilisables sur le serveur actuellement sélectionné ;
- des cartes interactives avec les véritables icônes Dofus ;
- une couleur stable pour chaque série ;
- la mise en évidence d'une carte et de sa courbe ;
- des tooltips affichant la date, l'indice et la valeur réelle ;
- la copie des noms et des valeurs pertinentes.

---
# Capture réseau passive

BestCrush fonctionne comme une application externe et ne s'injecte pas dans le processus Dofus.

Les données de marché et de concassage sont maintenant récupérées **passivement depuis le trafic réseau Dofus** grâce à Npcap/SharpPcap. Cette capture sert de source principale pour les prix locaux, les coefficients et les résultats de concassage.

L'OCR n'est plus utilisé pour scanner l'HDV ou les résultats de concassage. Il reste réservé à une action explicite : **`F8` lit l'infobulle de l'équipement actuellement survolé afin de le mettre en focus**.

## Focus d'un équipement

- **Clic molette** : met en focus le dernier équipement identifié de façon fiable sur le réseau, sans capture d'écran ni OCR.
- **F8** : lit l'infobulle de l'équipement actuellement survolé et le met en focus.

Un focus ouvre automatiquement l'overlay **Rentabilité**.

### Serveur obligatoire

Aucune donnée réseau n'est persistée tant qu'un serveur n'a pas été explicitement sélectionné dans BestCrush pour la session en cours.

---

# Overlays

BestCrush utilise plusieurs fenêtres indépendantes.

## Rentabilité

Affiche l'équipement actuellement en focus ainsi que :

- coefficient ;
- valeur des runes ;
- prix d'achat ;
- coût du craft ;
- bénéfices ;
- données manquantes.

Les valeurs affichées sont interactives :

- clic sur le nom d'un équipement, d'une rune ou d'un ingrédient : copie le nom ;
- clic sur un prix ou une valeur : copie la valeur numérique sans espace ni `K` ;
- clic sur le coefficient : copie sa valeur sans `%` ;
- survol du coefficient : affiche la date de l'observation au format `JJ/MM/AAAA` ;
- clic sur cette date : copie la date.

## Mise à jour marché

Affiche les informations liées aux captures de marché :

- objet reconnu ;
- type de donnée ;
- lots enregistrés ;
- prix détectés ;
- prix effectivement utilisés ;
- indication lorsqu'une valeur manuelle reste prioritaire ;
- succès ou erreur de lecture.

Pour les runes et ressources, les lots `x1`, `x10`, `x100` et `x1000` reconnus sont détaillés directement dans l'overlay. Les noms et prix affichés sont copiables individuellement.

## Résultat concassage

Affiche les runes réellement reconnues pendant une session de concassage :

- nom de la rune ;
- quantité obtenue ;
- détail des lots utilisés pour sa valorisation ;
- valeur estimée par rune ;
- valeur totale de la session ;
- nombre de cellules reconnues.

Interactions de copie :

- clic sur le nom d'une rune : copie le nom ;
- clic sur sa quantité : copie la quantité ;
- clic sur sa valeur : copie la valeur numérique ;
- clic sur un terme du détail des lots : copie une formule Excel comme `=2*99000` ;
- double-clic sur le détail des lots : copie la formule complète comme `=2*99000+4*9900+5*990+2*99` ;
- clic sur la valeur réelle totale : copie le total.

Les prix peuvent être abrégés visuellement (`k`, `M`, `Md`) pour garder l'overlay lisible, mais les valeurs copiées restent exactes.

## Barre de contrôle

Une petite barre always-on-top permet d'afficher ou masquer individuellement :

- Rentabilité ;
- Mise à jour marché ;
- Résultat concassage ;
- fenêtre principale / paramètres.

---

# Raccourcis actuels

| Action | Raccourci par défaut |
|---|---|
| Focus sur le dernier équipement vu sur le réseau | Clic molette |
| Lecture OCR de l'infobulle d'équipement | `F8` |
| Masquer / restaurer les overlays | `F7` |
| Non attribué | `F9` |

`F7` masque les overlays actuellement visibles.  
Un second appui restaure uniquement ceux qui étaient visibles avant le masquage.

> La configuration personnalisable des raccourcis est prévue dans une évolution ultérieure.

---

# Résultat de concassage passif

Lorsqu'un concassage est détecté sur le réseau, BestCrush récupère directement :

- l'équipement concerné lorsque sa correspondance est connue ;
- le coefficient de brisage ;
- les identifiants et quantités exactes des runes obtenues.

L'overlay **Résultat concassage** s'ouvre automatiquement et valorise les runes avec les prix locaux du serveur sélectionné.

Il n'est plus nécessaire de démarrer une session avec `F9`, de survoler les cellules ni d'utiliser l'OCR pour lire le résultat.

---

# DoFocus

BestCrush peut utiliser DoFocus comme **source initiale de coefficient**.

Un coefficient récupéré depuis DoFocus est affiché en **bleu** dans l'overlay tant qu'il n'a pas été remplacé par une donnée locale plus pertinente. Vider volontairement un coefficient local permet de revenir au coefficient DoFocus disponible.

Les prix du marché local ne dépendent pas de DoFocus.

L'utilisation de données communautaires ou leur éventuel partage doit rester contrôlable par l'utilisateur.

---

# Priorité des données

BestCrush distingue les données :

- manuelles ;
- capturées automatiquement en jeu.

Le comportement dépend de la priorité configurée.

### Priorité manuelle

Une capture en jeu ne doit pas remplacer silencieusement une valeur manuelle prioritaire.

### Priorité capture jeu

Une nouvelle lecture en jeu peut devenir la valeur active.

Cette logique s'applique notamment aux prix et coefficients gérés localement.

---

# Rafraîchissement automatique

Lorsqu'un prix est modifié ou capturé, BestCrush diffuse l'information aux vues concernées.

Cela permet notamment de mettre automatiquement à jour :

- les pages de prix locaux ;
- l'overlay de rentabilité ;
- la valorisation des résultats de concassage déjà affichés.

Les pages de prix disposent également d'un bouton **Rafraîchir** pour forcer manuellement une relecture de la base locale.

---

# Stockage local

Sous Windows, BestCrush stocke ses données dans :

```text
%LOCALAPPDATA%\BestCrush
```

On y trouve notamment :

```text
BestCrush\
├── Data\
│   └── bestcrush.db
├── Logs\
│   └── bestcrush*.log
└── Cache\
    └── images\
```

La base SQLite contient les données locales nécessaires au fonctionnement de BestCrush.

---

# Reconnaissance OCR

L'OCR est désormais limité à la **lecture explicite d'une infobulle d'équipement avec `F8`**.

```text
Infobulle d'équipement
    ↓
F8
    ↓
Capture de la fenêtre Dofus
    ↓
Extraction du titre
    ↓
OCR
    ↓
Reconnaissance DofusDB
    ↓
Mise en focus
```

Les prix HDV, les coefficients et les résultats de concassage ne dépendent plus de cette reconnaissance visuelle.

La reconnaissance des équipements utilise en priorité une correspondance exacte normalisée, puis une correspondance approchée lorsque nécessaire.

---

# Compilation depuis les sources

## Environnement

Le projet utilise notamment :

- C# ;
- .NET 10 ;
- .NET MAUI ;
- Blazor Hybrid ;
- Entity Framework Core ;
- SQLite ;
- OpenCvSharp ;
- Windows App SDK.

Le projet BestCrush cible actuellement Windows pour les fonctionnalités d'overlay et de hooks clavier/souris.

## Compiler sous Windows

Depuis la racine du dépôt :

```powershell
dotnet build .\BestCrush\BestCrush.csproj `
  -f net10.0-windows10.0.19041.0
```

Si une ancienne instance de BestCrush verrouille l'exécutable :

```powershell
Get-Process BestCrush -ErrorAction SilentlyContinue |
    Stop-Process -Force
```

Puis relancer la compilation.

---

# Générer une version Windows distribuable

Pour créer une publication Windows x64 autonome :

```powershell
dotnet publish .\BestCrush\BestCrush.csproj `
  -f net10.0-windows10.0.19041.0 `
  -c Release `
  -p:RuntimeIdentifierOverride=win-x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true `
  --self-contained true `
  -o .\publish\BestCrush
```

L'exécutable se trouve ensuite dans :

```text
publish\BestCrush\BestCrush.exe
```

Il faut distribuer **l'ensemble du contenu du dossier `publish\BestCrush`**, et pas uniquement l'EXE.

## Créer l'archive pour GitHub Releases

Exemple :

```powershell
Compress-Archive `
  -Path .\publish\BestCrush\* `
  -DestinationPath .\BestCrush-v0.2.0-win-x64.zip `
  -Force
```

L'archive obtenue peut être ajoutée directement aux assets d'une GitHub Release.

Elle n'a pas besoin d'être commitée dans le dépôt.

---

# Structure du dépôt

```text
DofusSharp/
├── BestCrush/
│   └── Interface MAUI, overlays et capture en jeu
│
├── BestCrush.Domain/
│   └── Modèles, base locale et logique métier
│
├── BestCrush.Migrations/
│   └── Migrations de la base BestCrush
│
├── DofusSharp.Dofocus.ApiClients/
│   └── Client DoFocus
│
├── DofusSharp.DofusDb.ApiClients/
│   └── Client DofusDB
│
├── Tests.BestCrush/
│   └── Tests BestCrush
│
└── dofusdb/
    └── Outils DofusDB issus du projet DofusSharp
```

---

# Principes du projet

BestCrush privilégie plusieurs principes :

**Données locales avant tout**  
Les prix utilisés pour les calculs sont ceux du marché réellement observé par l'utilisateur.

**Pas d'écrasement silencieux**  
Les saisies manuelles peuvent rester prioritaires sur les captures automatiques.

**Mode dégradé utilisable**  
Une donnée automatiquement récupérable doit pouvoir être saisie manuellement si la lecture automatique échoue.

**Focus unique**  
Une capture de rune ou de ressource complète les données sans modifier l'équipement analysé.

**Résultats prudents**  
Une donnée nécessaire manquante est signalée clairement au lieu d'être remplacée par une valeur inventée.

---

# Limitations connues

BestCrush est encore en développement actif.

Les principales limitations actuelles concernent notamment :

- la nécessité d'installer Npcap séparément sous Windows ;
- la dépendance du décodage réseau au protocole de la version courante de Dofus, qui peut nécessiter une adaptation après une mise à jour du jeu ;
- la dépendance de la lecture `F8` à l'apparence de l'infobulle Dofus ;
- certains comportements Windows liés au focus des fenêtres ;
- les raccourcis actuellement fixes.

Si une lecture paraît incohérente, vérifiez les données locales avant d'utiliser le résultat pour une décision en jeu.

---

# Contribuer

Les contributions, rapports de bugs et propositions d'amélioration sont les bienvenus.

Pour signaler un problème, il est particulièrement utile d'indiquer :

- la version de BestCrush ;
- la résolution utilisée ;
- le serveur sélectionné ;
- l'action effectuée ;
- le résultat attendu ;
- le résultat obtenu ;
- une capture de l'interface concernée lorsque possible ;
- les logs BestCrush si nécessaire.

---

# Origine du projet et crédits

Ce dépôt est un fork de :

**DofusSharp / DofusSharp**  
https://github.com/DofusSharp/DofusSharp

BestCrush réutilise et étend notamment les bibliothèques DofusSharp permettant d'interagir avec les données DofusDB et DoFocus.

Merci aux contributeurs du projet DofusSharp et aux services communautaires utilisés par BestCrush.

---

# Licence

Le code source du dépôt est publié sous **licence MIT**.

Certains contenus peuvent être soumis à la **Licence Ouverte 2.0** ou à des droits de tiers conformément au fichier :

```text
LICENSE.md
```

Consultez ce fichier pour les conditions complètes.

---

# Avertissement

BestCrush est un projet communautaire non officiel.

Il n'est ni développé, ni sponsorisé, ni approuvé par Ankama.

**Dofus** et les éléments associés au jeu appartiennent à leurs ayants droit respectifs.

L'utilisateur reste responsable de l'utilisation qu'il fait du logiciel et du respect des conditions d'utilisation des services concernés.

---

## Feuille de route

BestCrush évolue par étapes afin de conserver un ordre de développement clair.

| Étape | État | Objectif |
|---|---|---|
| **0 — Base v0.1.6** | ✅ Terminé | Prix et valeurs copiables, overlays harmonisés, valorisation détaillée des runes et prise en charge des équipements comme ingrédients. |
| **1 — Historique global** | ✅ Terminé | Historique par serveur des ressources, items, runes, coefficients et sessions de concassage. |
| **2 — Rentabilité dynamique** | ✅ Terminé | Décote configurable, prix maximum acceptable, coefficient minimum nécessaire et calculs selon le ROI cible. |
| **3 — Recherche avancée** | ✅ Terminé | Filtres économiques, filtres multi-runes, contrôle de la fraîcheur des données, tris et presets. |
| **4 — Analytics marché V1 — v0.1.7** | ✅ Terminé | Séries multiples, Base 100, agrégations, périodes, presets, cartes interactives et tooltips. |
| **5 — Capture réseau passive — v0.2.0** | ✅ Terminé | Prix HDV et concassage issus du réseau, focus réseau, OCR réservé à F8, overlays actualisés et prérequis Npcap contrôlé. |
| **6 — Analytics marché V2** | ⬜ À faire | Groupes d'items, ratios, comparaisons avancées, corrélations et décalages temporels. |
| **7 — Personnalisation** | ⬜ À faire | Raccourcis configurables et raffinements supplémentaires de l'ergonomie. |

### Nouveautés clôturées dans la v0.2.0

- capture réseau passive du trafic Dofus sur le port de jeu ;
- prix HDV locaux alimentés depuis les paquets réseau ;
- mise à jour immédiate des prix après achat pour les ressources et runes ;
- résultats de concassage récupérés directement depuis le réseau ;
- ouverture automatique de l'overlay de concassage à la réception d'un résultat ;
- clic molette pour reprendre le dernier équipement identifié sur le réseau ;
- `F8` réservé à la lecture OCR de l'infobulle d'équipement ;
- `F9` libéré ;
- overlays et bulles d'information scrollables ;
- affichage du niveau de l'équipement dans l'overlay Rentabilité ;
- affichage du prix d'achat dans les résultats incomplets ;
- détection de Npcap au lancement avec accès au téléchargement officiel sans redistribution de Npcap.

### Nouveautés clôturées dans la v0.1.7

- décote configurable de la valeur théorique du concassage ;
- seuils dynamiques de rentabilité pour l'achat et le craft ;
- coefficient minimum nécessaire selon le ROI cible ;
- recherche avancée et presets de filtres ;
- conservation temporelle des observations de marché ;
- regroupement des observations proches et limitation des doublons ;
- analyses simultanées de runes, ressources et équipements ;
- transformation en Base 100 utilisée par défaut ;
- cartes interactives avec icônes Dofus et couleurs de séries ;
- mise en évidence de la carte et de la courbe sélectionnées ;
- tooltips affichant l'indice et la valeur réelle ;
- presets d'analyses réutilisables entre les serveurs.

Les retours de test sont particulièrement utiles à ce stade.