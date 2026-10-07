# B4 — Suppression du parcours historique F9 / OCR concassage

## Base et périmètre

- Branche : `refactor/codebase-cleanup`.
- Base inspectée : `35a8b1d7256a97fbd60cbd5df6b752c3b7844f67` (B3).
- Checkout propre avant modification. Checkpoint B3 déclaré validé par l'utilisateur : build Windows réussi, 203 tests réussis, aucun échec/ignoré.
- B4 uniquement. Aucun service supprimé, aucune modification de `MauiProgram.cs`, des projets, solutions, migrations ou dépendances.

## Vérification des consommateurs avant modification

Recherche dans les sources C#, Razor/XAML, projets, tests et NetworkProbe ; examen des callbacks, événements et de l'enregistrement DI. Aucun accès par réflexion, binding, sérialisation ou classe partial vers les entrées historiques n'a été trouvé. L'usage de réflexion relevé dans `CurrentVersion.cs` concerne la version, pas ce service. Le patch historique `cleanup-captures.patch` est un artefact, pas une entrée compilée.

| Élément | Consommateurs au HEAD B3 | Décision |
|---|---|---|
| `CrushSessionService` | DI singleton ; `DofusNetworkCaptureService` ; `OverlayService` ; `CrushSessionOverlayPage` | Service actif conservé |
| `Toggle()` | Aucun appelant ; ne pas confondre avec `OverlayService.Toggle` et `MarketCaptureOverlayService.Toggle` | Supprimé |
| `StartNew()` | Seulement `Toggle()` | Supprimé |
| `Stop()` | Seulement `Toggle()` ; les méthodes homonymes réseau/probe sont distinctes | Supprimé |
| `InvalidateForScroll()` | Aucun appelant | Supprimé |
| `CloseAndReset()` | Deux branches de `OverlayService.Shutdown()` | Conservé |
| `StartMouseMonitoring()` | Seulement `StartNew()` | Supprimé |
| `StopMouseMonitoring()` | Démarrage/arrêt historique ; `CloseAndReset` ; callback `window.Destroying` | Supprimé avec les appels devenus inertes dans les deux chemins actifs |
| `CoefficientsUpdated` | Seule émission : `ProcessCaptureAsync` après scan OCR ; abonnement/désabonnement de `OverlayService` | Événement, émission et branche abonnée supprimés ensemble |
| `ApplyNetworkCrushAsync()` | `DofusNetworkCaptureService.PersistCrushAsync` | Conservé, texte identique |
| Géométrie de fenêtre | Gestes dans `CrushSessionOverlayPage` | Conservée |
| `Show` / `Hide` / `IsVisible` | Contrôle des overlays, F7 et parcours réseau | Conservés |

## Sous-graphe supprimé

- `Toggle` → `StartNew` / `Stop`.
- `StartNew` → `EnsureProcessingWorker` → `ProcessQueueAsync` → `ProcessCaptureAsync`.
- `StartNew` → `StartMouseMonitoring` → `MonitorMouseAsync` → `CaptureAndQueueAtCursorAsync` → channel `CapturedCursorWorkItem` → worker.
- `ProcessCaptureAsync` → détection panneau, scanner coefficients, détection d'infobulle, reconnaissance rune, détection cellule, `IsAlreadyScannedLocked`, accumulation OCR.
- `Stop` → `RequestHistorySave` ; producteur/worker → `CompleteActiveCapture` / `CompletePendingWork`.
- `StartNew` était le seul point abonnant **ce service** à `OnMarketDataChanged` → `RefreshRuneValuesFromMarketAsync`. Cette branche inaccessible est retirée ; aucun abonnement de remplacement n'est créé.
- `InvalidateForScroll` : entrée sans appelant.

Méthodes supprimées dans `CrushSessionService` (17) :

`Toggle`, `StartNew`, `Stop`, `InvalidateForScroll`, `EnsureProcessingWorker`,
`StartMouseMonitoring`, `CaptureAndQueueAtCursorAsync`, `ProcessQueueAsync`,
`ProcessCaptureAsync`, `OnMarketDataChanged`, `RefreshRuneValuesFromMarketAsync`,
`RequestHistorySave`, `CompleteActiveCapture`, `CompletePendingWork`,
`IsAlreadyScannedLocked`, `StopMouseMonitoring`, `MonitorMouseAsync`.

Autres suppressions :

- Propriété publique `IsRunning` sans consommateur (le champ et la propriété homonyme du snapshot restent).
- Événement `CoefficientsUpdated`, son invocation OCR, les trois opérations d'abonnement/désabonnement et `OnCrushSessionCoefficientsUpdated` dans `OverlayService`.
- Champs exclusifs : `_mouseMonitorCancellation`, `_mouseMonitorTask`, `_marketRefreshLock`, `_coefficientsScanned`, `_processingQueue`, `_processingCancellation`, `_processingTask`.
- Constantes exclusives : `MousePollMilliseconds`, `MouseStableMilliseconds`, `MouseMovementThreshold`, `RowFingerprintMaximumDistance`.
- Type de travail `CapturedCursorWorkItem`, structure Win32 `WinPoint` et import natif `GetCursorPos`.
- Imports `System.Numerics` / `System.Threading.Channels` devenus inutiles.
- Paramètres du constructeur devenus inutiles : `DofusWindowService`, `DofusCaptureService`, `DofusPanelDetectionService`, `DofusCrushRuneCellDetectionService`, `DofusCrushCoefficientScanService`, `MarketDataChangeNotifier`. Aucun enregistrement DI global modifié ; aucun constructeur explicite trouvé.

## États partagés volontairement conservés

| État | Lecteur/usage actif conservé |
|---|---|
| `_isRunning`, `_idleCaptureCount`, `_lastCursorX`, `_lastCursorY` | Arguments de `CreateSnapshot`, réinitialisations réseau et fermeture |
| `_scannedRuneCells` et `ScannedRuneCellIdentity` | `CreateSnapshot` lit le nombre ; réseau et fermeture vident la collection |
| `_activeCaptureCount`, `_pendingProcessingCount` | Gardes de `TryPrepareHistorySaveLocked`, remises à zéro réseau/fermeture |
| `_errorMessage` | Snapshot et garde de préparation historique |
| `_sessionId` | Réseau, fermeture, protection du retour asynchrone de sauvegarde |
| `_runes`, `_sessionEquipments` | Résultat réseau, valorisation et write model historique |
| Horodatage, serveur, remise, flags de sauvegarde | Réseau et historique |

Même si certains états ne peuvent plus devenir non nuls/non zéro après suppression du producteur OCR, B4 conserve leurs lecteurs actifs et leur contrat de snapshot. Aucun remplacement par des constantes, changement de record ou simplification de garde historique n'est inclus.

`CloseAndReset` et le callback `window.Destroying` restent actifs ; seul leur appel désormais inutile à `StopMouseMonitoring` est retiré. `ResetSessionState` ne perd que l'écriture de `_coefficientsScanned`, sans lecteur restant.

## Parcours actif préservé et preuves

`PersistCrushAsync` → `ApplyNetworkCrushAsync` → remplacement du résultat → valorisation → préparation du write model → `PublishSnapshot` → `ScheduleHistorySave` → affichage via `Show`.

Comparaison exacte à B3 réussie pour les 21 méthodes suivantes :

`ApplyNetworkCrushAsync`, `Show`, `Hide`, `ContainsScreenPoint`, `BeginDrag`,
`Drag`, `EndDrag`, `BeginResize`, `Resize`, `EndResize`, `RestoreDefaultLayout`,
`LoadStoredLayout`, `ApplyLayout`, `SaveCurrentLayout`, `CreateSnapshot`,
`PublishSnapshot`, `BuildRuneLotBreakdown`, `TryPrepareHistorySaveLocked`,
`ScheduleHistorySave`, `PersistHistoryAsync`, `MakeTransparent`.

SHA-256 du texte de `ApplyNetworkCrushAsync`, identique avant/après :
`1958e89b76186c3a9982f0eaf864aa9d184605773a329fed97a0cb0cf22d2699`.

Les records publics, `CrushCoefficientScanResult` et `RowY` sont conservés. Le dictionnaire par équipement garde le dernier coefficient ; chaque résultat réseau accepté remplace le précédent. L'appel final à `Show` est inchangé, notamment après F7. Les cas de retour anticipé existants restent identiques.

Dans `OverlayService`, comparaison exacte après retrait des seuls trois abonnements/désabonnements à `CoefficientsUpdated` et de son handler. Le reste est inchangé, y compris **`MarketDataChangeNotifier.Changed`**, F7, F8, clic molette, files d'infobulles, focus et sémaphore/génération.

Le hook clavier compare uniquement `VkF7` et `VkF8`, puis transmet via `CallNextHookEx`. Aucun traitement de F9 (`0x78`) identifié dans les sources de hooks/callbacks BestCrush. Aucun hook ajouté. Les mentions F9 restantes sont des commentaires ou documents historiques.

Les deux fichiers C# ne contiennent que des suppressions de lignes : les lignes restantes forment des sous-séquences ordonnées exactes de B3. Le fichier réseau, l'overlay de concassage (copie incluse), les états de focus, les services OCR et la DI globale sont inchangés.

## Services laissés pour B5

| Service conservé | Situation après B4 |
|---|---|
| `DofusPanelDetectionService` | Plus de consommateur métier trouvé, enregistrement DI conservé |
| `DofusCrushRuneCellDetectionService` | Plus de consommateur métier trouvé, enregistrement DI conservé |
| `DofusCrushCoefficientScanService` | Plus de consommateur métier trouvé, enregistrement DI conservé |
| `DofusRuneRecognitionService` | Plus de consommateur métier trouvé, enregistrement DI conservé |
| `DofusCrushRowDetectionService` | Encore référencé par les services historiques cellule/coefficient ; DI conservée |
| `DofusImageRegionService` | Encore référencé par le scanner coefficient et `DofusMarketLotReaderService` ; DI conservée |

Il ne faut pas déclarer les deux derniers sans références directes : leurs consommateurs historiques existent toujours. B5 devra refaire l'analyse avant toute suppression. `DofusCaptureService`, `DofusWindowService` et l'OCR/détection d'infobulle restent nécessaires à F8.

## Vérifications et limites

- `git diff --check` : réussi.
- Contrôles statiques et comparaisons exactes ci-dessus : réussis.
- `dotnet` absent (`command not found`) : build/tests non exécutés ici.
- Aucun scénario Windows exécuté ici ; la conservation textuelle ne remplace pas le checkpoint.
- Tests existants inchangés : référence attendue 203 réussis, aucun échec/ignoré. Ils ne couvrent pas l'intégralité du routage MAUI/réseau ; les lacunes A3 restent ouvertes avant Phase E.
- Aucun bug corrigé : revalorisation après changement de prix, changement de serveur, défaut TCP, SQLite NU1903 (`SQLitePCLRaw.lib.e_sqlite3` 2.1.10 / `GHSA-2m69-gcr7-jv3q`), lots estimés History et autres dettes restent séparés. Retirer la branche de rafraîchissement uniquement abonnée par `StartNew` ne répare pas la revalorisation du parcours réseau.

## Checkpoint Windows B4

Depuis la racine, sur `refactor/codebase-cleanup` :

```powershell
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Référence attendue : 203 tests réussis, 0 échec, 0 ignoré.

Rejouer avec DOFUS et Npcap opérationnels pour les scénarios réseau ; noter serveur, équipement, heure, captures d'écran et journal en cas d'écart. Les préconditions/détails A4 restent applicables.

1. Afficher des overlays, F7 pour masquer puis F7 pour restaurer : mêmes fenêtres restaurées selon A4.
2. Serveur choisi, F8 sur une infobulle d'équipement connu : identification et focus Rentabilité habituels.
3. Session sans serveur choisi, F8 : diagnostic existant, aucun focus/capture utile validé sans serveur ; comparer au protocole A4.
4. Serveur choisi et dernier équipement réseau fiable connu, clic molette : reprise de cet équipement sans capture OCR.
5. Contrôler le focus Rentabilité et ses mises à jour marché ; aucun recalcul via l'ancien événement OCR n'est attendu.
6. Déclencher un concassage réseau : C s'ouvre avec les runes/valeurs habituelles. Refaire un concassage : résultat remplacé, pas cumulé ; contrôler le dernier coefficient pour un même équipement dans l'historique.
7. Masquer par F7 puis concasser : C se rouvre automatiquement.
8. Vérifier la présence du concassage dans l'historique, les coefficients/ordre et la valorisation selon les données disponibles ; attendre la sauvegarde asynchrone.
9. Déplacer/redimensionner C et copier son contenu : comportement inchangé.
10. Appuyer sur F9 : aucune action BestCrush, ni capture ni ouverture d'overlay. L'éventuelle action propre à DOFUS ne relève pas de BestCrush.
11. Fermer l'application : `Shutdown` appelle toujours `CloseAndReset`, aucune erreur de fermeture attendue.

Arrêt après B4. B5 nécessite la validation Windows de l'utilisateur.
