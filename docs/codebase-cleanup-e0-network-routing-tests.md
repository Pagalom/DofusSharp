# E0 — Caractérisation du routage métier réseau

## Référence

Base : `507b5522294154bda6b24bdaf41aa293133b5262`, branche
`refactor/codebase-cleanup`.

Le checkpoint Windows de D a été déclaré validé : bibliothèque réseau, Probe et
BestCrush compilent ; 203 tests réussissent. E0 ne découpe pas encore
`DofusNetworkCaptureService`.

## Seam minimale ajoutée

Le code de routage reste dans `DofusNetworkCaptureService.ProcessMessageAsync`.
Aucune copie de la logique n'est créée.

Deux points d'entrée internes, accessibles uniquement à l'assembly de tests via
`InternalsVisibleTo`, permettent de rejouer le vrai worker sans Npcap :

- `LoadProtocolMapForReplay(path)` appelle le vrai `ProtocolMap.Load` puis démarre
  le même `ProcessMessagesAsync` / même `Channel<DofusWireMessage>` single-reader ;
- `ReplayMessageAsync(...)` écrit un vrai `DofusWireMessage` dans ce canal et
  attend un accusé de fin attaché uniquement aux messages de test.

Les messages produits par Npcap conservent `Completion = null` : leur traitement
reste identique. Les exceptions sont toujours journalisées/ignorées par le worker
de production ; pour un message de replay seulement, la même exception est aussi
renvoyée au test afin de caractériser les conversions checked.

Le constructeur de production garde exactement les mêmes dépendances publiques.
En interne :

- les flags marché/coefficient sont lus via `IBestCrushSettingsProvider` ;
- le flag de debug est fourni par un délégué qui lit toujours
  `BestCrushSettingsService.DevTool_KeepDebugArtifacts` en production ;
- la destination du résultat de concassage est un délégué typé qui appelle toujours
  `CrushSessionService.ApplyNetworkCrushAsync` en production.

Aucun mediator, repository, event bus ou processor parallèle.

## Tests E0

`Tests.BestCrush` cible maintenant Windows et référence le projet BestCrush afin
que les tests exécutent l'assembly applicatif réel au lieu de compiler des copies de
`LastNetworkEquipmentState` et `MarketDataChangeNotifier`.

Les tests E0 utilisent SQLite réel temporaire, les vrais `MarketPriceService`,
`CoefficientService`, `MarketDataChangeNotifier`, le vrai catalogue EF et le vrai
worker réseau.

Couverture ajoutée :

- jzn : minimum strictement positif x1/x10/x100/x1000, ordre des notifications,
  jzn vide identifiant encore l'équipement, prix ulong > long ignoré ;
- déduplication : une confirmation jzn identique peut ne pas écrire une nouvelle
  observation mais déclenche toujours la notification ;
- flags Equipment/Rune/ResourceCaptureEnabled ;
- kei/kef/kbd : OfferId, remplacement de requête en attente, fenêtre exactement
  5 s incluse, 5 s + 1 tick refusé, reçu supprimant la corrélation ;
- kef : rafraîchissement ressource et rune ; absence de remplacement du prix
  équipement jzn ;
- kdb/isa/iuq/kbu : UID/ItemId et validation du type catalogue avant écriture dans
  LastNetworkEquipmentState ;
- kci : UID inconnu => ligne entière ignorée, ordre des lignes connues, runes,
  coefficients strictement positifs, dernière observation positive effective,
  résultat transmis même avec capture coefficient désactivée ;
- conversion checked d'une quantité de rune > int.MaxValue ;
- ordre FIFO du vrai Channel single-reader ;
- timestamps de message contrôlés pour LastNetworkEquipmentState, la fenêtre achat
  et la destination du résultat de concassage.

Le total attendu passe de 203 à **224 cas** si toutes les nouvelles théories sont
découvertes comme prévu.

## Comportements existants volontairement conservés

- `MarketPriceService.AddObservationAsync` et `CoefficientService.AddObservationAsync`
  horodatent toujours leurs lignes avec `DateTime.UtcNow`, et non avec le timestamp
  du message réseau. E0 ne change pas ce comportement.
- une date kef antérieure à la requête n'est pas corrigée ici ;
- l'attribution au serveur sélectionné reste inchangée ;
- l'UID cache global, les doublons multi-interface, le restart après Stop et la
  durée de vie des caches restent inchangés ;
- une erreur d'écriture debug peut toujours interrompre le traitement métier ;
- le bug connu de chevauchement TCP reste caractérisé, pas corrigé ;
- le snapshot/historique complet de `CrushSessionService` n'est pas rejoué :
  E0 protège le dispatch jusqu'au résultat typé transmis.

Tout comportement incorrect découvert dans ces zones reste :
**BUG EXISTANT — CORRECTION FONCTIONNELLE À VALIDER**.

## Hors périmètre

Aucune extraction de `DofusNetworkMessageProcessor`,
`NetworkObservationWriter` ou `NetworkDebugWriter`.
Aucune Phase E1, F, G ou H.
Aucune modification SQLite NU1903, History, revalorisation, serveur, TCP ou nouvelle
fonctionnalité. Aucune intégration de `experiment/market-history-probe`.

## Checkpoint Windows E0

Depuis la racine :

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Référence attendue : anciens 203 cas toujours verts + 21 nouveaux cas E0 =
**224 tests réussis, 0 échec**.

Arrêt après E0. Ne pas commencer E1 avant validation Windows.
