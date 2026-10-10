# E1 — Restructuration de la capture réseau

Base : `bc4ffbd4490190711488ee09e8f4a660b560854b`.
Checkpoint E0 validé sous Windows : **224 tests réussis, 0 échec**.

## Découpage

- `DofusNetworkCaptureService` conserve Npcap/SharpPcap, TCP 5555,
  réassemblage, framing, Ankama Any, Channel single-reader, worker et lifecycle.
- `DofusNetworkMessageProcessor` reçoit les messages dans le même ordre et
  contient le dispatch sémantique, le cache UID->item et la corrélation
  kei/kef/kbd.
- `NetworkObservationWriter` contient les scopes EF, la résolution de type,
  les écritures prix/coefficient, notifications, LastNetworkEquipmentState et
  la construction/transmission des résultats de concassage.
- `NetworkDebugWriter` contient wire.jsonl, events.log et les métadonnées
  de session.
- `DofusWireMessage` devient un record interne partagé par ces composants.

Aucune nouvelle registration DI : les collaborateurs sont composés en interne
par `DofusNetworkCaptureService`. Les lifetimes restent donc inchangés avant F.

Le constructeur public et la seam de replay E0 sont conservés.

## Invariants inchangés

Aucun changement volontaire de protocole, ordre du Channel, fenêtre achat 5 s,
minimums jzn, déduplication, notifications, cache UID, gestion kci, coefficients,
affichage du crush, serveur sélectionné, timestamps, erreurs debug, Stop/restart
ou bug TCP déjà caractérisé.

## Checkpoint Windows

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
git status --short

dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Attendu : **224 tests réussis, 0 échec**.

Arrêt après E1. Phase F non commencée.
