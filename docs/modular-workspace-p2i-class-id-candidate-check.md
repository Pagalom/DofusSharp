# P2i — Validation expérimentale du candidat de classe leaf.f5.f3

## Point de départ (Dofus 3.6.11.15)

Le diagnostic réel P2h de deux personnages distincts du même serveur
montre des sous-champs internes qui diffèrent :

```text
p2h=f5-wire-shape=[f1:len,f3:v,f6:len,...]
f5-other-diff=[f3:v,f6.f2:structure,f6.f3:len,f6.f5:len,f6.f6:len]
class=unknown
```

Les fiches `ksb` et `ksc` étaient également décodables dans P2f,
avec `candidate-link=yes` quand elles se trouvaient dans la même
connexion TCP. Le sous-champ `leaf.f5.f3` est **candidat** pour un
identifiant de classe mais pas encore prouvé : d'autres propriétés
comme les couleurs et l'apparence diffèrent aussi.

Des sources communautaires indépendantes donnent les identifiants de
classe **Écaflip = 6**, **Huppermage = 17** :
- https://doc.nezubot.com/reference/conditions.html
- https://doc.ankabot.dev/ankabot-pc/methodes/character/breed

Ces valeurs sont une référence extérieure à tester, non une
preuve que le champ observé porte une classe. Nous ne faisons pas
d'hypothèse de classe par défaut sur la base d'un nom ou niveau.

## P2i optionnel, sans changement du mode standard

Le nouveau commutateur ne fonctionne qu'avec `--identity-fields` :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields --expected-class-id 6
```

La console demande comme auparavant la référence
`NomDuPersonnage:Niveau` **uniquement en RAM**.
L'identifiant de classe attendu est un nombre public (1 à 19),
passé explicitement en argument, jamais deviné ou extrait d'un
nom de personnage.

Le nouveau `CharacterBreedHypothesisStudy` vérifie de manière
bornée que la valeur interne du sous-champ `leaf.f5.f3`
de la fiche correspondant exactement à la référence locale
est égale au code de classe attendu :

- `p2i=f5-f3-check reference-equal=yes peers-differ=yes class=unverified`
  signifie que la comparaison est positive sur la fiche choisie
  et que les autres entrées `ksc` ont toutes un `f5.f3` différent ;
- `reference-equal=no` signifie que l'hypothèse numérique
  **ne concorde pas** avec la classe attendue ;
- `peers-differ=no` signifie qu'une autre entrée de la liste
  partage ce même candidat de classe (ce qui peut être normal
  pour deux personnages de même classe) ;
- `p2i=unavailable` signifie que l'information n'a pas pu être
  établie de façon sûre ;
- `p2i=reference-not-unique` signifie qu'aucune fiche
  décodée unique n'est associée à la référence.

Ne jamais imprimer la valeur réseau ni le nom, le niveau, l'ID,
le hash, le payload ou les données sensibles. L'étude ne conserve
pas de données personnelles sur disque et n'active aucun
profil personnage ni attribution des métiers.

## Double vérification recommandée

Faire deux **lancements distincts** :

1. Avec la classe connue Écaflip, `--expected-class-id 6`,
   et saisir en privé le nom:niveau du personnage Écaflip.
2. Avec la classe connue Huppermage, `--expected-class-id 17`,
   et saisir en privé le nom:niveau du personnage Huppermage.

À chaque lancement, retourner à la sélection et connecter le
personnage correspondant à la référence saisie pour obtenir
`ksc` puis `ksb`. N'envoyer que les lignes
`IDENTITY_FIELDS`, jamais la ligne d'entrée ni la capture brute.

Le contrôle de référence **incorrecte** est couvert en fixture
synthétique pour vérifier que `reference-equal=no` est affiché
si la valeur ne correspond pas.

## Checkpoint Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Avant P2i, seul le build de NetworkProbe a été implicitement
exécuté par `dotnet run`; le dernier bilan de suite globale
explicitement reçu (**579/579 tests**) est antérieur aux
nouvelles fixtures P2f/P2g/P2h. Ne pas les considérer
validées tant que ce checkpoint n'est pas transmis.

## Critères d'interprétation

Même deux `reference-equal=yes` sur des classes distinctes
ne suffisent pas à démontrer que le protocole a une identité
confirmée ou que le champ a définitivement une sémantique
de classe ; il faut encore caractériser son comportement
sur un changement d'apparence et entre plusieurs personnages
de la même classe. `CharacterWireCandidate.ClassId` reste
`null`, `IdentityConfirmed` reste `false`, et aucune
attribution automatique de `ipz` à un personnage n'est activée.
