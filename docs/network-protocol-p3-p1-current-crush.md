# P3.1 — Réintégrer le concassage `kch` / `kag` dans BestCrush

Branche : `refactor/codebase-cleanup` — état : **implémentation en attente du checkpoint Windows et de la validation en jeu**.

## Preuves P3.0

Le 11/10/2026, sur le client actuel, deux opérations indépendantes ont été corrélées :

1. **Baguette de Boisaille x1**, résultat **60 %**, sans rune. `jxy` C→S puis `kag` S→C. `kag.root.f1[0].f1` en float32 = **0,5976145** (~59,76145 %, 60 % arrondi par Dofus), `f3` = UID **11387412**, pas de `f4` rune.
2. **Second équipement x1**, résultat **162 %**, **6 Runes Vi**. `kch` S→C porte l'UID **9022491**, l'ID d'équipement **6924** et la quantité 1. `kag.root.f1[0]` contient `f1` float32 **1,615723** (~161,5723 %, 162 % arrondi), `f2` seconde valeur float32 très proche, `f3` UID **9022491**, `f4` message rune avec `f1=6` et `f2=1523` (Rune Vi). L'utilisateur confirme la quantité et le type de rune.

Ces valeurs ne sont pas inventées : les champs figurent dans les sorties de diagnostic et le résultat a été confirmé par l'utilisateur. La signification exacte du deuxième float reste inconnue ; le premier est utilisé, comme pour l'ancien modèle canonique.

## Adaptation minimale

**Préservation stricte de l'historique :** `item_detail=kdb` et `crush_result=kci` conservent leurs clés, correspondances et décodeurs. `protocol-map.json` comprend désormais **21 routes** au lieu de 19, dont :

- `item_detail_current=kch`, avec une correspondance normalisant `root.f3.f1` vers le modèle item canonique `root.f2.f5` ; item `f5 → uid`, `f4 → itemId`, `f3 → quantity`. Les stats détaillées `f7` ne sont pas encore caractérisées ; le décodeur d'identité les ignore.
- `crush_result_current=kag`, avec une correspondance normalisant `root.f1[]` : `f1(float) → coefficient canonique f2`, `f2(float) → secondaire canonique f4`, `f3 → uid`, `f4[] → rune canonique f1[]`; dans la rune `f2 → runeItemId canonique f1`, `f1 → quantité canonique f3`.

Cela permet de **réutiliser sans duplication** `SemanticDecoders.TryDecodeItemDetail` et `SemanticDecoders.TryDecodeCrush`. Les messages restent routés par `DofusNetworkMessageProcessor` vers :

`CurrentServerState` → `RecentItemDetailsCache` (UID→ID) → `NetworkObservationWriter.PersistCrushAsync` → `CoefficientService` / `CrushSessionService.ApplyNetworkCrushAsync` → résultat/overlay.

Le cache UID est vidé lorsqu'on change de serveur ou qu'une capture est redémarrée, comme avant. Un `kag` sans UID connu n'est **pas** attribué à l'équipement consulté le plus récemment. Un coefficient sans rune reste un résultat de concassage valide. Le NetworkProbe utilise le même fichier et le même normaliseur.

## Checkpoint Windows obligatoire avant F5

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Dernière base **explicitement validée sur Windows** dans ce fil : **313/313 tests** à P1. P2.1 a été déclaré validé fonctionnellement par l'utilisateur, sans copier une nouvelle sortie de tests .NET. Le nombre actuel de tests doit être constaté dans la sortie réelle et **ne doit pas être présenté comme validé à l'avance**.

## Validation manuelle

1. Lancer BestCrush avec F5, **sélectionner le serveur** et activer la capture automatique des coefficients ; laisser les captures de debug pour investigation.
2. Concasser **un équipement x1 peu coûteux**, après l'avoir introduit dans le concasseur : le résultat doit afficher **le bon équipement**, le **coefficient exact** et les **runes/quantités** observées en jeu. Le résultat sans rune doit aussi être pris en charge.
3. Vérifier l'overlay de résultat et l'observation de coefficient dans BestCrush ; en cas d'échec, garder `wire.jsonl` local sans partager des payloads bruts.
4. Vérifier le mode de capture passive des prix HDV, la sélection au clic molette et F8 (ce dernier **reste inchangé**).
5. Ne pas faire de release, de merge/main, de changement de version, de restauration de stash ni de suppression de données locales.

## Limites connues

- Statistiques détaillées de `kch` non décodées ; seule l'identité de l'instance (UID, ItemId, quantité) est caractérisée.
- Le second float `kag.f1.f2` reste non interprété autrement que par l'ancien champ canonique secondaire.
- La validité d'un concassage multi-objets est protégée par des fixtures synthétiques, mais n'a pas encore été confirmée par une capture réelle du client.
- Les autres messages réseau du client actuel, dont les anciens messages historiques `kec` de l'atelier, ne sont pas automatiquement considérés comme caractérisés.
