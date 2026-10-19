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

**Statut :** code de diagnostic créé ;
compilation et validation sur trafic
réel **en attente du checkpoint Windows**.
