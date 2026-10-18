# P3a — Étude de parcours ksc → ksb → ipz (connexion TCP)

## Situation

Sur le client Dofus `3.6.11.15`, la validation
expérimentale P2j a confirmé sur une même connexion
`ksc` suivi de `ksb`, ainsi qu'un candidat de classe
cohérent. Une capture metadata-only a aussi observé
`ksc → kta → ksb → ipg → ipz`.

Le mapping versionné `ipz-profession-mapping.3.6.11.15.json`
contient déjà **20 noms et IDs f5 candidats** issus
d'études précédentes. Les `professionId` confirmés
restent tous à `null`. L'association entre `ipz`
et une identité réelle reste non vérifiée.

## Objectif et fonctionnement

Ajouter un diagnostic **opt-in**, isolé de l'application
et de toutes les autres études :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-flow
```

Aucun nom, niveau, compte, serveur ou référence
locale n'est demandé. N'envoyer que les lignes
`PROFESSION_FLOW` et les numéros de session anonymisés.

- `ksc` : charge une liste expérimentale de candidats,
  annule tout personnage précédemment chargé sur cette
  connexion, même si le nouveau `ksc` est malformé ;
- `ksb` : remplace systématiquement le candidat
  précédemment chargé, même en cas d'échec de décodage ;
- `ipz` : vérifie la structure stricte des **20 lignes**
  et signale uniquement si les 20 `f5` sont présents,
  non nuls et uniques ;
- `selection-link=yes` : `ksb` apparaît une fois
  dans le `ksc` courant, égalité de l'ID candidat,
  du nom et du niveau ; `class-link=yes` ajoute
  une égalité du candidat de classe ;
- `selection-link=no` : présence de deux candidats
  non concordants sur la **même** connexion ;
- `selection-link=unavailable` : fiche chargée sans
  `ksc` préalable ;
- `selection-link=pending` : pas de `ksb` courant ;
- `owner=unverified mapping=unverified` **toujours** :
  l'ordre des paquets et l'égalité des fiches
  ne constituent pas une preuve d'identité indépendante.

Le diagnostic ne révèle jamais les valeurs `f5`,
les XP ou identifiants candidats. Pas d'écriture
dans la base, les fichiers de mapping, les profils
ou `CharacterIdentityCorrelator`.

### Cycle de vie et confidentialité

L'état P3a est limité à **une session TCP**.
Tout nouveau `ksc` révoque l'ancien chargement.
Tout `ksb`, même invalide, remplace l'ancien.
FIN/RST, limites de réassemblage, réutilisation
du tuple TCP ou éviction effacent cet état.
Une connexion indépendante n'hérite jamais d'une
fiche ou d'une association `ipz` précédente.

Les données de candidat ne sont détenues que
transitoirement en RAM : aucune sortie d'identité,
hash, payload ni session brute sur disque.

## Tests synthétiques

Le fichier `ProfessionFlowStudyTest.cs` couvre :

- parcours complet `ksc → ksb → ipz` ;
- `ipz` avant le chargement, ou `ksb` seul ;
- alternance A → B → A ;
- `ksb` erroné, classe contradictoire,
  identité absente de la sélection ;
- `ipz` malformé, `f5` absent ou dupliqué ;
- confidentialité des indicateurs ;
- isolement multi-TCP, fin de session et reprise SYN ;
- absence d'analyse des faux messages venant du client ;
- exclusivité du mode diagnostic.

**Aucune donnée réelle de joueur** n'est enregistrée
dans les tests ou la documentation.

## Checkpoint Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

La dernière suite **explicitement attestée avant P3a**
est `635 réussis, 0 échec, 0 ignoré` avec
17 avertissements. Le build P3a et ses nouveaux tests
doivent encore être validés sur Windows.

## Expérience réelle à conduire

1. Démarrer le probe en étant déjà connecté en jeu.
2. Retourner à la sélection des personnages et
   attendre `PROFESSION_FLOW=[p3a=ksc ...]`.
3. Charger le personnage A et attendre `ksb`,
   puis `ipz` **dans cette même session TCP**.
4. Quitter et charger un autre personnage B,
   puis répéter pour A.
5. Ne transmettre que `PROFESSION_FLOW`,
   sans aucune saisie ou trace contenant
   une donnée personnelle.

Objectif : vérifier l'absence d'héritage d'une
fiche A lors du chargement B et la réinitialisation
sur nouvelles connexions. **Ce n'est pas encore
une association automatique de métiers à un
personnage BestCrush.**

## Décision à ne pas anticiper

La confirmation des identifiants globaux `f5`,
et une preuve d'identité **sémantiquement indépendante**
des deux familles `ksc` et `ksb`, restent
nécessaires avant une attribution automatique
de métier ou une persistance.
