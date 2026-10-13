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
