# P3f — Identification du métier à partir de ipz mono-entrée

## Motivation

P3e.5 a validé expérimentalement, sur deux
séquences réelles (dont un passage niveau 5 → 6),
`ipz.f1.f1` comme **niveau** et `ipz.f1.f2`
comme **XP affichée**. Il reste à distinguer
**quel métier** fait l'objet d'une progression
dans les messages `ipz` mono-entrée.

L'opérateur peut fabriquer des objets donnant
de l'XP en **Sculpteur**, **Bricoleur**,
**Cordonnier**, **Forgemage**, **Sculptemage**.
Le premier contrôle P3f portera sur
**Sculpteur et Bricoleur** ; les trois autres
servent de contrôles additionnels si les
premiers résultats sont ambigus.

## Implémentation

Mode exclusivement opt-in, isolé :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-job-study
```

Le mode partage **le collecteur P3e en RAM**
pour les petits `ipz` mono-entrée
**serveur → client**, première session TCP
valide uniquement, jusqu'à **16** messages.
Les `ipz` de vingt entrées restent ignorés.
Aucune valeur numérique réseau, ID, payload,
hash ou nom de personnage n'est exporté.

L'interface affiche seulement
`PROFESSION_JOB=[p3e=mono-ipz accepted=yes index=N ...]`.
Après l'arrêt par `Ctrl+C`, saisir les
références **fournies manuellement**
`index:Métier:Niveau:XP` sur les indices
associés aux fabrications :

```text
1:Sculpteur:6:390,2:Bricoleur:12:875,3:Sculpteur:6:420,4:Bricoleur:12:900
```

**Exemple fictif** : ne pas utiliser ces
valeurs comme observations réelles.

Les références sont validées en local :
- deux métiers distincts au minimum ;
- deux observations **par métier**, au minimum ;
- indices ordonnés, uniques, ≤16 ;
- métiers parmi la taxonomie publique ;
- XP strictement croissante **au sein du
  même métier**, niveau non décroissant ;
- aucun compte ni nom de personnage demandé.

Après validation des champs niveau (`f1.f1`)
et XP (`f1.f2`) sur **toutes** les références,
l'algorithme cherche les champs numériques
`f1.f3` et suivants qui :

1. existent dans toutes les observations ;
2. ont **exactement la même valeur** pour
   les deux observations d'un même métier ;
3. présentent des valeurs **distinctes**
   entre les métiers.

Il affiche seulement les **chemins** candidats
(`candidate-paths=[f1.f5]`, par exemple)
et le statut `f1.f5-stable-distinct=yes/no`.
Aucun contenu numérique du message n'est
imprimé, même en cas de match. Les noms
des métiers sont utilisés pour former
les groupes en RAM, sans être réimprimés
dans la sortie.

**Interprétation :** `f1.f5` a été
observé comme champ candidat de profession
dans les instantanés à 20 entrées ; la même
sémantique n'est **pas encore démontrée**
pour les `ipz` mono-entrée. Les autres
champs comme les seuils d'XP peuvent être
stables pendant des fabrications à niveau
constant et apparaître aussi comme candidats.
Un résultat à plusieurs chemins est donc
**ambigu**, pas une erreur.

## Protocole expérimental

1. Relever avant capture niveau et XP de
   **Sculpteur** et **Bricoleur**.
2. Lancer P3f sur le client Dofus
   déjà connecté (un seul client).
3. Réaliser une fabrication XP sur Sculpteur,
   puis Bricoleur, puis répéter sur chacun ;
   `Sculpt./Sculpt./Bricol./Bricol.` est
   également accepté si nécessaire.
4. Après chaque fabrication, relever
   niveau et XP obtenus et index
   `PROFESSION_JOB` du `ipz` correspondant.
5. Arrêter, saisir les **quatre**
   références en une ligne, puis partager
   uniquement les lignes anonymisées
   de synthèse du diagnostic.
6. Au besoin, répéter avec
   **Cordonnier**, **Forgemage** ou
   **Sculptemage** sur une nouvelle capture
   pour lever les ambiguïtés.

## Critères de validation et limites

- Vérifier des correspondances niveau/XP
  valides avant de parler d'identité métier.
- Vérifier `f1.f5` stable sur deux
  observations du même métier et distinct
  entre au moins deux métiers.
- Si plusieurs chemins sont candidats,
  demander des contrôles supplémentaires,
  de préférence avec un changement de
  niveau dans un métier.
- Ne **pas** affirmer que `f1.f5` représente
  un ID global ni relier des comptes à partir
  de l'égalité de `f1.f5`.
- Ne modifier ni `CurrentAccountProfessionsState`,
  ni la base, ni les cartes de personnages
  avant la validation P3c compte + serveur.
- Ne pas réactiver les tests d'isolation
  multi-comptes reportés par l'opérateur.

## Validation Windows à effectuer

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Tests de P3f synthétiques uniquement :
validation des références, variations d'XP
entre métiers, constance/distinction de
`f1.f5`, ambiguïté `f1.f3/f1.f6`,
rejet de faux contrôles, sécurité des
résultats et respect des sens TCP.

**Statut initial :** code du diagnostic créé.
L'exécution Windows réelle est documentée dans
le checkpoint P3f.1 ci-dessous ; le résultat
séparé de la suite `dotnet test` reste à obtenir.


## P3f.1 — Première comparaison réelle de deux métiers

L'opérateur a lancé avec succès le mode
`--profession-job-study` sous Windows, sur
une session TCP unique de Dofus 3.6.11.15.

Quatre réponses `ipz` mono-entrée ont été
observées à 10:10:48.564, 10:11:44.045,
10:12:15.334 et 10:12:49.680 UTC.

| Index | Métier de référence | Niveau | XP affichée | Taille `ipz` |
| --- | --- | ---: | ---: | ---: |
| 1 | Sculpteur | 6 | 390 | 15 octets |
| 2 | Bricoleur | 2 | 58 | 12 octets |
| 3 | Sculpteur | 7 | 434 | 15 octets |
| 4 | Bricoleur | 3 | 85 | 12 octets |

Les valeurs sont des références **manuellement
saisies** et comparées **uniquement en RAM**
aux champs numériques du réseau.

- Sculpteur : **+44 XP**, niveau **6 → 7**.
- Bricoleur : **+27 XP**, niveau **2 → 3**.
- Le contrôle niveau `f1.f1` est validé
  sur les quatre références.
- Le contrôle XP `f1.f2` est validé
  sur les quatre références.
- Seul `f1.f5` reste stable par métier
  tout en différant entre les deux métiers.

Résultat du probe (anonymisé) :

```text
p3f=compared references=4 professions=2 level-match=yes xp-match=yes candidate-paths=[f1.f5] f1.f5-stable-distinct=yes candidate-status=single-candidate account=unverified server=unverified mapping=unverified
```

**Conclusion :** l'hypothèse
`ipz.f1.f5 = identifiant de métier` est
désormais fortement étayée par une expérience
réelle à deux métiers, chacun avec changement
de niveau, au-delà des comparaisons de taille
et de l'XP déjà réalisées en P3e. Le probe
ne révèle pas les valeurs de `f1.f5` et
ne crée **aucune table de correspondance**
entre ces valeurs et les noms de métiers.
L'unicité d'un champ candidat sur quatre
observations ne constitue pas encore
une preuve d'identifiant global.

La même trace a été fournie deux fois par
l'opérateur ; la compter **une seule fois**,
pas comme une répétition indépendante.

### Suite expérimentale envisagée

Réaliser un contrôle avec un **troisième métier**
(Cordonnier, Forgemage ou Sculptemage) et
un métier témoin déjà étudié, à deux
fabrications par métier. Si `f1.f5` reste
le seul champ stable et discriminant malgré
des changements de niveau, confirmer
une nouvelle fois l'hypothèse. Ensuite
concevoir un mapping **local, explicitement
validé**, si nécessaire, sans attribution
automatique à un compte tant que P3c
ne fournit pas une identité vérifiée.

**Statut P3f.1 : validation réelle positive**
pour la discrimination Sculpteur/Bricoleur
par `f1.f5`. Compilation implicite
confirmée par le lancement réussi du probe ;
la suite Windows `dotnet test` n'a pas
été communiquée. `account=unverified`,
`server=unverified`, `mapping=unverified`.


## P3f.2 — Capture Sculpteur/Cordonnier sans comparaison (erreur de saisie)

Une session TCP réelle a produit **sept**
messages `ipz` mono-entrée, de 10:20:19.844
à 10:23:10.427 UTC :

| Index | Taille `ipz` | Référence manuelle envisagée |
| --- | ---: | --- |
| 1 | 15 octets | Sculpteur, niveau 7, XP 475 |
| 2 | 15 octets | Sculpteur, niveau 7, XP 516 |
| 3 | 12 octets | Cordonnier, niveau 3, XP 78 |
| 4 | 15 octets | Sculpteur, niveau 7, XP 557 |
| 5 | 15 octets | Sculpteur, niveau 8, XP 598 |
| 6 | 12 octets | Cordonnier, niveau 3, XP 114 |
| 7 | 14 octets | non vérifié : possible Cordonnier niveau 4, XP 150 |

Le probe a refusé la saisie locale, sans
procéder au comparateur P3f, car :

1. la deuxième référence contenait **deux
   séparateurs `:` successifs** (au lieu
   de quatre champs `index:Métier:Niveau:XP`) ;
2. la dernière utilisait **l'index 9**,
   alors que les événements capturés ne
   dépassaient pas **l'index 7**.

Il serait incorrect d'affirmer que l'index 7
correspond à Cordonnier niveau 4, XP 150,
sans confirmation de l'opérateur.
Les six premières références auraient pu
suffire à la comparaison, mais le probe
a quitté après la saisie invalide et sa
mémoire a été libérée.

**Statut de cette capture : seulement
collecte validée ; AUCUN résultat de
correspondance `f1.f5` obtenu.** Ne pas
comptabiliser P3f.2 comme preuve positive
ou négative sur la discrimination Cordonnier.

### Correctif de robustesse

Le CLI P3e/P3f redemande désormais la saisie
si elle est invalide. Il refuse également
les références dont l'index dépasse le nombre
de messages réellement capturés. Tant que le
processus reste actif, l'utilisateur peut
corriger les références en RAM **sans refaire
ses fabrications**. Une ligne vide termine
volontairement le diagnostic ; aucune saisie
n'est enregistrée ni recopiée.

Test synthétique ajouté pour le double
séparateur, l'index non capturé et la saisie
corrigée ; la suite Windows reste à lancer.
