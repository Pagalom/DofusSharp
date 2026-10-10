# Stabilisation S1 — correctif de la bibliothèque SQLite native

## Périmètre

Point de départ : `refactor/codebase-cleanup` HEAD `664a1d4`, refactoring A–H confirmé sous Windows (226/226 tests et vérification fonctionnelle).

Constat : `Microsoft.EntityFrameworkCore.Sqlite` **9.0.8** dépend de `SQLitePCLRaw.bundle_e_sqlite3` >= 2.1.10. La résolution par défaut utilisait `SQLitePCLRaw.lib.e_sqlite3` **2.1.10**, signalée par `NU1903`.

Advisory GHSA-2m69-gcr7-jv3q / CVE-2025-6965 : bibliothèque native SQLite antérieure à **3.50.2** vulnérable (versions NuGet `SQLitePCLRaw.lib.e_sqlite3` <= 2.1.11 affectées).

Sources :
- https://github.com/advisories/GHSA-2m69-gcr7-jv3q
- https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13

## Correction proposée

Ajout explicite de `SQLitePCLRaw.bundle_e_sqlite3` **2.1.13** dans l'application, le domaine, l'outil de migrations et les tests. Le bundle >= 2.1.13 référence notamment la bibliothèque native et le provider >= 2.1.13.

La ligne Entity Framework Core reste en **9.0.8**. Aucune mise à jour de version MAUI/.NET, aucun changement de schéma ou d'écriture métier. Aucun changement dans les fichiers SQLite existants.

Un test de régression `SqliteNativeVersionTest.NativeLibraryMustIncludeTheFixForCve20256965` interroge `SELECT sqlite_version()` sur la DLL native chargée réellement par Microsoft.Data.Sqlite et impose une version >= 3.50.2.

## Validation Windows requise — ne pas déclarer S1 résolu avant validation

1. Vérifier la propreté des fichiers modifiés par S1 et faire un `git pull --ff-only` (les autres modifications locales de la solution, du probe ou de VS Code ne doivent pas être écrasées).
2. `dotnet restore .\Tests.BestCrush\Tests.BestCrush.csproj`
3. `dotnet list .\Tests.BestCrush\Tests.BestCrush.csproj package --include-transitive` : vérifier que les packages `SQLitePCLRaw.*` pertinents ne résolvent pas d'ancienne bibliothèque native et que `NU1903` disparaît pour `SQLitePCLRaw.lib.e_sqlite3`.
4. `dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0`
5. `dotnet build .\BestCrush.Migrations\BestCrush.Migrations.csproj`
6. `dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"` : attendu **227 réussis**, 0 échec.
7. Tester le démarrage BestCrush sous Windows, sans créer de nouvelle base utilisateur ; vérifier lecture et écriture des prix/coefficients sur une base de test, et contrôler les migrations sur une **copie** de la base existante (`%LOCALAPPDATA%\BestCrush\Data\bestcrush.db`), jamais l'original directement.

Si le test de version native, NuGet ou une migration échoue, considérer S1 comme non validé et arrêter avant les autres corrections.

## Hors périmètre

Aucun changement réseau/serveur/TCP, aucune nouvelle fonctionnalité, pas de modification des autres avertissements connus `NU1701`, des alertes C# nullability, ni de la version produit.
