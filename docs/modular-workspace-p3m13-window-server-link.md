# P3m.13 — ServerId candidat lié à la fenêtre Dofus (deux clients)

## Faits établis avant cette étude

- P3k a lié **deux flux TCP/5555** à **deux fenêtres Dofus distinctes** en utilisant le quadruplet TCP exact ↔ socket Windows `ESTABLISHED` ↔ PID ↔ HWND (20 octobre 2026).
- P3m.11d et P3m.12 ont démontré sur **un seul client à la fois** une sélection numérique `root.f1.f4.f1` liée à un chargement `ksc`+`ksb`, avec **Draconiros=295** et **Tylezia=293** (références communautaires concordantes, 22 octobre 2026).
- **Contrainte utilisateur P3m.13** : Draconiros et Tylezia appartiennent **au même compte Ankama** ; le second compte utilisable simultanément est sur **Kourial**. Les références communautaires concordantes [DofusGuide](https://github.com/Pierre-ZACHARY/dofusguideweb/blob/main/src/dofus/servers.ts) et [Dofus-Craft](https://github.com/Fronne/Dofus-Craft/blob/main/worker/src/servers.ts) attribuent **355 à Kourial**. Le couple de test multicomptes est donc **Draconiros 295 / Kourial 355**. La validation antérieure P3m.12 sur Tylezia=293 est conservée telle quelle.
- Sur changement de serveur, le client a d'abord émis un candidat du serveur précédent, suivi d'un essai `ksc` sans `ksb`, puis le serveur réellement chargé. Il ne faut donc pas attribuer le **premier** candidat de la séquence à la fenêtre.
- L'association par **sessions globales adjacentes N→N+1** testée en P3m.11d n'est PAS sûre avec deux clients simultanés : les sessions de chaque processus peuvent être entrelacées.

## Nouveau mode expérimental, indépendant de BestCrush

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-server-link
```

Le mode P3m.13 utilise :

1. Le `CharacterCaptureFlowTracker` qui reconstitue chaque flux TCP et reconnaît les `ksc`/`ksb`.
2. Le décodeur Connection **opt-in** `ConnectionServerSelectionStudy`, désormais muni d'un simple observateur de la valeur candidate, en mémoire.
3. Le `TcpSocketTuple.FromPacket` et le `TcpSocketOwnerMatcher` existants, qui exigent une égalité exacte des quatre champs IP/ports Windows et Npcap.
4. `WindowsDofusSocketTable.EstablishedIPv4` pour relever le PID pendant la **connexion courte encore établie**. Le probe essaie dès le premier payload client→serveur et réessaie sur les paquets suivants, dans la limite de 24 essais par flux.
5. `VisibleWindows` et une table volatile `(HWND,PID)→window-index` avec au maximum 16 fenêtres. Un socket associé au PID avant la création visible de la fenêtre peut être relié à celle-ci **plus tard**, uniquement si son PID correspond à une fenêtre unique.
6. Une étude isolée `WindowServerConnectionLinkStudy` qui regroupe les tentatives **par fenêtre/PID**, pas par IP distante ni adjacence globale. Pour une tentative Game portant `ksc`, elle recherche une seule sélection **du même index de fenêtre** entre la dernière tentative Game de cette fenêtre et la tentative présente, puis vérifie sa clôture et la présence éventuelle de `ksb`.

Les valeurs privées, adresses, ports, PIDs, HWNDs, titres de fenêtres, comptes, jetons et noms de personnages ne sont ni imprimés ni persistés. Aucun fichier de capture n'est créé. Pour **P3m.13**, les IDs de référence sont **Draconiros=295 et Kourial=355**, tirés de deux catalogues communautaires concordants. La référence **Tylezia=293** reste réservée à la validation historique P3m.12.

### Garde-fous

- Aucun socket sans propriétaire Windows unique n'est attribué par supposition.
- Un PID ayant plusieurs fenêtres Dofus, un changement de propriétaire ou une réutilisation du même quadruplet TCP avec un numéro de session différent ne permet pas d'établir de lien.
- Une sélection encore ouverte au moment du Game, une sélection multiple, un doublon non consommé, un paquet manquant ou un Game non terminé empêche le statut `linked-candidate`.
- Le sondage propriétaire ne capture **que TCP IPv4/5555**, pas les autres ports, protocoles ou clients IPv6 ; il peut manquer les sockets extrêmement brefs.
- Les références **295/355** de P3m.13 sont communautaires. Aucun ID de compte et aucune identité de personnage ne sont vérifiés. Une fenêtre avec `reference=Draconiros` ou `reference=Kourial` reste `server=unverified` dans cette étude.
- Le statut global `two-window-links-candidate` exige au moins deux fenêtres **distinctes**, chacune avec au moins un chargement complet et une sélection **reliée à la même fenêtre**. Tout propriétaire/socket pertinent non identifié ou conflit entraîne `not-confirmed`.
- Le mode est **diagnostic uniquement** : pas de mise à jour de `CurrentServerState`, de base, de fiches personnages, de `DofusNetworkCaptureService`, de routage métier ou d'overlay.

## Procédure Windows recommandée

Mettre à jour puis valider :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

1. **Fermer toutes les fenêtres Dofus**. Laisser le launcher ouvert.
2. Lancer `--window-server-link` **avant** d'ouvrir les clients Dofus.
3. Ouvrir le **premier compte sur Draconiros** et charger un personnage.
4. **Sans fermer le premier client**, ouvrir le **deuxième compte sur Kourial** et charger un personnage.
5. Laisser **les deux fenêtres ouvertes** ; ouvrir l'inventaire ou effectuer une action réseau ordinaire dans chaque client.
6. Revenir à la console et appuyer sur **Ctrl+C**. Ne pas fermer les fenêtres avant l'arrêt du probe.
7. Transmettre les lignes `p3m13=` ainsi que le résumé des tests. Le contenu du jeu et les identifiants privés ne sont pas nécessaires.

Le probe ne modifie pas le jeu et n'envoie aucun message réseau.

## Sortie

- `p3m13=loaded window-index=1 ... reference=Draconiros match=community-candidate` : dans cette fenêtre **anonyme**, une sélection numérique a été liée à une session Game chargée. Exemple illustratif, pas un résultat garanti.
- `p3m13=window window-index=... connection-selections=... game-attempts=... linked=... loaded=... status=linked-candidate/unverified` : bilan par fenêtre.
- `p3m13=summary ... owner-missing=... window-missing=... conflicts=... status=two-window-links-candidate/not-confirmed` : présence ou absence de deux liens attribuables.

**Résultat nul ≠ mécanisme absent.** La connexion d'amorçage dure parfois une seconde, et peut précéder la création de la fenêtre. Le probe conserve le PID s'il a pu être obtenu pendant la durée de vie du socket ; si celui-ci échappe au sondage, il refuse toute inférence.

## Tests

18 méthodes de test isolées incluent : sessions Connection et Game entrelacées entre 2 fenêtres, serveurs identiques, anti-association inter-PID, liaison tardive à la fenêtre, PID ambigu, socket Windows dupliqué, conflit de propriétaire, socket absent, sélection non terminée, deux sélections pour un seul Game, identifiant non reconnu, connexions successives sur le même client.

La compilation et la campagne de validation **Windows réelles** restent nécessaires.

## P3m.13 — Premier résultat réel et correctif P3m.13b (22 octobre 2026)

Le probe a été lancé sur deux comptes simultanés :
**Draconiros (295)** et **Kourial (355)**,
les deux fenêtres Dofus restant **ouvertes** au
moment de Ctrl+C. Sortie :

```text
p3m13=window window-index=1 connection-selections=2 game-attempts=1 linked=0 loaded=0 references-matched=0 status=unverified account=unverified server=unverified
p3m13=window window-index=2 connection-selections=1 game-attempts=1 linked=0 loaded=0 references-matched=0 status=unverified account=unverified server=unverified
p3m13=summary flows=6 windows-linked=2 windows-ready=0 loaded-pairs=0 references-matched=0 owner-missing=0 window-missing=0 conflicts=0 overflow=no status=not-confirmed account=unverified server=unverified
```

**Ce que cette sortie établit :** les deux fenêtres
sont distinctes et tous les flux pertinents disposent
d'un propriétaire de socket et d'une fenêtre,
sans conflit. Elle ne démontre pas pourquoi les
Game ne sont pas reliés, car P3m.13 n'imprimait
aucun motif de rejet.

**Défaut de logique identifié :** la fonction
`Summarize()` imposait
`game.Closed == true` pour chaque connexion Game.
Or le protocole d'essai prescrivait justement de
**conserver les deux clients connectés** pendant
l'arrêt du probe. Une connexion Game ayant reçu
`ksc`+`ksb` est considérée comme chargée
**même si le socket TCP reste ouvert**.

**Deuxième ambiguïté réelle :** fenêtre 1 avait
deux sélections candidates pour une seule tentative
Game, contre une sélection pour la fenêtre 2.
Cela peut correspondre à des tentatives successives
non reconnues comme `ksc` ou à une autre séquence ;
nous ne devons pas sélectionner arbitrairement
le dernier candidat ni supposer que les deux
valeurs sont égales.

### P3m.13b — Correction et observabilité minimale

- Accepte pour un chargement complet la connexion
  Game **active** si `ksc` et `ksb`
  ont été observés ; n'exige sa fermeture
  que si `ksb` est absent pour considérer
  le passage comme `ksc-only-closed`.
- Ajoute `p3m13=attempt` avec
  `game-state=loaded-live|loaded-closed|
  ksc-only-live|ksc-only-closed`,
  `preceding-selections`,
  `distinct-selection-values` et un
  **code de rejet catégoriel**.
- Si plusieurs sélections précèdent le même
  Game, signale `multiple-selections`
  ou `multiple-distinct-selections`.
  Même deux ID identiques ne prouvent pas
  quelle connexion a effectivement servi :
  **aucune attribution forcée**.
- Une tentative Game encore ouverte avec
  `ksc` mais sans `ksb` reste
  `game-incomplete`.
- Ajoute `reason=...` à
  `p3m13=window`.
- Les numéros de serveur capturés, les jetons,
  les PID, les HWND et les adresses ne sont
  toujours pas affichés ni persistés.

Le statut global `two-window-links-candidate`
continue d'exiger deux liens indépendants
non ambigus. Il est possible de confirmer
**une seule fenêtre**, avec statut global
`not-confirmed`, et d'étudier l'autre ensuite.

Les tests synthétiques supplémentaires vérifient
l'acceptation des chargements actifs,
l'interdiction des faux chargements partiels,
les conflits entre sélections différentes ou
identiques et le scénario réel
`2 sélections / 1 jeu` + `1 sélection / 1 jeu`.

Après mise à jour, **ne pas fermer Dofus** avant
Ctrl+C ; retransmettre les lignes `p3m13=attempt`,
`p3m13=window`, `p3m13=summary`.
Cette étape ne modifie aucun routage métier
ni la sélection manuelle des serveurs.

## P3m.13b — Résultat terrain : deux comptes et deux serveurs correctement associés (22 octobre 2026)

Après le correctif qui accepte un Game encore actif
(`ksc` + `ksb` sans exiger la fermeture TCP), le
protocole a été rejoué avec **deux comptes Dofus
connectés simultanément** : Draconiros (295) et
Kourial (355).

```text
p3m13=attempt window-index=1 game-session=4 game-state=loaded-live preceding-selections=1 distinct-selection-values=1 status=candidate-linked reason=none account=unverified server=unverified
p3m13=loaded window-index=1 connection-session=3 game-session=4 reference=Draconiros match=community-candidate account=unverified server=unverified
p3m13=window window-index=1 connection-selections=1 game-attempts=1 linked=1 loaded=1 references-matched=1 status=linked-candidate reason=none account=unverified server=unverified
p3m13=attempt window-index=2 game-session=2 game-state=loaded-live preceding-selections=1 distinct-selection-values=1 status=candidate-linked reason=none account=unverified server=unverified
p3m13=loaded window-index=2 connection-session=1 game-session=2 reference=Kourial match=community-candidate account=unverified server=unverified
p3m13=window window-index=2 connection-selections=1 game-attempts=1 linked=1 loaded=1 references-matched=1 status=linked-candidate reason=none account=unverified server=unverified
p3m13=summary flows=4 windows-linked=2 windows-ready=2 loaded-pairs=2 references-matched=2 owner-missing=0 window-missing=0 conflicts=0 overflow=no status=two-window-links-candidate account=unverified server=unverified
```

**Conclusion observée :** les deux flux **Connection**
des sessions #3 et #1 ont été associés, par le
quadruplet TCP exact et le propriétaire Windows
(PID/fenêtre), aux sessions Game respectives
#4 et #2. Les deux Game étaient toujours
ouverts, mais `ksc` puis `ksb` avaient été
observés. Chaque sélection comportait un unique
identifiant candidat ; les identifiants concordent
avec les références communautaires des deux
serveurs effectivement chargés.

Cela valide **dans cette situation précise**
l'association expérimentale de deux serveurs
différents à deux fenêtres distinctes, malgré
l'entrelacement des numéros de sessions.
Il ne s'agit **pas** d'une preuve d'identité
du compte Ankama ni de celle du personnage ;
les indicateurs `account=unverified` et
`server=unverified` sont donc conservés dans
le diagnostic et le routage production n'est
pas activé.

**Contrôles restant nécessaires :** inversion de
l'ordre d'ouverture, reconnexion d'un seul client
pendant que l'autre reste chargé, changement de
serveur avant sélection du personnage, fermeture
et réouverture d'une fenêtre, conflits/
sélections multiples, et vérification du
comportement sur le build réel du jeu.
L'étude P3m.13b ne suffit pas à garantir
l'association dans toutes ces situations.

**Tests :** P3m.13 compte désormais 18 tests
synthétiques dans
`WindowServerConnectionLinkStudyTest`,
dont le correctif `loaded-live` et les cas
de sélections multiples. L'exécution de
`dotnet test` sur Windows n'a pas été
communiquée lors de cette dernière capture ;
le lancement du probe atteste seulement que
celui-ci a compilé et pu s'exécuter.

## Après validation

Tester deux fenêtres **sur le même serveur**, une reconnexion indépendante et des sélections simultanées. Puis comparer la robustesse du lien pendant fermeture/réouverture et réassignations de focus. Ne brancher aucun routage en production tant que l'association socket→fenêtre et la séparation des caches métier par flux ne sont pas démontrées sur le build actuel. Le choix manuel du serveur reste **autoritaire sans validation obligatoire**.
