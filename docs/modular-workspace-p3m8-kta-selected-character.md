# P3m.8 — Comparaison de `kta.f1` au personnage chargé `ksb/ksc.parent.f3`

## Pourquoi cette expérience ?

P3m.7 a été observé sur quatre connexions **du même compte** :
A-X1 → A-Y → B-Z → A-X2. Le champ numérique **C→S:kta:f1**
était :

- stable entre A-X1 et A-X2, **même lorsque l'IP change** ;
- **différent** entre X et Y sur le même serveur A ;
- différent entre X et Z sur deux serveurs distincts.

Cela contredit l'hypothèse selon laquelle `kta:f1` serait
uniquement une valeur constante propre au serveur logique
dans cette expérience. Son comportement ressemble à un
**identifiant de personnage ou une référence de sélection**.

Par ailleurs, les essais P3m.3 avaient montré la stabilité
du candidat `parent.f3` de `ksc/ksb` pour un même personnage,
ainsi que des valeurs distinctes entre personnages du
même compte et serveur.

**Question P3m.8 : les deux valeurs candidates désignent-elles
numériquement le même personnage sélectionné ?**

## Diagnostic

Le mode expérimental `--kta-character-compare` relie, pour
**deux personnages distincts** d'un **même compte** et d'un
**même serveur** :

1. la valeur unique du champ racine `f1` d'un message
   **C→S `kta`** capturé avant `ksb` ;
2. le candidat `parent.f3` du message **S→C `ksb`**
   qui décrit le personnage chargé ;
3. la présence exacte du même ID candidat, nom et niveau
   dans la liste **S→C `ksc`** de la **même session de jeu**.

`kta` peut être observé sur une session TCP d'amorçage
distincte mais **dans la même phase de connexion**. Il faut
exactement une session de jeu `ksc` + `ksb` clôturée par phase.
Les valeurs provenant de phases différentes ne sont jamais
mélangées.

Le diagnostic dit pour chaque phase :

- `roster-linked=yes/no` : la fiche sélectionnée `ksb`
  correspond à exactement une entrée de `ksc` ;
- `kta-vs-ksb=equal/different/unavailable` ;
- nombre de sessions, messages `kta`, valeurs distinctes
  et statut de validité.

Le résumé `both-match=yes/no` compare les deux phases,
et `selected-f3-distinct=yes/no` vérifie que les deux
personnages sélectionnés ont des valeurs candidates distinctes.

**Une égalité numérique dans trois familles** est une
corroboration forte, non une preuve formelle de la
sémantique `CharacterId`. La capture actuelle utilise
le mapping de référence **3.6.11.15**, le build réel
n'étant pas vérifié.

## Manipulation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --kta-character-compare
```

Vérifier l'index avec `--list` si l'interface Realtek
a changé depuis le dernier essai.

1. Fermer totalement tous les clients Dofus.
2. **X** : connecter un personnage sur un serveur de ton
   choix, attendre le chargement, fermer complètement
   Dofus et appuyer sur **Entrée**.
3. **Y** : connecter un **autre personnage** du même
   compte et **du même serveur**, attendre le chargement,
   fermer Dofus et appuyer sur **Entrée**.
4. Recopier les lignes `p3m8=` ainsi que les éventuels
   échecs de compilation/tests.

Aucune saisie de nom ou de niveau n'est nécessaire.
Les valeurs `f1`, `f3`, les noms, les niveaux et les
payloads ne sont **ni affichés ni persistés**.
Les numéros de sessions locales et les comparaisons
`equal/different` sont uniquement diagnostiques.

## Limites et garde-fous

- Le résultat ne prouve pas l'identité du serveur logique ;
  celui-ci reste un **choix utilisateur autoritaire**
  dans BestCrush, sans validation réseau obligatoire.
- Max 20 sessions TCP et 12 messages `kta` par session,
  taille des messages de 8192 octets au maximum.
- Refus en cas de `kta.f1` absent, dupliqué, de
  type différent de varint, égal à zéro ou présentant
  plusieurs valeurs distinctes dans une phase.
- Refus en cas de `ksc` ou `ksb` non décodable,
  dupliqué ou discordant ; aucune attribution au seul nom.
- Refus si plusieurs sessions `ksc`+`ksb` sont
  candidates dans une même phase ou si aucune session
  de jeu n'est clôturée normalement.
- Les messages client→serveur restent strictement
  distincts des réponses serveur→client.
- Les snapshots `ksc/ksb` sont gardés en RAM uniquement
  le temps de cette exécution.
- Aucun raccordement à `CharacterIdentityCorrelator`,
  `CurrentCharacterState`, base de données, profils ou
  overlays en production.

## Après P3m.8

Si `both-match=yes` avec deux `selected-f3-distinct=yes`,
ce sera une corroboration que `kta.f1` référence la
sélection de personnage exprimée dans `ksb/ksc`.
La prochaine étude pertinente sera le routage fiable
de ces identités **par session TCP et par fenêtre Dofus**,
en particulier quand plusieurs clients sont ouverts
simultanément, sans persister de fiche après fermeture.
