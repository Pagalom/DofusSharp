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

## Checkpoint Windows validé — 18 octobre 2026

À la suite du correctif syntaxique du test P2j
(`97f044a`), Windows confirme :

- Suite complète `Tests.BestCrush` : **635 réussis, 0 échec,
  0 ignoré** (20,6 s d'exécution des tests).
- Build et restauration : **réussis avec 17 avertissements**,
  comprenant le NU1701 connu sur OpenCvSharp4.WpfExtensions,
  des avertissements Razor de nullabilité et trois
  avertissements CS8604 dans des modules réseau expérimentaux
  P2g/P2h/P2j.
- Un message `git fetch` de mise à jour de référence distante
  (`incorrect old value provided`) a été suivi par un
  `git merge --ff-only FETCH_HEAD` effectué avec succès :
  le correctif du test a été appliqué localement.

**Ce checkpoint confirme les tests synthétiques P2j,
pas encore le diagnostic P2j sur trames réelles.**
Les CS8604 sont une dette de nettoyage non bloquante,
à traiter séparément sans modifier le décodage observé.

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


## Observation réelle P2j — deux sessions distinctes

Le dernier test local `--identity-fields --expected-class-id 17`,
effectué avec une référence de personnage uniquement saisie en RAM,
montre que le décodeur de classe candidate reconnaît les deux
familles de messages sur le client 3.6.11.15 :

- **Session TCP #1**, `ksb` : `match=unique`,
  `p2i=reference-equal=yes`,
  `p2j=class-candidate=all class-link=pending class=unverified`.
- **Session TCP #3**, `ksc` : deux fiches distinctes,
  `p2i=reference-equal=yes peers-differ=yes`,
  `p2j=class-candidate=all class-link=pending class=unverified`.
- Aucune ligne `ksb` n'a été observée après le `ksc`
  sur la session TCP #3 avant l'arrêt du test.

**Conclusion :** la lecture typée de `leaf.f5.f3` est
effectivement exercée sur du trafic réel pour les deux
familles, et correspond ici au code public attendu de la
classe. La comparaison `class-link` reste `pending`
parce que les deux messages viennent de **connexions TCP
différentes**. C'est le comportement d'isolation souhaité,
non un défaut de décodage ni une contradiction.

**Contrôle suivant :** laisser le même probe actif,
revenir à l'écran de sélection pour recevoir `ksc`,
puis **charger le personnage correspondant dans cette
même connexion TCP**. Chercher `class-link=yes` sur
le `ksb` qui suit ; ne jamais comparer ces valeurs
au travers de deux sessions distinctes.

Aucune valeur de personnage, aucun identifiant, nom,
niveau, contenu de réseau ni référence privée n'est
ajouté au dépôt. `ClassId` reste `null` et
`IdentityConfirmed` reste `false`.


## Observation P2j suivante — fermeture TCP sans ksb correspondant

Un autre essai `--identity-fields --expected-class-id 17`
confirme à nouveau l'extraction réelle de la classe
candidate dans deux connexions successives :

- Session #1, `ksb` : `p2i reference-equal=yes`,
  `p2j class-candidate=all class-link=pending`.
- Session #3, `ksc` : `p2i reference-equal=yes
  peers-differ=yes`, `p2j class-candidate=all
  class-link=pending`.
- Session #3 fermée environ cinq secondes après le
  `ksc`, sans aucun `ksb` capturé sur cette
  même connexion.

Le résultat n'est pas une contradiction de classe :
il **manque une observation**, et le mécanisme
d'isolation TCP refuse correctement de rapprocher
les sessions #1 et #3.

**Question préalable à tout changement de code :**
le personnage a-t-il effectivement terminé son
chargement et atteint le jeu après le `ksc` de la
session #3 ? Si non, un `ksb` absent est normal.
Si oui, caractériser le parcours réseau/fermeture de
connexion avant de modifier les règles d'attribution.

Aucun identifiant ou nom privé, niveau, payload,
adresse, jeton ni donnée de compte n'est ajouté à
cette documentation.


## Complément déterminant — séquence complète même session TCP

Un relevé **métadonnées uniquement** (`--character-scan`),
commencé alors que le personnage était déjà en jeu, montre
la transition complète qui n'était pas visible dans les
précédentes captures arrêtées à l'écran de sélection :

- Session #1 : jeu actif, puis message `kmz` et fermeture ;
- Session #3 : `knc`, `kod`, `ksc` à **18:25:40.845 UTC** ;
- Session #3 : `kta` puis `ksb` à **18:26:01.322 UTC** ;
- Session #3 : `ipg`, `ipz` à **18:26:01.323 UTC**.

Soit environ **20,5 secondes** entre la sélection
(`ksc`) et le chargement (`ksb`) sur **la même
session TCP #3**. Cela concorde avec les actions
décrites par l'opérateur, incluant 20–30 secondes
d'attente après chaque changement.

**Correction de l'interprétation antérieure :**
l'absence de `ksb` sur les lignes précédemment
transmises ne démontrait aucun défaut réseau :
les relevés avaient été arrêtés avant le chargement
suivant. La capture complète montre le parcours
nominal. Aucun ajustement du suivi TCP n'est
justifié par cette observation.

**Ce que l'on peut conclure :** le protocole est
observé sur une même connexion avec `ksc`, `ksb`
et `ipz`. Cela ne prouve pas encore `class-link=yes`,
car ce relevé est en mode `--character-scan`,
sans le diagnostic `--identity-fields`. Cela
ne constitue pas non plus une deuxième source
sémantiquement indépendante d'identité.

**Vérification suivante :** lancer
`--identity-fields --expected-class-id 17` avant
de retourner à la sélection, puis attendre le
`ksc` et le `ksb` de la même connexion avant
d'arrêter ; rechercher `p2j class-link=yes`.
Conserver `ClassId=null`, `IdentityConfirmed=false`
et l'association automatique des métiers désactivée.


## Checkpoint réel P2j — même session TCP, concordance de classe

Après les tests synthétiques **635/635** et les deux
vérifications externes de classe (codes publics 6 et 17),
une capture `--identity-fields` sur Dofus 3.6.11.15
a enfin observé l'enchaînement complet sur la **session
TCP #3** :

- `20:09:39 UTC` — `ksc` : 2 fiches décodées,
  `p2f candidate-link=pending`,
  `p2j class-candidate=all class-link=pending` ;
- `20:09:46 UTC` — `ksb` : `match=unique`,
  `same-session=yes`, `p2f candidate-link=yes`,
  `p2j class-candidate=all class-link=yes class=unverified`.

La classe candidate correspond donc entre les
deux formes de fiche pour le **même identifiant,
nom et niveau candidats**, dans la **même connexion
TCP**, conformément aux règles d'isolation.
Cela valide l'observation de cohérence du champ
`leaf.f5.f3` sans confirmer l'identité en production.

L'option `--expected-class-id 17` désignait
la classe Huppermage alors que la référence
saisie localement correspondait à un personnage
Écaflip. L'option P2i a donc correctement produit
`reference-equal=no` dans `ksc` et `ksb`.
**Ce n'est pas une contradiction de décodage** :
le résultat P2j `class-link=yes` compare les
deux valeurs du réseau entre elles, pas avec la
classe attendue fournie en argument.

Aucune valeur privée (nom, niveau, identifiant,
serveur, compte, payload) n'est enregistrée
dans ce rapport.

### Décision de passage

P2j : **validation expérimentale réelle obtenue**.
La confirmation d'identité requiert encore une
preuve **sémantiquement indépendante** de
`ksc`/`ksb`. Aucune attribution automatique
des métiers `ipz` ni aucune écriture de profils
ne sont autorisées.

Suite : caractériser puis tester le cycle de vie
de la session (FIN/RST, erreurs de réassemblage,
conflits, alternance personnage A → B → A,
multi-client) avant tout branchement métier.
