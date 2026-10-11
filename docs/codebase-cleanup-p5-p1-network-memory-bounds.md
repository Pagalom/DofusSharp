# P5.1 — Bornage de la mémoire des flux TCP et du framing

**Branche :** `refactor/codebase-cleanup`.  
**Base :** `a2a5954` (audit P5.0) ; dernier checkpoint Windows **380/380 tests** au commit `8f1e7ca`.  
**Statut :** code et tests préparés ; **Windows build, tests et F5 non encore validés**.

## Risque traité

Dans `BestCrush.Network/Capture/TcpReassembler.cs`, chaque flux pouvait retenir sans plafond :

1. les segments TCP hors ordre (`_pending`) lorsqu'un trou persistait ;
2. les octets reçus en attente d'une trame complète (`VarintFrameBuffer._buffer`).

Le cache global des flux était déjà limité à **256** entrées (`NetworkFlowCache`), mais chaque entrée pouvait grossir indéfiniment.

## Garde-fous ciblés

### 1. File TCP hors ordre

- Plafond **8 Mio d'octets en attente** par assembleur ;
- plafond **4 096 segments distincts en attente** par assembleur ;
- compter les octets de chaque segment stocké, puis les décrémenter à son retrait, y compris si le segment a été entièrement couvert ou partiellement retransmis ;
- les retransmissions avec **même séquence** conservent la règle « première version retenue » et n'augmentent pas les compteurs ;
- les segments **en ordre** sont drainés immédiatement et ne sont pas considérés comme une file hors ordre pérenne.

Un dépassement est refusé **avant** l'ajout du segment et lève `TcpReassemblyLimitExceededException`, exception dédiée. Il ne crée pas un segment partiellement accepté.

### 2. Framing et trames valides proches de la limite

- La longueur maximale de trame acceptée par `TryReadFrame` **reste 16 Mio** (contrat préexistant).
- `VarintFrameBuffer.Append` refuse un dépassement de sa nouvelle capacité **16 Mio + 64 Kio + 10 octets**, avant copie des données.
- `DofusNetworkCaptureService.OnPacketArrival` alimente désormais la trame en **blocs d'au plus 64 Kio** et continue à extraire toutes les trames complètes entre les blocs, dans l'ordre initial.
- Les **64 Kio** de marge accueillent un dernier fragment contenant la fin d'une trame de 16 Mio et des octets de la suivante. Les **10 octets** supplémentaires couvrent la longueur maximale d'un préfixe Varint.
- Les paquets normaux, les segments fusionnés et les frames successives suivent toujours le même décodage Ankama Any.

### 3. Politique de saturation explicite

Un flux qui atteint l'une de ces limites n'est **pas** maintenu en état de décodage partiel. La capture :

1. intercepte uniquement `TcpReassemblyLimitExceededException` ;
2. retire **l'assembleur du flux concerné seulement**, si ce même assembleur est encore l'entrée du cache (`NetworkFlowCache.RemoveIfSame`) ;
3. journalise un **Warning** avec identification du flux et raison du dépassement ;
4. laisse intacts les autres flux, les messages déjà décodés, les prix et les snapshots SQLite.

Le prochain paquet de ce flux obtient un **nouvel assembleur** (réinitialisation du séquençage et du framing). **Une capture incomplète à proximité du seuil peut être perdue** : ce compromis de sécurité mémoire est explicite ; aucune déduplication ni reconstitution de la connexion après abandon ne sont garanties. Ce comportement ne doit se produire qu'en condition de saturation anormale, pas sur les captures habituelles.

### 4. Avertissements CA2014

`ProtocolWireNormalizer.TryRewrite` alloue maintenant ses deux petits `Span<byte>` de 4 et 8 octets **avant** la boucle des champs Protobuf, plutôt qu'un `stackalloc` répété par champ. Écriture Little Endian et correspondance des champs inchangées. Un test injecte plus de 16 000 paires de champs fixed32/fixed64 remappés.

## Couverture tests synthétiques sans Npcap

- `TcpReassemblerTest` : limites d'octets/segments hors ordre, doublons sans croissance, drainage complet et compteurs à zéro, segment en ordre dépassant la limite de file temporaire, arguments invalides (**6 cas de test supplémentaires**).
- `VarintFrameBufferTest` : rejet avant écriture et conservation de l'état, préfixe/payload incomplet, nouvelle instance après un dépassement, arguments invalides et **trame valide de 16 Mio suivie d'une autre** à travers des blocs de 64 Kio (**6 cas**).
- `NetworkFlowCacheTest` : éviction conditionnelle sans retirer une entrée recréée (**1 test**).
- `ProtocolWireNormalizerTest` : remapping massif fixed32/fixed64, sans allocation de pile dans la boucle (**1 test**).

**Total attendu : 394 tests .NET (380 + 14)**, zéro échec. Ce décompte doit être confirmé sous Windows.

## Risques explicitement non corrigés

- La file `Channel<DofusWireMessage>` demeure **non bornée** : bornage via `TryWrite` risquerait de perdre des prix et résultats de concassage. Le sujet P5-N1 (audit P5.0) exige une politique de saturation métier dédiée et une mesure du débit ; ce correctif P5.1 ne le résout **pas**.
- L'ensemble des flux peut encore consommer beaucoup de mémoire en situation adversariale : 256 flux × leurs plafonds ; le plafond **par flux** supprime seulement la croissance sans borne dans un flux individuel.
- Le wrap-around des numéros TCP uint32, les captures sur plusieurs adaptateurs et la déduplication inter-flux restent hors périmètre.
- Pas de politique de rétention des fichiers `wire.jsonl` : debug désactivé par défaut, ne jamais committer de captures brutes.
- Le point P4.1 (revalorisation dynamique pas démontrée en F5) demeure ouvert, reporté à la demande de l'utilisateur.

## Checkpoint Windows obligatoire

Préserver les fichiers locaux modifiés/non suivis `DofusSharp.sln`, `.vscode/`, `BestCrush/BestCrush.csproj.backup`, `q`. Utiliser le fast-forward explicite, pas `git pull` :

```powershell
git status --short --branch
git fetch origin refactor/codebase-cleanup
git merge --ff-only FETCH_HEAD
git rev-parse --short HEAD

dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Attendu :** 394 tests réussis ; les avertissements `CA2014` des lignes `ProtocolWireNormalizer` doivent disparaître. Le reste des avertissements connus `NU1701` et de nullable est hors périmètre.

**F5 après validation :** lancement, choix du serveur, capture prix équipement/rune/ressource via `jzs`, focus clic molette, concassage `kch/kag` avec runes et coefficient précis, History, F7/F8 sans régression. Ne pas simuler une saturation avec Npcap ni injecter des paquets privés sur le poste réel : les tests synthétiques assurent ce point.

Aucune action sur `main`, aucune migration, version ou release dans ce lot. La validation P6 reste séparée et requerra l'accord utilisateur.
