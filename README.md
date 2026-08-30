# BestCrush / DofusSharp

> Outil compagnon pour **Dofus** permettant de suivre les prix du marché, calculer la rentabilité du concassage et analyser les runes réellement obtenues.

**BestCrush** est développé sur un fork de [DofusSharp](https://github.com/DofusSharp/DofusSharp), un ensemble de bibliothèques et d'applications C# autour de Dofus.

Cette version de BestCrush ajoute une gestion locale du marché, des captures en jeu, des overlays Windows et un suivi réel des résultats de concassage.

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

Les releases incluent le runtime .NET et les composants Windows App SDK nécessaires.  

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

### Lots HDV

Pour les ressources et les runes, BestCrush peut enregistrer plusieurs tailles de lots (si disponibles) :

- x1 ;
- x10 ;
- x100 ;
- x1000.

Ces données servent ensuite aux calculs de craft.

---

## Coût réel d'un craft

Pour chaque ingrédient, BestCrush cherche le **montant minimum réellement nécessaire pour acheter suffisamment de ressources**, en combinant les tailles de lots disponibles.

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

### Couleur des données

Les couleurs de l'overlay permettent d'identifier rapidement l'état des informations :

- 🟢 donnée locale récente ;
- 🟠 donnée locale vieillissante ;
- 🔴 donnée ancienne ou donnée nécessaire manquante ;
- 🔵 coefficient initial provenant de DoFocus.

---

# Captures en jeu

## Clic molette — lecture contextuelle

Par défaut, le **clic sur la molette** déclenche une lecture de la zone Dofus située sous le contexte courant.

Selon l'écran détecté, BestCrush peut notamment :

- sélectionner un équipement de l'inventaire comme cible ;
- lire un prix d'équipement en HDV ;
- enregistrer les prix d'une rune ;
- enregistrer les prix d'une ressource ;
- lire un résultat de concassage et son coefficient.

### Serveur obligatoire

Aucune capture de marché n'est autorisée tant qu'un serveur n'a pas été explicitement sélectionné dans BestCrush pour la session en cours.

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

## Mise à jour marché

Affiche les informations liées aux captures de marché :

- objet reconnu ;
- type de donnée ;
- nombre de lots enregistrés ;
- succès ou erreur de lecture.

## Résultat concassage

Affiche les runes réellement reconnues pendant une session de concassage :

- nom de la rune ;
- quantité obtenue ;
- valeur estimée ;
- valeur totale de la session ;
- nombre de cellules reconnues.

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
| Lecture contextuelle | Clic molette |
| Masquer / restaurer les overlays | `F7` |
| Démarrer / arrêter une session de concassage | `F9` |

`F7` masque les overlays actuellement visibles.  
Un second appui restaure uniquement ceux qui étaient visibles avant le masquage.

> La configuration personnalisable des raccourcis est prévue.

---

# Session de concassage F9

`F9` démarre une session dédiée à la lecture des runes réellement obtenues.

## Fonctionnement

Après le concassage :

1. Démarrer la session avec `F9`.
2. Survoler chaque cellule de rune obtenue.
3. Laisser brièvement la souris immobile sur la rune.
4. BestCrush lit l'infobulle et récupère :
   - le nom exact de la rune ;
   - la quantité du lot.
5. La cellule est comptée une seule fois pendant la session.
6. Les quantités identiques sont agrégées.
7. Leur valeur est calculée à partir des prix locaux.

La valeur totale est automatiquement recalculée lorsque les prix locaux des runes changent.

## Important : ne pas scroller

Le scroll pendant une session F9 invalide volontairement la session.

BestCrush affiche alors :

```text
Ne pas scroller
```

Je n'ai pas encore réussi à passer cette limitation.

Pour le moment, il est donc recommandé de concasser suffisamment peu d'objets pour que toutes les lignes de résultat restent visibles simultanément.

---

# DoFocus

BestCrush peut utiliser DoFocus comme **source initiale de coefficient**.

Un coefficient récupéré depuis DoFocus est affiché en **bleu** dans l'overlay tant qu'il n'a pas été remplacé par une donnée locale plus pertinente.

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

La reconnaissance repose sur plusieurs étapes :

```text
Fenêtre Dofus
    ↓
Capture
    ↓
Détection du panneau / de l'infobulle
    ↓
Extraction de régions
    ↓
OCR
    ↓
Reconnaissance DofusDB
    ↓
Enregistrement / calcul local
```

La reconnaissance des équipements utilise en priorité une correspondance exacte normalisée, puis une correspondance approchée lorsque nécessaire.

Les systèmes OCR restent sensibles à certains changements d'interface, de résolution ou de rendu du jeu.

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

# Limitations connues

BestCrush est encore en développement actif.

Les principales limitations actuelles concernent notamment :

- la dépendance de l'OCR à l'apparence de l'interface Dofus ;
- la nécessité de garder les résultats F9 visibles sans scroll ;
- certains comportements Windows liés au focus des fenêtres ;
- les raccourcis actuellement fixes ;
- les éventuels changements futurs de l'interface ou des API externes.

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

## État du projet

BestCrush évolue encore rapidement.

Les prochaines évolutions prévues comprennent notamment :

- configuration des raccourcis clavier et souris ;
- amélioration continue de la reconnaissance OCR ;
- raffinements de l'ergonomie des overlays ;
- amélioration des outils de mise à jour du marché.

Les retours de test sont particulièrement utiles à ce stade.
