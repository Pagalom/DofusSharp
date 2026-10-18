# P2j — Décodage typé du candidat de classe (recherche uniquement)

## Validation réelle P2i — Dofus 3.6.11.15

Deux études avec références de classe **connues à l'avance**
ont maintenant été réalisées sur **deux personnages distincts**,
sans jamais publier leurs IDs, noms ou niveaux dans le dépôt :

- **Classe Écaflip (code public 6)** : dans les messages `ksb`
  et `ksc`, `leaf.f5.f3` correspond au code attendu
  (`reference-equal=yes`). La liste `ksc` indique
  `peers-differ=yes`.
- **Classe Huppermage (code public 17)** : les messages `ksb`
  et `ksc` donnent également `reference-equal=yes`, avec
  `peers-differ=yes` dans la liste `ksc`.

Cela étaye fortement le fait que le sous-champ `f5.f3`
représente la classe. Ces deux observations ne valident
pas toutes les classes possibles ni l'indépendance de la
preuve d'identité dans le protocole. Il faut également
ne pas confondre `f5.f3` (classe candidate) avec
`leaf.f3` (niveau) ou `parent.f3` (ID candidat).

## Lot P2j

Le décodeur expérimental `CharacterWireCandidateDecoder`
propose désormais :

- `ClassIdCandidate` : entier nullable lu dans le
  sous-message `leaf.f5.f3`, exclusivement dans les
  structures déjà acceptées de `ksb` et `ksc` ;
- `ClassId` **reste toujours null**, pour éviter
  toute confusion entre la donnée décodée et une
  attribution de classe vérifiée en production ;
- `IdentityConfirmed` reste toujours false ;
- si `f5` ou `f5.f3` manque, se répète, a un mauvais
  type ou ne se trouve pas dans la plage 1–19,
  `ClassIdCandidate` vaut null. Le nom, l'ID candidat
  et le niveau restent soumis au décodeur strict P2f.

La comparaison des classes entre `ksb` et `ksc`
est autorisée uniquement après égalité de l'identifiant
candidat, du nom et du niveau, et **sur la même session TCP**
dans `CharacterCaptureFlowTracker`.

La sortie anonymisée du mode existant
`--identity-fields` est enrichie :

- `p2j=class-candidate=all class-link=pending class=unverified` :
  les entrées du message ont une valeur candidate de classe,
  mais les deux familles ne sont pas encore présentes ;
- `class-candidate=partial` ou `none` :
  au moins une classe candidate manquante ou toutes
  les classes candidates manquantes ;
- `class-link=yes` : même ID, nom, niveau et
  valeur candidate de classe dans les deux familles
  sur une seule session TCP ;
- `class-link=no` : les identités candidates concordent,
  mais les classes candidates diffèrent ;
- `class-link=unavailable` : impossible de comparer
  les classes candidates.

Aucune valeur réseau n'est affichée, hashée ou écrite sur
disque. Aucun profil de personnage n'est activé,
aucun métier `ipz` n'est attribué et aucune sélection
globale de session TCP n'est faite.

Le schéma versionné
`BestCrush.Network/Protocol/character-identity-candidate.3.6.11.15.json`
mentionne les chemins candidats de classe sans fournir de
valeurs privées. Il est réservé à la recherche.

## Checkpoint Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields --expected-class-id 6
```

Pour vérifier le second personnage, lancer un second probe
avec `--expected-class-id 17` et fournir uniquement sa
référence dans la console locale. Ne partager que les
lignes `IDENTITY_FIELDS`, jamais les saisies privées.
Le résultat recherché est `class-link=yes` lorsque
`ksc` et `ksb` sont capturés dans une **même**
connexion TCP. L'absence de `ksb` ne doit jamais être
interprétée comme un échec de décodage de `ksc`.

La dernière suite globale explicitement attestée par
l'utilisateur est de **579/579**, avant la création
des tests P2f/P2g/P2h/P2i/P2j. Le fait que les probes
aient démarré atteste de la compilation du probe et
des dépendances, pas du succès de la suite de tests.

## Bloqueurs pour la production

- Confirmer une source d'identité **sémantiquement indépendante**.
  `ksc` (liste) et `ksb` (chargement) peuvent être liés
  et ne constituent pas automatiquement deux preuves.
- Gérer les changements de serveur et personnage ainsi
  que les messages tardifs en propageant l'identité TCP
  et ses générations au routage métier.
- Protéger la mémoire et les traces de debug de toute
  donnée personnelle et rejeter les contradictions.
- Ne jamais rattacher `ipz`, les inventaires ou les
  kamas à une identité non confirmée.
