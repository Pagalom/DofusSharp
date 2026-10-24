# P3d.4 — Progression réelle des métiers (BestCrush)

## Objectif

Afficher dans les fiches de session les barres **XP du niveau courant**
à partir du couple niveau + XP cumulée du métier reçu dans le `ipz`
de la fenêtre Dofus liée à un bail réseau Game valide.

Aucun enrichissement de l'identité de compte, aucune persistance SQLite,
aucune consultation réseau supplémentaire et aucun nouveau mode de capture.

## Barème de référence Dofus 3

Pour un métier au niveau `N` (`1 <= N < 200`) :

- XP cumulée au début du niveau : `10 * N * (N - 1)`
- XP cumulée au début du niveau suivant : `10 * N * (N + 1)`
- XP pour franchir le niveau : `20 * N`
- XP gagnée dans le niveau : `xpCumulée - débutNiveau`
- Barre : `100 * xpGagnée / xpPourFranchir`, arrondie à deux décimales.

Référence de contrôle : courbe des métiers DOFUS 3.7 (notamment
https://smart-calculators.net/fr-FR/tools/dofus-xp-calculator),
avec vérification indépendante des observations niveau/XP déjà relevées.
Les barèmes des anciens jeux Dofus / Dofus Retro ne sont **pas** utilisés.

Exemples anonymisés issus des observations de test utilisateur :

| Niveau | XP totale observée | Début niveau | Début suivant | XP dans niveau | Progression |
|---:|---:|---:|---:|---:|---:|
| 3 | 76 | 60 | 120 | 16 / 60 | 26,67 % |
| 8 | 708 | 560 | 720 | 148 / 160 | 92,5 % |
| 69 | 47 043 | 46 920 | 48 300 | 123 / 1 380 | 8,91 % |
| 195 | 379 581 | 378 300 | 382 200 | 1 281 / 3 900 | 32,85 % |

## Implémentation

- `ProfessionLevelExperienceReference.TryGetBoundaries` : projection pure,
  bornée aux niveaux 1..199, sans état et sans dépendance réseau.
- `CurrentWindowProfessionsState.WithExperienceBoundaries` : ajoute
  les deux seuils aux données **strictement temporaires** d'une fenêtre,
  après la validation déjà existante du bail réseau Game et des IDs métier.
- `ProfessionProgressPresentation.For` : calcule uniquement à partir des
  deux seuils fournis, sans arrondi approximatif des XP brutes.
- `CharacterSheetCard.razor` : montre barre + `XP gagnées / requises`
  + pourcentage lisible.

Au niveau 200, la carte montre **Niveau maximal** comme avant.

En cas de niveau ou XP incohérents, aucune barre chiffrée n'est inventée.
La carte affiche « Progression non déterminée » et la valeur brute
de l'XP cumulée lorsqu'elle est non nulle.

Les mises à jour mono-`ipz` reconstruisent les seuils selon le **nouveau**
niveau ; aucune limite de niveau obsolète n'est conservée après un passage
de niveau. Les deux fenêtres avec des pseudonymes identiques restent
indépendantes ; aucun état partagé par nom de personnage.

## Tests

- Calcul des seuils des 199 niveaux + repères 3/8/69/195.
- Rejet des couples niveau/XP incohérents, des niveaux hors bornes,
  et du niveau 200 (géré par la carte).
- Tests réels du code d'état à l'aide de paquets synthétiques :
  deux fenêtres, mise à jour mono-`ipz`, passage de niveau,
  et non-propagation d'une fenêtre à l'autre.
- Aucun changement des tests de décodage/protocole.

Depuis Windows :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0 -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

L'évolution future du barème doit être vérifiée à chaque version pertinente
de Dofus. Une observation incompatible conduit à masquer la barre plutôt
qu'à afficher un pourcentage faux.
