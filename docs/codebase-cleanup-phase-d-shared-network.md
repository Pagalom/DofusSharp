# Phase D — bibliothèque de protocole partagée

Base : `4b32d05`, branche `refactor/codebase-cleanup`.

## Déplacements

| Source dans BestCrush.NetworkProbe | Destination | Contenu |
| --- | --- | --- |
| Capture/TcpReassembler.cs | BestCrush.Network/Capture/TcpReassembler.cs | TcpReassembler et VarintFrameBuffer |
| Protocol/ProtoWire.cs | BestCrush.Network/Protocol/ProtoWire.cs | ProtoWire, ProtoWireType, ProtoField |
| Protocol/AnkamaFrameDecoder.cs | BestCrush.Network/Protocol/AnkamaFrameDecoder.cs | AnkamaFrameDecoder, AnkamaAny |
| Protocol/SemanticDecoders.cs (hors renderer) | BestCrush.Network/Protocol/SemanticDecoders.cs | ProtocolMap, tous les records d'observation et SemanticDecoders |
| Protocol/SemanticDecoders.cs (renderer) | BestCrush.NetworkProbe/Protocol/ConsoleRenderer.cs | ConsoleRenderer, conservé internal et exclusivement Probe |
| protocol-map.json | BestCrush.Network/protocol-map.json | Unique source canonique, contenu identique octet par octet |

Namespaces : `BestCrush.Network.Capture` et `BestCrush.Network.Protocol`.
Les types partagés de premier niveau deviennent publics : ils sont utilisés par les
consommateurs externes ou apparaissent dans leurs signatures. Les helpers privés
restent privés ; aucune ouverture des détails privés ni InternalsVisibleTo ajouté.
Le renderer conserve son namespace Probe et son corps intégral, affichages compris.

## Références et mapping

`BestCrush.Network.csproj` cible `net10.0`, sans référence de package ni de projet.
Il n'utilise ni MAUI, ni EF, ni SharpPcap, ni PacketDotNet.

BestCrush, BestCrush.NetworkProbe et Tests.BestCrush le référencent directement.
Les huit liens Compile protocolaires (quatre dans BestCrush, quatre dans les tests)
sont supprimés. Les deux liens vers LastNetworkEquipmentState et
MarketDataChangeNotifier restent en place. La solution et son filtre BestCrush
incluent le nouveau projet.

Chaque consommateur copie explicitement le même fichier canonique avec
`PreserveNewest`, vers son emplacement antérieur :

- BestCrush : `protocol-map.json`, toujours uniquement dans le groupe Windows ;
- Probe : `protocol-map.json` ;
- tests : `NetworkFixtures/protocol-map.json`.

La bibliothèque n'impose pas de copie transitive supplémentaire du JSON.
Les appels à ProtocolMap.Load et son implémentation restent identiques : fallback,
clés, casse, fichier absent, JSON invalide et priorité workshop_slot_put/clé
historique sont préservés. Le README du Probe indique la nouvelle source canonique.

## Vérifications et limites

Comparaison statique automatisée avec la base :

- quatre fichiers protocolaires identiques après substitution exacte du namespace
  et de la visibilité des types de premier niveau, et extraction du renderer ;
- corps de ConsoleRenderer identique ; JSON identique octet par octet ;
- neuf fichiers de tests réseau modifiés uniquement dans leurs imports ; aucun test
  ajouté, supprimé ou réécrit ; référence attendue : 111 tests réseau, 203 au total ;
- DofusNetworkCaptureService, DofusCaptureProbe et Program identiques hors imports ;
- XML des projets valide, références présentes, aucun lien Compile vers les sources
  du Probe, un seul protocol-map.json dans l'arbre ;
- aucune logique console ni dépendance de capture dans la bibliothèque ;
- `git diff --check` réussi.

Aucun algorithme modifié, y compris le bug de chevauchement TCP caractérisé par A3.
Capture passive, worker/Channel, caches, SQLite, notifications, routage métier,
réglages, overlays, historique et CrushSessionService restent dans leurs projets.
Aucune modification de DI, aucun changement économique, aucune intégration de
`experiment/market-history-probe` ou modification de `main`.

`dotnet` est absent de l'environnement de travail : compilation et exécution des
203 tests à confirmer au checkpoint Windows, sans prétendre les avoir exécutées.

```powershell
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Arrêt après D. La couverture du routage privé reste réservée au début de E.
