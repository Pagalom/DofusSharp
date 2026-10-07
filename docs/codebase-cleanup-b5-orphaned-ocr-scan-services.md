# B5 — Services historiques OCR/scan orphelins

## Base et portée

Branche `refactor/codebase-cleanup`, base `836e1bff5793bc2c1e99c4737e233863898238a8`.
Checkout propre avant modification. L'utilisateur déclare B4 validé sous Windows :
build OK, 203 tests réussis, aucun échec, contrôles manuels conformes.

Un seul lot B5 : neuf services, leurs types exclusifs et neuf inscriptions DI.
Les parcours marché et concassage partagent `DofusImageRegionService` ;
les autres candidats sont de petites branches terminales sans consommateur actif.
Pas de second chantier indépendant important nécessitant B5a/B5b. B6 non commencé.

## Graphe constaté avant suppression

Chaque service a aussi son inscription dans `MauiProgram.cs`, retirée avec lui.
« Aucun » signifie aucun consommateur métier dans le dépôt courant.

| Service | Consommateurs hors déclaration et DI | Décision |
|---|---|---|
| `DofusPanelDetectionService` | Aucun | Supprimer |
| `DofusCrushRuneCellDetectionService` | Aucun | Supprimer |
| `DofusCrushCoefficientScanService` | Aucun | Supprimer ; le modèle B3 reste |
| `DofusRuneRecognitionService` | Aucun | Supprimer |
| `DofusMarketPanelDetectionService` | Aucun | Supprimer |
| `DofusMarketLotReaderService` | Aucun | Supprimer avant le service région |
| `DofusResourceRecognitionService` | Aucun | Supprimer |
| `DofusCrushRowDetectionService` | Scanner coefficients et détecteur de cellules ci-dessus | Supprimer après ces deux consommateurs |
| `DofusImageRegionService` | Scanner coefficients et lecteur de lots marché ci-dessus | Supprimer après ces deux consommateurs |

Ordre appliqué : les sept services sans consommateur métier, puis ligne et région.
Les dépendances restantes appelées par les services retirés (`DofusOcrService`,
`DofusItemRecognitionService`, `CoefficientService`, `ItemsService`, `RunesService`,
`IServiceScopeFactory`) restent utilisées ailleurs et sont conservées.

## Preuves de non-utilisation

Pour chaque service et chaque type déclaré dans ses fichiers : recherche des
références dans tous les fichiers suivis par Git, pas seulement les appels de
méthodes. En dehors des neuf fichiers, seules la DI et des mentions documentaires
référencent les services ; aucun DTO supprimé n'a de consommateur extérieur.

Contrôles complémentaires :

- Constructeurs, instanciations explicites et résolutions `GetService` /
  `GetRequiredService` : aucun accès extérieur aux candidats hors DI.
- Callbacks et événements : aucune capture/résolution des candidats ; les callbacks
  de démarrage/fermeture de `App` utilisent `OverlayService` et le service réseau.
- Razor/XAML et bindings : aucune référence aux candidats ou à leurs DTO.
- Réflexion/activation dynamique : aucun chargement des candidats, aucun balayage
  d'assembly ou accès dynamique à ces types trouvé. La réflexion de `CurrentVersion`
  concerne la version.
- Classes partial : aucun candidat n'est partial ; aucune partie complémentaire.
- Code Windows : recherche sur les sources entières, y compris les branches
  conditionnelles ; pas de consommateur caché par `#if WINDOWS`.
- Tests et NetworkProbe : aucune référence aux candidats/DTO.
- Projets/props/targets : aucun fichier candidat lié explicitement ; les liens
  existants concernent les décodeurs, le réassemblage, l'état réseau et le notifier.
- Aucun mécanisme d'enregistrement automatique par réflexion trouvé : les neuf
  inscriptions explicites sont les seuls points DI de ces services.

Après suppression, contrôle de tous les 22 types retirés dans les fichiers suivis
restants (sources, configuration, projets et tests) : aucune référence résiduelle.
Les mentions des rapports historiques et des patches archivés ne sont pas du code
compilé et ne sont pas réécrites pour effacer l'historique.

## Fichiers supprimés et types associés

Tous les chemins ci-dessous sont dans `BestCrush/Services/`.

| Fichier supprimé | Types exclusifs supprimés en plus du service |
|---|---|
| `DofusPanelDetectionService.cs` | `DofusPanelDetectionResult`, enum `DofusPanelType`, record privé `RowGeometry` |
| `DofusCrushRuneCellDetectionService.cs` | `DofusCrushRuneCellDetectionResult` |
| `DofusCrushCoefficientScanService.cs` | Aucun : `CrushCoefficientScanResult` est conservé dans `Models/` |
| `DofusRuneRecognitionService.cs` | `RuneRecognitionResult` |
| `DofusCrushRowDetectionService.cs` | `CrushRowDetectionResult`, record privé `RowSegment` |
| `DofusImageRegionService.cs` | `RelativeImageRegion` |
| `DofusMarketPanelDetectionService.cs` | `DofusMarketPanelDetectionResult` |
| `DofusMarketLotReaderService.cs` | `DofusMarketLot`, record privé `MaterialLotReadRow` |
| `DofusResourceRecognitionService.cs` | `ResourceRecognitionResult`, record privé `ResourceCandidate` |

Bilan : neuf services, huit DTO publics à usage exclusivement historique,
un enum et quatre records privés.

Fichiers modifié/créé : `BestCrush/MauiProgram.cs` et ce rapport.

## Impact DI et éléments conservés

`MauiProgram.cs` perd exactement neuf lignes : sept `AddSingleton` (les services
supprimés sauf reconnaissance rune/ressource) et deux `AddScoped` (reconnaissance
rune/ressource). Toutes les autres lignes sont identiques à B4 ; aucun lifetime
restant ni configuration DbContext n'est modifié.

Après B5, aucun constructeur ou résolution restante ne demande un service supprimé
et aucun enregistrement ne peut le créer au démarrage. Cela ne signifie pas que les
anciens singletons étaient auparavant instanciés : une inscription seule ne prouve
pas une instanciation. L'OCR actif d'infobulle reste disponible pour F8.

Sont volontairement conservés, avec leur contenu inchangé :

- `DofusWindowService`, `DofusCaptureService`, `WindowsGraphicsCaptureHelper` ;
- `DofusItemTooltipDetectionService`, `DofusItemRecognitionService`,
  **l'intégralité de `DofusOcrService`**, dont `RecognizeTooltipLotQuantityAsync` ;
- OpenCV, WinRT, Direct3D, packages Vortice et toutes les références de projets ;
- les modèles d'infobulle, les artefacts debug F8 et leurs mécanismes de création ;
- `CrushCoefficientScanResult`, `NetworkCrushResultLine`, modèles réseau/historique ;
- tous les overlays, leurs gestes, copie, dimensions et positions ;
- `ApplyNetworkCrushAsync`, les snapshots, la sauvegarde asynchrone et le réseau passif.

Le parcours F8 reste : capture de la fenêtre → détection infobulle / OCR titre →
reconnaissance équipement → `FocusEquipmentAsync` → Rentabilité.
Le clic molette reprend toujours le dernier équipement réseau du serveur courant.

Les fichiers protégés (capture, détection, reconnaissance, OCR, helper natif,
`OverlayService`, `CrushSessionService`, `DofusNetworkCaptureService`, états du focus
et dernier équipement, modèle B3) ont été comparés octet pour octet à B4.
Le contrôle du périmètre Git garantit aussi que les autres fichiers existants
(projets, tests, ressources, fichiers de solution, etc.) sont inchangés.
Les ressources d'image historiques ne sont pas supprimées dans ce lot limité aux
services/DTO/DI ; aucune dépendance native n'est retirée par analogie de nom.

F7/F8/molette, génération/sémaphore de focus, les trois overlays et les callbacks
clavier sont inchangés. F9 reste libre : aucune branche ni nouveau raccourci ajouté.

## Vérifications et dettes conservées

- `git diff --check` : réussi.
- Recherche des consommateurs restants des 22 types supprimés : aucune référence
  dans le code/configuration/projets/tests restants.
- Vérification exacte des neuf suppressions DI et des fichiers protégés : réussie.
- `dotnet --version` : échec immédiat, `dotnet: command not found`.
- Build, tests et scénarios Windows : non exécutés ici.
- Aucun test supprimé/modifié ; référence attendue : 203 réussis, 0 échec.

SQLite NU1903 (`SQLitePCLRaw.lib.e_sqlite3` 2.1.10, `GHSA-2m69-gcr7-jv3q`),
défaut TCP, changement de serveur, revalorisation du concassage, lots estimés
History, lifetimes/DbContext, warnings nullable/OpenCV restent hors périmètre.
Les lacunes de routage A3 restent à caractériser avant extraction en Phase E.
Aucun correctif fonctionnel inclus ; aucune réduction de `DofusOcrService` (B6).

## Checkpoint Windows B5

Depuis la racine du dépôt :

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Référence attendue : 203 tests réussis, 0 échec. Les tests ne couvrent pas tous
les chemins MAUI/réseau : rejouer les scénarios A4/B4 correspondants :

1. Démarrage de l'application, sans erreur de résolution DI.
2. F7 masque/restaure les fenêtres selon leur état précédent.
3. Serveur choisi, F8 sur équipement connu : identification et focus Rentabilité.
4. F8 sans infobulle : diagnostic habituel ; aucun équipement inventé.
5. Session sans serveur choisi, F8 : comportement de refus actuel inchangé.
6. Clic molette avec dernier équipement réseau fiable : reprise sans OCR.
7. Focus Rentabilité et actualisation via le notifier marché.
8. Concassage réseau : ouverture C, runes/valeurs et historique habituels.
9. F7 puis concassage : C se rouvre.
10. F9 : aucune action BestCrush.
11. Fermeture de l'application : absence d'erreur de résolution/nettoyage.

En cas d'écart, relever étape, serveur, équipement, captures d'écran et journal.
Arrêt après B5 : attendre la validation Windows avant B6.
