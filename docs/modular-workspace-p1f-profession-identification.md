# P1f — Identification ciblée de métiers devenus distincts

## Motivation

La session P1e du client Dofus 3.6.11.15 confirmait
20 enregistrements `ipz`, dont cinq métiers de niveau 1 avec
une XP cumulée nulle. Les couples (niveau, XP totale) des métiers
ambiguës ont été rendus distincts par une progression en jeu.

Pas besoin d'ancienne trame, d'une différence avant/après, ni
des seuils de niveau `f3` / `f6` pour tenter le recoupement.
Cette variante P1f se contente d'une comparaison exacte de
`f1.f1` (niveau) et de `f1.f2` (XP cumulée) avec des références
entrées localement.

## Interface CLI

Sur Windows :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-identify --show-candidate-ids
```

Entrer 2 à 20 références :
`Métier:Niveau:XPtotale`, séparées par des virgules.
Par exemple, valeurs entièrement fictives :
`Cordomage:12:509,Façomage:11:468`.
Ne pas mettre les références réelles dans les arguments PowerShell
ni dans les fichiers de configuration.

Démarrer le diagnostic avant de connecter le personnage Dofus,
puis le connecter ; quitter après réception de l'opcode `ipz`.

## Règles de validation

Le décodeur P1d exige toujours les 20 lignes structurellement
correctes et refuse les mauvaises trames.

- Un métier doit avoir un seul enregistrement exactement égal
  sur niveau et XP cumulée.
- Deux références ne peuvent partager ni le même nom, ni
  le même couple (niveau, XP totale).
- Les 20 `f5` doivent être présents et distincts pour émettre
  des correspondances candidates.
- Il n'y a aucun rattachement automatique à un compte ou
  personnage, ni usage des candidats en production.
- `--show-candidate-ids` rend visibles seulement les
  associations `nom de métier / f5`, et **n'affiche jamais les XP**.
  Sans ce drapeau, seulement des nombres globaux sont affichés.

Un rapport normal ressemble à :

```text
IPZ_IDENTIFY=[rows=20 pairs=2/2 f5-unique=true candidate-links=2/2 mapping=unverified CANDIDATE_IDS=[Cordomage:f5=101;Façomage:f5=102]]
```

Les valeurs `101` et `102` sont fictives.

Les paires uniques constituent des *indices* sur la signification
de `f5`, pas une preuve définitive d'identifiant global.
Le mapping versionné
`BestCrush.Network/Protocol/ipz-profession-mapping.3.6.11.15.json`
ne sera modifié qu'après recoupement avec les valeurs réelles
du diagnostic. Aucun paramètre individuel (XP, personnages, etc.)
n'est persisté dans le mapping ou dans les tests.

## Validations à effectuer

Les tests Windows doivent vérifier parsing, paires répétées,
f5 manquants/dupliqués, confidentialité et séparation des
sessions TCP. Une reconnexion Dofus est nécessaire pour
observer les valeurs réelles des cinq métiers. Le commit ne
vaut pas validation Windows.

## Premier resultat reel et correspondances retenues

Une session du client 3.6.11.15 confirme :
- 20 lignes ipz avec 20 valeurs f5 distinctes.
- Quatre couples niveau/XP cumulee retrouves exactement une fois
  parmi cinq references metiers fournies.
- Correspondances metier -> f5 observees :
  Cordomage 62, Façomage 74, Forgemage 44, Joaillomage 63.
- Cordomage, Forgemage et Joaillomage concordent egalement
  avec la liste publique des IDs de metiers de 2020 :
  https://forum.cheat-gam3.com/threads/identifiant-ressources-et-m%C3%A9tier.204598/
- Façomage 74 reste seulement observe dans la session actuelle.
- Sculptemage : non retrouve de maniere unique parmi les cinq
  references. Le diagnostic P1f actuel ne differencie pas
  `absent` de `plusieurs lignes identiques`; aucune valeur
  f5 ne peut etre atribuee a ce metier avec cette observation.
- La liste publique ancienne propose Sculptemage 48 ; cela
  reste un `legacyProfessionIdHint` et NON un f5 observe
  pour Sculptemage sur le client actuel.

### Etape suivante

Pour distinguer un metier dont le couple niveau/XP pourrait
coincider avec un autre metier, faire progresser UNIQUEMENT
le metier ambigu pour obtenir un nouveau couple unique,
puis retenter P1f avec au moins deux references distinctes,
dont un metier deja reconnaissable. Eviter de faire progresser
les autres metiers dans le meme intervalle de verification.

Ne pas sauvegarder les XP du joueur dans les tests ou
dans `ipz-profession-mapping.3.6.11.15.json`.
L'identifiant global et le rattachement au personnage
restent non etablis.


## P1f — Apparente divergence levée par clarification

Une capture ultérieure (build 3.6.11.15) affichait
`rows=20 pairs=1/2 f5-unique=true`, avec
`Sculptemage:f5=62` dans la sortie. Cette association de
nom résultait de la **référence saisie incorrectement** :
l'utilisateur a confirmé avoir fait progresser **Cordomage**
et non Sculptemage.

Par conséquent :
- La corrélation réseau `f5=62` est cohérente avec Cordomage,
  déjà identifié lors d'une capture antérieure et recoupé
  avec une liste publique historique.
- Le signal `Sculptemage:f5=62` de la seconde capture ne
  constitue **pas** une identification de Sculptemage :
  la paire niveau/XP avait été attribuée au mauvais métier.
- Sculptemage reste **non identifié** dans le trafic actuel ;
  `48` est uniquement un indice historique externe.
- Il reste **10 métiers** associés à un f5 candidat observé
  dans la table, sans activation automatique de `professionId`.

Pour identifier réellement Sculptemage, vérifier son nom
avant de faire progresser exclusivement ce métier, puis
relever son niveau et son XP **totale**. Lancer P1f avec
Sculptemage et un deuxième métier de référence dont les
données n'ont pas changé. Une association unique permettra
alors de le lier à une valeur f5 sans confusion.

La table Git conserve cette clarification sans contenir
les XP personnelles ni les noms de personnages.

## P1f — Corrélation Sculptemage confirmée dans le trafic

Une nouvelle capture du client 3.6.11.15 a confirmé :
- 20 lignes ipz, `pairs=2/2`, `f5-unique=true`, `candidate-links=2/2`.
- Cordomage : `f1.f5=62`, conforme aux observations précédentes.
- Sculptemage : `f1.f5=48`, association unique et cohérente avec
  la liste publique historique des identifiants de métiers.

L'ancienne contradiction provenait d'une erreur d'étiquette du
métier monté en XP, explicitement clarifiée par l'utilisateur.

La table comporte désormais **11 identifiants candidats
observés sur 20 métiers**. Aucun ID n'est encore activé en
production : les champs `professionId` restent à null
tant que l'identité du personnage et la portée globale du
mapping ne sont pas validées. Les niveaux et XP personnelles
ne sont pas enregistrés dans le dépôt.
