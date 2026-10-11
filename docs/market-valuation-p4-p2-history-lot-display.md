# P4.2 — Corriger l'affichage des lots estimés dans l'historique

**Branche :** `refactor/codebase-cleanup`  
**Base :** `7488538` (P4.1 : checkpoint Windows 375/375, mais contrôle fonctionnel F5 non concluant).
**Statut :** modification ciblée et tests préparés, compilation/test Windows **non exécutés ici**.

## Défaut

Dans `History.razor`, l'expression d'une ligne de détail de lot multipliait toujours `Count * LotPrice`.

Or `CrushSessionService.BuildRuneLotBreakdown` conserve :
- **lot complet :** `Count` = nombre de lots (x10, x100, etc.) ;
- **lot estimé :** `Count` = nombre **d'unités restantes**, `LotQuantity` = taille du plus petit lot disponible, `LotPrice` = prix **du lot entier**.

Avec **3 runes** estimées au moyen du seul prix **x10 = 1 000 K**, `Count=3`, `LotQuantity=10`, `LotPrice=1000`, `IsEstimated=true`. Le calcul de marché et le snapshot figé ont toujours donné **300 K**, mais l'UI affichait **3×10 = 3 000 K**.

## Intervention minimale

- `BestCrush/Components/Pages/History.Formatting.cs` expose un formateur interne et testable `FormatCrushHistoryLot(CrushHistoryRuneLot)` qui utilise **les seules données stockées dans le snapshot**.
- Pour `IsEstimated && LotQuantity > 1` : afficher `3×(1 000/10) = 300 K` et calculer `Count * LotPrice / LotQuantity`.
- Pour les lots complets : conserver l'ancienne présentation `1×10 = 1 595 K` et la valeur `Count * LotPrice`.
- Pour les lots estimés x1 : aucune division supplémentaire nécessaire ; même résultat `3×1 = 300 K`.
- `History.razor` remplace les deux lignes d'expression par **l'appel au formateur**. Les annotations `• estimé`, source et horodatage restent identiques.
- Le format arrondi des kamas `N0` est conservé.

**Aucune modification** de `MarketPriceCalculator`, `CrushSessionService`, `HistoryService`, du schéma SQLite, des prix, de la priorité Manuel/Jeu, des coefficients, du protocole réseau ou des overlays.

## Tests

`Tests.BestCrush/Network/CrushHistoryLotFormattingTest.cs` : **5 tests** sur le **vrai formateur appelé par Razor**, et non sur une formule recopiée :

1. Reliquat estimé de 3 runes via prix x10=1000 → **300 K**, pas 3000.
2. Lot complet x10=1595 → affichage et valeur inchangés.
3. Six lots x1=130 → **780 K**.
4. Lot estimé x1 → **300 K**.
5. Six unités restantes estimées via x10=1000 → **600 K**.

Les tests P4.0 préexistants protègent la conservation du snapshot : aucune mise à jour historique n'est déclenchée par P4.2.

**Total attendu : 380 tests (375 + 5)**, à vérifier sur Windows.

## Incident P4.1 conservé ouvert

Lors du contrôle F5 sur Kourial, l'utilisateur a observé un dernier concassage de **17 Runes Vi** à **2 598 K** (détail **1×1 541 K + 7×151 K**), tandis que la page des prix affichait **x1 = 159 K (M)** et **x10 = 1 541 K (J)**. Aucune variation de la valeur de l'overlay n'a été constatée. La capture montre le focus sur la saisie ; l'UI indique que l'enregistrement se fait **à la sortie de la case**. La priorité de source Jeu/Manuel et l'éventuelle différence entre prix actuellement saisi et prix réellement sélectionné n'ont pas été contrôlées. **Ne pas déclarer P4.1 fonctionnellement validée et ne pas présumer du diagnostic.**

P4.2 ne corrige **pas** P4.1 ; ce point pourra être repris dans un lot séparé quand l'utilisateur le souhaitera, sans bloquer l'affichage de l'historique.

## Checkpoint Windows

Éviter `git pull` (configuration locale à branches multiples) et préserver les modifications locales hors projet :

```powershell
git status --short --branch
git fetch origin refactor/codebase-cleanup
git merge --ff-only FETCH_HEAD
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Attendu : **380 tests réussis, zéro échec** et compilation Windows OK. Les warnings préexistants NU1701, CA2014 et nullable sont suivis séparément.

En F5, vérifier dans l'historique l'affichage des lots pleins inchangés, ainsi qu'une ligne de reliquat estimé lorsqu'un lot unitaire manque. Ne pas demander un concassage uniquement pour reproduire un lot rare : les tests synthétiques couvrent ce cas. Ne pas modifier `main`, la version ni préparer de release.
