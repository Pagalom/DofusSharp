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

Si l'échec persiste, transmettre **uniquement** cette nouvelle ligne `p3m14=start-failed` après `git fetch` et `git merge --ff-only FETCH_HEAD`. Les 918 tests ne valident pas encore le mode ETW réel.

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

## État des validations et conditions de suite

- **Confirmé avant P3m.14 :** premier test multicomptes Draconiros/Kourial \`two-window-links-candidate\`.
- **Confirmé avant P3m.14 :** sur reconnexion, sélection #5 Draconiros sans PID, Game #6 dans la bonne fenêtre, \`owner-missing=1\`.
- **Checkpoint Windows récent :** 918 tests réussis, 0 échec, le 23/10/2026 avec 2 avertissements ; ETW n'a pas encore démarré en conditions réelles. Le correctif de diagnostic P3m.14a nécessite une nouvelle compilation.
- **Tests synthétiques :** index ETW exact, isolation des tuples, horodatage, délai ETW, refus des conflits PID/génération, débordement, intégration avec propriétaire unique et quarantaine ; pas de test ETW réel sans Windows.
- **À confirmer :** présence effective des événements \`TcpIpConnect\` sur le Windows utilisateur, droits ETW, livraison des événements à temps, correspondance du PID et de la fenêtre, comportement lors d'une reconnexion.

Aucun changement des données métier ou des portraits multi-comptes BestCrush. **Le serveur choisi manuellement reste autoritaire, sans obligation de validation réseau.** Aucune déduction de serveur en production tant que le lien réel n'est pas suffisamment fiable.
