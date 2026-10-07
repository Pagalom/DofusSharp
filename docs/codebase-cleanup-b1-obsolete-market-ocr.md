# B1 — Suppression du bloc privé de marché OCR dans OverlayService

## Base et périmètre

Base vérifiée : `c92d1ca476218941b58d7170466e6b57a65962c2`, branche
`refactor/codebase-cleanup`, dépôt propre avant intervention. La branche distante
pointait sur ce même commit. Le 7 octobre 2026, l'utilisateur a autorisé **B1
uniquement**, après validation déclarée des scénarios critiques A4.

Le changement produit se limite à un bloc contigu de
[OverlayService.cs](../BestCrush/Services/OverlayService.cs), compris entre la
fin de `ProcessCapturedReadAsync` et la déclaration de `PostUi`. Il retire sept
méthodes privées et un enum privé, soit 412 lignes. Aucune autre classe n'est
supprimée. Aucun ajout ni déplacement de code de production.

La mise à jour du [protocole A4](codebase-cleanup-a4-windows-manual-validation.md)
enregistre uniquement le retour de validation transmis par l'utilisateur ; elle
ne vaut pas exécution indépendante des scénarios dans cet environnement.

## Preuves examinées avant suppression

Les noms ont été recherchés dans tout le dépôt, y compris les fichiers cachés
hors `.git`, puis les entrées du service et ses liaisons ont été relues. Les
résultats ci-dessous concernent **le code de la base c92d1ca**, avant ajout de ce
document qui cite lui-même les noms supprimés.

| Élément supprimé | Références avant suppression | Conclusion |
|---|---|---|
| `ExtractMarketPrimaryName` | Déclaration uniquement | Aucune entrée dans l'extraction historique du nom HDV |
| `DetectExplicitMarketObjectHint` | Déclaration uniquement | Classification Rune/Ressource jamais appelée |
| `GetAdjustedMaterialConfidence` | Déclaration uniquement | Correction du score inter-familles jamais appelée |
| `MarketNamesEquivalent` | Déclaration et appel dans `GetAdjustedMaterialConfidence` | Dépend uniquement d'une méthode sans appelant |
| `NormalizeMarketName` | Déclaration et cinq appels : un dans `DetectExplicitMarketObjectHint`, deux dans `GetAdjustedMaterialConfidence`, deux dans `MarketNamesEquivalent` | Tous ses consommateurs sont dans le bloc retiré |
| `MarketObjectHint` | Déclaration, type de retour et valeurs utilisés par `DetectExplicitMarketObjectHint` | Enum privé sans autre consommateur |
| `WriteMarketClassificationDebugAsync` | Déclaration uniquement | L'écriture de `hdv-classification.txt` n'était plus atteinte |
| `RecordMarketLotsAsync` | Déclaration uniquement | L'ancien enregistrement OCR des lots n'était plus atteint |

Vérifications complémentaires :

- **Entrées actives et callbacks :** `Initialize`, les hooks clavier/souris,
  les bindings de barre et les gestes de fenêtre ne désignent aucun de ces
  membres. F8 appelle `RequestTooltipRead`, sa file puis
  `ProcessCapturedReadAsync`. Cette dernière se termine par le focus équipement
  et son diagnostic ; elle n'entre pas dans le bloc historique qui la suivait.
- **DI et constructeurs :** `MauiProgram` enregistre `OverlayService` comme
  singleton. Son constructeur reçoit les services actuels, pas une fonction de
  classification historique. Une inscription du service entier ne déclenche
  pas les méthodes privées statiques retirées. Les enregistrements historiques
  des autres services restent volontairement en place jusqu'au lot B5.
- **Événements :** les abonnements `MarketDataChangeNotifier.Changed` et
  `CrushSessionService.CoefficientsUpdated` appellent leurs handlers de recalcul,
  pas ce bloc. Les abonnements, handlers et désabonnements sont conservés ; le
  traitement de l'ancien événement appartient à B4.
- **Razor / MAUI :** les appels dans `Server.razor`, `App.xaml.cs`, `OverlayPage`
  et la barre concernent initialisation, visibilité, focus, recalcul,
  déplacement/redimensionnement, restauration et fermeture. Aucun binding,
  délégué ou nom de commande ne renvoie aux membres retirés.
- **Partial / code Windows :** `OverlayService` est une classe `sealed` non
  `partial`. Le bloc n'est pas conditionné par `#if WINDOWS` et ne contient
  aucune directive de compilation ; sa suppression ne retire pas de branche
  Windows ni de déclaration de hook.
- **Réflexion / sérialisation / EF :** aucun accès réflexif aux membres privés,
  attribut de préservation, contrat sérialisé ou modèle EF associé à ce bloc
  n'a été trouvé. Les usages de métadonnées JSON dans les clients DofusDb
  concernent leurs DTO et ne découvrent pas les méthodes d'`OverlayService`.
  L'enum privé n'est ni stocké ni exposé par un membre conservé.
- **Projets / liens / tests :** inspection des `Compile Include`, références de
  projets et ressources. `BestCrush` compile ce service normalement ; les liens
  vers NetworkProbe portent sur les sources protocolaires. Le Probe ne lie pas
  `OverlayService`. `Tests.BestCrush` ne référence pas l'application MAUI et ne
  lie pas ce fichier ; aucun test ne cible les membres retirés. La suite de
  203 tests ne remplace donc pas le build Windows ni le checkpoint F8.

Ce n'est pas une conclusion fondée sur les noms : les cinq méthodes sans
appelant forment des racines inaccessibles, et les deux helpers restants ainsi
que l'enum n'ont de références que dans ce sous-graphe fermé.

## Parcours et dépendances conservés

Le préfixe et le suffixe du fichier entourant le bloc supprimé sont identiques
octet pour octet à la base. Cela conserve notamment :

- `PostUi`, `RequestTooltipRead`, `EnsureTooltipReadWorker`,
  `ProcessTooltipReadQueueAsync`, `CleanupAbandonedCaptureAsync`,
  `ProcessCapturedReadAsync` et la file F8 ;
- `FocusLastNetworkEquipmentAsync`, `FocusEquipmentAsync`,
  `RefreshFocusedProfitabilityAsync`, le sémaphore et la génération ;
- les hooks F7/F8/molette, le filtrage des clics sur les overlays, les deux
  états distincts du focus et de l'observation réseau ;
- tous les événements, fenêtres, gestes, dimensions, positions et états F7.

F8 garde intégralement `DofusWindowService`, `DofusCaptureService`,
`WindowsGraphicsCaptureHelper`, `DofusItemTooltipDetectionService`,
`DofusOcrService` et `DofusItemRecognitionService`, leurs normalisations,
OpenCV, WinRT et Direct3D. `RecognizeTooltipLotQuantityAsync` reste exécutée
par la détection F8. Les artefacts, nettoyages et diagnostics F8, y compris les
libellés historiques « clic molette », ne sont pas changés.

Les services historiques de marché/scan, leurs DTO, les méthodes `Show…` des
pages, `CrushCoefficientScanResult` et sa propriété `RowY` restent en place.
La DI, `CrushSessionService`, le réseau, l'historique et les calculs économiques
ne sont pas modifiés. Les imports d'`OverlayService` restent utiles au code
conservé et ne sont pas changés.

## Vérifications réellement effectuées

- Contrôle exact : fichier final = préfixe de la base + suffixe de la base,
  après suppression du seul bloc identifié. Les fins de ligne sont préservées.
- Nouvelle recherche des huit symboles : aucune référence restante dans le
  code ; seules leurs mentions documentaires demeurent.
- Inspection du diff : un seul bloc de suppression dans le fichier produit ;
  les autres modifications sont les deux documents de suivi B1/A4.
- `git diff --check` : sans erreur.
- Aucun changement de `DofusSharp.sln`, `DofusSharp.DofusDb.Filter.slnf` ou
  `.vscode/`. Ces modifications indépendantes signalées sur le poste Windows
  sont exclues du commit.
- `dotnet --info` : échec avec `dotnet: command not found` (code 127).
  **Build Windows et tests non exécutés ici.** Aucun résultat de compilation,
  test automatique ou scénario manuel n'est déduit de ces contrôles statiques.

Il n'y a pas de nouveau test automatisé qui reproduirait artificiellement le
bloc mort. Le checkpoint porte sur le vrai build et les entrées utilisateur
conservées, avec les tests économiques/réseau existants.

## Checkpoint Windows B1

Depuis la racine du dépôt, vérifier la branche avant récupération. Conserver
les changements locaux indépendants ; ne pas les réinitialiser pour ce lot.

```powershell
git branch --show-current
git status --short
# Continuer uniquement sur refactor/codebase-cleanup.
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Référence avant B1 : **203 tests réussis, 0 échec**, d'après le checkpoint
Windows A3 transmis par l'utilisateur. Le nombre de tests n'est pas modifié
par B1. Renvoyer le HEAD testé, les résultats du build/des tests, les
avertissements et les écarts manuels éventuels.

Rejouer les scénarios du [protocole A4](codebase-cleanup-a4-windows-manual-validation.md)
suivants sur le commit B1 :

| Vérification | Scénarios A4 |
|---|---|
| Démarrage, masquage et restauration F7 | `A4-F7-01`, `A4-F7-02` |
| F8 sans serveur, équipement connu, aucune infobulle | `A4-F8-01`, `A4-F8-02`, `A4-F8-03` |
| Molette sans OCR, dernier équipement et exclusion des overlays | `A4-MID-01`, `A4-MID-03` |
| Focus Rentabilité et protection des recalculs | `A4-FOCUS-01`, `A4-FOCUS-03` (noter Partiel si aucun chevauchement observé) |
| Concassage réseau et historique | `A4-CR-01` |
| Réouverture du résultat après F7 | `A4-INT-01` |
| F9 toujours libre | Vérification F9 de `A4-KEY-01`, sans lancer de session OCR |

En cas d'écart, joindre les éléments E0 définis par A4 : ordre exact des actions,
serveur, réglages, captures, diagnostic et traces pertinentes. Le footer R après
copie, le diagnostic « Dofus non détecté » sans infobulle et le retour de C après
F7 restent les particularités de référence, pas des régressions à corriger ici.

## Limites et suites réservées

Les [lacunes de routage A3](codebase-cleanup-a3-network-tests.md) restent ouvertes
et doivent être caractérisées avant toute extraction des traitements réseau en
Phase E. B1 n'ajoute aucun accès de test dans le service de capture.

Le [suivi séparé](codebase-cleanup-follow-ups.md) conserve les bugs TCP,
changement de serveur, revalorisation du concassage et lots estimés dans
History, ainsi que **NU1903 / SQLitePCLRaw.lib.e_sqlite3 2.1.10 /
GHSA-2m69-gcr7-jv3q**. Aucun correctif de ces problèmes n'est inclus.

**Arrêt après publication de B1. B2 et les sous-lots suivants attendent la
validation Windows de B1.**
