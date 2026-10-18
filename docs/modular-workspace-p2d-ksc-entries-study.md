# P2d — Comparaison anonyme des entrées répétées de ksc

## Constat motivant l'étude

P2a/P2b ont localisé, pour Dofus 3.6.11.15 :

- `ksb` : nom `f1.f1.f4.f6`, niveau `f1.f1.f4.f3`,
  candidat numérique `f1.f1.f3` ;
- `ksc` : nom `f2.f4.f6`, niveau `f2.f4.f3`,
  candidat numérique `f2.f3`.

Sur une session TCP, les deux candidats f3 sont égaux.
Sur deux connexions différentes du même personnage,
le candidat `ksb.f1.f1.f3` est également resté égal.

Cela ne tranche pas entre un identifiant de personnage, de
compte ou un autre identifiant stable.

Le résumé de structure P1b de `ksc` présentait deux champs
`f2` répétés. Ce fait **n'établit pas à lui seul** que les
deux lignes désignent deux personnages appartenant au même
compte : leur sens métier reste à vérifier.

## Diagnostic ajouté au mode existant --identity-fields

Lorsqu'un message `ksc` contient exactement une ligne
correspondant au nom et niveau saisis localement, inspecter
chaque entrée racine `f2` de façon bornée et anonyme :

1. Une entrée doit contenir une valeur numérique non nulle
   `f3` et exactement un sous-message `f4`.
2. Dans `f4` : exactement un champ nom `f6` non vide et
   un niveau `f3` plausible (1 à 200).
3. Comparer en mémoire les **octets** des noms entre lignes,
   et les valeurs numériques `f2.f3` entre lignes.
4. Ne publier que le nombre de lignes, le nombre de lignes
   complètes, ainsi que des booléens d'unicité. Ne jamais
   afficher de nom, ID, valeur, empreinte ou charge réseau.

Exemple **entièrement fictif** :
```text
ksc-rows=[total=2 complete=2 names-distinct=true f3-distinct=true selected-f3-unique=true semantic=unverified]
```

Si le diagnostic trouve une ligne incomprise, il affiche
`ksc-rows=[layout=unverified]` plutôt que de supposer
qu'elle représente un personnage.

**Interprétation prudente :** des noms distincts et des f3
distincts, dans un même `ksc`, renforceraient l'hypothèse
que f3 est propre à chaque entrée. Cela ne prouve pas
encore que les entrées sont les personnages du compte ou
que f3 est la clé globale du personnage. Si les f3 sont
identiques, ce résultat est compatible avec une valeur de
compte, mais ne suffit pas à la prouver.

## Validation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

Saisir `NomDuPersonnage:Niveau` seulement dans la console
locale, puis démarrer Dofus, se connecter, revenir à la
sélection et se reconnecter pour déclencher `ksc`.

Ne partager que les lignes `IDENTITY_FIELDS=[...]`.
Ne pas partager la ligne de saisie ni les trames brutes.

Les champs `professionId` de la table des métiers restent
désactivés. Aucune identité n'est confirmée et aucune fiche
BestCrush n'est alimentée. Aucun fichier personnel
n'est enregistré.
