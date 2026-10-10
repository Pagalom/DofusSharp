# S3 — Réassemblage TCP : drainage des segments en attente qui se chevauchent

Base : `49624ec` (stabilisation de l'initialisation S2c). Le démarrage Windows a été signalé comme fonctionnel ; le décompte des tests S2c reste à vérifier sur les logs Windows.

## Anomalie préexistante (caractérisée en A3)

Exemple :
- `Push(100, [1,2])` donne `[1,2]`, prochain octet attendu à 102 ;
- `Push(104, [5,6,7,8])` met le segment en attente ;
- `Push(102, [3,4,5,6])` délivre 102..105, mais le segment en attente reste indexé à 104 ;
- l'ancienne boucle ne traitait que la clé *exactement* égale à 106, ce qui bloquait 106..107 et les segments suivants.

## Correction

`BestCrush.Network/Capture/TcpReassembler.cs` draine désormais la première clé en attente tant qu'elle démarre **à ou avant** le prochain numéro de séquence attendu.

- Segment devenu totalement ancien : suppression, aucune nouvelle émission.
- Segment partiellement déjà délivré : découpe du préfixe redondant et émission du seul suffixe utile.
- Segment plus loin que le prochain octet attendu : le trou est maintenu, les données restent en attente.
- Les doublons arrivant avec **la même clé de départ** conservent la règle initiale « premier segment en attente gagnant ».

Aucune modification de `VarintFrameBuffer`, des messages/decoders Ankama, de Npcap, du contexte serveur, des règles de prix ou du concassage.

## Tests

Le test A3 documentant intentionnellement le blocage est remplacé par l'attendu corrigé.

Trois nouveaux tests vérifient :
1. le recouvrement de plusieurs segments en attente, sans doublons ;
2. l'élimination de segments totalement couverts ;
3. l'intégrité d'une succession de frames Varint réassemblées malgré les recouvrements.

Total attendu après S3 : **238 tests** (235 précédemment + 3).

## Limites hors S3

- Ordonnancement cyclique des numéros de séquence TCP uint32 lors d'un *wrap-around* : non modifié ;
- caches réseau non bornés, reprise Npcap après Stop et doublons multi-adaptateurs : lots ultérieurs ;
- aucune fonctionnalité nouvelle ou modification de version.

## Checkpoint Windows

```powershell
git switch refactor/codebase-cleanup
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Attendu : **238 réussis, 0 échec**. Vérification manuelle : lancement BestCrush/F5, serveur sélectionné sans confirmation, capture HDV d'un équipement et d'une rune, résultat de concassage et F7/F8 si possible.

Le code GitHub n'a pas été compilé localement : validation requise sur le Windows utilisateur.
