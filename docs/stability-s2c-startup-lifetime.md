# Stabilisation S2c — durée de vie du DbContext au démarrage

## Constat (Windows, 2026-10-10)

Le lancement sous `77c49e8` est finalement débloqué après suppression de la table SQLite orpheline `__EFMigrationsLock` et redémarrage.

Les logs du lancement précédent (`ee90cf2`) montraient :
- `System.ObjectDisposedException: BestCrushDbContext` dans `GameDataUpgradeHandler.UpgradeAsync` ;
- `InvalidOperationException: This SqliteTransaction has completed` dans `Database.MigrateAsync` ;
- des initialisations rapprochées, laissant soupçonner deux exécutions concurrentes.

Ce sont des indices, non une preuve absolue de la cause initiale du verrou orphelin.

## Correction

- `Splash.razor` ne lance plus le démarrage dans `Task.Run` non attendu : il reste attaché à `OnInitializedAsync`.
- Le nouveau singleton `StartupInitializationService` crée et conserve un **scope DI indépendant du composant** pendant toute la migration et la mise à jour des données de jeu. Les deux handlers et le `DbContext` restent actifs pendant les opérations asynchrones.
- Un `SemaphoreSlim` sérialise les lancements concurrents ; après succès, un nouvel écran de démarrage ne réexécute pas l'initialisation dans le même processus. En cas d'échec, la tentative ultérieure peut réessayer avec un nouveau scope.
- Le callback de progression n'effectue plus de `.GetAwaiter().GetResult()` bloquant.
- Un composant Splash déjà détruit ne tente ni navigation ni mise à jour de son interface une fois son initialisation terminée.

Aucune modification des migrations EF, du schéma SQLite, des requêtes réseau métier, des calculs ou de la version produit. Le verrou `__EFMigrationsLock` n'est **jamais supprimé automatiquement** : il peut correspondre à une migration active.

## Tests ajoutés

- Deux initialisations concurrentes ne doivent exécuter qu'une seule opération, avec son scope valide jusqu'au terme du travail.
- Une initialisation en échec doit libérer son scope et permettre une tentative suivante, sans compter le premier échec comme succès.

## Validation Windows requise

```powershell
git switch refactor/codebase-cleanup
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Attendu : **235 tests**, 0 échec. Contrôler ensuite deux démarrages consécutifs (F5, arrêt propre, F5) avec la base **sauvegardée**. Vérifier que le Splash progresse et qu'aucune erreur `ObjectDisposedException`, `SqliteTransaction has completed` ni attente anormale de migration n'apparaît dans `%LOCALAPPDATA%\BestCrush\Logs`.

En cas d'attente prolongée, fermer proprement l'app, recueillir les logs et vérifier le verrou **en lecture seule**. Ne jamais supprimer une table de verrou lorsque BestCrush est en cours d'exécution.
