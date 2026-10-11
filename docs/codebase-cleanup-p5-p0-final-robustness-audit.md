# P5.0 — Audit final de robustesse BestCrush v0.2.0

**Référence auditée :** `refactor/codebase-cleanup`, HEAD `8f1e7ca`.  
**Point de contrôle Windows communiqué :** **380 tests réussis, 0 échec**, build Windows réussi au même HEAD (11/10/2026).  
**Portée :** lecture de code, rapprochement des tests, rapports historiques et observations F5 ; **aucune correction de production dans P5.0**. Pas de preuve d'un incident mémoire réel, d'un problème Npcap ni d'une régression de concurrence en conditions de jeu.

## Synthèse de décision

| ID | Priorité | Constat / preuve | Risque | Traitement proposé |
| --- | --- | --- | --- | --- |
| P5-N1 | **P1 si durcissement pré-release** | `DofusNetworkCaptureService.cs:155-162` utilise `Channel.CreateUnbounded<DofusWireMessage>` ; `OnPacketArrival` enfile par `TryWrite` et le worker persiste séquentiellement dans SQLite/Debug. | Consommateur plus lent que producteur sur une période prolongée : mémoire croissante. Aucun dépassement observé pendant les tests fonctionnels. | P5.1 ciblé : métriques/compteurs et test de saturation reproductible ; choisir explicitement un comportement sans perte de résultats métiers avant de borner la file. **Ne pas simplement remplacer par un BoundedChannel + TryWrite** : le jeu ne rejouerait pas nécessairement les résultats perdus. |
| P5-N2 | **P1 si durcissement pré-release** | `BestCrush.Network/Capture/TcpReassembler.cs:5-62` conserve des segments `_pending` sans plafond local. `VarintFrameBuffer.cs` (classe dans ce fichier, lignes 66–98) accumule des octets sans limite propre avant la lecture d'une trame ; le plafond de **16 Mio** s'applique à la longueur annoncée de trame, pas à toute mémoire en attente. | Flux avec trous persistants ou framing incomplet : usage mémoire potentiellement disproportionné, même si le cache global n'autorise que 256 flux. | P5.1 : caractériser par tests des trous/fragments longs, définir plafond + durée d'inactivité des données partielles avec réinitialisation explicite du flux ; ne pas altérer les séquences normales. |
| P5-N3 | **P2** | `DofusNetworkCaptureService.cs:488-495` inclut `DeviceName` dans `TcpFlowKey`, et `:537-545` enfile chaque message décodé. Aucun mécanisme de déduplication inter-adaptateurs n'a été identifié. `CrushSessionService.ApplyNetworkCrushAsync` crée une session d'historique par résultat reçu. | Si le **même paquet** est capturé sur plusieurs interfaces, un résultat de concassage peut être traité en double. Condition **non démontrée** sur le PC de l'utilisateur. | Reproduction synthétique multi-interface et vérification d'unicité dans un lot indépendant ; ne pas dédupliquer par seul ItemId (deux concassages légitimes possibles). |
| P5-N4 | **P2** | `ProtocolWireNormalizer.cs:186,192` alloue via `stackalloc` dans une boucle sur les champs Protobuf ; warnings Windows **CA2014** reproduits. | Empilement répété en présence de trames riches en champs (le buffer source peut atteindre le plafond protocolaire). | Petite correction isolée et test de normalisation sur beaucoup de champs, sans changer le mapping. |
| P5-U1 | **P2, à reproduire si demandé** | P4.1 (`CrushSessionService.cs:1112-1247`) s'abonne aux notifications de prix et recalcule le dernier résultat. **Tests unitaires P4.1 réussis**, mais lors du F5 sur Kourial, le montant **2 598 K** n'a pas varié. La capture montrait **17 Vi**, `1×1 541 + 7×151`, et dans le champ x1 une **valeur manuelle 159** avec focus encore actif. | **Pas de diagnostic certain** : la page `RunePrices.razor:334-416` sauvegarde/notifie via `OnChange` ; la priorité Manuel/Jeu peut aussi conserver le prix auto 151. | Reporter conformément au choix utilisateur. Un unique test discriminant suffit si repris : quitter la case, vérifier la confirmation d'enregistrement et la priorité active, puis observer la valeur courante ET l'historique (figé). |
| P5-S1 | **Suivi sécurité, pas d'alerte ouverte confirmée** | `BestCrush.csproj`, `BestCrush.Domain.csproj`, `BestCrush.Migrations.csproj` et le projet de tests référencent explicitement **SQLitePCLRaw.bundle_e_sqlite3 2.1.13** ; test `SqliteNativeVersionTest` de S1 protège la version SQLite native. L'avis **GHSA-2m69-gcr7-jv3q** répertorie les versions `SQLitePCLRaw.lib.e_sqlite3 <= 2.1.11` comme affectées. Les derniers logs Windows ne montrent plus `NU1903`. | Une résolution transitive doit toujours être vérifiée dans chaque cible/publish ; on ne conclut pas à la sécurité totale du graphe par une version directe seule. | P6 : `dotnet list package --include-transitive --vulnerable` (ou syntaxe SDK équivalente), `SqliteNativeVersionTest`, et test des migrations sur **copie** de base. Ne pas réouvrir le vieux défaut 2.1.10 sans nouvelle preuve. |
| P5-D1 | **P3 / documentation** | `NetworkDebugWriter.cs:43-64` écrit le corps des messages en **Base64** dans `%LOCALAPPDATA%\BestCrush\DebugCaptures\Network\...` quand le debug est activé ; pas de rotation/purge automatique visible. Le paramètre `DevTool_KeepDebugArtifacts` est désactivé par défaut. | Occupation disque et données brutes locales potentiellement sensibles si activation prolongée. | Garder la capture désactivée hors diagnostic ; ne jamais committer/partager les fichiers `wire.jsonl` ; envisager politique de rétention ultérieure. |
| P5-P1 | **P3 / protocole** | `protocol-map.json` centralise 21 routes, mais une partie porte une preuve **legacy**, pas une validation du client actuel. `jzs`, `kch`, `kag` ont été observées et vérifiées pendant P1–P3. | Une mise à jour Dofus ou une route rarement utilisée peut nécessiter un nouveau diagnostic. | P6 : ne tester manuellement que les chemins courants ; maintenir le fichier de profil + fixtures synthétiques. |
| P5-T1 | **P3 / historique** | `docs/codebase-cleanup-follow-ups.md` décrit l'état de **A3/A4** : nombreuses réserves ont depuis été levées (S1 SQLite, S2b serveur, S3 overlap TCP, P4.2 affichage estimé). | Si lu sans les phases ultérieures, le vieux rapport surestime la dette courante. | Utiliser **ce P5.0** comme inventaire actuel ; ne pas supprimer le rapport historique. |

### Mesures déjà en place — éviter les faux positifs

- `NetworkFlowCache` : au plus **256** entrées, éviction par inactivité/LRU ; `RecentItemDetailsCache` : au plus **16 384** UID.
- `DofusNetworkCaptureService` conserve son canal et son worker pendant Stop/Start mais invalide l'`_captureEpoch` et vide les flux ; les anciens messages en file sont ignorés.
- `DofusNetworkMessageProcessor` vide les associations UID et achats lorsqu'un bail de sélection serveur ou une époque de capture change.
- `TcpReassembler` possède **déjà** le correctif S3 des segments en attente chevauchants. Reste hors scope : wrap-around de `uint32` et bornes de `_pending`.
- `CurrentServerState` traite **la sélection explicite par l'utilisateur** comme faisant autorité ; il ne prétend pas déduire le serveur depuis les paquets. Cette politique S2b a été choisie et validée par l'utilisateur ; ne pas réintroduire une confirmation obligatoire.
- P4.2 a corrigé **uniquement le format d'affichage** des reliquats ; les snapshots sont persistés sans revalorisation historique.

## Priorisation pratique avant P6

**Option prudente :** P5.1 très ciblé pour les bornes mémoire (`_pending`, framing) et le warning `CA2014`, en commençant par des tests de caractérisation ; la file unbounded doit d'abord avoir une **politique de saturation explicitement choisie**, sinon risque de régression fonctionnelle plus élevé que le risque théorique actuel.

**Option rapide :** ne pas modifier la capture à l'aveugle. Faire P6 (tests .NET, compilation, capture HDV, concassage, sélection serveur, F7/F8, historique, démarrage/Stop-Start si accessible). Documenter P5-N1/N2/N3 comme dette de robustesse ; l'utilisateur décide ensuite si elle bloque la diffusion.

**Avis de l'audit :** aucune régression bloquante nouvelle n'a été *reproduite* sur l'état P4.2 (**380/380**). Les risques de croissance mémoire et de capture multi-interface sont **étayés par lecture de code, non observés en production**. P4.1 demeure non validé en F5 mais l'utilisateur a demandé de ne pas s'y arrêter.

## Critères de clôture P5.0

- Revue documentée des frontières : collecte Npcap, buffers TCP, file et worker, caches, bail/époque, normalisation Protobuf, échecs, persistance, notifications, historique, sécurité des dépendances et des traces.
- Hiérarchisation claire entre risque de conception, anomalie fonctionnelle constatée et ancien défaut **déjà résolu**.
- Aucun changement de code ni de tests ; pas de nouveau checkpoint Windows demandé **pour ce seul document**.
- Aucun changement de `main`, version, migration, fichiers utilisateur ni release.

## Sources

- Code source du dépôt (HEAD `8f1e7ca`) : chemins et lignes indiqués dans le tableau.
- GHSA SQLite : https://github.com/advisories/GHSA-2m69-gcr7-jv3q
- S1 : `docs/stability-s1-sqlite-native.md`.
- S2b : `docs/stability-s2b-selection-authoritative.md`.
- S3 : `docs/stability-s3-tcp-reassembly.md`.
- S4a/S4b : `docs/stability-s4a-npcap-restart.md`, `docs/stability-s4b-network-caches.md`.
- P4.0/P4.1/P4.2 : `docs/market-valuation-p4-*.md`.
