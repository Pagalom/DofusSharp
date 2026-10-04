# A3 — Caractérisation réseau avant extraction

Base : `38f1978`, branche `refactor/codebase-cleanup` (v0.2.0 + A1/A2).
Le journal Windows A2 confirme le build de l'application et 92 tests réussis.

## Portée et exécution

A3 ajoute 111 cas, soit 203 avec A1/A2. Checkpoint au commit `7b42061` déclaré
validé par l'utilisateur sous Windows : build réussi avec 16 avertissements,
111 tests réseau réussis et 203 tests réussis pour la suite complète, aucun
échec. Ces exécutions n'ont pas été reproduites dans l'environnement de
préparation, où `dotnet` est absent.

Le [protocole manuel A4](codebase-cleanup-a4-windows-manual-validation.md)
complète la référence des raccourcis et overlays. Il ne ferme pas les lacunes
de routage décrites ci-dessous ; leur caractérisation reste un préalable à
l'extraction des méthodes de traitement réseau en Phase E.

`Tests.BestCrush/Tests.BestCrush.csproj` compile par liens les quatre fichiers
protocolaires déjà partagés par l'application et le Probe, ainsi que
`BestCrush/Services/LastNetworkEquipmentState.cs` et
`BestCrush/Services/MarketDataChangeNotifier.cs`. Il copie le vrai
`BestCrush.NetworkProbe/protocol-map.json` dans `NetworkFixtures/` à la sortie.
Les tests vérifient la présence de ce fichier pour ne pas tester accidentellement
les valeurs de secours. Aucune implémentation produit n'est modifiée ni dupliquée.

Ces liens de test sont transitoires : ils devront devenir une référence au
projet partagé si `BestCrush.Network` est créé ultérieurement. Les tests
n'importent ni l'application MAUI, ni SharpPcap, ni PacketDotNet. Ils ne démarrent
pas la capture, ne contactent pas DOFUS et ne nécessitent pas Npcap. Les tests
d'historique réutilisent `Tests.BestCrush/Utils/TestDatabase.cs` avec une base
SQLite temporaire, sans migration ni ouverture de la base utilisateur.

`Tests.BestCrush/Network/NetworkPayload.cs` fabrique des octets synthétiques
depuis les champs lus par les décodeurs existants. Ce ne sont pas des captures
réelles et ces fixtures ne prétendent pas démontrer la validité d'un protocole
DOFUS différent de celui actuellement reconnu. Des vecteurs hexadécimaux
littéraux vérifient aussi les types wire, une enveloppe Ankama et `kei` sans
dépendre du générateur de fixtures. Aucun routeur métier n'est reproduit dans
les tests.

## Origine des structures de messages

Toutes les méthodes ci-dessous sont dans
`BestCrush.NetworkProbe/Protocol/SemanticDecoders.cs`.

| Message | Structure utilisée par les fixtures | Source |
|---|---|---|
| `jzn` courant | Racine f1 ItemId, f2 offres ; offre f2 ItemId, f5 OfferId, f6 prix packed | `TryDecodeCurrentJzn`, avec son commentaire « observed on wire » |
| Marché structuré/compact | f2/f3 structurés ou recherche récursive existante | `TryDecodeStructuredMarket`, `CollectCompactMarket` ; testés comme fallbacks, pas comme nouveaux formats validés |
| `kei` | f1 prix, f2 quantité, f5 OfferId | `TryDecodePurchaseRequest` |
| `kef` | f1 ItemId, f2 OfferId, f4 prix packed, f5 caractéristiques | `TryDecodePurchaseOffer` |
| `kbd` | f2 quantité, f3 OfferId | `TryDecodePurchaseReceipt` |
| `kdb` / `isa` | Enveloppe f2 puis objet f5 | `TryDecodeItemDetail`, `TryDecodeInventoryAdd` |
| `iuq` | Enveloppe f1 puis objet f5 | `TryDecodeCraftOutput` |
| `kbu` | f2 code, enveloppe f3 puis objet f2 | `TryDecodeSmithmagicResult` |
| Objet/effet | Objet f1 UID, f2 quantité, f3 effets, f5 ItemId ; effet f1 identifiant, f10 valeur signée | `TryDecodeItemObject`, `DecodeStats` |
| `kci` | Racine f1 lignes ; ligne f1 runes, f2 float32 fraction, f3 UID, f4 fraction secondaire ; rune f1 ItemId, f3 quantité | `TryDecodeCrush`, avec son commentaire « observed on wire » |

Les décodeurs d'inventaire, d'atelier, de pile FM et de listing sont également
appelés avec leurs propres champs actuels. Les clés sont vérifiées contre la
carte de production ; cette vérification ne remplace pas le dispatch métier.

## Cas ajoutés

Tous ces fichiers sont dans `Tests.BestCrush/Network/`.

| Suite | Cas | Ce qui est effectivement exercé |
|---|---:|---|
| `ProtoWireTest.cs` | 17 | Types wire, doublons, champs vides/tronqués, varints, limite unsigned, overflow de numéro de champ, décodage UTF-8 actuel |
| `AnkamaFrameDecoderTest.cs` | 8 | Enveloppes directes/imbriquées, profondeur 5/6, priorité racine et ordre des frères, préfixe exact, corps opaque |
| `TcpReassemblerTest.cs` | 5 | Trou, segments désordonnés, retransmission, chevauchement déjà émis, première entrée en attente conservée, défaut de chevauchement en attente |
| `VarintFrameBufferTest.cs` | 5 | Préfixe/corps fragmentés, frames regroupées, resynchronisation d'un octet, limite de taille, séquence réelle réassemblage → enveloppes → décodeurs dans l'ordre |
| `ProtocolMapTest.cs` | 7 | Carte livrée, secours fichier absent, clés manquantes/nulles, casse, clé historique atelier, JSON invalide |
| `MarketDecoderTest.cs` | 13 | Offres conservées, absence d'offres, zéros, quatre positions de lots, prix unsigned, slots supplémentaires, nettoyage des ladders et priorité des fallbacks |
| `PurchaseDecoderTest.cs` | 14 | Champs/OfferId de `kei/kef/kbd`, valeurs obligatoires, stats signées, ladder absent ou invalide, quantités unsigned |
| `ItemDecoderTest.cs` | 15 | UID/ItemId, quatre enveloppes d'équipement, quantité par défaut/zéro, stats, conversions, inventaire, atelier, FM, listings |
| `CrushDecoderTest.cs` | 12 | Ordre des lignes/UID répétés, absence de cache au décodage, fractions → pourcentages, bornes/NaN, fraction secondaire indépendante, ordre des runes et valeurs non filtrées |
| `LastNetworkEquipmentStateTest.cs` | 8 | Dernier `Set`, horodatage conservé, serveur ordinal, observation invalide ignorée, un seul snapshot, effacement |
| `MarketDataChangeNotifierTest.cs` | 2 | Notifications identiques retransmises, quantité par défaut, abonnés synchrones ordonnés, exceptions propagées |
| `CrushHistorySnapshotTest.cs` | 5 | Persistance du snapshot fourni, provenance des prix/lots, icônes au moment de la sauvegarde, indépendance des données ultérieures, valeur incomplète, ajout sans déduplication, entrées invalides |

## Limites de couverture à ne pas confondre avec une protection du routage

Les méthodes `ProcessMessageAsync`, `PersistMarketAsync`, `PersistCrushAsync`
et `RememberLastEquipmentAsync` de
`BestCrush/Services/DofusNetworkCaptureService.cs` sont privées, sous
`#if WINDOWS`. La carte et le worker sont initialisés dans `Start`, qui ouvre
Npcap. Le service dépend de `BestCrushSettingsService` (scellé, préférences
MAUI statiques), de `CrushSessionService` (scellé, appel non virtuel vers l'UI),
et de services concrets de persistance. Le projet de tests actuel cible
`net10.0` et ne référence pas l'application MAUI Windows.

La réflexion seule n'isolerait pas les préférences ni l'UI. Ajouter de fausses
classes de production homonymes, recopier la sélection des prix ou simuler le
routeur ne prouverait pas le fonctionnement du graphe réel. A3 ne le fait pas.

| Invariant demandé | État A3 / obstacle précis |
|---|---|
| `jzn`, minimum strictement positif et lots x1/x10/x100/x1000 | Décodage des offres et positions couvert. La sélection du minimum, le rejet des prix > `long.MaxValue`, les paramètres de capture et les écritures sont dans `PersistMarketAsync` : non exécutés par A3. |
| `kei/kef/kbd`, corrélation | Champs décodés couverts. OfferId corrélé, limite inclusive de 5 s, remplacement de la requête en attente, effet du reçu et interdiction de rafraîchir les équipements : non couverts, état privé du routeur. |
| UID/ItemId et dernier équipement fiable | Décodage et stockage de `LastNetworkEquipmentState` couverts séparément. Remplissage de `_itemDetails`, résolution du catalogue, filtrage des ressources/runes et appels à `RememberLastEquipmentAsync` : non couverts. Aucun test ne prétend prouver qu'une observation est un équipement fiable. |
| `kci` UID connu/inconnu, coefficients, runes | Ordre et contenu décodés couverts. Le décodeur accepte tout UID positif ; c'est `PersistCrushAsync` qui ignore un UID inconnu. Ce filtrage, les conversions `checked`, les écritures de coefficients et leur ordre ne sont pas couverts. |
| Coefficients désactivés sans suppression du résultat | Non couvert : branche dans `PersistCrushAsync`, puis appel direct à `CrushSessionService.ApplyNetworkCrushAsync`. |
| Écritures et notifications, y compris déduplication | Notifier couvert isolément ; déduplication métier couverte par A2. L'ordre et le nombre des appels du routeur ne sont pas couverts par leur juxtaposition. |
| Constitution résultat/historique | `HistoryService.SaveCrushSessionAsync` couvert depuis un write model explicite. Agrégation des runes, dernier coefficient par équipement, remplacement du précédent résultat, valorisation/décote et construction du write model dans `CrushSessionService` : non couverts par A3. |
| Ordre global / Channel | Ordre des octets et frames couvert. Le Channel à lecteur unique, l'ordre des traitements métier, les retours anticipés et l'isolation des erreurs du worker restent non couverts. |

La constitution du résultat passe par l'état privé de
`BestCrush/Services/CrushSessionService.cs` : `ApplyNetworkCrushAsync`,
`TryPrepareHistorySaveLocked`, `PublishSnapshot`, `ScheduleHistorySave`, puis
`MainThread.InvokeOnMainThreadAsync(Show)`. Le chemin non vide mélange métier,
sauvegarde asynchrone et fenêtres MAUI ; il n'offre pas de destinataire de
résultat substituable. Les tests d'historique ne reconstruisent pas ce chemin.

## Adaptations minimales proposées — NON appliquées, accord requis

Avant une extraction réseau, compléter les tests d'intégration du routage avec
un point de rejeu hors capture dans le vrai service :

1. Rendre l'entrée de traitement et son DTO accessibles en interne aux tests,
   avec `InternalsVisibleTo`, et permettre le chargement de `ProtocolMap` sans
   appeler `Start`. Garder le même dispatch, les mêmes horodatages et le même
   Channel. Utiliser une cible de tests Windows référençant l'application.
2. Consommer `IBestCrushSettingsProvider`, qui expose déjà les quatre flags,
   et injecter séparément la lecture du flag de debug (actuellement seule autre
   lecture de `BestCrushSettingsService` dans le service réseau).
3. Permettre de substituer uniquement le destinataire de
   `ApplyNetworkCrushAsync`, par une fonction typée reliée à la méthode actuelle
   dans la DI. Conserver les vrais `MarketPriceService`, `CoefficientService`,
   `MarketDataChangeNotifier` et SQLite temporaire dans les tests.

Cela permettrait les séquences `kei/kef/kbd` à horodatages explicites
(5 s et 5 s + un tick), `kdb/isa/iuq/kbu → kci`, les paramètres de capture et
les observations consultées depuis les callbacks de notification. Aucun
mediator, bus général, repository ou nouvel algorithme n'est nécessaire.

La formation du snapshot de session demanderait en plus de séparer les effets
UI et la planification de sauvegarde de la méthode existante ; cette adaptation
relève d'un lot distinct à approuver. A3 ne peut pas constituer à lui seul un
feu vert pour extraire le routage avec tous ses invariants protégés.

## Suivis séparés

`docs/codebase-cleanup-follow-ups.md` conserve le warning SQLite NU1903
(`SQLitePCLRaw.lib.e_sqlite3` 2.1.10, `GHSA-2m69-gcr7-jv3q`) et décrit le
défaut TCP de chevauchement en attente comme **BUG EXISTANT — CORRECTION
FONCTIONNELLE À VALIDER**. Aucun des deux n'est corrigé dans A3.

`DofusSharp.sln` et `DofusSharp.DofusDb.Filter.slnf` ne sont pas modifiés par
A3 ; les changements locaux signalés sur le poste Windows restent indépendants.

## Checkpoint Windows

Depuis la racine du dépôt, après récupération du commit A3 :

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --filter "FullyQualifiedName~Tests.BestCrush.Network" --logger "console;verbosity=normal"
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Nombres confirmés par le retour utilisateur du checkpoint : 111 ciblés, 203
pour la suite complète. Le lot A3 s'est arrêté à ce checkpoint sans nettoyage
OCR/F9, changement des raccourcis ou refactoring réseau. A4 fait ensuite l'objet
d'un lot documentaire distinct.
