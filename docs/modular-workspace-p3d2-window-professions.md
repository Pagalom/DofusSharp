# P3d.2 — Affichage provisoire des métiers sur la fenêtre Game

## Constat réel du 24 octobre 2026

Sur Tylezia, la nouvelle recette P3d.1 a confirmé :
- `ipz shape=roster20 link=verified-game slot=1` au chargement ;
- `ipz shape=other link=verified-game slot=1` après une fabrication.

Le même bail Game TCP/fenêtre/serveur est donc disponible pour une lecture
**locale et provisoire** des métiers, sans identité Ankama.

## Implémentation

- `CurrentWindowProfessionsState` est enregistré comme singleton temporaire.
- Seuls les paquets serveur → client `ipz` portant un bail Game actif
  correspondant **exactement** à la fenêtre, l'époque et la connexion sont acceptés.
  Un nom de personnage, le focus ou un filtre SQL ne suffit jamais.
- Roster initial : exactement 20 lignes `ipz` décodables. Les identifiants
  candidats `f1.f5` doivent être uniques et constituer **exactement**
  l'ensemble de 20 IDs déjà étudié en P3g.1, puis posséder un nom dans le
  référentiel public `ProfessionPublicJobIdReference`.
- Ce référentiel associe des IDs à des **noms publics** ; seules certaines
  associations ont été confrontées manuellement dans les études passées.
  L'écran présente ces noms comme **provisoires**.
- Niveau et XP cumulative proviennent de `f1.f1` et `f1.f2`, caractérisés
  précédemment ; on **n'invente pas** de seuil de progression.
- Mises à jour `ipz` mono-entrée : appliquées à l'ID `f1.f5` déjà présent
  dans le roster du **même bail**. Une mono-entrée ne crée jamais une fiche
  de métiers à elle seule. Les diminutions non justifiées sont ignorées.
- Une reconnexion produit un nouveau bail, ce qui masque le roster précédent
  jusqu'à une nouvelle observation. Une fenêtre fermée est purgée lors de
  l'inventaire Win32 périodique.
- Sur la fiche : « Métiers observés sur cette fenêtre Game — noms du
  référentiel public, compte non vérifié. »
- Aucun stockage SQLite ni modification du système de profils de compte.
  Prix, coefficients, capture et filtres SQL inchangés.

## Validation Windows requise

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Pour capturer `ipz` dès le chargement, démarrer BestCrush **avant**
les fenêtres Dofus. Charger Tylezia, ouvrir BestCrush → Personnages, vérifier
que la fiche affiche les noms, niveaux et XP cumulative observés.
Après une fabrication, vérifier que le niveau et/ou l'XP concerné évolue.
Éventuellement connecter Kourial pour contrôler l'isolement des deux
fenêtres (même nom de personnage possible).

Si aucun métier n'apparaît, la première chose à vérifier est la
correspondance exacte des 20 IDs P3g.1 (version du jeu) et la réception
du roster dans la session courante, **sans** inventer de mapping de secours.

## État

Sources et tests ajoutés à la branche `feature/modular-workspace`.
Pas de compilation locale Windows ni de validation fonctionnelle à ce stade.
