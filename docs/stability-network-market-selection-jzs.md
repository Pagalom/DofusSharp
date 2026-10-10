# Compatibilité réseau HDV observée — sélection `jzs` (11/10/2026)

## Symptôme

Le clic molette n'actualise pas l'équipement en focus malgré une sélection explicite en hôtel de vente. La capture Npcap est active et des messages Ankama sont écrits dans `wire.jsonl`, mais aucun `events.log` n'est créé : les anciennes clés du protocole 3.6.11.15 (`jzn`, `kdb`, `isa`) n'apparaissent pas pendant les sélections.

## Constat local, sans contenu de trame dans le dépôt

Deux sélections successives ont été caractérisées par l'utilisateur :

| Sélection | C→S `kcy`, f1 | S→C `jzs`, f3 | S→C `jzs`, f2 |
|---|---:|---:|---:|
| Bracelet du Glutin | 12851 | 12851 | 9 |
| Autre équipement | 1356 | 1356 | 3 |

Les `jzs` peuvent contenir des offres imbriquées sous un champ racine f1, mais une réponse sans offre contient toujours f2/f3. Les valeurs observées sont une preuve de corrélation sélection → réponse, **pas une preuve de la signification des prix et quantités**.

## Modification limitée

1. `protocol-map.json` déclare `market_selection_response: "jzs"`. Les champs historiques demeurent inchangés et le fallback sans configuration ne prétend pas connaître cette clé.
2. `SemanticDecoders.TryDecodeMarketSelectionItemId` extrait uniquement f3 positif sous condition que f2 (catégorie) soit présent et positif ; refuse les structures invalides et les identifiants impossibles pour l'application.
3. `DofusNetworkMessageProcessor` route cette observation vers **l'existant** `NetworkObservationWriter.RememberLastEquipmentAsync` : seuls les IDs réellement connus dans `Equipments`, pour la génération de serveur active, actualisent `LastNetworkEquipmentState`.
4. Le clic molette et le focus ne sont pas modifiés. **Aucun prix `jzs` n'est écrit dans SQLite.**

Le premier `events.log` nouveau devrait contenir `[MARKET-SELECTION]` et, si l'ID correspond bien à un équipement reconnu, `[LAST-EQUIPMENT]`.

## Tests

Sept cas décodeur synthétiques : valeurs 12851 et 1356, réponse vide, absence de f2 ou de f3, identifiant nul et corps malformé.

Quatre cas de routage réel sans Npcap : sélection avec offres, sans offres, ressource non éligible au focus et structure incomplète, avec vérification explicite de l'absence de prix enregistrés.

La suite complète S4b visait 253 tests, non encore certifiés sur Windows ; ce lot en ajoute 11, soit **264 attendus** en cas de réussite du reste.

## Checkpoint Windows

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Puis F5 :
- Serveur sélectionné, capture réseau opérationnelle.
- Sélectionner en HDV le Bracelet du Glutin puis un autre équipement ; cliquer sur la molette en zone Dofus **hors overlays** ; confirmer que le dernier équipement sélectionné passe en focus.
- Si les artefacts de développement sont activés, vérifier `[MARKET-SELECTION]` et `[LAST-EQUIPMENT]` dans `%LOCALAPPDATA%\BestCrush\DebugCaptures\Network\session-*\events.log`.
- Vérifier F8, capture du prix (sans conclure que le nouveau `jzs` est compatible), et concassage séparément. Les anciens événements `jzn` restent couverts par la suite existante.

## Hors périmètre

Le nouvel encodage `jzs` des offres et la migration des autres clés du protocole `kci`, `kdb`, `isa`, etc. **ne sont pas traités ici**. Leur décodage exige une caractérisation métier distincte. Ne pas associer aveuglément `jzs` à `price_list`.
