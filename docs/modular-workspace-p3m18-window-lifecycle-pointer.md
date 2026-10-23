# P3m.18 — Fenêtres Dofus : durée de vie et ciblage

## Checkpoint

P3m.17 : **935 tests réussis**, aucun échec, résultat Windows communiqué.
P3m.18 : **14 nouveaux tests** (4 pointeur, 3 équipement, 7 profils), compilation et tests Windows non encore exécutés.

## Changements

Le relevé des fenêtres Dofus déjà présent dans `MainLayout.razor` fonctionne toutes les secondes, même en l'absence de trafic TCP. Il invalide désormais les équipements temporaires des fenêtres absentes via `WindowScopedNetworkEquipmentState.RemoveMissingWindows`, en complément des liens TCP existants.

`CurrentCharacterState` peut associer une fiche à une fenêtre **uniquement après confirmation indépendante de l'identité du personnage et de la fenêtre**. Cette association est en RAM. Si la fenêtre disparaît, la fiche liée, sa sélection active et sa connexion sont supprimées. Une preuve de connexion tardive ne peut pas ressusciter cette fiche. Une fermeture de connexion TCP sans fermeture de fenêtre conserve la fiche temporaire.

Les fiches non liées à une fenêtre prouvée ne sont pas supprimées par déduction du nom, de l'onglet actif ou du serveur. Le décodage d'identité personnage **n'est pas encore relié au flux de production** : cet ajout prépare le cycle de vie, pas une identification automatique de personnage.

`DofusWindowService.GetDofusWindowAtScreenPoint` expose une résolution stricte par `WindowFromPoint` et `GetAncestor(GA_ROOT)`. La cible ne peut être retenue que si un handle top-level Win32 correspond à une seule fenêtre Dofus valide et que les coordonnées sont incluses. Pas de repli vers la première fenêtre ou celle au premier plan.

**La molette n'utilise pas encore ce nouveau ciblage.** F7, F8 et le traitement économique existant ne sont pas modifiés. Le serveur choisi manuellement demeure autoritaire.

Les profils personnages et les associations de fenêtres restent uniquement en RAM et disparaissent à la fermeture de BestCrush. Les historiques de marché et de coefficients restent des données durables distinctes.

## Validation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Résultat attendu : **949 tests** si tous les tests sont découverts et que la suite initiale reste inchangée. Aucune validation Windows n'est encore connue pour P3m.18.
