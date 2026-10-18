# P2g — Recherche anonyme du champ de classe

## Checkpoint de départ

La capture réelle P2f sur le client Dofus 3.6.11.15 a donné :

- `ksc` sur session TCP #3 : `p2f=decoded candidates=2
  class=unknown candidate-link=pending identity=unverified` ;
- `ksb` sur cette même session : `p2f=decoded candidates=1
  class=unknown candidate-link=yes identity=unverified` ;
- `ksc` ultérieur sur une autre session : liste des deux
  candidats décodée mais sans `ksb` validant le chargement ;
- le `ksb` d'un autre personnage ne correspondait pas à la
  référence saisie, comme attendu.

**Attention :** cette capture atteste du succès de la compilation
de NetworkProbe, mais aucun nouveau relevé de
`dotnet test` postérieur à P2f n'a été fourni.
Dernière validation globale chiffrée : 579 tests sur 579
réussis, **avant** l'ajout des tests P2f/P2g.

## Objectif

Localiser où la classe de personnage pourrait être codée.
Le champ de classe n'a PAS encore été observé ni identifié.
Ne pas présumer que l'une des valeurs numériques déduites
de deux personnages différents correspond nécessairement
à une classe : avatar, apparence, serveur et paramètres divers
peuvent eux aussi varier.

## Implantation expérimentale

`CharacterClassWireShapeStudy.Describe` explore seulement
`ksb` et `ksc` lorsqu'ils sont déjà décodables par P2f,
et uniquement pour une correspondance unique du nom et du
niveau saisis localement.

Le relevé `p2g` ajoute aux résultats existants
`IDENTITY_FIELDS` :

- `leaf=[...]` : **numéros de champs, types protobuf et
  répétitions** du sous-message contenant nom/niveau ;
- `parent=[...]` : mêmes métadonnées pour le parent
  contenant l'ID candidat et le sous-message ;
- `between-characters-diff=[...]` : chemins des champs
  scalaires ou blobs **dont les contenus diffèrent** entre
  la ligne sélectionnée et les autres entrées d'un
  `ksc` du même message. Jamais leurs valeurs ;
- `class=unknown` : l'étude ne sait pas identifier la classe.

Les champs connus `leaf.f3` (niveau),
`leaf.f6` (nom), `parent.f3` (identifiant candidat)
et `parent.f4` (sous-message) sont exclus de la comparaison,
afin de ne pas présenter ces différences déjà expliquées
comme d'hypothétiques champs de classe.

Aucun identifiant, nom, niveau, valeur de classe,
payload ni empreinte cryptographique n'est imprimé ou
persisté. Le traitement est borné par le décodage P2f
à des messages et listes de taille limitée, avec
comparaison seulement au sein du message `ksc`.

La correspondance `candidate-link=yes` **reste**
`identity=unverified`. Aucun métier `ipz` n'est
attribué et aucun module métier BestCrush n'est modifié.

## Checkpoint Windows demandé

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

Saisir un nom/niveau de personnage seulement dans le
terminal, connecter un personnage puis revenir à la
sélection et reconnecter le personnage correspondant à
la référence. Le `ksc` doit inclure les personnages
de classes différentes. Partager uniquement les lignes
`IDENTITY_FIELDS`, jamais la ligne de saisie ni
les captures réseau brutes.

## Suite

Exploiter la comparaison de forme pour sélectionner
un petit nombre de chemins candidats. La classe ne pourra
être décodée qu'après validation du sens de ces champs,
avec des références de classe connues, des fixtures
synthétiques et des observations contradictoires
(comme deux personnages de même classe, lorsque disponibles).
Ne pas faire dépendre les fonctionnalités de production
d'une classe devinée à partir de deux fiches.
