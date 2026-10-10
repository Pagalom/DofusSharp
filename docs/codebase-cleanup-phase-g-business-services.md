# Phase G — responsabilités des services métier

Branche : `refactor/codebase-cleanup`. Base : `94e9fc5` (Phase F validée Windows, 226/226).

## Périmètre

- `MarketPriceService` conserve ses méthodes publiques de lecture/écriture EF et délègue les deux algorithmes **distincts** de valorisation de vente et de coût minimal d'achat à `MarketPriceCalculator`, composant stateless interne. Les algorithmes ont été déplacés sans modification.
- `HistoryService` conserve son instance scoped et son DbContext. Le code d'écriture des snapshots de concassage reste dans le fichier principal ; les requêtes **marché**, **équipements**, **sessions de concassage** et la pagination sont regroupées en fichiers partiels ciblés. Les expressions LINQ, les tris, les filtres et les projections sont inchangés.
- `EquipmentProfitabilityService` conserve l'orchestration, le chargement des données et le fallback DoFocus. La construction des scénarios et la recherche du coefficient minimal sont regroupées dans un fichier partiel dédié, sans changement des formules ni du traitement des données incomplètes.
- Les types publics, les signatures, les cycles de vie DI, les modèles EF, le schéma de base et l'ordre des opérations ne changent pas.

## Non-objectifs

Pas de nouvelle fonctionnalité, pas de recalcul d'historique, pas de modification des règles de lots, coefficients, rune Pa/Ra, valorisation, priorité des sources, déduplication ou notifications. Les anomalies documentées (TCP overlap, changement de serveur, UI History, rafraîchissement des prix, vulnérabilité SQLite NU1903) restent hors périmètre.

## Validation Windows requise

```powershell
git switch refactor/codebase-cleanup
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Objectif : compilation Windows réussie et **226 tests réussis, 0 échec**, sans nouveau warning ni modification fonctionnelle constatée. La validation Windows est nécessaire : le SDK .NET n'est pas disponible dans l'environnement de préparation.
