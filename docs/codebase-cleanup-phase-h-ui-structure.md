# Phase H — séparation de la logique des pages Razor

Base : `7eefebf` (Phase G validée sous Windows, 226/226 tests).

## Intervention

- `Server.razor` conserve à l'identique toute sa partie markup, la déclaration des injections, les états d'écran et l'orchestration de la recherche. La logique existante est répartie sans changement entre `Server.Presets.cs` (presets), `Server.Filters.cs` (filtres économiques), `Server.Results.cs` (tri, filtrage catalogue, adaptation des résultats) et `Server.State.cs` (modèle d'état mémorisé par serveur).
- `History.razor` conserve à l'identique toute sa partie markup, les champs d'état et les déclarations du composant. Les méthodes existantes sont réparties entre `History.Loading.cs` (chargement et filtres), `History.Analytics.cs` (construction et gestion des analyses), `History.Formatting.cs` (formatage des données).
- Les fichiers `.cs` sont des parties de la même classe Razor (`partial class Server` / `partial class History`). Aucune nouvelle DI, aucune navigation ni nouveau composant, aucun changement du rendu, aucun changement des expressions métier.

## Invariants

La sélection du serveur reste le contexte global actuel ; aucun nouvel onglet métier n'est introduit. Les événements, paramètres, formulaires, champs statiques de session, caches et cycle de vie des pages ne changent pas. Même politique pour F7/F8/F9, focus explicite, notifications réseau, sauvegarde des snapshots, filtres/sort et analyses.

## Contrôle de source

Les segments de code déplacés ont été gardés textuellement identiques ; les blocs Razor de rendu jusqu'à `@code` restent inchangés. Les méthodes sont déplacées au sein des **mêmes classes partielles**, pas copiées ni réécrites. Les segments restants conservent les mêmes champs et le même ordre de déclaration.

## Checkpoint Windows obligatoire

```powershell
git switch refactor/codebase-cleanup
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Attendu : build Windows réussi, 226/226 tests. Vérification manuelle conseillée : ouverture de Server et History, sélection serveur, recherche/tri/filtres et presets, onglets History et éditeur Analyses ; aucun changement attendu des sorties UI.

Le SDK .NET n'est pas disponible dans l'environnement de préparation ; cette étape reste à valider sur le poste Windows.
