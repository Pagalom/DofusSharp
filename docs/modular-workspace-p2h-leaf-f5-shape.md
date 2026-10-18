# P2h — Structure imbriquée du champ leaf.f5 (3.6.11.15)

## Résultat P2g ayant motivé cette étape

Une capture réelle avec deux personnages distincts du même
serveur a fourni, trois fois de suite, dans `ksc` :

```text
p2g=observed leaf=[f3:v,f5:len,f6:len] parent=[f3:v,f4:len] between-characters-diff=[leaf.f5:len] class=unknown
```

Sur les sessions TCP #2 et #4, le décodage `ksb` et
`ksc` concordait, avec `candidate-link=yes`, et
la référence `ksb` du personnage était stable entre les
deux connexions. La session #6 ne contenait pas de
`ksb` correspondant avant la fin du relevé.

`leaf.f5` est le seul champ supplémentaire observé
dans les deux fiches et sa valeur diffère. **Ce résultat
ne prouve pas qu'il représente la classe** : il peut
décrire une apparence, des couleurs ou des options de
personnage. Son type protobuf `len` signifie seulement
qu'il contient des octets, pas nécessairement un message
protobuf imbriqué.

## Objectif du contrôle P2h

Étudier les **structures** possibles à l'intérieur de
`leaf.f5`, avec une profondeur et des ressources bornées,
sans afficher de données réelles.

`CharacterAppearanceWireStudy.Describe` :

1. utilise uniquement les messages `ksb`/`ksc`
   acceptés par le décodeur strict P2f ;
2. sélectionne la bonne fiche par le nom/niveau exact
   saisis **localement** et son identifiant candidat,
   sans afficher ces informations ;
3. lit seulement le champ `leaf.f5` et présente
   `f5-wire-shape=[...]` : chemins, types et répétitions
   de champs, si son contenu possède une forme protobuf
   décodable ;
4. pour `ksc`, compare en mémoire les entrées distinctes
   sur les champs internes, et affiche uniquement les
   **chemins** divergents `f5-other-diff=[...]` ;
5. si le contenu n'est pas décodable, répond
   `p2h=f5-opaque` ou `opaque-peer` et refuse de
   lui attribuer une structure inventée.

Les limites incluent 8192 octets par message, 16
personnages par liste `ksc`, 2048 octets par
`f5`, 256 champs par nœud protobuf, profondeur 3
et 40 chemins de sortie au maximum. Aucun nom, ID,
niveau, scalaire de classe, valeur binaire, base64,
empreinte ou fichier privé n'est émis ni persisté.

Toutes les réponses se terminent par `class=unknown`.
Même deux valeurs internes différentes sur deux
personnages de classes différentes ne suffisent pas
à valider un champ de classe.

## Validation demandée sur Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

Saisir la référence `NomDuPersonnage:Niveau` dans la
console locale, se connecter, retourner à la sélection
et se reconnecter. Partager uniquement les lignes
`IDENTITY_FIELDS=[...]`, sans les saisies privées.

Le mode CLI reste inchangé. La classe, la confirmation
de l'identité et l'attribution des métiers `ipz`
restent volontairement inactives dans BestCrush.

## Suite éventuelle

Comparer les chemins internes qui diffèrent avec
des références **connues** pour plusieurs classes,
puis vérifier stabilité face aux changements d'apparence.
Ne pas attribuer automatiquement une classe ni
valider le profil tant que le schéma et une preuve
indépendante d'identité ne sont pas démontrés.
