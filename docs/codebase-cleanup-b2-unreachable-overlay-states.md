# B2 — Présentations historiques inaccessibles

## Base et périmètre

Base vérifiée : `d9e6db2c92a83359052932697a37ae77d5190f78` (B1), branche
`refactor/codebase-cleanup`. HEAD local et distant identiques, dépôt propre
avant intervention. L'utilisateur a autorisé B2 par « Test ok, on passe à B2 ».
Ce retour valide le checkpoint précédent ; aucun nombre de tests ni nouveau
journal de build Windows n'a été fourni avec ce message.

Ce lot retire seulement les présentations sans consommateur et leurs
auxiliaires exclusifs dans trois fichiers :

| Fichier | Suppressions |
|---|---|
| [OverlayPage.cs](../BestCrush/Overlay/OverlayPage.cs) | 23 méthodes publiques d'affichage et 3 helpers privés |
| [MarketCaptureOverlayPage.cs](../BestCrush/Overlay/MarketCaptureOverlayPage.cs) | 14 méthodes publiques d'affichage, 2 helpers privés et leur import `System.Globalization` |
| [MarketCaptureOverlayService.cs](../BestCrush/Services/MarketCaptureOverlayService.cs) | Les 14 wrappers correspondants et le DTO de présentation `MarketCapturePriceLine` |

Soit 56 méthodes et un record retirés, 1 155 lignes de production supprimées,
sans ajout de code de production. Aucun changement de DI, de constructeur,
de champ, de disposition des contrôles, de texte actif ou de logique métier.
`CrushSessionOverlayPage` reste entièrement inchangée : son `Update` est actif.

## Preuves de non-utilisation

Les recherches portent sur tout le dépôt, y compris les fichiers cachés hors
`.git`, et sont recoupées avec les entrées, constructeurs et liaisons. Les
constats ci-dessous concernent le code de la base B1, avant suppression ; les
documents citant les anciens noms ne constituent pas des appels.

### Anciennes présentations communes

Chacune des méthodes suivantes existait dans les trois fichiers du tableau :

| Méthode | Ancienne présentation |
|---|---|
| `ShowMarketPanelDetected` | Panneau HDV reconnu |
| `ShowMarketEquipmentRead` | Première offre lue |
| `ShowMarketEquipmentRecorded` | Prix d'équipement enregistré |
| `ShowMarketEquipmentRecognitionFailed` | Échec de reconnaissance HDV |
| `ShowAuxiliaryMarketDataRecorded` | Lots de ressource/rune enregistrés |
| `ShowAuxiliaryMarketReadFailed` | Lecture de lots incomplète |
| `ShowPanelNotDetected` | Aucun panneau reconnu |
| `ShowPanelDetected` | Panneau de concassage reconnu |
| `ShowCrushRowNotDetected` | Ligne de concassage absente |
| `ShowLastCrushRowDetected` | Ligne de concassage détectée |
| `ShowCrushFieldsExtracted` | Champs OCR isolés |
| `ShowCrushOcrResult` | Résultat du concassage OCR |
| `ShowServerNotSelected` | Coefficient non enregistré faute de serveur |
| `ShowRecognizedEquipment` | Équipement/coefficient issus de l'ancien parcours |

Dans `OverlayPage`, aucune de ces méthodes n'a d'appelant. Dans
`MarketCaptureOverlayPage`, leur seul appelant est le wrapper homonyme de
`MarketCaptureOverlayService`. Aucun de ces 14 wrappers n'a lui-même d'appelant,
y compris dans l'ancien `CrushSessionService`. Les lambdas qui appellent la
page sont créées à l'intérieur des wrappers ; elles ne constituent donc pas
une entrée indépendante dans ce sous-graphe.

### Homonymes F8 et auxiliaires

`OverlayPage` perd également ses huit méthodes homonymes des diagnostics F8
listées dans le tableau suivant, ainsi que `ShowTooltipEquipmentNotRecognized`.
**Seules les versions d'`OverlayPage` sont inaccessibles.** Les huit versions
dans `MarketCaptureOverlayService` et `MarketCaptureOverlayPage` restent
actives et sont conservées. Le seul rendu public appelé sur `OverlayPage` est
`ShowProfitability`, depuis le recalcul d'`OverlayService`.

Les auxiliaires retirés sont vérifiés séparément :

- `OverlayPage.ShowNormalDetails` : ses sept appels appartiennent tous aux
  présentations retirées. `ShowProfitabilityDetails` reste utilisé et conservé.
- `OverlayPage.SetColoredLine` et `OverlayPage.FormatProfitability` :
  déclaration uniquement, aucun appel ni délégué. Ce sont des helpers de
  présentation, pas les calculs de rentabilité actifs.
- `MarketCaptureOverlayPage.SetCopyableDetails` : cinq appels, tous dans les
  anciens affichages marché/équipement retirés.
- `MarketCaptureOverlayPage.FormatClipboardNumber` : sept appels, tous dans
  ces anciens affichages. Son import devient inutilisé. Le helper homonyme
  actif d'`OverlayPage`, `MakeCopyable` et les deux méthodes de copie vers le
  presse-papiers sont conservés.
- `MarketCapturePriceLine` : sa déclaration, les deux signatures
  `ShowAuxiliaryMarketDataRecorded` du service et de la page, et la boucle de
  cet affichage sont ses seules références. Il n'a plus de producteur après
  B1, n'est ni une entité EF ni un DTO sérialisé. Aucun modèle de capture ou de
  concassage encore consommé n'est supprimé.

### Appels indirects et projets

- **DI :** `MauiProgram` enregistre les services, pas les méthodes retirées.
  Les pages sont construites explicitement par leurs services. Les
  constructeurs, inscriptions, dépendances et initialisations restent identiques.
- **Callbacks / événements :** les gestes de drag, resize, copie et survol,
  les callbacks Windows, les boutons de barre et les notifications de marché
  ne désignent aucun membre retiré. Les abonnements restent inchangés.
- **MAUI / Razor :** pas de binding ou de commande vers ces membres ; les
  usages des types conduisent aux constructeurs, à la visibilité et au focus.
  Les classes concernées ne sont pas `partial`.
- **Réflexion / sérialisation / EF :** aucun accès par nom de méthode,
  découverte réflexive, attribut de préservation ou mapping de ces
  présentations/du record n'a été trouvé. Les interfaces de persistance et
  les modèles JSON du dépôt ne les utilisent pas.
- **Compilation conditionnelle :** aucune directive `#if`/`#endif` dans les
  blocs retirés. Toutes les branches Windows, P/Invoke et callbacks de fenêtre
  sont conservés à l'identique.
- **Probe / liens / tests :** les références de projets et `Compile Include`
  ont été examinés. NetworkProbe n'utilise pas ces pages/services.
  `Tests.BestCrush` référence le domaine et lie des sources réseau/état, sans
  charger l'application MAUI ni ces trois fichiers. Aucun test ne cible les
  membres supprimés ; le build Windows et le checkpoint manuel restent requis.

## Diagnostics F8 conservés

Les huit paires de méthodes du service et de la page M sont identiques à B1,
y compris signatures, textes, couleurs, détails et footers. Leurs appelants
dans `OverlayService` sont également inchangés.

| Méthode conservée dans M | Entrée réellement utilisée | Particularité de référence |
|---|---|---|
| `ShowServerSelectionRequired` | `RequestTooltipRead`, serveur non choisi | Texte historique « lecture clic molette » conservé |
| `ShowReadCancelled` | `RequestTooltipRead`, fenêtre absente ; `ProcessCapturedReadAsync`, zéro candidat | « Dofus non détecté » même lorsqu'aucune infobulle n'est trouvée |
| `ShowCaptureStarted` | `RequestTooltipRead`, fenêtre trouvée | « Clic molette — capture… » reste le texte F8 |
| `ShowCaptureSuccess` | Début de `ProcessCapturedReadAsync` | Ne met à jour que le statut et sa couleur ; peut être transitoire |
| `ShowCaptureFailed` | Exception traitée par la file F8 | « Lecture impossible », message de l'exception conservé |
| `ShowMultipleTooltipsDetected` | Plus d'un candidat | Nombre d'infobulles, lecture ambiguë, retour anticipé |
| `ShowEquipmentRecognitionFailed` | Candidat unique sans reconnaissance | Texte OCR et diagnostic d'équipement non reconnu |
| `ShowTooltipEquipmentFocused` | Après `FocusEquipmentAsync` pour le candidat reconnu | Nom, confiance et diagnostic de focus conservé |

`Update`, `_pendingUpdate` et `EnsureWindow` ne changent pas : le dernier
diagnostic est mémorisé sans ouvrir M automatiquement. L'ouverture volontaire
affiche ce diagnostic. Les textes initiaux, les champs et contrôles, y compris
le label `_readStatus` d'`OverlayPage`, sont conservés pour ne pas modifier la
disposition ou l'état de démarrage dans ce lot.

F8 conserve toute sa chaîne de capture/détection/OCR, y compris
`RecognizeTooltipLotQuantityAsync`, OpenCV, WinRT et Direct3D. F7, F9 libre,
molette sans OCR, protection du focus par sémaphore/génération, prix et
coefficients ne changent pas. `CrushSessionService.ApplyNetworkCrushAsync`,
valorisation, historique, notifications et affichage réseau de C restent
intacts. `CrushCoefficientScanResult` et `RowY` restent à leur emplacement
actuel ; leur déplacement relève de B3.

## Vérifications réellement effectuées

- Contrôle des blocs retirés : chaque fichier final est exactement le fichier
  B1 moins les plages listées, octet pour octet. Les membres conservés, leurs
  séparateurs et les fins de ligne n'ont pas été réécrits.
- Nouvelle recherche des noms : aucune référence de code aux 14 présentations
  communes, à `ShowTooltipEquipmentNotRecognized`, au record et aux helpers
  sans homonyme conservé. Les homonymes actifs ont été contrôlés par classe.
- Diff relu : uniquement des suppressions dans les trois fichiers produit,
  plus ce document. `git diff --check` : sans erreur.
- Aucun changement de test, projet, dépendance, migration, version,
  `DofusSharp.sln`, `DofusSharp.DofusDb.Filter.slnf` ou `.vscode/`.
- `dotnet --info` : **`dotnet: command not found`, code 127**.
  **Build Windows, tests automatiques et scénarios manuels non exécutés ici.**
  Ces contrôles statiques ne constituent pas une validation d'exécution.

Aucun test artificiel des affichages morts n'est ajouté. La suite existante
protège le métier/réseau qu'elle couvre ; elle ne compile pas ces pages MAUI.

## Checkpoint Windows B2

Depuis la racine du dépôt, conserver les modifications locales indépendantes.
Vérifier la branche avant récupération ; ne pas utiliser de reset destructif.

```powershell
git branch --show-current
git status --short
# Continuer uniquement sur refactor/codebase-cleanup.
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse HEAD
dotnet --version

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Le nombre de tests n'est pas modifié. Dernier résultat chiffré communiqué :
**203 réussis, 0 échec** au checkpoint A3 ; B1 a ensuite été déclaré validé.
Renvoyer HEAD, SDK, résultat du build, résultats des tests et avertissements.

Rejouer les cas suivants du [protocole A4](codebase-cleanup-a4-windows-manual-validation.md),
qui contient leurs préconditions, actions et attentes exactes :

| Vérification | Scénarios |
|---|---|
| Tous les diagnostics F8, M ouverte puis masquée, dernier diagnostic à l'ouverture | `A4-F8-01` à `A4-F8-07` ; statut de capture dans `A4-F8-02` |
| État initial, F7 et F9 libre | `A4-F7-01`, `A4-F7-02`, vérification F9 d'`A4-KEY-01` |
| Molette sans OCR et exclusion des overlays | `A4-MID-01`, `A4-MID-03` |
| Focus Rentabilité et protection des recalculs | `A4-FOCUS-01`, `A4-FOCUS-03` |
| Concassage réseau, détails, historique et retour après F7 | `A4-CR-01`, `A4-INT-01` |
| Barre, drag/resize, scroll/survol, copie et restauration après redémarrage | `A4-UI-01` à `A4-UI-05`, particulièrement R et M |

Ouvrir M via **↻** pour observer les diagnostics. Refaire au moins le F8 connu
avec M masquée : le focus ouvre R, M reste masquée, puis **↻** montre le dernier
diagnostic. Comparer les textes du tableau, sans corriger les anciens libellés.
Les statuts intermédiaires peuvent être trop brefs pour être observés.

Les cas de capture impossible, plusieurs infobulles, candidat inconnu/ambigu et
chevauchement de recalcul dépendent des conditions réellement rencontrées.
Noter **Partiel / Non exercé** si la branche visée n'est pas atteinte, avec la
raison, conformément à A4. Ne pas les déclarer réussis sur la seule présence
d'un écran final, ni modifier le produit, le catalogue ou les messages pour les
forcer. En cas d'écart, joindre le relevé E0 d'A4 et les artefacts disponibles.

## Limites et lots suivants

Aucun nouveau bug confirmé pendant B2. Les particularités actives ci-dessus
restent celles d'A4. Le [suivi séparé](codebase-cleanup-follow-ups.md) conserve
**NU1903 / SQLitePCLRaw.lib.e_sqlite3 2.1.10 / GHSA-2m69-gcr7-jv3q**, le défaut
de chevauchement TCP et les anomalies déjà documentées de serveur,
revalorisation et affichage du lot estimé dans History. Aucun correctif inclus.

Les [lacunes A3 de routage métier](codebase-cleanup-a3-network-tests.md) restent
un préalable obligatoire à la Phase E. Les services OCR historiques encore
instanciés et leurs inscriptions DI ne sont pas retirés avant les lots prévus.

**Arrêt après publication de B2. B3 et les lots suivants attendent le retour
du checkpoint Windows B2.**
