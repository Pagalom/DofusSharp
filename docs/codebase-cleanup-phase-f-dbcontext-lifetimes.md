# Phase F — DI / DbContext lifetimes

## Référence

Base : `860d3d47a64597eaa288bef96ab34a708190efd3`,
branche `refactor/codebase-cleanup`.

Le checkpoint Windows E1 est validé : **224 tests réussis, 0 échec**.

## Problème traité

`RunesService` et `CrushService` sont enregistrés en singleton mais recevaient
directement `BestCrushDbContext`, qui est un service scoped.

Cela transformait de fait leur DbContext en contexte longue durée capturé par un
singleton. Ce modèle est également fragile face aux opérations parallèles de la
page serveur.

Changer simplement ces services en scoped n'est pas retenu : `Server.razor`
exécute en parallèle `ItemsService.GetEquipmentsAsync()` et
`RunesService.GetRunesByCharacteristicAsync()`. Deux services scoped partageant
le même DbContext rendraient alors ce `Task.WhenAll` dangereux.

## Correction

`ConfigureDatabase` utilise désormais
`AddDbContextFactory<BestCrushDbContext>`.

Cette registration conserve la résolution scoped de `BestCrushDbContext` pour
les services existants, tout en fournissant un
`IDbContextFactory<BestCrushDbContext>` singleton.

### RunesService

Le service reste singleton.

Chaque opération de lecture crée et dispose son propre DbContext via la factory.
Le `Task.WhenAll` de `Server.razor` ne partage donc pas le DbContext
d'`ItemsService`.

### CrushService

Le service reste singleton.

Chaque appel public de calcul crée un DbContext dédié et le partage uniquement
pendant ce calcul. Les requêtes de sélection de rune de base conservent leur ordre
et leurs règles existantes.

## Ce qui ne change pas

- aucun changement de formule de concassage ;
- aucune modification des règles Pa/Ra ;
- aucun changement de priorité des prix/coefficient ;
- aucun changement du `Task.WhenAll` de Server.razor ;
- aucun changement des services scoped existants ;
- aucun changement de schéma, migration ou fichier SQLite ;
- aucune modification réseau ;
- aucune Phase G/H.

## Tests

Deux caractérisations supplémentaires vérifient :

- deux opérations RunesService concurrentes demandent deux contextes factory
  distincts ;
- chaque calcul public CrushService demande son propre contexte factory.

`CrushServiceTest` utilise désormais la même frontière factory que la production.

Référence attendue après F : **226 tests réussis, 0 échec**.

## Checkpoint Windows

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
git status --short

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Arrêt après Phase F. Ne pas commencer G avant validation Windows.
