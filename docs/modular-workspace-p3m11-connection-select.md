# P3m.11 — Observer `SelectServer` dans les trames du Connection Server

## Découvertes et problème concret

P3m.9 a traité **96 messages Ankama décodés** (32 par
phase A1/B/A2) sur TCP/5555, sans retrouver les noms
Draconiros et Tylezia. La recherche est bornée aux messages
`type.ankama.com/...` et aux chaînes de texte.

P3m.10 a inventorié les sockets Windows Dofus/Ankama
**sur tous les ports**, avec :

- **A1 Draconiros** : `52.30.61.61:5555` et
  `34.252.214.29:5555` ;
- **B Tylezia** : `108.128.247.72:5555` et
  `54.77.100.255:5555` ;
- **A2 Draconiros** : `108.128.247.72:5555`,
  `52.30.61.61:5555`, `54.77.100.255:5555` et
  `18.202.11.172:5555`.

D'autres canaux `443`, `6337` et loopback ont été
observés ; leur rôle dans la sélection reste **inconnu**.
Les IP de jeu changent : une adresse seule ne peut pas
servir de clé permanente du serveur logique.

### Protocole de connexion trouvé publiquement

Deux sources de recherche indépendantes décrivent un
protocole Unity **Connection**, distinct du protocole **Game** :

- [LuaxY — connection/message.proto](https://github.com/LuaxY/dofus-unity-protocol-builder/blob/main/proto/connection/message.proto)
  : `Message.request=1`, `Request.selectServer=4`,
  `SelectServerRequest.server=1` (`int32`) ;
  la réponse contient `host`, `ports`, `token`.
- [Jondo — GameProtocol.proto](https://github.com/D3SignedSkin/3.x/blob/main/Jondo.Unity.Launcher/Protocol/GameProtocol.proto)
  : `GameMessage.auth=1`,
  `AuthenticationTicketMessage.selectedServer=4`,
  `SelectedServerSelection.serverId=1` (`int32`).
  Cet émulateur cible **Dofus Unity 3.6.4.3** et
  décrit Connection Server et Game Node en TCP/5555.

**Important :** dans les deux sources, la structure minimale
de sélection est par hasard le même chemin numérique
`root.f1.f4.f1`, mais l'enveloppe, les préambules et le
sens métier peuvent diverger. Ces sources ne prouvent pas
que le schéma est inchangé dans le **client actuellement
installé** ni que notre capture inclut réellement
le Connection Server.

## Défaut identifié dans le décodeur précédent

`CharacterCaptureFlowTracker` appelait :

```csharp
AnkamaAny? any = AnkamaFrameDecoder.FindAny(frame);
if (any is null)
    continue;
```

Il ignorait **toute trame protobuf ne contenant pas**
une enveloppe `type.ankama.com/...`. Les messages
de sélection décrits dans les deux schémas Connection
n'ont pas cette enveloppe. Notre recherche historique
n'était donc **pas adaptée à ce protocole**.

## Implémentation opt-in

Le mode `--connection-selection-study` :

- reçoit les **trames reconstituées TCP/5555** juste
  avant le filtre de messages du protocole de jeu ;
- ignore les trames possédant une enveloppe `AnkamaAny`
  (elles sont traitées par le pipeline Game habituel) ;
- examine uniquement les trames **client→serveur**,
  au maximum **256 trames sans enveloppe Game par session, 8 Kio/trame**,
  24 sessions dans l'étude ;
- vérifie une structure stricte : racine protobuf
  avec un seul `f1` (message), sous-message ayant
  un `f1` texte non vide (langue ou UUID) et
  `f4` message, puis `f4.f1` varint positif ;
- conserve en RAM les **entiers candidats** à
  `serverId`, sans afficher leur valeur, sans
  jeton, UUID, login ni contenu d'authentification ;
- garde la séparation **par flux TCP, session et
  phase manuelle** A1/B/A2 ;
- exige une unique session **`ksc`+`ksb` clôturée**
  par phase pour valider le parcours du jeu ;
- compare les valeurs candidates par phase :
  motif A1=A2≠B, sans l'interpréter comme
  une preuve d'identification du serveur ;
- affiche l'IP:port de la connexion ayant porté
  un candidat, ce qui aide à distinguer
  Connection Server et Game Node.

La simple forme `f1.f4.f1` **n'est pas unique** au
protocole de connexion. Même si elle suit A-B-A,
la sémantique devra encore être vérifiée par
la liste des serveurs et l'association
`server_id` ↔ nom.

Aucun service métier, compte, serveur choisi
manuellement, overlay, profil, base de données
ou release n'est modifié.

## Procédure Windows

Depuis la racine du dépôt, avec Dofus fermé :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

L'index Npcap constaté au dernier essai était **3** ;
relancer `--list` si la carte n'est plus la même.

Suivre les phases :

1. A1 : sur Draconiros, attendre le chargement
   en jeu, fermer complètement Dofus, puis Entrée ;
2. B : sur Tylezia, idem ;
3. A2 : revenir sur Draconiros, idem.

Une fenêtre de jeu à la fois. La capture
est lancée **avant chaque démarrage de Dofus**.

## Lecture de la sortie

Les lignes `p3m11=stage-...` comportent :

- `frames` : nombre de trames reconstituées ;
- `non-game-frames` : trames sans
  `type.ankama.com` (pas nécessairement
  toutes des messages Connection) ;
- `select-candidates` : occurrences respectant
  **la structure de sélection candidate** ;
- `distinct-values` : diversité de valeurs
  candidates dans cette phase ;
- `selector-session`, `selector-destination` :
  session TCP du candidat, quand univoque ;
- `truncated` : une limite du collecteur
  a été atteinte.

La ligne `p3m11=summary` est
`comparable=yes` **uniquement** quand chaque
phase a une connexion de jeu complète et
exactement **une** valeur candidate. Elle donne
les comparaisons A1/A2 et A1/B sans afficher
les valeurs numériques.

Si `select-candidates=0`, vérifier d'abord
`non-game-frames`. Il peut s'agir d'un
autre enmarquage de trames, d'une nouvelle
structure Protobuf, d'un canal TLS ou du
fait que le client choisit le serveur sans
réémettre ce message à chaque connexion.

## Résultat terrain P3m.11 — 22 octobre 2026

L'opérateur a exécuté `--connection-selection-study` avec
Draconiros (A1), Tylezia (B), Draconiros (A2), sur
l'interface Ethernet Npcap index 3.

| Phase | Connexions TCP | Session de jeu complète | Trames comptées | Trames sans Any | Candidates | Limite |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| A1 | 2 | #2 | 192 | 43 | 0 | oui |
| B | 4 | #6 | 172 | 52 | 0 | oui |
| A2 | 4 | #10 | 197 | 50 | 0 | oui |

Les connexions jeu avaient toutes été reconnues
et clôturées ; leurs destinations étaient respectivement
`54.216.176.68:5555`,
`54.246.90.95:5555` et
`54.216.176.68:5555`.

```text
p3m11=summary comparable=no reason=missing-ambiguous-or-incomplete-phase meaning=structural-candidate-only server=unverified
```

**Interprétation** : aucun candidat `root.f1.f4.f1`
n'a été détecté, mais le diagnostic a signalé des
limites atteintes dans les trois phases.
Le quota **192 trames par session comptabilisait
également les trames Game**, ce qui constituait un
défaut de conception du diagnostic :
il pouvait tronquer le parcours utile à l'étude
Connection pendant les rafales de chargement.

**Correctif P3m.11b** : conserver l'exécution
`--connection-selection-study` mais :

- ne plus consommer le budget Connection avec
  les trames reconnues comme `type.ankama.com` ;
- arrêter la collecte d'une session après `ksb` ;
- limiter séparément les trames sans Any à **256
  par session**, avec **8192 octets** maximum par trame ;
- distinguer `non-game-c2s` et `non-game-s2c` ;
- afficher des lignes `p3m11=socket` donnant
  pour chaque connexion TCP concernée **IP:port**,
  nombre de trames client→serveur hors protocole Game,
  ainsi que **les seuls numéros de champs Protobuf
  et leurs wire types** dans la racine et les
  sous-messages `f1` et `f4`.
  Exemples : `root(f1:L)/f1(f1:L.f4:L)/f4(f1:V)`
  ou `invalid` (forme non protobuf).
- conserver uniquement un **histogramme de
  signatures**, sans valeurs numériques, longueurs,
  UUID, caractères privés ou contenu texte.
  Au maximum 12 signatures par connexion, 8 imprimées.

Une signature dont la structure ressemble à
`f1.f4.f1` ne démontre pas sa sémantique.
En l'absence de match strict, les signatures
nous diront si le client utilise une autre
enveloppe ou si les candidats sont absents du
flux réellement observé.

Aucun nouveau serveur logique n'est déclaré vérifié,
aucune base/profil/overlay n'est modifiée.

## Résultat réel P3m.11b — 22 octobre 2026

Après correction du quota des trames, l'essai de l'opérateur
Draconiros → Tylezia → Draconiros a confirmé :

| Phase | Trames avant ksb | Hors Game C→S / S→C | Sessions complètes | Troncature | Match strict f1.f4.f1 |
| --- | ---: | --- | ---: | --- | ---: |
| A1 | 30 | 3 / 7 | 1 | non | 0 |
| B | 51 | 7 / 12 | 1 | non | 0 |
| A2 | 51 | 7 / 12 | 1 | non | 0 |

Mais les histogrammes structurels des trames du **client
vers serveur** contiennent **exactement** la signature :

```text
root(f1:L)/f1(f1:L.f4:L)/f4(f1:V)
```

sur les connexions **d'amorçage** suivantes :

- A1 : session TCP #1 → `34.247.205.111:5555`
- B : sessions #3 et #5 → `108.128.247.72:5555`
- A2 : session #7 → `52.30.61.61:5555` ;
  session #9 → `108.128.247.72:5555`

Les connexions de jeu distinctes et complètes avaient
pour destinations `34.252.214.29:5555`,
`54.77.100.255:5555` et `18.202.11.172:5555`.

**Interprétation :** l'enveloppe `f1.f4.f1` du protocole
Connection documenté est bien présente **structurellement**
sur les connexions courtes, mais l'ancien validateur
acceptait `f1.f4.f1` seulement si `root.f1.f1`
était une chaîne UTF-8 lisible **d'au moins deux octets**
et si la valeur sélectionnée était un entier positif
au plus égal à `int.MaxValue`.

Le journal P3m.11b ne révèle **pas** laquelle de ces
conditions faisait échouer le candidat ; la signature
n'affiche volontairement ni contenu, ni longueurs,
ni valeurs numériques. Il est donc incorrect de
déclarer ce champ `ServerId` confirmé.

**Correctif P3m.11c** :

- traiter le champ contexte `root.f1.f1` comme un
  ensemble **opaque de 1 à 256 octets**, sans exiger
  qu'il soit du texte UTF-8 ;
- exiger que `root.f1` contienne **exactement**
  les deux champs `f1:L` et `f4:L`,
  avec `f4` contenant seulement `f1:V` ;
- continuer de rejeter valeurs nulles ou hors
  plage `int32` positive ;
- produire `shape-matches` et
  `rejection-codes` catégoriels, notamment
  `contextabsentortoolong`, `zeroid`,
  `outofint32range`, `selectionfieldnotnumeric` ;
- ne jamais exposer de valeur, texte de contexte,
  jeton, UUID ou identifiant de compte dans
  les lignes de résultat.

Ce correctif ne branche aucune attribution automatique ;
`server=unverified` demeure en vigueur. Un test
supplémentaire A → B → A doit déterminer si une valeur
candidate est répétée pour A et distincte pour B.

## Résultat réel P3m.11c et nouvelle corrélation P3m.11d

L'opérateur a exécuté l'essai Draconiros (A1) →
Tylezia (B) → Draconiros (A2). Le décodeur P3m.11c
a reconnu **cinq candidats** `root.f1.f4.f1`
dans des trames client→serveur, sans aucun rejet,
sans troncature, et une session jeu complète
`ksc`+`ksb` par phase.

| Phase | Sélections observées | Connexions de jeu |
| --- | --- | --- |
| A1 | #1 sur `52.30.61.61:5555` | #2 : `ksc`, `ksb`, close |
| B | #3 et #5 sur `52.30.61.61:5555` | #4 : `ksc` seul ; #6 : `ksc`, `ksb`, close |
| A2 | #7 et #9 sur `34.247.205.111:5555` | #8 : `ksc` seul ; #10 : `ksc`, `ksb`, close |

Les phases B et A2 affichent deux valeurs
numériques distinctes chacune. Le résumé général
de P3m.11c `comparable=no` était donc logique :
il regroupait **plusieurs tentatives** en une
phase. Ce résultat **ne signifie pas que les valeurs
sont contradictoires ou que le schéma Connection est faux**.

### Hypothèse à tester

Les tentatives se succèdent exactement dans cet ordre
de sessions TCP :

```text
A1 : sélection #1 → jeu #2, personnage chargé
B  : sélection #3 → jeu #4, pas de ksb
     sélection #5 → jeu #6, personnage chargé
A2 : sélection #7 → jeu #8, pas de ksb
     sélection #9 → jeu #10, personnage chargé
```

**Possibilité non prouvée** : lors d'un changement de
serveur, une première sélection vise le contexte
précédent, puis une seconde vise le serveur demandé.
Pour la tester, P3m.11d évalue si la valeur de la
première tentative B est égale à celle de la
dernière tentative chargée A1, et si la première
tentative A2 est égale à la dernière chargée B.

### Diagnostic P3m.11d

Le même mode CLI `--connection-selection-study`
produit **en supplément**, sans supprimer les sorties
P3m.11 existantes :

- `p3m11=linked-pair` : association entre une
  session de sélection candidate **clôturée** et
  une session de jeu **immédiatement suivante**
  (`numéro de session + 1`) dans **la même phase** ;
- `observed-outcome=loaded` si `ksc` puis `ksb`
  et fermeture ont été observés ; sinon
  `observed-outcome=did-not-load` si `ksc`
  sans `ksb` puis fermeture. Le second état
  **ne prouve pas** que la connexion a échoué ;
- `p3m11=linked-stage-A1/B/A2` : nombre
  de tentatives, paires adjacentes et chargements,
  avec **un seul chargement complet exigé par phase** ;
- `p3m11=linked-summary` : égalité entre **les
  valeurs associées aux chargements complets**
  et comparaison de la première tentative
  incomplète de B/A2 à celle du chargement complet
  de la phase précédente.

Une phase n'est pas comparable s'il manque une
paire adjacente, si plusieurs chargements complets
existent, s'il reste des sélections sans paire,
si une session importante est incomplète ou
si le collecteur est tronqué/évincé.

**Important :** les numéros de sessions de capture
sont une corrélation temporelle expérimentale,
pas un identifiant de compte, personnage ou serveur.
Ce mécanisme ne doit pas être intégré au routage
multicomptes de production sans validation
indépendante. Les valeurs de `f1.f4.f1` ne
sont jamais affichées ni persistées.

Commande Windows après build/tests :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

La nouvelle sortie `p3m11=linked-summary` peut
avoir `comparable=yes` alors que le résumé brut
`p3m11=summary` reste `comparable=no` :
le second exige une seule valeur par phase,
le premier porte uniquement sur la sélection
associée au chargement complet.

**Prochaine étape** : si `loaded-pattern=A-B-A`
et les deux comparaisons des tentatives
intermédiaires sont `equal`, vérifier ensuite
que les valeurs candidates correspondent bien
aux `ServerId` officiels Draconiros/Tylezia,
sans se limiter à l'égalité. Ce diagnostic ne
fournit pas encore cette validation.

## Résultat réel P3m.11d — corroboration A-B-A

L'exécution Draconiros → Tylezia → Draconiros
du **22 octobre 2026** a produit :

```text
p3m11=linked-stage-A1 game-attempts=1 adjacency-linked=1 loaded=1 did-not-load=0 comparable=yes server=unverified
p3m11=linked-stage-B game-attempts=2 adjacency-linked=2 loaded=1 did-not-load=1 comparable=yes server=unverified
p3m11=linked-stage-A2 game-attempts=2 adjacency-linked=2 loaded=1 did-not-load=1 comparable=yes server=unverified
p3m11=linked-summary comparable=yes loaded-A1-vs-A2=equal loaded-A1-vs-B=different loaded-pattern=A-B-A B-did-not-load-vs-A1-loaded=equal A2-did-not-load-vs-B-loaded=equal meaning=sequence-only semantic=unverified server=unverified
```

Les deux tentatives sans `ksb` conservent exactement
la valeur candidate associée au personnage chargé
dans la phase **précédente**. La tentative suivante
sélectionne une autre valeur et charge un personnage
dans le serveur déclaré de la nouvelle phase.

**Conclusion** : comportement compatible avec le
fait que Dofus tente d'abord le serveur précédent
puis celui demandé ; la capture prouve le motif de
**sélections numériques**, pas la sémantique du
champ par elle-même. Les IP de jeu, qui varient,
ne sont toujours pas une clé stable.

## P3m.12 — Vérification des références numériques communautaires

Deux projets indépendants publient les mêmes IDs
numériques de serveur utilisés dans leurs listes :

- **Draconiros** → `295` ;
- **Tylezia** → `293`.

Sources consultées le 22 octobre 2026 :

- [DofusGuide — liste annoncée issue des filtres du ladder Ankama](https://github.com/Pierre-ZACHARY/dofusguideweb/blob/main/src/dofus/servers.ts)
- [Dofus-Craft — correspondance aux identifiants Huzounet](https://github.com/Fronne/Dofus-Craft/blob/main/worker/src/servers.ts)

Ce sont des **références communautaires concordantes**.
Nous n'avons pas encore vérifié directement que ce
sont les IDs officiels attendus par la version
actuellement installée du client Dofus.

Le mode `--connection-selection-study` ajoute les
lignes `p3m12=stage-A1`, `stage-B`, `stage-A2`
et `p3m12=summary`.

L'étude :

1. conserve la corrélation prudente : une sélection
   candidate sur la session N, clôturée, immédiatement
   suivie de la session jeu N+1, **dans la même phase** ;
2. exige exactement un chargement complet
   `ksc`+`ksb` dans chaque phase ainsi que
   l'absence de tentatives non appariées,
   troncatures et évictions ;
3. compare **uniquement la valeur numérique associée
   au chargement complet** de chaque phase à
   la référence attendue (A1/A2 = 295, B = 293) ;
4. ne compare pas l'essai intermédiaire sans `ksb`
   à l'ID attendu du nouveau serveur ;
5. n'affiche pas les valeurs capturées,
   les UUID, les jetons ou les caractères privés ;
6. affiche pour chaque phase
   `matches-reference=yes/no/unavailable`
   et un résumé `all-loaded-match=yes/no/unavailable`,
   toujours `provenance=community` et
   `semantic=unverified server=unverified`.

**Aucune attribution réseau automatique en production.**
Le serveur que l'utilisateur choisit dans BestCrush
reste autoritaire, sans validation obligatoire.

Exécution Windows (après `dotnet test`) :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

Parcours A1 Draconiros → B Tylezia → A2 Draconiros,
avec Dofus complètement fermé entre les phases.
Envoyer les lignes `p3m11=linked-*` et `p3m12=`.

Un `all-loaded-match=yes` serait une **corroboration
numérique forte**, mais ne prouverait ni
l'authenticité absolue du schéma Unity 3.6.11.15,
ni la fiabilité d'une association en situation
multicomptes simultanée.

## Résultat réel P3m.12 — 22 octobre 2026 : correspondance numérique complète

L'opérateur a réalisé Draconiros (A1) → Tylezia (B) →
Draconiros (A2), en fermant chaque client Dofus avant
de passer à la phase suivante. Le diagnostic
`--connection-selection-study` a confirmé **sans
troncature** une session jeu complète `ksc`+`ksb`
par phase, ainsi que des sélections candidates
`root.f1.f4.f1` sur les connexions d'amorçage.

Résultats P3m.11d :

```text
p3m11=linked-summary comparable=yes loaded-A1-vs-A2=equal loaded-A1-vs-B=different loaded-pattern=A-B-A B-did-not-load-vs-A1-loaded=equal A2-did-not-load-vs-B-loaded=equal meaning=sequence-only semantic=unverified server=unverified
```

Résultats P3m.12, références **Draconiros=295** et
**Tylezia=293** (deux répertoires communautaires
indépendants décrits plus haut) :

```text
p3m12=stage-A1 reference=Draconiros game-attempts=1 selections-linked=1 loaded=1 comparable=yes matches-reference=yes provenance=community server=unverified
p3m12=stage-B reference=Tylezia game-attempts=2 selections-linked=2 loaded=1 comparable=yes matches-reference=yes provenance=community server=unverified
p3m12=stage-A2 reference=Draconiros game-attempts=2 selections-linked=2 loaded=1 comparable=yes matches-reference=yes provenance=community server=unverified
p3m12=summary comparable=yes all-loaded-match=yes reference-ids=community-not-official semantic=unverified server=unverified
```

**Conclusion expérimentale solide :** les nombres capturés
dans le chemin du protocole Connection correspondent
exactement aux références numériques connues des
serveurs **effectivement chargés**, et le changement
de serveur conserve d'abord le candidat du serveur
précédent, avant celui du serveur demandé.

Aucune valeur extraite n'a été écrite dans la base
ou dans les fiches BestCrush. Le test est limité
à **un client à la fois**, deux noms de serveurs et
une association des sélections aux sessions Game
fondée sur leur **adjacence de numéros de sessions**.

La ligne `p3m11=summary comparable=no` n'est pas
une contradiction : le résumé brut exige une seule
valeur par phase, alors que deux valeurs sont
légitimement présentes pendant les changements.

Les schémas et la table de correspondance restent
de provenance externe ; ne pas considérer le
mécanisme de liaison au compte/personnage comme
validé en production.

### Étape suivante envisagée : P3m.13

**Ne pas réutiliser l'adjacence N→N+1 pour
associer le serveur à un client multicomptes.**
Les sessions de deux fenêtres peuvent être
entrelacées, et l'ordre global ne garantit
aucun propriétaire.

Faire d'abord un probe **opt-in, en RAM**, qui :

1. associe séparément chaque connexion
   **Connection** et chaque connexion **Game**
   au **quadruplet TCP** exact dans Npcap ;
2. lit le propriétaire de ce socket via la
   table TCP Windows **pendant que la connexion
   est établie** (les connexions Connection sont
   brèves et doivent être liées avant fermeture) ;
3. n'accepte que la liaison **unique PID ↔ HWND**
   d'une fenêtre Dofus déjà supportée par P3k
   et P3m.1 ;
4. teste sous **deux fenêtres simultanées** :
   serveurs différents, mêmes serveurs,
   reconnexions et intercalage des sessions ;
5. ne joint une sélection et une session Game
   que lorsqu'elles appartiennent **sans
   ambiguïté au même PID/HWND/cycle de fenêtre**
   et que la continuité temporelle est prouvée ;
6. refuse toute attribution si socket disparu
   avant observation, propriétaire absent ou
   multiple, fenêtre fermée, ou chronologie
   ambiguë ;
7. ne publie que des **indices anonymes
   de fenêtres**, des nombres de liaisons
   et des statuts catégoriels, sans compte,
   jeton, nom de personnage, tuple TCP ni ID privé.

La capture réseau est une aide **optionnelle**
à BestCrush, jamais une validation préalable du
serveur choisi manuellement. Le décodage des
professions/équipements et l'isolation des caches
métier par flux sont des sujets séparés.

**État des tests :** l'exécution du probe
P3m.12 atteste une compilation possible ;
le résultat de la suite complète `dotnet test`
après le dernier correctif n'a pas été transmis
dans cet échange.

## Suite

Si A1=A2≠B est observé : déchiffrer
**le sens structurel** grâce à la réponse
Connection/ServerList et aux identifiants
de serveur présents dans les données
publiques. Ne pas encore attribuer
automatiquement un serveur à un personnage.

Si aucun candidat : étude de l'enveloppe/framing
du Connection Server sur une session choisie,
sans collecte de tokens ou payloads persistés.

**Le serveur choisi par l'utilisateur reste
autoritaire dans BestCrush.**
