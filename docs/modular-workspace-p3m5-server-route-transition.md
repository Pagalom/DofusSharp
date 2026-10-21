# P3m.5 — Étude A → B → A des destinations TCP (serveur logique non vérifié)

## Point de départ

P3m.4 a été exécuté deux fois sur deux connexions du même
personnage : `ksc` puis `ksb` reconnus, `linked=yes`
dans les sessions TCP #2 et #4, `cross-session-f3=equal`.
Parmi **8 messages ciblés par session**, aucun chemin
numérique de troisième opcode ne correspondait au candidat
f3 : `candidate-paths=[none]`.

Cette absence ne prouve pas que f3 n'existe dans aucun autre
message : l'étude ne couvrait qu'un sous-ensemble de messages,
de tailles et de profondeurs. L'étude exploratoire f3 est
suspendue ; son attribution de production demeure désactivée.

## Résultat réel P3m.5 — 21 octobre 2026

L'opérateur a réalisé A1 → B → A2. Les trois phases comportent
exactement une session de jeu avec `ksc`, `ksb` et `CLOSED` :
A1 **#2**, B **#6**, A2 **#10**. Le résultat :
`destination-pattern=other A1-vs-A2=different A1-vs-B=different
B-vs-A2=different server=unverified`.

**Même la reconnexion au serveur logique A emprunte une destination
TCP différente.** Ces résultats excluent l'hypothèse d'une destination
unique et stable pour le serveur logique A dans ces essais. Ils ne
montrent pas pourquoi ce routage change.

Les adresses IP n'avaient pas été affichées ni persistées :
elles ne sont **pas récupérables** à partir des journaux fournis.

### Évolution des sorties

Une session TCP de jeu reconnue de manière univoque
(`ksc` + `ksb`, terminée normalement) produit désormais :
`game-session=N destination=IP:port`. IPv6 est affiché sous
la forme `[IPv6]:port`. Les phases ambiguës/incomplètes
produisent `game-session=unavailable destination=unavailable`.

Afficher ces métadonnées réseau techniques est utile pour étudier
les routages et les plages d'hébergement. Cela **ne constitue
toujours pas une preuve du serveur logique**. Les payloads,
jetons et identifiants de compte restent exclus du diagnostic.

## But et limites de P3m.5

Étudier la transition manuelle entre deux **serveurs logiques**
du jeu, annoncés par l'opérateur :

- phase A1 : connexion au serveur A ;
- phase B : connexion au serveur B ;
- phase A2 : reconnexion au serveur A.

Le diagnostic compare **uniquement les destinations TCP**
des sessions de jeu observées durant les trois phases.
**IP/port ne prouvent pas le serveur Dofus :** deux serveurs
peuvent partager un frontal réseau, un serveur peut employer
plusieurs IP, et un routage peut changer à la reconnexion.

Le protocole ne lit pas de nom/ID de serveur logique, ne
détermine pas de compte et ne reçoit aucun identifiant de
personnage. **Le seul rattachement à la phase A ou B est
la manipulation manuelle de l'opérateur.**

## Mode opératoire Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-route-study
```

L'interface Realtek testée portait l'index **6** au dernier
essai. Vérifier `--list` si l'index Npcap change.

**Pour éviter toute confusion, garder le même compte et une
seule fenêtre Dofus à la fois** ; aucun compte secondaire,
aucun autre lancement du jeu durant l'étude.

1. Commencer le diagnostic, Dofus complètement fermé.
2. `PHASE A1` : lancer le jeu, choisir un personnage du
   **serveur A**, attendre le chargement complet, fermer
   complètement la fenêtre du client, puis appuyer sur
   **Entrée** dans la console.
3. `PHASE B` : relancer, choisir un personnage du
   **serveur B** sur le même compte, attendre le chargement,
   fermer complètement la fenêtre, puis appuyer sur **Entrée**.
4. `PHASE A2` : relancer sur **le serveur A**, attendre le
   chargement, fermer complètement la fenêtre puis appuyer sur
   **Entrée**.
5. Le probe s'arrête et affiche les trois sessions de jeu
   avec leurs **IP et ports de destination**.
   `Ctrl+C` pendant l'une des phases provoque un arrêt
   anticipé, généralement marqué `unavailable`.

**Ne pas appuyer sur Entrée avant d'avoir fermé complètement
la fenêtre Dofus de la phase en cours.**

## Évaluation

Le tracker TCP donne un numéro de session propre à la
capture. Chaque phase doit avoir **exactement une session**
dans laquelle le serveur a envoyé **ksc et ksb**.
Elle doit également être terminée (`CLOSED` / `EVICTED`).
Les autres sessions TCP de connexion sont comptées mais
ne sélectionnent pas de destination finale.

Exemple fictif :

```text
p3m5=stage-A1 tcp-sessions=2 ksc-ksb-sessions=1 closed-complete=1 comparable=yes game-session=2 destination=198.51.100.12:5555 server=unverified
p3m5=stage-B tcp-sessions=2 ksc-ksb-sessions=1 closed-complete=1 comparable=yes game-session=4 destination=198.51.100.34:5555 server=unverified
p3m5=stage-A2 tcp-sessions=2 ksc-ksb-sessions=1 closed-complete=1 comparable=yes game-session=6 destination=198.51.100.12:5555 server=unverified
p3m5=summary destination-pattern=A-B-A A1-vs-A2=equal A1-vs-B=different B-vs-A2=different server=unverified meaning=route-only
```

- `A-B-A` : mêmes destinations lors des deux phases A,
  destination B différente. Piste de routage seulement.
- `same-all` : les trois phases rejoignent la même
  destination, pouvant être un frontal commun.
- `other` : au moins une destination de reconnexion
  ne reproduit pas la première, ou autre combinaison.
- `unavailable` : données incomplètes, plusieurs sessions
  de jeu dans une phase ou connexion non terminée.

**Aucun** de ces statuts ne confirme l'ID du serveur logique,
l'identité du compte ou du personnage.

## Isolation et confidentialité

- Destinations conservées **en RAM** uniquement, jusqu'à
  24 sessions TCP de cette exécution.
- Les IP/ports des destinations des sessions de jeu reconnues
  sont affichés. Aucun port client éphémère, nom de compte,
  nom de personnage, jeton ni payload n'est affiché.
- Comparaisons effectuées sur IP/port en mémoire, résultats
  `equal/different` accompagnés des destinations retenues.
- Le programme utilise le tracker de flux existant avec ses
  invalidations de sockets et aucune création de fiche.
- Aucun enregistrement en base, aucun changement au routage
  métier, aucune validation de serveur de production.
- 11 tests synthétiques couvrent A-B-A, destination commune,
  fermeture absente, éviction, sessions ambiguës, événements tardifs,
  transitions invalides et absence de phase.

## Après cette expérience

Si A-B-A existe, tenter une **vraie preuve sémantique**
du serveur dans un message réseau identifié, avec
transition contrôlée. Si les trois destinations sont égales,
ne pas inférer que A et B désignent le même serveur :
cela peut signifier une passerelle commune.

Ne jamais créer `CharacterKey`, attribuer une profession,
inventaire ou kama, ou marquer le serveur `verified`
sur le seul résultat de cette étude.
