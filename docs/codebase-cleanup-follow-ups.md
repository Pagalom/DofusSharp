# Suivis séparés du refactoring BestCrush

## Dépendance SQLite native — avertissement de vulnérabilité élevée

- Signalement utilisateur reçu le 3 octobre 2026, après le checkpoint Windows A1
  (`61bcada`, SDK .NET 10.0.400, build Windows réussi et 24 tests réussis).
- Paquet signalé : `SQLitePCLRaw.lib.e_sqlite3` **2.1.10**.
- Gravité signalée par l'avertissement : **élevée**.
- Statut : **à analyser dans un lot de sécurité distinct** ; aucune dépendance
  mise à jour et aucun avertissement masqué dans le lot A2.

Le journal complet et l'identifiant de l'avis de sécurité n'ont pas été fournis.
La version transitive résolue et l'avis exact restent à relever sur le poste
Windows. La restauration et l'audit NuGet n'ont pas pu être exécutés dans
l'environnement de préparation A2, qui ne dispose pas du SDK .NET.

Le dépôt référence `Microsoft.EntityFrameworkCore.Sqlite` 9.0.8 dans
`BestCrush/BestCrush.csproj`, `BestCrush.Domain/BestCrush.Domain.csproj` et
`BestCrush.Migrations/BestCrush.Migrations.csproj`, ainsi que
`Microsoft.Data.Sqlite.Core` 9.0.8 dans `Tests.BestCrush/Tests.BestCrush.csproj`.

Pour le lot ultérieur : relever le message NuGet complet, identifier la chaîne de
dépendances et l'avis de sécurité, puis choisir une mise à jour compatible.
Valider séparément le chargement de SQLite natif sous Windows, les migrations
sur une copie de base existante, les lectures/écritures et les tests BestCrush.
