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

12 méthodes de test isolées incluent : sessions Connection et Game entrelacées entre 2 fenêtres, serveurs identiques, anti-association inter-PID, liaison tardive à la fenêtre, PID ambigu, socket Windows dupliqué, conflit de propriétaire, socket absent, sélection non terminée, deux sélections pour un seul Game, identifiant non reconnu, connexions successives sur le même client.

La compilation et la campagne de validation **Windows réelles** restent nécessaires.

## Après validation

Tester deux fenêtres **sur le même serveur**, une reconnexion indépendante et des sélections simultanées. Puis comparer la robustesse du lien pendant fermeture/réouverture et réassignations de focus. Ne brancher aucun routage en production tant que l'association socket→fenêtre et la séparation des caches métier par flux ne sont pas démontrées sur le build actuel. Le choix manuel du serveur reste **autoritaire sans validation obligatoire**.
