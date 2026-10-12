# P1e — Verification du protocole des metiers (ipz)

## Etat des connaissances

Client Dofus 3.6.11.15 ; serveur vers client ; opcode `ipz`.
La capture P1d.1 du 12 octobre 2026 montre 20 entrees et six
correspondances exactes niveau / experience totale cumulee.

Les champs `f1.f1` et `f1.f2` correspondent aux niveaux
et a l'experience cumulee de six metiers verifies.
Le champ `f1.f5` est present dans les 20 entrees avec 20 valeurs
differentes : **candidat** pour un identifiant metier global.
Ce n'est pas une preuve d'identite. Il faut une seconde source
independante pour fixer un ID global de maniere permanente.

Le champ `f1.f3`, present 14 fois, pourrait designer le seuil
cumulatif du niveau actuel. Le champ `f1.f6`, present 20 fois
avec 14 valeurs differentes, pourrait designer le seuil du
niveau suivant. Ces hypotheses sont testees, et non considerees
comme acquises. Enfin, `f1.f4` peut exister sous une autre
forme protobuf que varint : P1e inspecte tous les types.

**Precision** : il y a six metiers de niveau 1 dans les captures
d'interface, dont cinq ont une XP cumulee nulle. Cela explique
les cinq absences de `f2` signalees par P1d.1.

## Execution

Sur la branche `feature/modular-workspace`, sous Windows :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-study
```

Le programme demande 2 a 20 references de format
`Metier:Niveau:XPtotale:XPdansNiveau:XPnecessaire`, separees
par des virgules. Entrer le nom du metier et les quatre nombres
dans l'invite du programme. La ligne de commande ne contient
aucune reference personnelle.

Exemple fictif :

```text
Alchimiste:82:30000:150:1640,Tailleur:63:19000:245:1260
```

La capture doit commencer AVANT d'entrer le personnage en jeu,
et s'arreter apres reception d'`ipz`. Toutes les references
sont en memoire uniquement, jamais en base ni en fichier.

## Sortie attendue

Les compteurs retournent :

- `rows` : nombre de lignes ipz parsees.
- `pairs` : nombre de couples niveau/XP cumulée uniques retrouves.
- `ambiguous` : references absentes ou non uniques dans ipz.
- `f3-lower` : nombre de correspondances avec
  `XPtotale - XPdansNiveau`. L'absence de f3 ne compte comme
  zero que si le seuil attendu vaut zero.
- `f6-upper` : correspondances avec
  `XPtotale - XPdansNiveau + XPnecessaire`.
- `f4` : compte des lignes contenant un champ f4, puis compte
  de toutes ses formes (`v`, `len`, `32`, `64`).
- `f5` : presence et nombre de valeurs distinctes.
- `candidate-links` : couples uniques ayant aussi les deux
  seuils f3/f6 valides ET un f5 unique dans les 20 lignes.

Le diagnostic ne montre par defaut aucun f5 brut, aucune XP,
aucun niveau individuel, aucun nom de personnage et aucune IP.

Avec `--show-candidate-ids`, **a la demande explicite de
l'operateur**, le programme affiche seulement le nom de metier
generique fourni dans l'invite et la valeur f5 associee pour
les entrees qui ont deja passe les validations precedentes.
Ces identifiants sont toujours `mapping=unverified`.
Il peut s'agir d'autre chose que d'identifiants metier : ne pas
les considerer publics ou stables avant verification.

## Table permanente et politique de validation

`BestCrush.Network/Protocol/ipz-profession-mapping.3.6.11.15.json`
est la table de correspondance **versionnee** : elle contient
les 20 noms de metiers et les chemins de champs. Tous les IDs
reseau y sont initialement `null`, avec statut `unverified`.

Avant de remplacer un `null` par un vrai ID, exiger :

1. La liaison univoque avec un metier connu et deux seuils f3/f6
   concordants dans une capture d'un personnage.
2. La stabilite de ce candidat sur au moins deux connexions
   independantes (pour un meme personnage).
3. Un recoupement de la signification globale de l'ID avec
   une reference de donnees de metiers ou un autre personnage.
4. Aucune collision avec les 19 autres metiers du mapping.

L'unique personnage actuellement disponible ne suffit PAS a
etablir seul une table complete de 20 IDs globaux. Des metiers
de niveau 1 a XP nulle sont indiscernables par niveau/XP seul.

**Ne jamais versionner les XP, identifiants de personnage,
inventaires, comptes, trames brutes, logs avec valeurs ou
identifiants de connexion.** La table versionnee ne doit
contenir que des constantes de protocole validees.
Aucune persistance du profil de personnage ne doit etre ajoutee
a BestCrush. Cette phase n'integre pas le decodage a l'UI,
ni a la detection de personnage.

## Verification Windows

Les tests synthetiques couvrent les seuils, les types de f4,
les references ambigues, l'absence de fuite des identifiants
sans option, le filtrage S vers C et la separation des
sessions TCP. La compilation et l'observation reelle sur
Windows sont les prochaines etapes, pas encore validees.
