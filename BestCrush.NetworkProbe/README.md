# BestCrush.NetworkProbe

Prototype isolé de collecte réseau passive pour BestCrush.

## Objectif

Valider, sans toucher à la base BestCrush ni aux overlays, que le trafic Dofus TCP/5555 permet de reconstruire :

- les observations HDV (runes, ressources, équipements) ;
- les résultats de concassage ;
- le coefficient réel de concassage ;
- les runes obtenues et leurs quantités.

Le clic molette / OCR de focus n'est pas concerné par ce prototype.

## Prérequis Windows

1. Installer **Npcap**.
2. Activer le mode de compatibilité WinPcap pendant l'installation.
3. Vérifier que Dofus possède une connexion TCP établie vers le port 5555.

Le probe utilise SharpPcap + PacketDotNet et n'injecte aucun paquet : il écoute uniquement le trafic reçu/envoyé par la machine.

## Lancement

Lister les interfaces :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
```

Puis lancer la capture sur l'interface choisie :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --all
```

### P3m.13b : diagnostiquer les connexions Game encore ouvertes

La première exécution à **deux comptes**, Draconiros
et Kourial, a reconnu deux fenêtres et leurs sockets
sans conflit ni propriétaire manquant, mais les
deux associations ont échoué (`linked=0`).

**Cause identifiée :** la règle précédente imposait
la fermeture du socket de jeu avant d'accepter une
association. Or le test conserve volontairement les
deux fenêtres ouvertes. P3m.13b accepte une
connexion Game **encore active**, dès lors que
`ksc` et `ksb` ont été observés.

La première fenêtre a également **deux sélections
pour une tentative de jeu**. La nouvelle ligne
`p3m13=attempt` explique pourquoi un lien est
rejeté, notamment
`reason=multiple-selections` ou
`reason=multiple-distinct-selections`, sans
révéler les identifiants numériques.

**Ne pas attribuer arbitrairement la dernière
sélection**, même en présence de deux valeurs
identiques. La liaison réseau doit rester
sans ambiguïté.

Exécution inchangée après `dotnet test` :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-server-link
```

Garder **les deux clients ouverts** à l'arrêt.
Récupérer les lignes `p3m13=attempt`,
`p3m13=window` et `p3m13=summary`.

Voir `docs/modular-workspace-p3m13-window-server-link.md`.

### P3m.13 : relier les selections serveur aux deux fenetres Dofus

Les ServerId candidats **Draconiros=295** et
**Tylezia=293** ont été confirmés expérimentalement
sur un client à la fois dans P3m.12. **Pour P3m.13**,
le second compte est sur **Kourial=355**, confirme
par deux catalogues communautaires concordants.
Le mode `--window-server-link`
étudie désormais **deux clients simultanés** et associe
les connexions Connection/Game à la même fenêtre
à partir du **quadruplet TCP exact, PID propriétaire
Windows et HWND**. L'adjacence **globale** des sessions
TCP n'est pas utilisée.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-server-link
```

Démarrer le probe avec Dofus fermé, ouvrir le premier
compte sur Draconiros, puis le deuxieme compte sur Kourial
**sans fermer le premier**. Après le chargement
des deux personnages, laisser les deux fenêtres
ouvertes et appuyer sur Ctrl+C dans la console.
Envoyer les lignes `p3m13=`.

Les indices de fenêtres sont anonymes. Aucun
token, IP, PID, HWND, personnage ou ID capturé
n'est imprimé. Lien expérimental uniquement :
`server=unverified`, aucune attribution automatique
ou validation du serveur choisi manuellement.

Voir `docs/modular-workspace-p3m13-window-server-link.md`.

### Résultat réel P3m.12 : correspondance des ServerId candidats

**22 octobre 2026** : l'essai A1 Draconiros →
B Tylezia → A2 Draconiros a produit
`p3m12=summary comparable=yes all-loaded-match=yes`.

Les **trois chargements complets** correspondent
aux références communautaires : **Draconiros 295**,
**Tylezia 293**, **Draconiros 295**.
Les tentatives de transition non chargées
correspondent au serveur de la phase précédente
(`p3m11=linked-summary` : deux égalités).

**Validation expérimentale du ServerId candidat
sur un client à la fois**, pas validation
de la liaison multicompte, ni de la pérennité
du mapping pour une autre version Dofus.

La prochaine étude est le lien *Connection
TCP → PID/HWND → Game TCP* en présence de
deux fenêtres actives, par quadruplet TCP exact.
L'adjacence globale des sessions utilisée
en P3m.11d/P3m.12 **ne doit pas** servir
au routage multicompte de BestCrush.
Le serveur manuellement choisi reste
autoritaire.

Voir `docs/modular-workspace-p3m11-connection-select.md`.

### P3m.12 : comparer les valeurs candidates aux références 295 / 293

P3m.11d a fourni
`loaded-pattern=A-B-A`,
`B-did-not-load-vs-A1-loaded=equal` et
`A2-did-not-load-vs-B-loaded=equal`.
Ces trois critères corroborent la sélection d'un
serveur numérique, indépendamment de l'IP du
serveur de jeu.

Deux listes publiques indépendantes donnent
**Draconiros=295** et **Tylezia=293**.
P3m.12 compare ces références à la sélection
associée au **chargement complet** de chacune
des trois phases, et non à la tentative initiale
sans `ksb` en cas de changement de serveur.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

Les lignes `p3m12=` donnent uniquement
`matches-reference=yes/no/unavailable` ;
les valeurs réellement capturées ne sont pas
affichées, ni persistées. Les références
proviennent de communautés ; aucun serveur
de BestCrush ne fait l'objet d'une vérification
réseau obligatoire.

Voir `docs/modular-workspace-p3m11-connection-select.md`.

### P3m.11d : corréler chaque sélection TCP à son chargement

P3m.11c a reconnu un candidat numérique sur
Draconiros (A1), **deux** sur Tylezia (B)
et **deux** sur Draconiros (A2), sans aucune
troncature. Les sessions jeu correspondantes
étaient A1 #2 `ksc`+`ksb`,
B #4 `ksc` seul puis #6 `ksc`+`ksb`,
A2 #8 `ksc` seul puis #10 `ksc`+`ksb`.

La nouvelle sortie `p3m11=linked-*` associe,
uniquement pour l'étude et une instance Dofus,
une sélection candidate de session N à
une connexion jeu **N+1 de même phase**.
Elle compare exclusivement les candidats liés
à un chargement complet, et indique si les
tentatives incomplètes ressemblent à une
réutilisation de la sélection précédente.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

Reproduire A1/B/A2, recopier en particulier
`p3m11=linked-pair`, `linked-stage` et
`linked-summary`. Le résultat ne prouve
ni la sémantique du `ServerId`, ni une
association fiable multicomptes.

Voir `docs/modular-workspace-p3m11-connection-select.md`.

### P3m.11c : structure Connection trouvée dans les connexions d'amorçage

L'essai P3m.11b réel n'était plus tronqué et a identifié,
dans chaque phase Draconiros → Tylezia → Draconiros,
des messages C→S de forme

`root(f1:L)/f1(f1:L.f4:L)/f4(f1:V)`

**sur les sessions TCP courtes précédant le jeu**.
Malgré cela, le décodeur strict retournait zéro
candidat : il imposait que le champ de contexte
`f1.f1` soit du texte UTF-8 lisible.

P3m.11c accepte désormais un contexte opaque,
conserve les contrôles de forme/valeur de sélection,
et ajoute `shape-matches` ainsi que
`rejection-codes` pour lever l'ambiguïté
**sans afficher les contenus privés**.

Après mise à jour et `dotnet test`, relancer :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

Les lignes `p3m11=socket` et `p3m11=summary`
permettront de vérifier s'il existe une sélection
numérique candidate A1=A2≠B. Cela **ne valide pas**
encore la sémantique `ServerId`.

Voir `docs/modular-workspace-p3m11-connection-select.md`.

### P3m.11b : distinguer les trames de connexion des trames de jeu

P3m.11 : trois phases A1/B/A2 valides,
`non-game-frames=43/52/50` mais
`select-candidates=0` et
`truncated=yes` aux trois phases.

Un défaut du mode faisait consommer le quota de
192 trames par session par les **trames Game**.
Le correctif exclut ces trames du quota Connection
et produit de nouvelles lignes
`p3m11=socket` : destination TCP et **structure
protobuf des messages C→S hors Game**, sans jamais
afficher les valeurs de champs ou les jetons.

Même commande après mise à jour :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

La sortie `p3m11=socket` indiquera uniquement
les numéros de champs et leurs types (`L`, `V`,
etc.). Elle doit nous permettre d'identifier
l'enveloppe pertinente avant toute attribution
de `ServerId` en production.

Voir `docs/modular-workspace-p3m11-connection-select.md`.

### P3m.11 : recherche du message de sélection numérique `SelectServer`

P3m.10 montre plusieurs connexions Dofus en TCP/5555
par phase, sans identifier leur rôle logique.
Les schémas publics du protocole Unity **Connection**
montrent deux familles de messages ayant un champ
numérique candidat `root.f1.f4.f1` (sélection serveur).

Le mode expérimental suivant inspecte les trames TCP/5555
**avant** le filtre qui écartait les messages sans
`type.ankama.com`, sur A1 (Draconiros) → B (Tylezia)
→ A2 (Draconiros) :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

Il n'affiche jamais les tokens ni les valeurs candidates.
Il signale le nombre de trames sans enveloppe Game,
les sélections candidates et la correspondance A-B-A.
Le sens `ServerId` reste **à vérifier** et la
sélection manuelle reste autoritaire.

Voir `docs/modular-workspace-p3m11-connection-select.md`.

### P3m.10 : relever les autres connexions TCP du client / launcher

P3m.9 a observé **96 messages Ankama décodés** (32 par
connexion A1/B/A2) sur TCP/5555, sans rencontrer aucun
des deux noms publics `Draconiros` ou `Tylezia`.
La capture était `coverage=bounded` et non exhaustive
du trafic hors de ce port.

Ce nouveau script Windows recense en RAM les sockets
TCP des processus contenant `dofus` ou `ankama`
sur **tous les ports** pendant A1 → B → A2. Il affiche
leur **processus, IP et port distants** ; aucun payload.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\probe_dofus_all_ports.ps1
```

Fermer Dofus avant le début et entre les phases, puis
appuyer sur Entrée après chaque fermeture complète.
Transmettre les lignes `p3m10=`.

**Limites** : sondage de sockets Windows (500 ms par
défaut), pas de capture de toutes les ouvertures,
pas de preuve de serveur ; d'autres processus, UDP/QUIC,
TLS ou des connexions très courtes peuvent échapper
à ce contrôle. P3m.8 est toujours reporté.

Voir `docs/modular-workspace-p3m10-upstream-tcp-endpoints.md`.

### P3m.9 : détecter les noms des serveurs Dofus en connexion

Le nom du serveur pourrait être transmis directement, soit dans
un message de sélection, soit dans une liste qui le relie aux
personnages. Les essais antérieurs cherchaient surtout des
**nombres**, et P3m.9 cherche **deux noms publics connus**
dans les champs texte des messages décodés sur TCP/5555.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-name-study
```

Saisir les noms des serveurs A et B, puis reproduire
**A1 → B → A2**, une fenêtre Dofus à la fois. Fermer
complètement le jeu avant d'appuyer sur Entrée à chaque phase.

La sortie `p3m9=` donne direction/opcode/chemin de champ et
indique si le nom A ou B apparaît dans le message. Elle
distingue également les cas où **les deux noms apparaissent
dans une liste commune**, ce qui ne prouve pas la sélection.

**Limite :** uniquement les corps de messages Ankama décodés
sur TCP/5555 ; la sélection peut avoir lieu sur un autre
canal. Aucun `ServerId` n'est déclaré validé et le serveur
choisi manuellement dans BestCrush reste autoritaire.
P3m.8 est conservé mais reporté.

Voir `docs/modular-workspace-p3m9-server-name-wire-study.md`.

### P3m.8 : vérifier si `kta.f1` désigne le personnage sélectionné

P3m.7 a montré que `kta.f1` **change entre deux personnages
du même serveur et du même compte**, mais reste stable
pour un même personnage après reconnexion, y compris
lorsque l'adresse IP du jeu change.

Un nouveau diagnostic expérimental compare `kta.f1`
(client→serveur) au candidat `parent.f3` du personnage
chargé dans `ksb`, après contrôle de présence de
ce personnage dans la liste `ksc`. Les données
sont isolées par connexion TCP et par étape utilisateur.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --kta-character-compare
```

Fermer Dofus puis lancer une connexion pour **X**,
fermer le jeu et appuyer sur Entrée, puis faire de même
pour **Y, autre personnage du même compte et du même serveur**.
Transmettre uniquement les lignes `p3m8=`.

Cette égalité, si observée, resterait
`identity=unverified` jusqu'à corroboration. Aucune
validation obligatoire du serveur ni intégration
de l'identité au routage de production.

Voir `docs/modular-workspace-p3m8-kta-selected-character.md`.

### P3m.7 : contrôle ciblé `kta:f1` — personnage ou serveur ?

P3m.6 a relevé un seul candidat `C-S:kta:f1` suivant
`A1=A2≠B`, avec `coverage=partial` dans l'exploration
générale. Son sens reste **inconnu** : ID de personnage,
compte/serveur, sélection ou autre entier.

P3m.7 lit **uniquement** le `f1` varint de `kta`
sur les premières connexions et le compare sur
**quatre étapes**, toujours sur le même compte et
avec une seule fenêtre Dofus :

1. serveur A, personnage X ;
2. même serveur A, personnage Y différent ;
3. serveur B, personnage Z ;
4. serveur A, **retour au personnage X**.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --kta-f1-control
```

Fermer complètement Dofus avant d'appuyer sur Entrée
à chaque étape. À la fin, recopier les lignes `p3m7=`.

Le diagnostic affiche l'IP/port de la session jeu et les
comparaisons `equal/different`, jamais la valeur de `kta:f1`.
Les résultats demeurent `semantic=unverified server=unverified`.
Aucune attribution au moteur métier.

Voir `docs/modular-workspace-p3m7-kta-f1-control.md`.

### P3m.6 : découverte de chemins réseau candidats pour le serveur logique

Le test A → B → A a donné une reconnexion au serveur A via
`34.252.214.29:5555` et la connexion B via
`54.77.100.255:5555`, **sans preuve sémantique du serveur**.

Le mode expérimental suivant recherche, dans les messages
protocole **TCP/5555 avant le chargement du personnage**,
des champs numériques qui suivent un motif **A1 = A2 ≠ B**.
Il n'affiche que les directions, opcodes et chemins de champs,
jamais leurs valeurs réseau, et n'active aucune attribution.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-wire-study
```

Exécuter sur un seul compte et une seule fenêtre Dofus,
en suivant les trois phases affichées : connexion A1,
fermeture puis Entrée, connexion B, fermeture puis Entrée,
retour au **même personnage** A2, fermeture puis Entrée.

**Attention :** le motif peut tout autant provenir d'un
identifiant de **personnage** que d'un serveur : les résultats
sont `semantic=unverified server=unverified`. Dofus pourrait
choisir un serveur via un autre port ou protocole non écouté.
La capture de production, les fiches et la base sont inchangées.

Voir `docs/modular-workspace-p3m6-server-wire-candidates.md`.

### P3m.5 : transition serveur A → B → A (destination réseau uniquement)

P3m.4 a trouvé un candidat f3 stable après reconnexion, mais
**aucune correspondance f3 dans les autres messages ciblés**
(8 messages examinés par session, 2 sessions valides).
Cela n'invalide pas f3, mais n'apporte pas une troisième
famille de preuve.

Pour étudier le routage réseau **sans attribuer de serveur**,
le nouveau mode expérimental compare en RAM les destinations
TCP de trois connexions, annoncées manuellement A → B → A :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-route-study
```

Une seule fenêtre Dofus à la fois. Fermer complètement le
client, puis **appuyer sur Entrée** entre chaque phase. La
capture doit reconnaître `ksc` et `ksb` dans une session TCP
clôturée par phase pour produire des comparaisons
`equal/different`, ainsi que **l'IP et le port serveur**
des sessions de jeu reconnues. Aucun jeton ni contenu de
paquet n'est affiché. Les résultats `A-B-A`, `same-all` ou
`other` portent exclusivement sur les destinations TCP et
**ne vérifient pas le serveur logique Dofus**.

Voir `docs/modular-workspace-p3m5-server-route-transition.md`.

### P3m.4 : recherche exploratoire d'un troisième opcode contenant f3

Les essais réels ont montré que le candidat `parent.f3` est
stable après reconnexion, concordant entre `ksc` et `ksb` et
différent entre deux personnages d'un même compte/serveur
(selon l'observation locale de l'opérateur). Cela **ne prouve pas
sa sémantique officielle** ; `ksc` et `ksb` partagent une
structure proche.

Le mode expérimental suivant inspecte une liste fermée de
messages serveur de connexion afin de rechercher des chemins
numériques où la valeur est égale à ce candidat dans la
**même session TCP**. Aucun nombre, nom, niveau ni payload
n'est affiché ou enregistré. Une égalité dans un autre opcode
n'est qu'une piste, pas une confirmation indépendante.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --identity-cross-opcodes
```

Saisir une seule référence locale `Nom:Niveau` et connecter
ce personnage deux fois, sans arrêter le probe entre les
connexions. Terminer par `Ctrl+C` et consulter les seules
lignes `p3m4=` anonymisées. L'index 6 correspond à l'interface
Ethernet du poste étudié le 21/10/2026 ; vérifier `--list`
s'il change.

La **preuve du serveur réseau** nécessite une étude séparée :
ni l'interface Npcap, ni le PID, ni l'IP du serveur, ni le serveur
sélectionné manuellement dans BestCrush ne la démontrent.
Aucune attribution automatique de personnage/profil.

Voir `docs/modular-workspace-p3m4-cross-opcode-field-study.md`.

### P3b : comparaison anonyme de la continuité des métiers

Après validation P3a réelle A → B → A, le mode
`--profession-continuity` compare les vingt identifiants
`f5` candidats, les niveaux et les compteurs XP de
chaque `ipz` à la **première capture valide** de cette
exécution, uniquement en RAM.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-continuity
```

**Un seul client DOFUS à la fois, un même compte +
serveur pour l'essai.** Le diagnostic affiche uniquement
`PROFESSION_CONTINUITY` avec `yes/no/unavailable`,
jamais d'identifiant, d'XP, de payload ou de hash.
L'égalité des métiers n'établit **pas** l'identité
du compte ; les résultats affichent toujours
`account=unverified server=unverified mapping=unverified`.
Aucun profil ni mapping n'est modifié.

Voir `docs/modular-workspace-p3b-profession-continuity.md`.

### P3a : métiers partagés par compte + serveur (sans attribution)

Pour observer `ksc → ksb → ipz` dans une même connexion
TCP sans saisir le nom du personnage ni des références XP :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-flow
```

Ce mode autonome émet uniquement `PROFESSION_FLOW` avec
des compteurs et des indicateurs anonymisés : qualité de
décodage, égalité des fiches candidates dans la session,
présence et unicité des 20 IDs `f5` candidats.
L'alternance A → B → A, les erreurs et FIN/RST révoquent
l'observation du chargement du personnage, **pas les métiers
du compte**. Les métiers sont partagés sur le couple
**compte + serveur**, non stockés par personnage.
**Toujours**
`scope=account-server account=unverified server=unverified mapping=unverified` :
le diagnostic TCP ne vérifie pas l'identité du compte
ni du serveur et n'attribue ou persiste aucun métier.
La structure `CurrentCharacterState.ProfessionLevels`
par personnage reste une dette d'architecture à refondre.

Voir `docs/modular-workspace-p3a-profession-flow-study.md`.

### P2j : décodage expérimental de la classe candidate

Le décodeur de `ksb` et `ksc` extrait maintenant
`ClassIdCandidate` depuis `leaf.f5.f3` lorsque
le champ est présent et plausible, mais laisse
`ClassId=null` et `IdentityConfirmed=false`.
Dans une même connexion TCP, la sortie
`p2j=class-candidate=all/partial/none class-link=yes/no/pending/unavailable class=unverified`
permet de comparer la classe candidate entre les familles
sans afficher sa valeur. Le routage métier et les fiches
BestCrush restent inchangés.

Voir `docs/modular-workspace-p2j-class-candidate-decoder.md`.

### P2i : comparer f5.f3 à une classe attendue

Pour vérifier **localement** l'hypothèse que `leaf.f5.f3`
contient le code de classe, lancer :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields --expected-class-id 6
```

Le champ `--expected-class-id` est optionnel et accepté
uniquement avec `--identity-fields`. Il provoque une simple
comparaison anonymisée `p2i=f5-f3-check reference-equal=yes/no`,
sans afficher de valeurs réseau ni confirmer l'identité.
Voir `docs/modular-workspace-p2i-class-id-candidate-check.md`.

### P2h : lecture anonyme de la structure interne de leaf.f5

Le mode `--identity-fields` inclut maintenant un relevé
`p2h` pour `leaf.f5`. Il n'affiche que la structure
protobuf potentielle et les chemins des sous-champs
qui diffèrent entre les personnages de `ksc`,
jamais les valeurs. Un contenu non décodable reste
`opaque`. Le champ de classe demeure inconnu.
Voir `docs/modular-workspace-p2h-leaf-f5-shape.md`.

### P2g : structure des champs de classe possibles

Le mode `--identity-fields` ajoute un relevé `p2g` limité
aux types et chemins des champs protobuf des fiches
`ksb`/`ksc`, ainsi qu'aux **chemins** dont les valeurs
diffèrent entre deux entrées de `ksc`.
Aucune valeur personnelle n'est publiée. La classe reste
`unknown`, et l'identité reste `unverified`.
Voir `docs/modular-workspace-p2g-class-field-shape.md`.

### P2f : décodeur d'identité candidat (non confirmé)

Le mode existant `--identity-fields` teste maintenant un
décodeur strict et versionné pour `ksb`/`ksc`
(Dofus 3.6.11.15). Il peut afficher
`p2f=decoded candidates=N class=unknown candidate-link=yes/pending/no
identity=unverified`, sans révéler de nom, niveau, identifiant,
payload ou empreinte. Aucune attribution automatique n'est
activée et la classe n'est pas encore décodée.

Voir `docs/modular-workspace-p2f-candidate-identity-decoder.md`.

### P2b : comparer ksb et ksc sur la même connexion TCP

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

Fournir `NomDuPersonnage:Niveau` dans la console locale.
Le diagnostic vérifie le nom exact et le niveau à leurs
chemins protobuf P2a dans `ksb` et `ksc`, puis affiche
uniquement les **numéros** de champs numériques égaux dans
les deux messages, au sein de la même connexion. Il ne
publie aucune valeur, ne mémorise rien sur disque et n'active
aucune identité. Retirer la ligne d'entrée contenant le nom
avant de partager les résultats. Voir
`docs/modular-workspace-p2b-identity-sibling-fields.md`.

### P2a : exploration des champs d'identité, valeurs privées masquées

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-name
```

Saisir le **nom exact du personnage** et son niveau dans
le terminal local au format `NomDuPersonnage:Niveau`.
Seuls les chemins protobuf où le nom apparaît exactement,
et les chemins numériques voisins égaux au niveau déclaré,
sont affichés. Les valeurs, identifiants, adresses TCP et
trames ne sont jamais affichés ni sauvegardés. La **ligne
de saisie elle-même** reste visible dans le terminal :
la masquer avant de partager une capture.

Les messages ciblés sont `krg`, `ksb`, `kes`, `jev`,
`jgi`, `ksc`, `knc`, `kod`, `ifg`, `kmz`.
Ce mode expérimental n'identifie pas encore un personnage :
voir `docs/modular-workspace-p2a-identity-field-discovery.md`.

### Identification ciblée par couple niveau/XP (P1f)

Pour un métier qui était ambigu au niveau 1, il suffit de le faire
progresser jusqu'à posséder un couple `niveau:XP totale` unique.
Le mode `--profession-identify` demande de 2 à 20 couples
`Métier:Niveau:XPtotale` dans sa console. Aucun état initial ni
aucun seuil de progression supplémentaire n'est nécessaire.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-identify --show-candidate-ids
```

`--show-candidate-ids` est volontaire : sans cette option,
seuls des compteurs de correspondance sont affichés. Les noms et
les valeurs `f5` n'apparaissent que pour des correspondances
univoques dans le même message `ipz`, lorsque les 20 `f5`
sont présents et différents. Aucun mapping n'est écrit dans
le dépôt automatiquement ; les IDs restent candidats.

Voir `docs/modular-workspace-p1f-profession-identification.md`.

### Étude ciblée des champs métiers (P1e)

Le mode `--profession-study` corrèle les deux seuils XP
supposés (`f3` / `f6`), recense **tous les types** de `f4`,
et teste si `f5` peut servir de clé de métier.
L'interface demande des références locales
`Métier:Niveau:XPtotale:XPdansNiveau:XPnecessaire` uniquement en RAM.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-study
```

Par défaut, aucune valeur du message ni ID candidat n'est affiché.
Le drapeau explicite `--show-candidate-ids` autorise l'affichage
local des seuls couples `Métier / f5 candidat` ayant passé les
contrôles (sans les valider comme IDs globaux). Voir
`docs/modular-workspace-p1e-ipz-field-study.md`.

La table **versionnée**, sans données de personnage, est
`BestCrush.Network/Protocol/ipz-profession-mapping.3.6.11.15.json`.
Ses 20 identifiants sont volontairement `null` avant validation.

### Décodeur expérimental des métiers (P1d)

Mode `--profession-decode` : demande des couples `niveau:XP_totale` dans le terminal,
puis affiche seulement le nombre de lignes `ipz`, les correspondances et les
statistiques des champs inconnus. Voir `docs/modular-workspace-p1d-ipz-profession-decoder.md`.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-decode
```

### Étude comparative des métiers (P3f — expérimental)

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-job-study
```

**Ne pas utiliser `--profession-xp-study` pour comparer deux métiers :**
ce mode P3e impose que les XP augmentent globalement, ce qui
n'est pas vrai lorsqu'on change de métier.

Le mode **P3f** est indépendant et **opt-in**.
Il conserve temporairement les champs numériques de 16 messages
`ipz` mono-entrée au maximum, dans la première session TCP
observée. Ni contenu brut, ni identifiant de métier, ni ID de compte
ne sont affichés ou enregistrés.

Protocole conseillé sur un seul compte/client et serveur :

1. Choisir deux métiers, par exemple **Sculpteur** et **Bricoleur**.
   Pour chaque métier, connaître le niveau et l'XP affichés
   **après la fabrication**. Noter l'index `PROFESSION_JOB` associé.
2. Effectuer **deux fabrications donnant de l'XP par métier**,
   idéalement dans l'ordre Sculpteur → Bricoleur → Sculpteur →
   Bricoleur. Deux fabrications consécutives de chaque métier
   sont aussi acceptées si les ateliers sont éloignés.
3. Arrêter avec **Ctrl+C** puis renseigner :
   `index:Métier:Niveau:XP`, index croissants. Exemple **fictif** :

   ```text
   1:Sculpteur:6:390,2:Bricoleur:12:875,3:Sculpteur:6:420,4:Bricoleur:12:900
   ```

Les valeurs d'XP doivent progresser **pour chaque métier séparément**,
pas nécessairement entre deux métiers différents. Au moins
deux références par métier et au moins deux métiers sont requis.
Les autres `ipz` sans fabrication peuvent être omis en sautant
leur index. **Attention : les indices ne peuvent pas dépasser le nombre
de messages `ipz` capturés.** En cas de faute de frappe ou d'index
inexistant, le programme **redemande les références sans perdre
la capture**, tant que le processus est toujours actif. Appuyer sur
Entrée sans rien saisir permet de quitter volontairement.
Les champs niveau (`f1.f1`) et XP (`f1.f2`) doivent
correspondre aux références pour **tous** les événements retenus ;
sinon, aucun candidat de métier n'est déclaré.

Le résultat `p3f=compared` contient uniquement les **chemins**
des champs stables au sein de chaque métier et distincts entre métiers,
ainsi que `f1.f5-stable-distinct=yes/no`. Un résultat
`candidate-status=ambiguous` est normal : les seuils d'XP
peuvent également être stables pour un métier. Même
`candidate-status=single-candidate` ne prouve pas un ID
de métier global ; un autre métier, plusieurs niveaux
ou une nouvelle session devront servir de contrôles.

**Aucun mapping n'est créé** dans BestCrush ; compte et serveur
non vérifiés. Les tests d'isolation multi-comptes restent
reportés. Voir
`docs/modular-workspace-p3f-profession-identity-study.md`.

### Diagnostic XP métier mono-entrée (P3e — expérimental)

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-xp-study
```

Cette expérience est **opt-in** et indépendante de tous les autres
modes. Elle conserve temporairement les champs `varint` d'un maximum
de **16 messages `ipz` à une entrée**, reçus du serveur sur
**la première session TCP valide**. Aucun autre opcode ou message
client → serveur n'est étudié. Le snapshot `ipz` complet
(20 métiers) est ignoré, pas réinterprété.

1. Avant le diagnostic, noter le niveau et l'XP du métier.
2. Lancer la commande, effectuer **deux fabrications seulement**
   qui rapportent de l'XP, puis noter l'XP obtenue après chacune.
3. Attendre quelques secondes entre les fabrications ; noter les
   heures des actions. Ne pas ouvrir d'autres panneaux pendant l'essai.
4. Terminer avec **Ctrl+C**. Le probe affiche
   `PROFESSION_XP=[p3e=mono-ipz accepted=yes index=1 ...]`,
   `index=2`, etc. L'index est **local à la capture**.
5. Répondre à la question **après Ctrl+C**, en donnant les
   références `index:niveau:XP` dans l'ordre des messages
   `ipz` associés aux fabrications.
   Exemple **fictif** : `1:3:124,2:3:138`.
   Si un autre `ipz` non lié à une fabrication est présent,
   sauter simplement son index.

L'analyse compare les champs varint à **au moins deux XP distinctes**,
puis affiche uniquement des chemins candidats
(`xp-field-candidates=[f1.f2]`) ou `none`/`ambiguous`.
Les niveaux sont contrôlés séparément. Les valeurs réseau, champs
bruts, identifiants, hashes, contenus PCAP et références locales
ne sont jamais recopiés dans le résultat ni enregistrés sur disque.
La saisie locale peut rester visible dans ton propre terminal :
**ne la partager qu'après anonymisation si nécessaire**.

Il ne s'agit **pas** d'une validation sémantique du champ :
le matching exact sur deux fabrications ne prouve pas
l'identité d'un métier ni du compte. Le protocole et
le mapping sont toujours `unverified`. **Aucun métier
n'est injecté dans BestCrush**. Une seule session/client
peut participer à la comparaison ; redémarrer le probe
pour une nouvelle expérience.

Voir `docs/modular-workspace-p3e-profession-xp-progression.md`.

### Analyse des structures protobuf (P1b — sans valeurs personnelles)

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape
```

Les opcodes sont ciblés à partir des observations de connexion précédentes
(`kta`, `krg`, `irl`, `ico`, `jei`, `isr`, `irp`, `mdp`, etc.).
Pour réduire la sortie à quelques messages :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys irl,ico,jei,isr
```

Exemple de format **fictif** :

```text
[14:31:03.000 UTC] Session #1 S→C irl 64 octets shape=[root=[2:lenx4] sampled=[?2.1:vx3]]
```

`root=[...]` compte les champs **sur tout le message**. `2:lenx4`
signifie quatre occurrences du champ n° 2, à contenu opaque. `sampled=[...]`
résume seulement un échantillon des premières occurrences imbriquées (au plus
trois par chemin) : ses répétitions ne sont **pas** celles du message complet.
`1:v` désigne un champ varint, jamais sa valeur. Le `?` signifie
« sous-message protobuf plausible », et non identifié de manière certaine.
`[partial-root]` indique que la limite des 32 768 champs racine a été
atteinte. `unrecognized` désigne un corps non reconnu comme protobuf.

Les valeurs, textes, quantités, identifiants, noms et données bancaires ne sont
jamais affichés, hachés ni enregistrés. Limites : messages de 256 Kio,
32 768 champs racine, 512 champs imbriqués échantillonnés,
profondeur 2, 48 chemins affichés par section et **au plus trois
structures distinctes par opcode et connexion**. Ce mode masque tous les autres
messages et le bruit réseau ordinaire. Il reste distinct du diagnostic
historique `--all`, qui peut afficher des données sensibles.

### Exploration des personnages (P1a — sans valeurs personnelles)

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-scan
```

Ce mode est **indépendant de l'analyse de marché** et affiche uniquement :

```text
[15:42:18.100 UTC] Session #1 S→C abc 42 octets
[15:42:19.020 UTC] Session #1 C→S def 7 octets
[15:42:20.450 UTC] Session #1 S→C CLOSED
```

Chaque numéro de session identifie une **connexion TCP spécifique**. Aucun endpoint, nom de personnage, contenu protobuf, token ou payload n'est imprimé ou sauvegardé par ce mode. Les flux sont plafonnés à 128 connexions et purgés après 30 minutes d'inactivité. Un FIN/RST est journalisé même sans payload. `--character-scan` prend la priorité sur `--all` lorsqu'ils sont combinés.

**Attention :** les traces terminal peuvent toujours révéler un timing d'activité. Ne partage que les lignes nécessaires et relève manuellement l'heure des actions en jeu. Aucun décodage d'identité, de métier, de banque ou de kamas n'est encore revendiqué.

`--all` affiche chaque message Ankama détecté dans le **mode historique**. Sans `--all`, seuls les événements sémantiques reconnus sont affichés. Ce mode historique peut afficher des contenus réseau : ne pas le choisir pour l'étude des personnages.

## Mapping protocole

`../BestCrush.Network/protocol-map.json` est la source canonique, copiée en sortie du Probe sous `protocol-map.json`. Ce fichier contient les opcodes observés pour le build Dofus testé :

- `price_list = jzn`
- `crush_result = kci`

Les opcodes étant obfusqués et susceptibles de tourner entre builds, le reste du code n'utilise jamais ces chaînes directement.

## Test de validation

1. Ouvrir une rune ou une ressource en HDV et comparer les lots/prix affichés avec `[MARKET]`.
2. Ouvrir un équipement et comparer les offres individuelles.
3. Effectuer un concassage et comparer `[CRUSH]` avec l'écran :
   - UID d'instance ;
   - coefficient ;
   - IDs des runes ;
   - quantités.
4. Répéter sur plusieurs objets avant toute intégration à la base BestCrush.

## Limites V0

- sélection manuelle de l'interface réseau ;
- mapping limité aux messages déjà identifiés ;
- pas encore de résolution UID -> DofusDbId pour le concassage ;
- aucune écriture dans `bestcrush.db` ;
- aucun remplacement de F8/F9/OCR à ce stade.


### P3g : vérifier les identifiants publics des métiers et les 20 lignes ipz

Ce diagnostic **opt-in** enrichit P3f sans toucher aux données BestCrush :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-job-study --check-public-job-ids
```


Démarrer le probe **avant** de connecter le personnage, afin de capter
le `ipz` complet (20 lignes) ; conserver le même client et la même connexion
TCP pour les deux métiers et au moins deux fabrications XP par métier.
Après Ctrl+C, saisir `index:Métier:Niveau:XP` comme dans P3f.

Le contrôle P3g compare localement `f1.f5` à une table **publique et
non encore validée pour Dofus 3.6.11.15** (Frigost.dev, `Jobs.json`,
9 octobre 2026). Il affiche seulement `match=yes/no/unavailable`.
Si et seulement si le résultat est `yes`, il examine le roster complet
et indique combien des **20 métiers de référence** y figurent, les
éventuels noms publics absents/ajoutés et la présence de doublons.
La source contient **23** ID (notamment Base, Bestiologue, Éleveur) ;
20 lignes de capture ne prouvent pas à elles seules que les 20 ID de
référence sont présents. Le roster peut être `not-captured`.

Les valeurs numériques observées sur le réseau, niveaux, XP, noms de
personnages, adresses et paquets ne sont pas affichés ou persistés.
Résultat positif = concordance **locale**, pas preuve d'identité
compte/serveur ni activation du mapping de production.
Voir `docs/modular-workspace-p3g-public-job-id-check.md`.


**P3g.1 (capture 2026-10-19 UTC)** : `match=yes` sur Cordonnier=15
et Sculpteur=13. La capture complète a donné vingt ID distincts, tous
connus, mais l'ancienne liste de test P3g attendait Parchomage=75
et non Éleveur=79 : d'où 19/20. Le nouveau jeu de 20 valeurs
reprend celui déjà décrit dans le mapping versionné de recherche.
Les associations des autres métiers ne sont pas individuellement
revalidées par P3g.1. L'attribution à compte/serveur reste bloquée.


### P3k — fenêtre Dofus ↔ socket TCP ↔ flux Npcap

~~~powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-flow-link
~~~

Avec deux clients connectés, ouvrir l'inventaire sur chacun
pour déclencher du trafic, puis arrêter avec Ctrl+C.
Le diagnostic compare le quadruplet TCP de la capture Npcap
au socket IPv4 Windows propriétaire et à sa fenêtre Dofus.
Les seuls résultats sont des index anonymes et des indicateurs
matched/not-confirmed ; aucun contenu du jeu ni identifiant
personnel n'est conservé.

Un statut two-client-links-candidate démontre deux liens
fenêtre/socket/capture candidats indépendants, pas l'identité
du compte ni du serveur. Voir
docs/modular-workspace-p3k-window-tcp-flow-link.md.
