# P4.0 — Caractérisation des valorisations et de l'historique de concassage

**Branche :** `refactor/codebase-cleanup`  
**Base :** `577339b` — checkpoint Windows P3 : **361/361 tests** et concassage `kch/kag` validés en jeu.  
**Périmètre :** audit + tests de caractérisation **sans correction métier ni modification de l'UI**.

## Observation réelle servant d'oracle

Le 11/10/2026, le concassage réseau du **Rasoir Infernal**, coefficient précis **99,57 %** (100 % arrondi dans Dofus), a fourni **16 Runes Vi**. La capture BestCrush du détail d'historique montre les prix figés suivants, datés du 21/09/2026 :

- **1 lot x10 à 1 595 K** ;
- **6 lots x1 à 130 K** chacun, soit **780 K** ;
- total brut : **2 375 K** ;
- net après décote 5 % : **2 256,25 K** affiché **2 256 K** (format sans décimales).

L'historique affiche le bon équipement, la bonne quantité de runes, le coefficient non arrondi et des prix/provenances figés. La différence avec l'estimation affichée par le jeu (2 432 K) ne suffit pas à indiquer un défaut : les bases de prix peuvent différer.

## Surfaces auditées et preuves dans le code

### P4.A — Cohérence de la décomposition en lots

- `MarketPriceCalculator.CalculateValue` (`BestCrush.Domain/Services/MarketPriceCalculator.cs`) réalise la décomposition **x1000 → x100 → x10 → x1**, puis estime les unités restantes à partir du **prix unitaire du plus petit lot disponible**.
- `CrushSessionService.BuildRuneLotBreakdown` construit le détail avec les mêmes priorités de lots. Dans un reliquat **estimé**, `Count` est **le nombre d'unités**, alors que `LotQuantity` et `LotPrice` caractérisent le lot source (ex. Count=3, LotQuantity=10, LotPrice=1000).
- `CrushSessionOverlayPage.BuildExcelTerm` utilise déjà `Count * LotPrice / LotQuantity` pour ces lots estimés.
- **Écart actuel confirmé par lecture :** `BestCrush/Components/Pages/History.razor` affiche pour **tous** les lots `Count × LotQuantity = Count × LotPrice`, sans division pour les estimations. Cela affiche **3×10 = 3000 K** au lieu de **3×(1000/10) = 300 K**, même si la valeur brute du snapshot stocké est bien **300 K**.

**Correction ultérieure P4.2 proposée :** extraire un formatage de ligne de lot testable et distinguer lots entiers et unités estimées, sans toucher aux formules économiques ni revaloriser rétroactivement l'historique. Ne pas bloquer P4.0 par un test qui attendrait déjà la correction.

### P4.B — Actualisation du dernier résultat en overlay

- `CrushSessionService.ApplyNetworkCrushAsync` charge les observations de runes au moment du concassage, construit la valeur et publie le snapshot. Il sauvegarde ensuite la valeur initiale dans l'historique.
- Le constructeur actuel du service n'injecte **pas** `MarketDataChangeNotifier` ; aucune inscription à son événement `Changed` n'est présente dans ce service. `Show()` republie un snapshot des valeurs déjà en mémoire, **sans recharger les prix**.
- **Écart documenté :** une capture/édition ultérieure du prix de la Rune Vi peut modifier le prix local en base, mais ne revalorise **pas automatiquement le dernier résultat déjà affiché**. C'est une analyse de code, **pas encore une comparaison F5 avant/après prix**.
- **Contrat proposé pour P4.1, à confirmer :** si un résultat de concassage reste affiché, une notification de changement de **rune concernée + serveur courant** recalcule uniquement sa valeur **courante**, en tenant compte des flags/priorités de prix ; elle ne modifie **jamais** le snapshot déjà enregistré dans History. Prendre en compte la concurrence (capture suivante, changement de serveur, fermeture) et éviter les abonnements fuyants.

### P4.C — Concassage sans rune et historisation

- `TryPrepareHistorySaveLocked` refuse l'enregistrement si `_runes.Count == 0`.
- Un `kag` sans rune peut être traité et affiché par le réseau, mais **aucune opération d'historique n'est construite** dans cette situation.
- **Décision fonctionnelle requise** avant une éventuelle correction : faut-il historiser tous les concassages, y compris ceux à **zéro rune et zéro valeur** ? Le comportement actuel n'est **pas** changé par P4.0.

### P4.D — Conservation du snapshot économique

- `HistoryService.SaveCrushSessionAsync` persiste les libellés d'équipement, coefficients, runes, valeur brute, décote, valeur nette, détails de lots et provenances reçus dans un **write model**.
- Les tests existants `CrushHistorySnapshotTest` établissent déjà qu'une modification ultérieure du prix ou du catalogue ne réécrit pas ce snapshot.
- Un nouveau test ajoute explicitement le reliquat `Count=3, LotQuantity=10, LotPrice=1000, IsEstimated=true, Value=300` : après changement du cours x10 à 2 000 K, l'ancien historique reste à **300 K**.

## Tests de caractérisation P4.0

`Tests.BestCrush/Network/CrushValuationCharacterizationTest.cs` exerce **les fonctions réelles** `MarketPriceService.CalculateValue` et la méthode privée statique existante `CrushSessionService.BuildRuneLotBreakdown` via réflexion, **sans dupliquer leurs algorithmes** dans les tests. Le recours à la réflexion est borné à ce seul lot documentaire pour éviter toute nouvelle seam de production.

Scénarios :

1. **16 Runes Vi** aux prix observés (x10=1595, x1=130) → **2375 K**, lots 1×10 et 6×1 sans estimation.
2. **3 runes, seulement x10=1000** → **300 K**, détail estimé de **3 unités** du lot x10, et non trois lots complets.
3. **16 runes, seulement x10=1000** → **1600 K**, une vente de lot plein et six unités estimées.
4. Conservation de la provenance et de l'horodatage de l'observation utilisée.
5. Aucun prix : valeur inconnue, aucun lot.
6. Prix d'autres runes : ne fuitent pas vers la rune cible.
7. Prix nuls/négatifs : ne deviennent pas des lots vendables.

Le huitième test, ajouté à `CrushHistorySnapshotTest`, assure le snapshot figé d'un reliquat estimé malgré une variation de marché.

**Référence attendue après P4.0 : 369 tests .NET (361 + 8)**, à confirmer sous Windows. Les tests ne doivent **pas** faire passer le rendu erroné de History pour un comportement désiré.

## Checkpoint Windows

Ne pas réutiliser le `git pull` qui a précédemment échoué sur une configuration de branches multiples. Vérifier la branche et récupérer le fast-forward sans écraser les modifications locales :

```powershell
git status --short --branch
git fetch origin refactor/codebase-cleanup
git merge --ff-only FETCH_HEAD
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Validation P4.0 :** build + tests réussis ; aucune correction P4.1/P4.2 autorisée par la seule réussite de ce checkpoint. Ne modifier ni `main`, ni version/release, ni SQLite/migrations, ni F8, ni la capture réseau `jzs` / `kch` / `kag`. Ne pas toucher aux fichiers/stashes locaux.
