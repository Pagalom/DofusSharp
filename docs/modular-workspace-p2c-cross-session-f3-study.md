# P2c — Contrôle exploratoire de stabilité de f3 entre connexions

## Preuve qui motive ce test

Avec Dofus 3.6.11.15, une capture réelle a confirmé :

- `ksb` : nom et niveau correspondants, parent `f1.f1.f3` présent ;
- `ksc` : nom et niveau correspondants, parent `f2.f3` présent ;
- les deux familles ont livré la **même valeur parent f3 dans une
  seule session TCP** (`same-session=yes shared-equal-parent=[f3]`).

Cette égalité ne permet PAS de distinguer une clé de personnage,
une clé de compte ou un autre identifiant partagé.

## Extension de --identity-fields

Le mode `--identity-fields` continue de demander uniquement en RAM
`NomDuPersonnage:Niveau`, de manière exclusive aux autres modes.

Pour chaque nouvelle session TCP possédant un `ksb` avec
**une correspondance exacte et unique** nom+niveau, il étudie
la varint `f1.f1.f3` sans jamais la publier, la hacher ni
l'écrire sur disque.

Il annonce dans le terminal :

- `cross-session-f3=baseline` : première référence reconnue ;
- `cross-session-f3=equal` : la valeur est égale à **toutes**
  les références `ksb` précédentes conservées dans cette
  exécution, sur d'autres sessions TCP ;
- `cross-session-f3=different` : au moins une référence diffère ;
- `cross-session-f3=unavailable` : aucun candidat positif unique ;
- `cross-session-f3=repeat` : nouvelle observation identique
  dans la même session TCP.

`reference-sessions=N` ne compte que les sessions TCP ayant
émis une référence `ksb` exploitable. `ksc`, qui peut être
une **liste de personnages sélectionnables**, ne constitue
pas à lui seul une référence intersession. Les données sont
conservées uniquement en mémoire, pour **au plus 16 sessions
récentes** au cours de ce lancement du probe. Les duplications
ne sont pas recomptees. Une contradiction dans un même type
de message sur la même session supprime cette référence et
bloque la session jusqu'à une nouvelle connexion.

Une égalité entre plusieurs sessions **n'est pas une preuve
d'identifiant de personnage** : si le même compte changeait
de personnage, un identifiant de compte pourrait rester égal.
Nous ne considérons pas deux flux TCP simultanés comme un
changement de personnage prouvé. Aucun décodeur de production,
profil de personnage ou persistance n'est activé.

## Test Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

Lancer la capture avant de connecter Dofus, saisir localement
le nom et niveau du personnage, puis :
1. Connexion du personnage ; attente de quelques secondes.
2. Retour à la sélection puis reconnexion de ce même personnage.
3. Répéter éventuellement une fois, sans changer de personnage.
4. Arrêter avec Ctrl+C.

Partager seulement les lignes `IDENTITY_FIELDS=[...]`,
en supprimant la ligne de saisie contenant le nom.

## Critères de poursuite

Même avec `cross-session-f3=equal` observé, il restera à
valider le type sémantique du champ, le rôle distinct de
`ksb` et `ksc`, et des contrôles d'isolation, de
déconnexion et de sessions multiples. Aucun `ipz`
ne sera rattaché automatiquement avant une attribution
confirmée par deux **familles réellement indépendantes**.
