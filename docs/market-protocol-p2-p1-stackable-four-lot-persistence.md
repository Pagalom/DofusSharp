# P2.1 — Persistance des quatre lots de runes et ressources depuis jzs

**Branche :** `refactor/codebase-cleanup` — **Statut :** implémenté, en attente de validation Windows.

## Preuve capturée et concordance avec le HDV — 11 octobre 2026

La capture et les captures d'écran transmises par l'utilisateur confirment la même disposition pour les deux familles empilables :

| Type | Objet | ItemId | Catégorie réseau | x1 | x10 | x100 | x1000 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Rune | Rune Ini | 7448 | 78 | 139 | 1 399 | 14 284 | 151 014 |
| Ressource | Ailes de Scarafeuille Rouge | 1457 | 104 | 483 | 897 | 9 179 | 85 000 |

Tous les montants sont des **kamas**. La Rune Ini x10 affichée à 1 399 K dans la capture la plus récente correspond à l'enregistrement réseau `18:03:53.9121873Z` ; un relevé antérieur donnait 1 400 K avant la modification du marché.

Wire : `jzs` (S→C), racine `f3=itemId`, `f2=grouping`, `f1` répété pour les offres ; entrée `f1=itemId`, `f2=packed [x1,x10,x100,x1000]`, `f3` référence numérique opaque (ne pas l'interpréter comme un prix), `f4=grouping`. Un `jzs` sans offres suit fréquemment une réponse avec offres : ce message ne doit jamais effacer un prix.

Validation équipement P1 préservée : Tongues Wabbits, `itemId=6493`, `f2` packed `[prix x1,0,0,0]`, minimum 4 899 K, déjà confirmé fonctionnellement avec SQLite.

## Changements

1. `SemanticDecoders.TryDecodeJzsPriceLadders` est une méthode privée stricte partagée :
   - exactement un ID racine positif et convertible en `long`, une catégorie positive ;
   - au moins une offre ; chaque offre doit désigner exactement le même ID/groupe et une référence d'offre positive ;
   - chaque `offer.f2` doit contenir **exactement quatre** entiers varint, tous `<= long.MaxValue`, avec au moins un positif ;
   - aucune offre invalide, tronquée, ambiguë, hors limites ou entièrement nulle n'autorise la persistance **d'une partie** de l'observation ;
   - conserve l'ordre exact des prix et les positions des zéros, sans heuristique de `CleanLadder`.
2. `TryDecodeJzsEquipmentPrices` réutilise le validateur mais impose **x1 positif et tous les autres lots nuls sur chaque ligne**. `TryDecodeJzsStackablePrices` autorise les quatre lots, y compris l'absence de certains lots (zéro).
3. `DofusNetworkMessageProcessor` garde le focus sélectionné et route les deux interprétations indépendantes sur le message `jzs`.
4. `NetworkObservationWriter.PersistMarketAsync` vérifie la catégorie réelle de l'ID **dans SQLite** :
   - `requireEquipment` autorise exclusivement `Equipment` pour la voie P1 ;
   - `requireStackable` autorise exclusivement `Rune` ou `Resource` pour la voie P2 ;
   - le groupe réseau (ex. 78/104) ne sert pas à déterminer un type d'objet ;
   - les paramètres individuels de capture équipement/rune/ressource, la lease serveur et la notification sont **déjà** contrôlés par ce même writer ;
   - les minima strictement positifs sont calculés séparément pour x1/x10/x100/x1000, sans insertion de zéro.
5. `protocol-map.json` conserve `market_selection_response=jzs` et les `field_mappings` existants ; seul son état de vérification est complété.

Aucun changement de base ou migration, de F8/OCR, de la capture Npcap, du transport, des chemins de fichiers, de la version, de la release ou de `main`. Les anciens messages `jzn` conservent leur décodage indépendant.

## Tests synthétiques

`JzsStackablePriceDecoderTest.cs` : comparaison exacte avec les deux captures, slot absent, position originale, minimum inter-offre, message vide, racine invalide, lignes mixtes, formats tronqués, débordements et doublons de champs.

`DofusNetworkRoutingTest.cs` : routage réel de `jzs` via les seams Npcap existantes et SQLite de test ; sauvegarde des quatre lots et notifications en ordre, minima positifs, lots absents, réponse vide, paramètres de capture, focus équipement préservé, absence de serveur et ancienne lease, format invalide refusé.

Les données originales des captures ne sont pas commitées ; les tests utilisent des payloads Protobuf **synthétiques** et les IDs catalogue de test 100 (rune), 200 (ressource) et 42 (équipement), avec des montants observés.

**Checkpoint attendu :** 344 tests .NET (= 313 tests P1 Windows confirmés + 31 cas nets nouveaux P2.1), **à confirmer sur Windows**. Les 17 tests Python P2.0 restent indépendants et n'ont pas encore de résultat Windows transmis.

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

## Checkpoint manuel après builds/tests

1. Lancer BestCrush avec F5 ; sélectionner le serveur ; activer individuellement la capture des runes et ressources.
2. Consulter Rune Ini et Ailes de Scarafeuille Rouge dans l'HDV. Comparer les **prix affichés à ce moment précis**, susceptibles d'avoir changé, et les quatre lots x1/x10/x100/x1000 en base/interface BestCrush.
3. Vérifier la notification/actualisation attendue et que les réponses `jzs` vides ne remplacent aucune valeur existante.
4. Vérifier qu'une consultation de rune/ressource ne change pas le **focus équipement**, puis tester le focus molette et F8 **sans chercher à modifier sa robustesse dans cette phase**.
5. En cas d'anomalie, couper la capture automatique des runes/ressources, conserver les captures locales et remonter les différences ; ne pas effacer la base utilisateur.

**Interdit :** migration SQLite non demandée, fusion `main`, release/publish, modification F8 ou restauration d'un stash local sans autorisation.
