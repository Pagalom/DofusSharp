# P3m.14 — ETW TCP Connect ↔ Npcap ↔ fenêtre Dofus

## Objectif précis

P3m.13 a identifié **deux clients Dofus simultanés** sur Draconiros et Kourial par quadruplet TCP exact → socket Windows → PID → HWND. En revanche, lors d'un **retour à la sélection des personnages sans fermer Dofus**, une nouvelle connexion de sélection Connection #5, portant Draconiros, précède Game #6 mais disparaît avant l'obtention du PID. L'indice chronologique **ne doit pas** être traité comme un propriétaire vérifié.

P3m.13e, lecture périodique de la table TCP toutes les 25 ms, a fourni :

\`\`\`text
p3m13=unbound session=5 role=selection reason=owner-missing selection-candidates=1 account=unverified server=unverified
p3m13=unbound-hypothesis selection-session=5 candidate-reference=Draconiros adjacent-loaded-game-session=6 adjacent-game-window-index=1 matches-previous-loaded-selection=yes meaning=sequence-only linked=no account=unverified server=unverified
p3m13=owner-polling samples=0 matches=0 active-at-stop=3 interval-ms=25 meaning=exact-tuple-only account=unverified server=unverified
\`\`\`

Le compteur \`samples=0\` montre que le polling n'a pas relevé de socket dans ses conditions de déclenchement. Il ne prouve pas un dysfonctionnement de Windows.

## Implémentation — mode isolé

**Commande :**

\`\`\`powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-server-etw
\`\`\`

Ce mode est distinct de \`--window-server-link\`, opt-in, **Windows uniquement**. Il ajoute la dépendance **Microsoft.Diagnostics.Tracing.TraceEvent 3.2.8** au seul projet \`BestCrush.NetworkProbe\`, pas à BestCrush. Les fonctions d'indexation ETW sans dépendance OS restent dans \`BestCrush.Network\` pour les tests.

La trace ETW :

1. démarre **avant Npcap** et avant l'ouverture des deux Dofus ;
2. crée une session kernel ETW **au nom unique** (ne prend pas le contrôle de la session globale \`NT Kernel Logger\`) ; nécessite probablement **PowerShell administrateur** pour le provider ;
3. s'abonne seulement à \`TcpIpConnect\` via \`KernelTraceEventParser.Keywords.NetworkTCPIP\`. Cet événement expose, selon le format Windows/TraceEvent, la source IPv4, la destination IPv4, les deux ports et le PID ;
4. sélectionne uniquement les connexions sortantes dont le **port distant est 5555** et dont le PID est strictement positif ; aucune adresse, port local, PID, titre de fenêtre ni valeur privée ne sort du processus ;
5. conserve en RAM, bornés à **128 quadruplets ETW**, un PID et l'heure de l'événement. Un second événement donnant un PID différent ou une génération incompatible du même quadruplet le met en conflit ;
6. rapproche ETW et Npcap **par égalité exacte des quatre champs et par proximité des horodatages (≤10 s)** du premier paquet Npcap sortant ;
7. présente le PID obtenu au \`WindowServerConnectionLinkStudy\` déjà existant, qui exige une **fenêtre unique de ce PID**. Toute preuve contradictoire est mise en quarantaine. Aucune corrélation fondée seulement sur \`session #5 suivie de #6\` ;
8. à Ctrl+C, stoppe Npcap et la session ETW, donne à la source jusqu'à 5 s pour terminer le traitement puis la libère. Si ETW ne peut pas démarrer, le mode **échoue explicitement** au lieu de revenir au diagnostic sans ETW.

Une session ETW en temps réel collecte des événements noyau et peut être utilisée par d'autres outils ; elle est démarrée seulement sur demande. Aucun fichier ETL ou capture d'adresses n'est créé par ce mode. Le programme n'injecte aucun paquet réseau.

**Source API et version :**

- [Guide TraceEvent de Microsoft — Real Time Processing](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventProgrammersGuide.md)
- [TraceEventSession du projet PerfView](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/TraceEventSession.cs)
- [TcpIpConnectTraceData du projet PerfView](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Parsers/KernelTraceEventParser.cs)
- [Package NuGet 3.2.8](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/3.2.8)

## Sorties attendues

En plus des lignes précédentes \`p3m13=...\`, P3m.14 affiche :

\`\`\`text
p3m14=matches exact-tuples=... events-indexed=... index-overflow=no meaning=exact-tuple-and-timestamp-only account=unverified server=unverified
p3m14=etw ready=yes processing-stopped=yes tcp-connect-events=... port-connect-events=... rejected=... provider=kernel-tcpip-connect account=unverified server=unverified
\`\`\`

Le nombre \`exact-tuples\` signifie que le PID a été récupéré depuis un événement correspondant **au même quadruplet et à une fenêtre temporelle acceptable** ; il ne remplace pas le bilan \`p3m13=window\` de la liaison complète.

Si ETW ne démarre pas (privilèges, session concurrente, version), la sortie indique :

\`\`\`text
p3m14=start-failed stage=... reason=... hresult=0x... elevated=yes|no|unknown account=unverified server=unverified
\`\`\`

**P3m.14a — diagnostic d'accès ETW (23/10/2026).** La compilation Windows et les **918 tests** ont réussi (deux avertissements laissés de côté). En revanche, le démarrage ETW échoue avec `UnauthorizedAccessException`, y compris après validation d'un contrôle UAC. La cause exacte n'est **pas encore identifiée** ; il serait prématuré de changer de provider ou d'arrêter une autre session ETW. Le journal de démarrage indique désormais l'étape précise (`session-construction`, `kernel-provider-enablement`, `event-source-acquisition`, `callback-registration`, `event-processing-start` ou `capture-or-other`), le HRESULT de l'exception d'origine, et les droits administrateur du **processus effectif**. Ces champs ne contiennent aucun identifiant réseau ou personnel.

L'échec initial a ensuite été caractérisé par `stage=kernel-provider-enablement reason=UnauthorizedAccessException hresult=0x80070005 elevated=no`. En utilisant un processus réellement élevé, le mode ETW a démarré et achevé deux captures. Les 918 tests constituent la validation Windows antérieure à ce résultat, sans démontrer à eux seuls le comportement ETW.

**Aucun PID, UUID, port local, IP ni nom de compte n'est affiché**, même en cas d'échec.

## Procédure Windows

**0. Préparation :** fermer toutes les fenêtres Dofus, lancer PowerShell en tant qu'administrateur **uniquement pour cette expérimentation ETW**, depuis le dépôt. Npcap doit être installé.

**1. Mise à jour et tests :**

\`\`\`powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
\`\`\`

**2. Capture :**

\`\`\`powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-server-etw
\`\`\`

**3. Scénario :**

1. Ouvrir le compte Draconiros et charger un personnage.
2. Ouvrir **l'autre compte** Kourial et charger un personnage, tout en gardant Draconiros ouvert.
3. Sur Draconiros, revenir uniquement à la sélection des personnages puis reconnecter le personnage **sans fermer la fenêtre**.
4. Laisser les deux fenêtres ouvertes, attendre que le chargement soit fini et appuyer sur Ctrl+C dans PowerShell.
5. Communiquer les lignes \`p3m13=...\`, \`p3m14=...\` et les résultats \`dotnet test\`, sans aucune capture brute ni identifiant privé.

## Résultats Windows réels — 23/10/2026

**Capture initiale à deux clients (sans reconnexion)** : `flows=4`, `windows-linked=2`, `loaded-pairs=2`, `owner-missing=0`, `conflicts=0`. ETW : `exact-tuples=4`, `events-indexed=4`, `tcp-connect-events=45`, `port-connect-events=4`, `rejected=0`, `ready=yes`, `processing-stopped=yes`.

**Capture de reconnexion sans fermeture du client** : Kourial `connection-session=1 → game-session=2` en fenêtre 1 ; Draconiros `connection-session=3 → game-session=4` puis `connection-session=5 → game-session=6` en fenêtre 2.

```text
p3m13=window window-index=2 connection-selections=2 game-attempts=2 linked=2 loaded=2 references-matched=2 status=linked-candidate reason=none account=unverified server=unverified
p3m13=summary flows=6 windows-linked=2 windows-ready=2 loaded-pairs=3 references-matched=3 owner-missing=0 window-missing=0 conflicts=0 overflow=no status=two-window-links-candidate account=unverified server=unverified
p3m14=matches exact-tuples=6 events-indexed=6 index-overflow=no meaning=exact-tuple-and-timestamp-only account=unverified server=unverified
p3m14=etw ready=yes processing-stopped=yes tcp-connect-events=37 port-connect-events=6 rejected=0 provider=kernel-tcpip-connect account=unverified server=unverified
```

**Interprétation limitée :** dans le scénario observé, la seconde sélection Draconiros (#5) et son nouveau Game (#6) sont tous deux liés à la fenêtre Draconiros, sans manque de propriétaire. L'index ETW a rapproché 6/6 quadruplets exacts. Ce résultat ne prouve pas qu'ETW est la **seule** source de propriétaire, puisque l'interrogation directe de la table TCP est également active ; `owner-polling samples=0` se rapporte seulement au sondage asynchrone de 25 ms. Le serveur affiché provient d'une correspondance avec une référence communautaire, non d'une identité de serveur officiellement attestée. Il n'y a aucune preuve d'identité de compte ni validation du routage métier de production.

Le défaut de diagnostic antérieur `owner-missing=1` est **non reproduit dans ce test ETW**, et le lien expérimental de reconnexion est observé ; cela ne constitue pas une garantie globale de fiabilité en conditions de concurrence, de perte d'événements ou de réutilisation de tuples.

## État des validations et conditions de suite

- **Confirmé avant P3m.14 :** premier test multicomptes Draconiros/Kourial \`two-window-links-candidate\`.
- **Confirmé avant P3m.14 :** sur reconnexion, sélection #5 Draconiros sans PID, Game #6 dans la bonne fenêtre, \`owner-missing=1\`.
- **Checkpoint Windows communiqué :** 918 tests réussis, 0 échec, avec 2 avertissements. Les captures ETW ci-dessous ont ensuite fonctionné sur Windows ; aucun nouveau décompte de tests après la modification de diagnostic P3m.14a n'a été communiqué.
- **Tests synthétiques :** index ETW exact, isolation des tuples, horodatage, délai ETW, refus des conflits PID/génération, débordement, intégration avec propriétaire unique et quarantaine ; pas de test ETW réel sans Windows.
- **Confirmé expérimentalement :** le fournisseur `TcpIpConnect` délivre les événements, le processus élevé peut démarrer ETW, et les six tuples du scénario avec reconnexion sont correspondants ETW ↔ Npcap sans propriétaire manquant.
- **À confirmer avant production :** reproductibilité sur davantage de scénarios, comportement lors des collisions, attribution au-delà d'une simple preuve candidate de serveur, isolement des caches de décodage métier par flux, séparation entre sessions et cycles de fenêtres. Le mode ETW demeure opt-in et diagnostique.

Aucun changement des données métier ou des portraits multi-comptes BestCrush. **Le serveur choisi manuellement reste autoritaire, sans obligation de validation réseau.** Aucune déduction de serveur en production tant que le lien réel n'est pas suffisamment fiable.
