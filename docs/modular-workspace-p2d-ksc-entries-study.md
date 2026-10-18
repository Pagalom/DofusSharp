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


## Observation réelle P2d — deux entrées distinctes

Une capture du client 3.6.11.15, effectuée sur un seul
lancement de NetworkProbe, donne :

- Session TCP #1 : `ksb`, correspondance unique sur le
  nom et niveau de référence, `parent-numeric=[f3]`,
  `cross-session-f3=baseline`, une session enregistrée en RAM.
- Session TCP #3 : `ksc`, correspondance unique sur la
  référence, `parent-numeric=[f3]`, et :
  `ksc-rows=[total=2 complete=2 names-distinct=true
  f3-distinct=true selected-f3-unique=true
  semantic=unverified]`.
- Sur cette **même** session #3, `ksb` confirme
  `shared-equal-parent=[f3]` face à `ksc`.
- Entre les sessions TCP #1 et #3, le candidat
  `ksb.f1.f1.f3` reste égal
  (`cross-session-f3=equal reference-sessions=2`).

Interprétation : il existe, dans ce snapshot de `ksc`,
**deux entrées complètes avec noms distincts et f3 distincts** ;
la valeur de l'entrée sélectionnée par la référence locale
correspond à celle du `ksb` qui suit sur la même connexion.
Cette valeur a été stable sur deux chargements du même
personnage. Cela renforce l'hypothèse d'un identifiant
propre à chaque entrée.

**Limites non levées :**
- Le protocole métier des entrées `ksc` n'est pas encore
  établi (liste de personnages ? autre liste ?).
- Le caractère durable/global de f3 et son absence de
  collision sur d'autres comptes/serveurs ne sont pas validés.
- Deux opcodes différents ne garantissent pas deux sources
  de preuve indépendantes.
- Les tests synthétiques de sécurité et le checkpoint Windows
  complet ne sont pas attestés par ce seul journal.

**Prochaine validation souhaitée :** confirmer directement
combien de personnages sélectionnables sont visibles sur
l'écran de sélection Dofus. Si deux sont visibles,
corréler le nombre et les différences d'entrées sans
copier de nom ou d'ID dans le dépôt. Sinon étudier
les conditions d'émission et la sémantique de `ksc`
avant toute attribution automatique.

Aucun nom, niveau, identifiant de personnage ou XP réelle
n'est conservé dans cette documentation ou dans un mapping
de production. `professionId` reste à null.

## P2e — Contrôle croisé avec deux personnages du même serveur

Observation complémentaire : une capture de l'écran de sélection du
client présente exactement **deux personnages distincts** sur le
**même serveur**, appartenant à des classes et niveaux différents.
Ceci corrobore le nombre de lignes observé dans `ksc`, sans, à lui
seul, constituer une preuve du type du message.

Lors du test expérimental suivant, l'opérateur a fourni une référence
locale **correspondant au second personnage** (aucune valeur réelle
n'est conservée ici), puis a effectué une connexion au premier et un
changement vers le second :

- **Session TCP #1**, `ksb` : `match=absent`, conforme au fait que
  la référence locale ne désigne pas le premier personnage connecté.
- **Session TCP #3**, `ksc` : `match=unique`,
  `ksc-rows=[total=2 complete=2 names-distinct=true
  f3-distinct=true selected-f3-unique=true semantic=unverified]`.
- **Même session TCP #3**, `ksb` : `match=unique`,
  `same-session=yes shared-equal-parent=[f3]` : l'entrée visée
  dans `ksc` possède le même candidat f3 que le personnage chargé.
- **Session TCP #5**, `ksc` : `match=unique` ; aucun nouveau `ksb`
  n'a été fourni dans la trace, donc aucune conclusion sur une
  connexion effective du personnage à ce stade.

Le test antérieur sur le premier personnage avait montré la même
concordance `ksc`/`ksb`, ainsi que la stabilité de f3 après
reconnexion. Ces observations réelles sont donc compatibles avec
`ksc.f2.f3` / `ksb.f1.f1.f3` comme **identifiant individuel
stable du personnage**, et non une simple valeur commune au compte.

**Statut : candidat d'identifiant fortement étayé**, mais non branché
à BestCrush. Restent à valider : stabilité/versionnement du protocole,
caractérisation du champ de classe, signification indépendante des
sources `ksc` et `ksb`, association sûre au serveur/flux TCP,
remise à zéro sur déconnexion et tests de non-mélange entre clients.

La capture du diagnostic ne constitue pas une preuve d'exécution des
builds/tests Windows. Aucun nom, niveau réel, XP, identifiant, payload
ou donnée de session privée n'est versionné. Les métiers `ipz` ne
sont pas attribués en production.
