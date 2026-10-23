# P3c — Caractérisation indépendante du compte et du serveur

## Checklist P3c — validations reportées

**Statut : ÉTUDE RELANCÉE — deux comptes Dofus distincts disposent désormais d'un personnage sur un même serveur (23 octobre 2026).**
Ces contrôles ne sont pas requis pour poursuivre les autres phases
de recherche, mais **aucune identité de compte vérifiée, attribution
automatique des métiers ou persistance compte + serveur** ne doit être
activée tant qu'ils restent non validés.

- [x] Disposer d'un **second compte Dofus distinct**, avec
  un personnage **sur le même serveur** que le premier (confirmé par l'opérateur le 23/10/2026).
- [ ] Contrôle positif : personnage A → B sur un seul compte / serveur ;
  le candidat de compte reste stable.
- [ ] **Contrôle négatif indispensable : compte A → compte B sur le même serveur** ;
  le candidat de compte doit différer, sans déduction à partir de
  `ipz`, `ksc`/`ksb`, du nom de personnage ou des connexions TCP.
- [ ] Contrôle serveur : utiliser le même compte sur deux serveurs
  distincts, si disponible ; le candidat de serveur doit différer.
- [ ] Contrôle multiclient : deux comptes simultanés, puis changement
  de fenêtre, déconnexion/reconnexion et messages tardifs ; aucune
  contamination de compte, serveur ou métiers entre sessions.
- [ ] Vérifier que les preuves reposent sur des messages
  **sémantiquement indépendants et caractérisés**, sans consulter,
  imprimer, hacher ou sauvegarder d'authentifiants, tokens, payloads
  ni identifiants personnels ; seuls des indicateurs anonymes sont permis.
- [ ] Seulement après validation : définir un modèle de métiers
  **compte + serveur**, distinct des profils personnages,
  avec isolation stricte et tests synthétiques.

**Décision actuelle :** laisser `account=unverified server=unverified`
et ne pas interpréter l'égalité des vingt métiers P3b comme une
preuve d'identité. Reprendre cette checklist lorsque le second
compte sera disponible.

## Avancement parallèle P3d

P3d prépare la séparation des métiers hors des profils
personnages et leur affichage compte + serveur, sans
ingestion réseau ni association automatique.
Voir `docs/modular-workspace-p3d-account-professions-workspace.md`.

**Les contrôles d'identité multi-compte et les tests
d'isolation restent reportés dans cette checklist** :
aucun second compte de test n'est encore disponible.
L'intégration des métiers à un compte réel est
toujours bloquée jusqu'à leur validation.

## État confirmé

- P2j : `ksc` et `ksb` concordent sur une même session
  pour les champs candidats du personnage et de sa classe.
- P3a : parcours réel A → B → A avec trois sessions TCP,
  chacune transportant 20 métiers `ipz` valides.
- P3b : dans trois connexions successives A → B → A,
  `f5-set-equal=yes levels-equal=yes xp-equal=yes`.
- Les métiers sont partagés au niveau **compte + serveur**.
  Aucun message déjà décodé n'établit l'identité fiable
  du compte ou celle du serveur du trafic observé.

**Ne jamais déduire le compte** de la similarité des
métiers, du nom de personnage, du `ksc`/`ksb`,
d'un numéro de session TCP, d'une adresse IP ou du
serveur sélectionné manuellement dans l'application.

## Deux informations distinctes

1. **Serveur** : `CurrentServerState` contient un serveur
   explicitement choisi par l'utilisateur et un bail
   générationnel protégeant les messages en attente.
   Ce bail n'authentifie pas le serveur du jeu.
2. **Compte** : aucune `AccountKey` vérifiée
   n'est actuellement déduite du protocole.
   Un compte n'est pas la même chose qu'un personnage
   et ne doit pas être confondu avec le client TCP.
   Une détection fiable devra être établie indépendamment,
   sans publier d'identifiant réel ou de secret.

## Étape d'exploration passive, sans changement de code

La prochaine observation utilise uniquement le
résumeur de formes protobuf existant :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys knc,kod,ksc,krg,ipg
```

Capturer depuis l'écran de sélection, puis charger
un personnage, revenir à la sélection et charger
un deuxième personnage du même compte et serveur.

**N'envoyer que les lignes `shape=[...]` des cinq
opcodes ciblés et les numéros de session anonymes.**
Le mode `--character-shape` ne révèle ni chaînes,
ni nombres du protocole. Les structures de
`knc` et `kod` pourraient contenir des champs
liés à l'authentification : ils doivent rester
strictement opaques. **Ne pas utiliser `--all`,
une capture PCAP, ou un dump brut** pour ce travail.

Une forme protobuf seule ne suffit pas à identifier
l'usage sémantique d'un champ. La présence d'un
champ numérique n'autorise pas à le déclarer
identifiant de compte ou de serveur.

## Suite conditionnelle

Après caractérisation, construire si utile un
diagnostic expérimental **opt-in** qui compare
uniquement des indicateurs égal/différent à travers
plusieurs scénarios *connus de l'opérateur* :

- personnage A → personnage B sur le même compte
  et serveur : compte stable, serveur stable ;
- compte A → compte B sur le même serveur :
  le candidat de compte doit changer ;
- même compte sur serveur A → serveur B :
  le candidat de serveur doit changer ;
- deux clients connectés simultanément :
  aucune attribution croisée, aucune session
  ne peut emprunter l'identité de l'autre ;
- déconnexion/reconnexion, changement de fenêtre
  et messages tardifs : pas de réemploi d'un
  compte ou d'un serveur périmé.

Les scénarios nécessitant plusieurs comptes ou
serveurs seront réalisés seulement si l'opérateur
dispose de ces environnements de test. Ne jamais
demander de mot de passe, jeton ou ID réel dans
le terminal partagé ou dans les documents versionnés.

## Critères d'acceptation

- Signaux sémantiquement indépendants et
  validés sur les scénarios négatifs, non de
  simples identifiants de personnages.
- Portée de chaque observation bornée par
  la connexion, le client concerné, le cycle
  de capture et le serveur.
- Absence de logs, hashes, identifiants
  personnels, authentifiants, payloads
  ou persistance avant validation.
- Échec ou doute : conserver
  `account=unverified server=unverified`.
- Mettre ensuite les métiers dans un état
  **compte + serveur**, distinct de
  `CharacterSessionProfile`; ne pas modifier
  ce modèle de production avant vérification.

P3c est **ouvert mais non validé**.


## Observation réelle P3c.1 — formes seules

Client `3.6.11.15`, diagnostic opt-in `--character-shape`,
opcodes ciblés `knc,kod,ksc,krg,ipg`.

Sur trois retours à l'écran de sélection, connexions
TCP **#3, #5 et #7** :

- `knc` C→S : `root=[1:len 2:len]` à chaque fois ;
- `kod` C→S : `root=[1:len]` à chaque fois ;
- `ksc` S→C : `root=[2:lenx2]` à chaque fois,
  puis échantillonnage des champs de fiches déjà
  observés, notamment `f2.f3`, `f2.f4` ;
- `krg` S→C : échantillonnage interne stable et
  un unique champ longueur en racine, mais tailles
  de message **815, 848, 815 octets**
  respectivement en sessions **#1, #3, #5** ;
- `ipg` S→C : 20 éléments `f1` et taille
  **664 octets** à chacun des chargements observés.

La session #7 s'est arrêtée après la sélection
(`ksc`) : **aucun `krg` ni `ipg` sur cette
session n'apparaît dans la trace soumise**.
Les différences de taille de `krg` pourraient
suivre le changement de personnage, mais il
s'agit uniquement d'une corrélation temporelle ;
elles ne démontrent ni un champ de classe, ni
un champ de compte, ni un serveur.

**Conclusion négative importante :** une structure
identique n'implique pas des valeurs identiques
et ne valide aucun identifiant. L'étude P3c
n'a établi **aucune identité de compte ni
de serveur** à ce stade.

### Confidentialité et ciblage de l'étude suivante

`knc` et `kod` sont des messages client → serveur
du parcours de connexion. Leur contenu pourrait
comporter des données d'authentification :
**ne pas afficher, extraire, hacher ou comparer
leurs valeurs**, ni chercher à créer une
empreinte d'authentifiant, même éphémère.

Pour chercher des preuves de compte et serveur,
privilégier un diagnostic passif et borné des
messages **serveur → client sans secret**,
validé sur plusieurs scénarios contrôlés,
avec uniquement des résultats catégoriels
et des tests synthétiques. Ne pas assimiler
`ipg` à `ipz` : les deux opcodes sont distincts,
même si les deux peuvent contenir vingt lignes.

**Avant d'implémenter une recherche comparative,**
vérifier que l'opérateur dispose de scénarios
négatifs utiles (au moins deux comptes distincts,
idéalement sur un même serveur) ; sinon
un simple candidat de compte corrélé risque
d'être présenté prématurément comme fiable.
Les clés de compte et de serveur demeurent
`unverified` jusqu'à preuve sémantique
et isolation multiclient.

## Reprise P3c.2 — deux comptes sur le même serveur (23 octobre 2026)

**Contexte contrôlé par l'opérateur :** deux personnages de deux comptes Ankama distincts se trouvent maintenant sur un même serveur Dofus. Le test réel de la molette multicomptes P3m.19 est réussi (**959 tests automatisés** et **5/5 contrôles manuels**). Ceci prouve un routage pratique d'équipement par fenêtre dans ce scénario, **pas une identité de compte fiable**.

**Objectif immédiat :** observer, par sessions TCP indépendantes, les formes des messages **S→C** `ksc`, `ksb`, `krg` et `ipg` lors de connexions contrôlées. Cette étape ne compare **aucune valeur** de compte/secret ; elle sert à déterminer s'il existe des structures candidates raisonnables pour concevoir plus tard un comparatif de valeurs strictement limité, en RAM, sans émission des données.

Commande existante (index réseau à vérifier par `--list`) :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --character-shape --shape-keys ksc,ksb,krg,ipg
```

L'index `6` correspondait à une capture ancienne ; **ne pas supposer qu'il reste correct**. Le probe imprime aussi les tailles et marqueurs de connexion anonymes. Il n'affiche **pas** de valeur de champ protobuf ni de nom de personnage. Les `knc`/`kod` client→serveur pouvant porter de l'authentification ne font **pas** partie de cette expérience.

### Parcours conseillé

1. **Fermer complètement les deux clients Dofus** (laisser le launcher possible), puis démarrer le probe.
2. **Phase A1 :** ouvrir le compte A sur ce serveur ; charger un personnage ; attendre `ksc` et `ksb`. Noter localement « A1 », puis fermer entièrement sa fenêtre Dofus.
3. **Phase B :** ouvrir le compte B sur **le même serveur**, charger le nouveau personnage, attendre `ksc`/`ksb`, puis fermer sa fenêtre.
4. **Phase A2 :** rouvrir le compte A et reconnecter le **même** personnage qu'en A1, afin de contrôler la stabilité à travers une reconnexion. Si un second personnage du compte A est disponible sur ce serveur, prévoir ensuite une session complémentaire distincte pour vérifier l'effet du changement de personnage au sein d'un même compte.
5. Arrêter le probe par **Ctrl+C**. Ne partager que les lignes `Session #...` concernant `S→C ksc`, `S→C ksb`, `S→C krg`, `S→C ipg` et les marqueurs `OPENED/CLOSED` correspondants. Ne pas partager une saisie locale, un corps Base64, une trace `--all`, un PCAP ni d'autres champs privés.

**Interprétation :** mêmes structures ou tailles entre A1/A2 ou différences entre A/B = *pistes seulement*. Elles peuvent dépendre du personnage, de l'inventaire, des métiers ou de l'état de session. Elles ne constituent pas une preuve d'identité de compte. Sans candidat indépendant vérifiable, `account=unverified server=unverified` demeure impératif.

### Protection logicielle P3c.2

`CurrentAccountProfessionsState.ForVerifiedSnapshot` est un comparateur pur de snapshots **déjà vérifiés**. Il reste séparé de l'ingestion, et les tests synthétiques vérifient que deux comptes sur le même serveur ne peuvent jamais s'emprunter leurs métiers, que deux personnages d'un compte/serveur peuvent lire le même snapshot, et qu'aucun métier n'est restauré au redémarrage. **Aucune observation de métier n'est ajoutée en production.**

L'étape suivante dépendra de ces formes et des captures réelles anonymisées : choix prudent des seuls champs S→C pertinents, expérience comparative contrôlée (même compte/personnages différents vs comptes distincts même serveur), puis validation sémantique avant tout branchement automatique.

## Résultat terrain P3c.2 — captures A1 / B / A2 (23 octobre 2026)

L'opérateur a effectué dans le même processus de diagnostic trois chargements successifs : **compte A, compte B distinct sur le même serveur, puis retour au même personnage du compte A**. Les données de personnages, comptes et corps des messages ne sont pas publiées.

| Session Game | ksc (octets) | krg (octets) | ksb (octets) | ipg (octets) |
| --- | ---: | ---: | ---: | ---: |
| A1 : #2 | 90 | 828 | 153 | 449 |
| B : #4 | 69 | 812 | 129 | 449 |
| A2 : #6 | 90 | 828 | 153 | 449 |

- Les trois séquences ont observé `ksc`, `krg`, `ksb` et `ipg` en direction **S→C**, sur des sessions distinctes. Les sessions de connexion transitoires #1, #3 et #5 ont été clôturées avant les Game correspondants.
- **A1 et A2** ont les mêmes tailles et les mêmes formes protobuf échantillonnées sur les quatre opcodes. C'est cohérent avec la reconnexion du même personnage, mais les valeurs ne sont pas comparées.
- **B** diffère en taille sur `ksc`, `krg`, `ksb`. Une des formes échantillonnées `krg` sur A comprend aussi `?1.6:v` que B n'a pas ; cela pourrait être propre au personnage ou à son état.
- `ipg` a une taille de **449 octets**, une forme racine `1:lenx20` et une forme échantillonnée identiques dans les trois sessions. **Aucune égalité de valeurs, de niveaux métiers, d'identité ou de comptes ne découle des mêmes formes/tailles.**
- Ce mode diagnostic ne rapporte ni valeur numérique ni secret ; il n'est pas possible de déduire un identifiant de compte des seules lignes.

**Conclusion : account=unverified ; server=unverified au sens des preuves protocole.** La sélection manuelle du serveur dans BestCrush demeure autoritaire pour l'économie et aucune validation de serveur n'est imposée.

### Étape suivante : étudier les métiers, sans prétendre confirmer le compte

Le mode expérimental **déjà existant** `--profession-continuity` compare les snapshots `S→C ipz` d'une session à la première observation, uniquement par des résultats `equal/no/unavailable` et sans affichage des niveaux, XP ou identifiants. Les formes `ipg` ci-dessus ne sont **pas** les contenus `ipz` ; il faut un nouveau test ciblé sur le bon opcode.

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device <INDEX> --profession-continuity
```

1. Fermer toutes les fenêtres du jeu, lancer le probe **avant** le compte A.
2. Ouvrir le compte A sur le serveur commun et charger un personnage jusqu'à l'observation `PROFESSION_CONTINUITY=[p3b=ipz ... baseline=created]`. Fermer entièrement sa fenêtre.
3. Ouvrir le **compte B différent**, même serveur, et attendre `baseline=compared`. Fermer entièrement sa fenêtre.
4. Revenir au **même personnage du compte A**, attendre un nouveau `baseline=compared`. Puis terminer par `Ctrl+C`.
5. Transmettre seulement les lignes `PROFESSION_CONTINUITY=` et les numéros anonymes des sessions, sans identifiants de compte, captures de paquets ni contenu `wire.jsonl`.

**Attentes qualitatives, pas des résultats prédéfinis :** les 20 clés candidates `f5` pourraient représenter un référentiel commun ; si le compte B possède des métiers réellement différents, `levels-equal=no` ou `xp-equal=no` serait cohérent. Un compte B tout neuf avec valeurs par défaut et un compte A comparable pourraient néanmoins donner `yes` partout. Le retour A2 devrait concorder avec A1 en l'absence de progression entre les deux.

**Limite décisive :** une égalité/différence de progression ne prouve pas l'identité compte, même si les résultats du test sont nets. Cette étude sert à caractériser la portée des données métiers et à préparer une recherche d'identité **indépendante**, qui demeure nécessaire avant toute attribution automatique ou stockage.

La suite de tests P3c.2 (12 cas supplémentaires, 971 au total attendus après la base P3m.19 de 959) n'a **pas encore fait l'objet d'un retour de validation Windows dans ce fil**.

## Retour de terrain P3c.3 — professions A1 / B / A2 (23 octobre 2026)

L'opérateur a rejoué l'ordre prévu **compte A → compte B → compte A**, une seule instance Dofus à la fois, et transmis des sorties `--profession-continuity` :

| Phase | Session Game | ipz (octets) | Référence A1 | `f5` |
| --- | --- | ---: | --- | --- |
| A1 | #2 | 174 | `baseline=created` | 20 valides |
| B | #4 | 213 | `levels-equal=no xp-equal=no` | `f5-set-equal=yes` |
| A2 | #6 | 213 | `levels-equal=no xp-equal=no` | `f5-set-equal=yes` |

**Ce qui est prouvé :** les trois messages sont des `ipz` à 20 entrées décodables, avec un ensemble candidat `f5` constant. B et A2 ont une progression de métiers **différente d'A1**, sans valeur imprimée.

**Ce qui ne l'est pas :** le format actuel compare les deux dernières captures uniquement à A1. Les mêmes tailles (213 octets) et les deux comparaisons `no` ne prouvent en aucun cas que **B et A2 sont identiques entre eux**. Ces observations ne vérifient ni compte Ankama ni serveur logique, et ne prouvent pas non plus le parcours déclaré au niveau du protocole.

### Correctif d'observabilité P3c.3

Le mode `--profession-continuity` existant conserve maintenant en RAM **la dernière capture valide** en plus de la référence A1, et ajoute sur chaque message valide suivant :

- `previous-f5-set-equal=yes|no`
- `previous-levels-equal=yes|no|unavailable`
- `previous-xp-equal=yes|no|unavailable`

`levels-equal` et `xp-equal` conservent leur sens historique : comparaison à **A1** ; `previous-...` compare uniquement à **la capture ipz valide immédiatement précédente**, pas à un compte identifié. Une trame invalide ou aux `f5` dupliqués ne remplace pas le dernier état, et un `f5` différent rend les comparaisons de progression `unavailable`.

Aucune sortie de niveau, XP, identifiant de métier, nom, hash ou payload ; seulement des booléens catégoriels. Pas de modification de l'ingestion métier de BestCrush, aucune persistance, aucune attribution de compte.

**Essai Windows après mise à jour :** avec l'interface renvoyée par `--list`, relancer `--profession-continuity` dans le même ordre **A1 → B → A2** ; fermer chaque fenêtre entre deux phases, vérifier qu'une seule ligne `ipz` validée est utilisée dans chaque phase. Recopier uniquement les trois lignes `PROFESSION_CONTINUITY` avec leurs numéros de session. Une seconde connexion A à la place de B empêcherait de tester le contraste voulu : vérifier localement les deux clients.

Si A2 diffère encore d'A1, le statut de l'expérience sera **anomalie de continuité observée, cause inconnue** jusqu'à examen de preuves de sélection de personnage et de connexion. Le statut `account=unverified server=unverified` reste obligatoire.

**Tests :** 5 tests additionnels couvrent A-B-B, A-B-A, trames invalides intermédiaires, changement du set `f5` et non-divulgation. Après les 959 tests P3m.19 et les 12 tests P3c.2 en attente de confirmation Windows, total prévisionnel = **976 tests** (sous réserve de compilation/découverte des tests).

## Résultat terrain P3c.3b — continuité retrouvée (23 octobre 2026)

Le diagnostic amélioré avec comparaison à la **précédente capture valide** a été rejoué par l'opérateur dans l'ordre déclaré **A1 → B → A2** (même personnage du compte A au début et à la fin, comptes distincts A et B sur le même serveur) :

| Phase | Session | Taille `ipz` | vs A1 : `f5` / niveaux / XP | vs précédente : `f5` / niveaux / XP |
| --- | ---: | ---: | --- | --- |
| A1 | #2 | 174 | référence créée | référence créée |
| B | #4 | 213 | yes / no / no | yes / no / no |
| A2 | #6 | 174 | yes / yes / yes | yes / no / no |

Les trois `ipz` ont été décodés, avec 20 lignes et un champ candidat `f5` unique par ligne. Les niveaux/XP sont **identiques entre A1 et A2**, tandis que B diffère des deux. Les `f5` candidats restent identiques en tant qu'ensemble, ce qui est compatible avec une table de référence des métiers partagée, mais **ne démontre pas leur sémantique officielle**.

**Conclusion bornée :** le retour à A restaure exactement le même **snapshot de progression de métiers** mesuré par `ipz` malgré une connexion du second compte entre les deux. Le précédent essai où A2 différait d'A1 n'est pas reproduit ici ; sa cause n'est pas identifiée. Aucun champ identifiant de compte n'a été validé par ces comparaisons : **account=unverified, server=unverified** restent obligatoires dans le protocole.

P3c doit maintenant comparer une source potentiellement liée au compte qui soit **indépendante** de la progression `ipz` et de l'identifiant candidat de personnage `ksc/ksb`, avec au moins deux personnages différents sur un même compte et un second compte sur le même serveur. Les résultats de professions ne peuvent pas servir de preuve de compte.

**Checkpoint de tests :** P3m.19 a été confirmé à 959/959. P3c.2 a ajouté 12 cas et P3c.3 5 cas ; **976 attendus**, mais le résultat Windows n'a pas encore été transmis.

## P3c.4 — Chemins numériques indépendants des métiers (préparé le 23 octobre 2026)

**Objectif strictement exploratoire :** chercher, dans les réponses **serveur → client `krg` uniquement**, des chemins protobuf numériques uniques qui présentent le motif suivant sur un **même serveur choisi manuellement** :

- Phase 1 **A-X** : compte A, personnage X.
- Phase 2 **A-Y** : **même compte A**, personnage Y **différent** sur le même serveur.
- Phase 3 **B-Z** : compte B **différent**, personnage Z sur le même serveur.
- Phase 4 **A-X2** : retour au personnage X initial du compte A.

La sortie ne signale comme **chemin de recherche candidat** qu'un scalaire présent sur les quatre phases, égal en A-X/A-Y/A-X2 mais différent en B-Z. Cela ne constitue **absolument pas une identification de compte** : une constante de jeu, une propriété du personnage, une métadonnée ou une coïncidence peuvent suivre ce motif. Aucun chemin n'est promu au modèle de production. Le serveur choisi manuellement reste autoritaire ; il n'est pas validé par cette étude.

### Mode isolé ajouté au NetworkProbe

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --account-wire-study
```

**Vérifier l'index** Npcap via `--list` : le `6` n'est qu'un exemple issu d'une précédente capture. Fermer **toutes** les fenêtres Dofus avant le démarrage.

Le programme guide les **quatre phases**, une seule fenêtre Dofus à la fois, avec **Entrée après la fermeture complète de chaque fenêtre**. Démarrer la capture AVANT le premier client. À la phase 2, choisir impérativement un autre personnage du **même compte et même serveur** ; le test n'a aucun sens si le même personnage est utilisé en A-X et A-Y. À la phase 3, choisir le **deuxième compte** sur le même serveur. La phase 4 reprend **exactement X**.

Après chaque phase, le probe doit obtenir un seul message `S→C krg` valable sur une session Game ouverte **dans cette phase**. Plusieurs Game candidates dans la même phase, une contradiction, un défaut de trame ou l'absence de phase rendent la comparaison `incomplete` et empêchent toute conclusion.

À la fin, transmettre **uniquement les lignes `p3c4=`**, plus les éventuels `ACCOUNT_WIRE=[p3c4=...]`, sans corps bruts ni autres traces privées. Un résultat `account-shaped-paths=[none]` est parfaitement acceptable et signifie qu'aucun champ unique n'a satisfait cette recherche limitée : **ne pas inventer un identifiant**.

### Garantie de minimisation

- Ne traite **aucun message client → serveur**, en particulier les échanges d'authentification `knc` et `kod`.
- `krg` : corps au plus **8192 octets**, protobuf analysé de manière bornée (profondeur maximale 3, 256 champs par nœud, 64 chemins scalaires retenus) ; conteneurs répétés ignorés pour éviter les grands tableaux d'inventaire.
- Valeurs candidates conservées **uniquement en RAM pendant ce probe** ; aucune valeur, hachage, nom, login, token, compte, ID, niveau ou payload n'est affiché ou sauvegardé.
- N'accepte pour une phase qu'un TCP ouvert dans celle-ci ; toute deuxième session Game candidate la rend ambiguë.
- Même si le motif A-X=A-Y=A-X2≠B-Z est observé : `semantic=unverified account=unverified server=unverified`.
- Aucune mutation de `CurrentCharacterState`, `CurrentAccountProfessionsState`, SQLite, capture métier, historique économique ou gestion de la molette.

**Tests synthétiques ajoutés :** 13 cas isolés (motif positif, motif absent, champ dépendant du personnage, contrôle C→S, conflit multi-Game, contradiction de même Game, duplication identique, trame malformée, mauvaise époque/phase, isolation et non-divulgation).

**Checkpoint Windows P3c.4 confirmé par l'opérateur le 23 octobre 2026 : 989 tests réussis, 0 échec, 0 ignoré, 7,5 s.** Cela couvre les 959 tests antérieurs et les 30 nouveaux tests P3c.2–P3c.4. Le comportement réel du diagnostic `--account-wire-study` et la sémantique d'un éventuel champ d'identité Ankama **restent à vérifier** par capture réelle. Un nombre de tests seul ne confirme ni la sémantique du protocole ni l'identité Ankama.

## P3c.4b — Deux messages krg par chargement (capture terrain 23 octobre 2026)

**Observation réelle, un seul client à la fois, phases opérateur A-X → A-Y → B-Z → A-X :**

| Phase | Game TCP | 1er `S→C krg` | 2e `S→C krg` | Ancien statut |
| --- | ---: | ---: | ---: | --- |
| A-X | #2 | 828 octets | 851 octets | `contradictory-snapshot` |
| A-Y | #4 | 812 octets | 808 octets | `recorded`, 7 chemins |
| B-Z | #6 | 812 octets | 808 octets | `recorded`, 7 chemins |
| A-X (retour) | #8 | 828 octets | 851 octets | `contradictory-snapshot` |

Bilan du premier diagnostic : `phase1=conflicted`, `phase2=recorded`, `phase3=recorded`, `phase4=conflicted`, `summary=incomplete`. **Aucun compte ni candidat de compte n'a été identifié.**

**Défaut réel du diagnostic P3c.4 :** deux messages `krg` sur **la même session Game** peuvent différer pendant un chargement ordinaire. Le code exigeait auparavant une identité de tous les chemins numériques entre deux `krg` ; une simple évolution temporelle rejetait donc toute la phase. Les mêmes tailles `812 → 808` pour A-Y et B-Z ne prouvent aucune égalité de valeurs.

### Correction P3c.4b (expérimentale seulement)

- Pour chaque phase, conserver uniquement **l'intersection de chemins scalaires stables**, présents et de valeur identique dans **tous les `krg` valides** de l'unique session Game. Les chemins fluctuants ou disparus sont supprimés de manière irréversible, même s'ils réapparaissent ensuite.
- Deux messages successifs qui diffèrent n'entraînent plus automatiquement `conflicted` ; un défaut de trame après une observation valide, une seconde session Game, zéro champ stable ou plus de **8 messages `krg`** font toujours échouer la phase.
- Un message malformé sur la première session Game ne permet pas de remplacer silencieusement cette session par une autre.
- Le résumé par phase affiche uniquement `fields=N krg=M unstable-dropped=K` et le statut. **Aucune valeur, hash ou identifiant privé n'est affiché/persisté.**
- Le motif interphase recherché reste **A-X = A-Y = A-X2 ≠ B-Z** au même chemin numérique. Ce n'est **pas une preuve sémantique** d'identité de compte, même en cas de correspondance.
- Aucune modification à la capture métier en production, aux profils RAM, aux métiers ni à la base de données.

**Checkpoint de test à refaire sur Windows :** 989 tests réussis avant P3c.4b ; 6 tests additionnels sur les mises à jour, la disparition d'un chemin, les ambiguïtés et la non-réutilisation d'une autre session Game, soit **995 tests attendus**.

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --account-wire-study
```

Index `6` à vérifier via `--list`. Refaire le même parcours **quatre phases**, arrêter chaque client entièrement avant Entrée. À communiquer : les lignes `ACCOUNT_WIRE`, `p3c4=phase` et `p3c4=summary`, sans corps réseau, noms ou identifiants.

## Résultat terrain P3c.4b — quatre phases acceptées (23 octobre 2026)

L'opérateur a rejoué le parcours **A-X → A-Y → B-Z → A-X** sur le même serveur manuel, chaque client Dofus étant fermé entre deux phases. Le comparateur `krg` corrigé pour tenir compte de deux messages successifs pendant le chargement a produit :

| Phase | Session Game | Deux `krg` | Chemins numériques stables | Chemins supprimés |
| --- | ---: | --- | ---: | ---: |
| A-X | #2 | 828, 851 octets | 7 | 1 |
| A-Y | #4 | 812, 808 octets | 7 | 0 |
| B-Z | #6 | 812, 808 octets | 7 | 0 |
| A-X (retour) | #8 | 828, 851 octets | 7 | 1 |

**Sortie :** `p3c4=summary status=compared account-shaped-paths=[none] account=unverified server=unverified semantic=unverified`.

**Conclusion bornée :** P3c.4b a traité correctement les quatre phases et les mises à jour `krg`. Aucun des **chemins numériques uniques et stables examinés dans `krg`** n'a le motif `A-X = A-Y = A-X2 ≠ B-Z`. Ce résultat **ne démontre pas** l'absence d'un identifiant de compte dans Dofus : les chaînes, enveloppes imbriquées ignorées, opcodes distincts et champs répétés n'ont pas été analysés comme identité. Ne pas chercher à forcer une égalité ni à traiter le contenu d'authentification.

### P3c.5 — piste indépendante : groupes candidats de personnages dans ksc/ksb

La famille `ksc` (S→C) contient une **liste candidate de personnages** (`f2.f3`) dans les essais P2f ; `ksb` contient le personnage chargé candidat (`f1.f1.f3`). Les deux familles ont été rapprochées dans une même session Game, sans authentification de leur sémantique.

Le mode de diagnostic **strictement opt-in** `--account-roster-study` étudie la continuité de cette liste :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --account-roster-study
```

L'index de Npcap `6` est **un exemple à vérifier avec `--list`**. Fermer toutes les fenêtres Dofus avant le début ; le programme affiche quatre phases :

1. **A-X** : compte A, personnage X sur le serveur commun.
2. **A-Y** : **même compte A**, autre personnage Y sur le même serveur.
3. **B-Z** : compte B distinct sur **ce même serveur**, personnage Z.
4. **A-X** : reconnexion du même personnage X initial.

Une seule fenêtre Dofus par phase ; attendre `ksc` et `ksb`, **fermer totalement la fenêtre**, puis appuyer sur **Entrée** dans le probe. À la fin, partager uniquement les lignes `p3c5=` et `ROSTER_STUDY` anonymes. Ne jamais transmettre la saisie d'un nom, une valeur d'ID, un body/protobuf, un jeton ni un fichier de capture.

Le diagnostic requiert :
- un seul TCP Game ayant un `ksc` décodable et un `ksb` correspondant, tous deux S→C et **dans la même session** ;
- un ensemble candidat `f3` constant entre A-X, A-Y et A-X2, distinct et sans recouvrement avec B-Z ;
- X et Y numériquement distincts, mais X stable au retour ;
- échec fermé lors d'une liste invalide ou contradictoire, d'une sélection absente du roster, d'un second TCP Game candidat, ou d'une phase manquante.

**Si ce motif est observé :** `roster-group-candidate` signifie uniquement qu'une **liste de personnages candidate** peut servir de référence temporaire pendant cette étude, **pas qu'un identifiant de compte Ankama a été trouvé**. Des listes égales/différentes ne prouvent pas l'exclusivité du compte ni le serveur logique et ne permettent aucune alimentation des métiers en production à ce stade.

Aucune connexion, identité, nom, hash ou liste n'est affiché ni persisté. La mémoire disparaît avec le probe. Aucun changement aux overlays, à F7/F8, à la molette, aux métiers en production, à SQLite ou aux fiches personnages. Cette piste complète la recherche d'un champ d'identité de compte, mais ne remplace pas une preuve indépendante.

**Validation Windows requise :** dernier checkpoint confirmé 989 tests P3c.4 ; 6 tests P3c.4b publiés (validation non communiquée), puis 17 tests P3c.5 publiés : **1012 tests attendus**, sous réserve du build Windows et de la découverte des tests.

## P3c.5 — Résultat terrain confirmé (23 octobre 2026)

L'opérateur confirme le checkpoint Windows **1012 tests réussis / 0 échec / 0 ignoré / 8,1 secondes** après ajout des 17 tests P3c.5.

Parcours réel successif A-X → A-Y → B-Z → retour A-X, sur le même serveur, un seul client à la fois :

| Phase | Session TCP Game | S→C ksc | S→C ksb | Nombre dans roster | Statut |
| --- | ---: | ---: | ---: | ---: | --- |
| A-X | #2 | 157 octets | 153 octets | 2 | linked |
| A-Y | #4 | 157 octets | 127 octets | 2 | linked |
| B-Z | #6 | 69 octets | 129 octets | 1 | linked |
| A-X retour | #8 | 157 octets | 153 octets | 2 | linked |

Sortie récapitulative exacte et anonyme :

```
p3c5=summary status=roster-group-candidate same-account-rosters=yes cross-account-distinct=yes cross-account-disjoint=yes selected-x-y-distinct=yes selected-x-return-equal=yes account=unverified server=unverified semantic=unverified
```

**Preuves positives et négatives dans ce protocole :** les deux personnages différents de A et le retour à X disposent des mêmes deux identifiants candidats de roster `ksc` ; le personnage Z de B a un roster d'un membre entièrement disjoint ; chaque `ksb` trouve son candidat dans la liste de **sa propre connexion**. Le modèle de données candidat devient réellement intéressant pour le regroupement éphémère de personnages durant une session.

**Limite :** cela ne valide ni un compte Ankama identifié sémantiquement, ni le serveur du flux, ni une identité compte+serveur indépendante, ni l'isolation en présence de deux connexions ouvertes **en simultané**. Le mode est expérimental et ne doit pas écrire `VerifiedAccountServerKey`, les métiers, le profil ou SQLite.

**Étape suivante recommandée :** caractérisation multiclient simultanée A-X et B-Z : les deux TCP/ksc/ksb doivent coexister sans emprunt du roster ou du personnage de l'autre, avec fermeture/reconnexion et une comparaison anonyme des groupes. Conserver cette étude séparée de la production et de la sélection manuelle du serveur.

## P3c.6 — Étude des groupes candidats avec deux clients simultanés

**Point de départ confirmé :** P3c.5 a été validé en vrai par l'opérateur le 23/10/2026, avec **1012 tests automatisés réussis**, deux personnages A-X/A-Y sur un compte, un personnage B-Z sur un autre compte sur le même serveur, et une reconnexion A-X. `roster-group-candidate` obtenu, mais ni identifiant Ankama ni serveur réseau sémantiquement authentifié.

### Objectif

Contrôler **deux connexions Game concurrentes**, sans association implicite à une fenêtre ni à un compte : garder A ouvert, connecter B, **fermer et reconnecter B en maintenant A ouvert**. Le diagnostic existant de fenêtres P3m.19 est indépendant. Cette expérience ne prouve pas encore le lien PID/HWND↔roster et n'alimente pas le profil en production.

Mode expérimental ajouté **sans modification aux chemins BestCrush de production** :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --account-roster-concurrent
```

Vérifier l'interface Npcap (par `--list`) ; `6` est un **exemple historique**. Fermer **tous les clients Dofus avant le démarrage** et lancer le probe avant d'ouvrir les clients.

1. **Étape 1 :** ouvrir le compte **A**, personnage **X**, même serveur économique choisi pour l'expérience, attendre le `ksc` puis `ksb`, **garder A OUVERT**, appuyer sur **Entrée** dans le probe.
2. **Étape 2 :** ouvrir **B**, personnage **Z**, sur le même serveur, attendre `ksc` et `ksb`, puis **fermer uniquement la fenêtre B** ; A reste ouvert. Appuyer sur **Entrée**.
3. **Étape 3 :** **rouvrir B**, reprendre exactement **Z** sur le même serveur, attendre `ksc` et `ksb` ; **garder A et B ouverts**, puis appuyer sur **Entrée**.
4. Copier seulement les lignes `CONCURRENT_ROSTER` et `p3c6=` de la sortie (pas de contenu binaire, noms, identifiants, titres Windows, captures pcap, ni `--all`). Après le bilan, fermer les clients librement.

### Critères du diagnostic

- Un `ksc` décodable et un `ksb` correspondant au **même TCP Game** pour chaque rôle ; la liste doit rester constante pendant cette session ; toute contradiction ou 2e Game candidat dans le même rôle bloque ce rôle.
- Pour être acceptée, l'étape B1 nécessite A encore lié et ouvert ; l'étape B2 nécessite A toujours lié/ouvert, B1 lié **et fermé**.
- Des messages tardifs de B1 après sa fermeture ne modifient pas son groupe ; une fin de flux A avant la liaison de B2 rend l'expérience incomplète.
- `B-reconnect-roster-stable=yes` signifie que les ensembles candidats du premier B et de son retour sont égaux, avec le même personnage chargé candidat.
- `A-B-disjoint=yes` exige zéro membre candidat partagé entre A et chacune des connexions B ; `both-overlap-A=yes` exige que les deux observations B aient été obtenues tandis que la connexion A était encore ouverte.
- Le statut possible `concurrent-roster-group-candidate` ne vaut **ni preuve d'identité de compte**, ni preuve d'association à une fenêtre, ni preuve du serveur Dofus. `account=unverified server=unverified semantic=unverified` reste inchangé.
- Seuls les messages `ksc`/`ksb` **serveur vers client** sont décodés ; les contenus d'authentification `knc`/`kod` ne sont jamais inspectés. Tous les candidats restent en RAM sans hash ni log de valeur.
- Aucun ajout à `CurrentCharacterState`, `CurrentAccountProfessionsState`, SQLite, routes métier, overlays F7/F8 ou molette réseau.

**Validation attendue sur Windows :** 16 nouveaux tests synthétiques de sécurité/isolation, donc **1028 tests attendus**, sur la base P3c.5 confirmée 1012/1012. L'étape reste **à valider** jusqu'à réception du résultat Windows et de la capture réelle.

**Si le parcours réussit :** étudier ensuite le rattachement `TCP/ksc/ksb → PID/HWND` avec la preuve d'appartenance OS déjà disponible en P3m.19, en protégeant changement de personnage, fermeture de fenêtre, réemploi TCP et réinitialisation de capture. Toute attribution automatique de compte/métiers nécessitera des preuves indépendantes et restera non active entre-temps.

## P3c.6 — Résultats réels des deux essais simultanés (23 octobre 2026)

**Premier essai : procédure invalide, protection correcte** — à l'étape 1, le Game #2 a produit `ksc 69` / `ksb 129`, puis, **avant Entrée**, un second Game #4 a produit `ksc 157` dans la même étape. Le verrou `multiple-game-sessions` a correctement mis l'étape 1 en `conflicted`, sans attribuer une liste à un autre client ; `summary=incomplete`. Il ne s'agit **pas** d'une preuve de confusion A/B, mais d'un cas de sécurité déclenché par l'ordre des connexions par rapport aux phases opérateur.

**Deuxième essai : protocole intégral réussi** — l'opérateur a relancé `--account-roster-concurrent` sur l'interface réseau `[3]`, sans changer de serveur :

| Étape | Session Game | `S→C ksc` | `S→C ksb` | Roster candidats | État Game au bilan |
| --- | ---: | ---: | ---: | ---: | --- |
| A-X initial | #2 | 157 octets | 153 octets | 2 | ouvert |
| B-Z pendant A-X | #4 | 69 octets | 129 octets | 1 | fermé |
| B-Z reconnecté pendant A-X | #6 | 69 octets | 129 octets | 1 | ouvert |

Les trois étapes ont obtenu `status=linked`, sans contradiction. B1 a été fermé avant le lancement de B2 ; A est resté lié et ouvert lorsque B1 et B2 ont été chargés.

Résumé exact et anonyme :

```
p3c6=summary status=concurrent-roster-group-candidate B-reconnect-roster-stable=yes A-B-disjoint=yes A-B-distinct=yes both-overlap-A=yes account=unverified server=unverified semantic=unverified
```

**Conclusion P3c.6 :** pendant ce scénario, les deux flux Game simultanés portent des listes de personnages candidates différentes et disjointes ; la liste et le personnage chargé candidats de B restent stables après reconnexion, sans fermer le Game A. Le routage temporel par étape est propre, mais le probe **n'a pas encore prouvé quel PID/HWND est propriétaire de chacun de ces flux** et n'établit ni compte Ankama authentifié ni serveur réseau vérifié.

**Checkpoint automatisé :** 1012/1012 confirmés pour P3c.5. Les **16 tests P3c.6** ont été ajoutés dans le code et la capture réelle a réussi ; l'opérateur n'a **pas encore fourni le récapitulatif de la suite Windows P3c.6**. Ne pas annoncer 1028/1028 comme validés.

### Prochain jalon P3c.7 — preuve locale de la fenêtre propriétaire

Étudier le raccordement **Game TCP capturé (quadruplet exact) → socket Windows unique → PID unique → HWND Dofus visible unique → roster `ksc`/`ksb` du même Game**, à partir des primitives déjà disponibles de P3k/P3m.19.

Critères de sûreté : vérifier 2 clients simultanés avec **fenêtres distinctes**, maintien de A pendant fermeture/réouverture de B, réévaluation du propriétaire OS (et non attribution par numéro global de phase), éviction immédiate des liaisons de fenêtre fermées, échec fermé si un PID a deux HWND Dofus, si le socket/propriétaire est absent ou contradictoire, ou si un paquet est retardé au-delà du cycle de connexion. Conserver les preuves **en RAM seulement**, aucune valeur d'identifiant, nom, tuple, PID ou HWND dans les traces.

Le serveur choisi manuellement reste le seul contexte économique ; **ne pas imposer une validation de serveur**. Un lien Windows prouvé ne constitue **pas** une identité de compte Ankama. Les métiers et fiches personnages restent non alimentés par ces diagnostics jusqu'à leur validation indépendante.

## P3c.7 — Relier les listes candidates ksc/ksb à la fenêtre OS propriétaire

**Départ :** P3c.6 a produit un résultat réel positif (voir ci-dessus), mais le probe ne vérifiait pas l'appartenance des sessions Game TCP à un processus/fenêtre Windows. L'opérateur n'a **pas encore donné de rapport de tests Windows sur les 16 cas P3c.6**, même si sa capture réelle est concluante.

### Ajout expérimental isolé

La commande `--account-roster-window` conserve **exactement le protocole manuel P3c.6** et ajoute une vérification locale de chaque connexion Game portant un `ksb` lié à son `ksc` :

1. Reconstruire le **quadruplet TCP exact** sur le paquet Game de cette session, sans le publier.
2. Lire la table IPv4 Windows des sockets `ESTABLISHED` de destination TCP/5555 pour rechercher **un propriétaire PID unique** de ce quadruplet.
3. Rechercher **une seule fenêtre Dofus visible** appartenant à ce PID ; une fenêtre absente, deux HWND pour un PID ou plusieurs propriétaires de socket donnent `os-unavailable`.
4. Lorsque B est lié, **réévaluer le socket de A avec le même instantané OS**, exiger le même PID/HWND que lors du chargement de A et une fenêtre **différente** de celle de B.
5. Lors de B reconnecté, exiger la fermeture du premier B, une nouvelle session logique Game, la stabilité de A et une fenêtre distincte de A. Un changement de propriétaire sur un même Game, un flux périmé ou une contradiction échoue fermé.

L'outil n'infère **jamais** l'identité Ankama depuis PID, HWND, port, nom de processus, ordre d'ouverture, nom de personnage ou sélection manuelle de serveur. Le statut affiché `window-roster-link-candidate` signifie seulement qu'un **rattachement technique local** est cohérent avec les deux listes candidates `ksc/ksb` et les observations OS. Le bilan positif **requiert aussi** `p3c6=summary status=concurrent-roster-group-candidate` ; si le motif de listes échoue, le bilan P3c.7 est bloqué.

**Rien n'est branché dans BestCrush en production.** Pas de persistance de profils ni de personnages, pas de nouveau contrôle obligatoire du serveur choisi manuellement, aucun changement de molette, F7, F8, métiers, prix ou SQLite.

### Protocole Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --account-roster-window
```

La carte `3` était celle utilisée dans le dernier essai P3c.6, **à vérifier via `--list`**. Le programme démarre avant Dofus :

1. Ouvrir **A-X**, attendre `ksc/ksb`, **garder A ouvert**, puis Entrée.
2. Ouvrir **B-Z**, attendre `ksc/ksb`, fermer **B uniquement** tout en gardant A ouvert, puis Entrée.
3. Rouvrir **B-Z**, attendre `ksc/ksb` et garder **A+B ouverts**, puis Entrée.
4. Transmettre **uniquement** les lignes `CONCURRENT_ROSTER`, `WINDOW_ROSTER`, `p3c6=` et `p3c7=` et le bilan des tests. Ne jamais fournir de PID/HWND, noms, ID personnages, adresses IP, quadruplets TCP, payloads, hashes, traces `--all` ou PCAP.

Les lignes par phase P3c.7 sont `status=os-matched|unavailable|conflicted` et le bilan n'est positif que si A reste sur son HWND unique pendant les deux connexions Game de B, avec des fenêtres A/B différentes, en plus du contrôle des rosters candidats P3c.6. L'observation du propriétaire OS est instantanée à la réception des messages `ksb`; si une table est indisponible à cet instant, le résultat est **incomplet**, sans association de secours.

### Tests et précautions

**16 tests synthétiques** supplémentaires pour le lien OS : deux Game simultanés + reconnexion, absence de fuite de PID/HWND/tuple, collisions propriétaire/PID, fermeture de A, perte d'owner pendant B, changements de propriétaire, arrivée tardive B1, nouvelle session et observations répétées. Après **1012 tests P3c.5 confirmés**, **16 cas P3c.6 ajoutés** et **16 cas P3c.7 nouveaux**, **1044 tests attendus** sous Windows, non encore exécutés sur le poste de l'opérateur.

**Portée des résultats :** un lien exact TCP/PID/HWND n'authentifie pas un compte Dofus ni un serveur logique ; `account=unverified server=unverified semantic=unverified` demeure obligatoire et l'intégration des métiers au profil reste désactivée.

## P3c.7 — Validation terrain des fenêtres et des listes candidates (23 octobre 2026)

**Parcours réel de l'opérateur :** après fermeture de tous les clients, ouvrir A-X et le conserver, ouvrir B-Z et fermer uniquement B, puis reconnecter B-Z en conservant A et B ouverts. Le diagnostic utilisé est `--account-roster-window` (P3c.7) ; aucun identifiant, nom, socket, PID ni HWND réel communiqué.

| Étape | TCP Game | ksc / ksb (octets) | Liste | Fenêtre Windows | État final |
| --- | --- | --- | ---: | --- | --- |
| A-X | #2 | 157 / 153 | 2 | `os-matched` | Game ouvert |
| B-Z premier | #4 | 69 / 129 | 1 | `os-matched` | Game fermé |
| B-Z reconnecté | #6 | 69 / 129 | 1 | `os-matched` | Game ouvert |

**Contrôle réseau P3c.6 :**
```
p3c6=summary status=concurrent-roster-group-candidate B-reconnect-roster-stable=yes A-B-disjoint=yes A-B-distinct=yes both-overlap-A=yes account=unverified server=unverified semantic=unverified
```

**Contrôle propriétaire OS P3c.7 :**
```
p3c7=stage=1 status=os-matched game-closed=no A-still-matched=not-applicable reason=none account=unverified server=unverified
p3c7=stage=2 status=os-matched game-closed=yes A-still-matched=yes reason=none account=unverified server=unverified
p3c7=stage=3 status=os-matched game-closed=no A-still-matched=yes reason=none account=unverified server=unverified
p3c7=summary status=window-roster-link-candidate A-window-stable-across-B=yes A-B-distinct-windows=yes account=unverified server=unverified semantic=unverified
```

**Conclusion stricte :** le scénario établit expérimentalement un **rattachement technique cohérent Game TCP → socket Windows propriétaire unique → HWND unique → même session `ksc`/`ksb`**, avec comptes candidats simultanés disjoints et retour de B sur son groupe candidat. Cela ne prouve pas un identifiant de compte Ankama ni le serveur logique. Aucun métier, profil, volet UI, SQLite ou historique n'a été alimenté depuis ce diagnostic. Le contexte économique de serveur choisi manuellement reste autoritaire, sans validation forcée.

**Remarque d'observabilité :** dans la capture, `WINDOW_ROSTER=[p3c7=stage=N status=os-matched...]` était affiché **avant** la ligne `S→C ksb` correspondante. L'explication est technique : `CharacterMetadataProbe` calculait et *imprimait* le diagnostic OS pendant le prétraitement du lot TCP, **avant** d'afficher les événements décodés du lot. Cela ne constitue pas une liaison au message `ksc` précédent. Un correctif ne changeant **que l'ordre d'affichage** publie maintenant `WINDOW_ROSTER` immédiatement après la ligne `ksb` déclenchante, selon l'index de l'événement dans le lot. Il ne change ni la logique de liaison ni le comportement de production. Ce correctif attend un nouveau checkpoint de compilation/tests Windows.

**État automatisé :** dernier total **effectivement confirmé par l'opérateur : 1012/1012**, avant P3c.6 (16 tests) et P3c.7 (16 tests). **1044 tests attendus**, mais aucun bilan Windows pour ces ajouts n'est encore communiqué dans ce fil. Ne pas annoncer 1044/1044 sans mesure.

### Suite possible, distincte de la production

L'étape de développement la moins intrusive est un **index éphémère expérimental, strictement en RAM**, qui pourrait conserver pour la durée d'une capture les groupes candidats `ksc` et leurs fenêtres **uniquement après** résolution exacte du propriétaire du Game. Le lien devra être invalidé à la fermeture de fenêtre, au changement de capture, au changement de serveur manuel, à la perte de socket, à une sélection contradictoire, ou à tout réemploi de tuple TCP. Rien ne doit renseigner `VerifiedAccountServerKey` sans preuve sémantique indépendante ; la publication des professions compte + serveur reste bloquée.

## P3c.7 — Checkpoint Windows 1044/1044 et P3c.8 (23 octobre 2026)

**Validation de compilation et suite complète sur le poste Windows : 1044 tests exécutés, 1044 réussis, 0 échec, 0 ignoré, 7,4 secondes.** Cela confirme les 16 nouveaux tests P3c.6, les 16 tests P3c.7 et le correctif d'ordonnancement `WINDOW_ROSTER` (aucun test Windows ultérieur annoncé).

La preuve terrain P3c.7 ci-dessus est conservée distincte de la réussite des tests unitaires : les groupes `ksc/ksb` candidats ont été joints à leurs propriétaires OS TCP uniques et fenêtres HWND, avec A stable pendant la reconnexion de B ; **cela ne constitue toujours pas un identifiant Ankama vérifié ni un serveur réseau vérifié**.

### P3c.8 — Modèle candidat éphémère, **non branché à la production**

Nouveau composant purement RAM `BestCrush/Services/EphemeralCandidateRosterGroupsState.cs`, interne à BestCrush et **sans enregistrement de service, sans appel depuis les captures et sans interface utilisateur**. Cette première étape caractérise les invariants de durée de vie avant toute proposition d'ingestion réseau.

- Pour une fenêtre `DofusClientWindowKey(HWND, PID)` unique, une observation candidate porte un bail de serveur **choisi manuellement**, une époque Npcap, un identifiant logique de connexion et le candidat sélectionné présent dans un ensemble `ksc`. Le type est interne et n'a **aucune signification d'identité de compte**, même lorsque les deux listes sont égales.
- Les ensembles de candidats sont **uniquement en RAM**, bornés à **16 fenêtres** et **16 candidats par fenêtre**. Pas de noms de personnages, d'identifiants en logs, de jetons, de hash, de PID/HWND, de payloads ni d'écriture sur disque.
- La comparaison ne renvoie que les catégories `SameRosterCandidate`, `DisjointRosterCandidates`, `Ambiguous` ou `Unavailable`. Des ensembles différents mais se recouvrant partiellement sont **quarantinés**, pas fusionnés.
- Changer de connexion sur une même fenêtre **révoque** la précédente ; un `connectionId` tardif ne peut pas revenir. Les contradictions du même token révoquent et mettent en quarantaine la fenêtre. La déconnexion ne touche qu'au token exact.
- Une fenêtre supprimée de l'inventaire Win32 est révoquée ; son ancien `HWND/PID` reste **quarantiné pour toute l'époque de capture**, empêchant la réutilisation ambiguë. Une nouvelle fenêtre obtient sa propre identité locale.
- Un changement de bail de serveur manuel ou un redémarrage de capture **efface immédiatement tous les candidats**. Les époques/générations antérieures ne peuvent pas récupérer ces données.
- **Les classes et chemins existants `CurrentCharacterState`, `CurrentDofusClientsState`, `CurrentAccountProfessionsState`, `LiveDofusClientNetworkBindings`, `DofusNetworkCaptureService`, overlays F7/F8, molette, SQLite et prix restent inchangés.** Aucune attribution de métiers, de niveaux, de kamas ou d'inventaire n'est autorisée par ce seul état.
- Le modèle suppose explicitement qu'un **appelant futur** aura déjà établi `ksc/ksb` sur la même connexion et l'appartenance OS unique via le quadruplet TCP ; le modèle en RAM **ne prouve pas lui-même ces éléments** et ne peut donc pas être branché sans étape d'intégration/revalidation indépendante.

**Tests additionnels :** 17 tests xUnit dans `EphemeralCandidateRosterGroupsStateTest.cs` couvrent l'isolation de deux fenêtres, la comparaison égal/disjoint/inconnu, les nouvelles connexions et messages tardifs, les contradictions, le nettoyage à fermeture, le redémarrage Npcap, la génération du serveur manuel, les limites de taille et la non-divulgation.

**Checkpoint suivant : 1061 tests attendus**, sous réserve de compilation/tests Windows. Aucun résultat de validation P3c.8 n'a encore été fourni.

## P3c.8 — Checkpoint Windows 1061/1061 (23 octobre 2026)

L'opérateur confirme la compilation Windows et la suite xUnit complète **1061 tests exécutés, 1061 réussis, 0 échec, 0 ignoré, 7,3 secondes**. Les 17 nouveaux tests `EphemeralCandidateRosterGroupsStateTest` sont donc validés sous Windows. Cela confirme le socle purement RAM de P3c.8 ; **aucun rattachement des métiers n'est activé**.

## P3c.9 — Observateur ksc/ksb à injection explicite (staging, 23 octobre 2026)

**Objectif :** vérifier la connexion entre le vrai worker de messages `DofusNetworkCaptureService` et `EphemeralCandidateRosterGroupsState` en **rejeu synthétique**, avant une future activation de capture live. La liaison `ksc`/`ksb` utilise les chemins candidats observés en P3c.5, mais ne prouve aucun compte Ankama ni serveur réseau officiel.

### Implémentation désactivée par défaut

- Nouveau composant interne `EphemeralCandidateRosterWireObserver` : ne considère que les messages `S→C ksc` et `S→C ksb`, sur **le même token de connexion logique TCP**, le même bail de serveur **choisi manuellement**, la même époque de capture et la même fenêtre OS candidate revalidée **au moment de la consommation par le worker**.
- Conserve temporairement dans sa mémoire le `ksc` décodé (y compris ses noms candidats **sans jamais les imprimer**) jusqu'à réception d'un `ksb` concordant sur la même connexion. Après concordance, ne transfère au workspace P3c.8 que l'ensemble numérique candidat et le candidat sélectionné, sans nom, niveau ni classe.
- Les messages `C→S`, les opcodes autres que `ksc/ksb`, les fenêtres non résolues, les époques périmées, les baux non actifs, les collisions de fenêtre/token et les contradictions ne fournissent **aucune** donnée utilisable. Les flux contradictoires sont bloqués sans repli sur le titre, l'ordre des connexions, le focus, une identité de compte ni les métiers.
- Fermeture de fenêtre : `ReconcileWindows` invalide les liens ET les listes en attente et met en quarantaine le HWND/PID jusqu'au changement d'époque de capture. Une déconnexion n'efface que le token actif ; un ancien message ne peut défaire une nouvelle connexion.
- Changement du serveur manuel : effacement immédiat de l'état RAM (sans validation imposée du serveur). Stop/Start Npcap : nouvelle époque, effacement et interdiction des messages de l'ancienne époque. Les identifiants candidats, les HWND, les PID, les IP et les corps de messages ne sont **ni imprimés, ni persistés, ni hachés**.
- `DofusNetworkCaptureService` accepte un **observateur interne optionnel** via sa construction utilisée en tests ; **le constructeur public et l'enregistrement DI de production ne lui en fournissent aucun**. L'observateur ne s'exécute qu'après le routeur métier, dans un `try/catch` séparé : il ne doit pas bloquer prix, concassage ou historique. En rejeu, le drapeau synthétique de fenêtre est explicitement contrôlé par le harness de tests.
- **Ce jalon ne permet pas d'utiliser l'outil de capture live pour publier les groupes** : la réception live des événements par cet observateur n'est pas activée dans l'application ordinaire. Une activation opt-in distincte ne sera envisageable qu'après validation Windows et vérifications de sécurité supplémentaires.
- `CurrentCharacterState`, `CurrentAccountProfessionsState`, `VerifiedAccountServerKey`, les données économiques, les overlays F7/F8, la molette et SQLite restent inchangés. Aucune identité de compte ni preuve de serveur réseau n'est créée.

### Tests ajoutés (à valider Windows)

**17 tests isolés** de l'observateur `EphemeralCandidateRosterWireObserverTest` : deux groupes disjoints, deux personnages d'un même groupe, nom discordant, mélange interflux/interfenêtres, absence de propriétaire, refus de messages C→S, messages répétés, fermeture/réemploi de fenêtre, déconnexion et reconnexion, changement de bail manuel, époques périmées, redaction.

**3 tests de bout en bout** dans `DofusNetworkRoutingTest` : le vrai worker de rejeu envoie les messages synthétiques `ksc/ksb` à l'observateur uniquement sur injection explicite ; changement de serveur et redémarrage détruisent les candidats ; aucun observateur ni compte/métier n'apparaît en production non opt-in.

**Checkpoint Windows visé : 1081 tests** (1061 confirmés + 20 nouveaux). Compilation et tests **non effectués par l'agent** : environnement local de validation .NET/MAUI Windows indisponible. Ne pas présenter 1081 comme un résultat acquis.

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Suite après validation :** caractériser en opt-in une liaison de capture live qui ne publie que des **indicateurs anonymes**, puis éprouver fermeture de fenêtres, reconnexion, changement de personnage, serveur manuel et capture Stop/Start ; l'attribution de professions à `VerifiedAccountServerKey` demeure interdite sans preuve de compte+serveur indépendante.

## P3c.9 — Checkpoint Windows 1081/1081 et P3c.10 : diagnostic réseau live en Debug

**Validation transmise par l'opérateur le 23 octobre 2026 : 1081 tests réussis, 0 échec, 0 ignoré, durée 7,2 secondes.** P3c.9 (observateur RAM dans le vrai worker en rejeu synthétique) est confirmé. Il n'existait encore aucun raccordement live visible pour les groupes candidats.

### P3c.10 — activation explicite sans impact sur l'application normale

**Objectif :** permettre une nouvelle expérience **réelle** depuis le worker réseau de BestCrush, pour observer la cohérence `S→C ksc` puis `S→C ksb` par fenêtre/propriétaire TCP, et afficher **uniquement** des compteurs catégoriels. Aucun nom, identifiant de personnage, numéro de compte Ankama, tuple, HWND/PID, adresse, capture brute ou hash n'est écrit par ce nouveau diagnostic.

La construction du `DofusNetworkCaptureService` reste inchangée. Le système standard n'injecte pas d'observateur. En **compilation Debug Windows seulement**, si la variable d'environnement du **processus** `BESTCRUSH_P3C10_ROSTER_STUDY` vaut **exactement** `1` et que les services de rattachement Windows existent, le constructeur instancie un observateur séparé **en RAM**, sans l'exposer aux profils/métiers.

Lors de la consommation réseau, le worker continue de revalider son token logique TCP/fenêtre OS et ses baux de capture avant de passer les messages au routeur économique existant. **Après** ce routeur métier, l'observateur optionnel examine `ksc/ksb`, sans jamais bloquer les données de marché/concassage. Un message non attribuable à une fenêtre, contradictoire ou périmé ne crée pas de groupe. Une fermeture de fenêtre est rapprochée du prochain inventaire Win32 ; une reconnexion exige de nouveaux `ksc/ksb`.

**Sorties observables autorisées** dans les journaux Debug : `p3c10=status=enabled` et `p3c10=status=pending|linked|rejected`, suivis de `linked-windows=N equal-roster-pairs=N disjoint-roster-pairs=N account=unverified server=unverified`. Les nombres ne sont **pas des comptes authentifiés**. Limite de **128 événements** par activation de capture, puis `status=report-limit`, pour éviter une croissance incontrôlée des journaux. Un changement de serveur choisi manuellement donne `status=reset`.

**Isolation :** aucune donnée de groupe candidat ne sort de la RAM hormis les compteurs anonymisés. La session ne peuple pas `CurrentCharacterState`, `CurrentAccountProfessionsState`, `VerifiedAccountServerKey`, les portraits, les métiers, l'inventaire, les prix, SQLite ni la molette. Pas de vérification forcée du serveur manuel, ni d'authentification Ankama. Le mapping réseau étudié reste non prouvé sémantiquement.

### Validation Windows préalable

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**5 tests ajoutés** dans `LiveCandidateRosterDiagnosticTest.cs` : le flag exactement `1` requis, rejet des modes Release/absence de services Windows, anonymat des compteurs même avec noms/IDs fictifs connus, égalité et disjonction des listes sans inférence de comptes, reset manuel et changement d'époque. Dernier total **1081 confirmé**, **1086 tests attendus** tant que le checkpoint Windows n'est pas transmis. Aucune compilation .NET locale n'est possible dans l'environnement de l'agent.

### Après validation uniquement : manipulation réelle

Arrêter les clients Dofus et BestCrush, lancer BestCrush **depuis un terminal PowerShell** avec la variable d'environnement temporaire, de manière à ce qu'elle soit héritée du processus Debug :

```powershell
$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Si la configuration locale de l'application Windows ne permet pas `dotnet run`, utiliser le mécanisme local existant de lancement du binaire Debug en conservant **la variable d'environnement de ce processus**, sans inventer de chemin vers un exécutable. Ne pas utiliser le mode Release pour cette expérience. Après l'essai, `Remove-Item Env:BESTCRUSH_P3C10_ROSTER_STUDY` supprime le flag du PowerShell courant. Il n'est **pas enregistré** dans les paramètres de BestCrush.

Dans le jeu, sélectionner manuellement un même serveur, lancer **A-X**, attendre l'affichage complet, puis lancer **B-Z** tout en gardant A ouvert ; fermer et relancer B si utile. Ne copier **que les lignes commençant par `p3c10=`** du terminal/journal Debug, et les éventuelles erreurs de build/test. Ne pas partager le journal intégral, le corps de messages, un dump mémoire, le PCAP, les noms, les tokens ou les identifiants. Si `status=rejected` apparaît, cela peut signaler une fenêtre non attribuable au moment de `ksc`/ `ksb` ; **ne pas en inférer** une erreur de compte Ankama.

**Attention :** ce diagnostic Debug dépend de l'association OS déjà existante dans `LiveDofusClientNetworkBindings` avec inventaires rafraîchis périodiquement ; il n'est pas une réexécution exacte du snapshot Windows ponctuel P3c.7. Pour une intégration multicomptes réelle, il restera nécessaire de tester les fermetures hors trafic, les connexions recyclées et le changement de personnage. Aucune publication de données de professions n'est autorisée.

## P3c.10 — Checkpoint Windows confirmé le 23 octobre 2026

L'opérateur confirme : **1086 tests exécutés, 1086 réussis, 0 échec, 0 ignoré, 7,5 secondes**. Cela valide le build/test de l'implémentation du diagnostic P3c.10, pas encore son fonctionnement sur un flux Dofus réel.

**Prochaine étape : essai réel contrôlé sans nouvelle modification de code.** Fermer BestCrush et les clients Dofus ; lancer la version **Debug** de BestCrush avec `$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"`, sélectionner manuellement le serveur souhaité, ouvrir A-X, puis B-Z en laissant A ouvert, fermer et reconnecter B. Ne partager que les lignes `p3c10=` (status et compteurs anonymes), jamais les journaux complets ni les trames. Terminer en effaçant la variable du PowerShell : `Remove-Item Env:BESTCRUSH_P3C10_ROSTER_STUDY`.

Ce checkpoint **ne valide aucun compte Ankama, aucune identité de serveur réseau, aucune attribution de métiers**, et ne justifie pas de les activer. Le mode Release et le lancement normal sans variable restent sans observateur.


## P3c.10 — Capture réelle BestCrush Debug confirmée (23 octobre 2026)

**Préalable automatisé validé :** 1086/1086 tests Windows réussis, 0 échec, 0 ignoré, 7,5 s. L'application MAUI Debug a démarré par `dotnet run` avec `BESTCRUSH_P3C10_ROSTER_STUDY=1`. Aucun `p3c10` n'est apparu sur le terminal pendant l'essai ; les événements sont dans le fichier Serilog quotidien `%LOCALAPPDATA%\BestCrush\Logs\bestcrush20261023.log`. Les avertissements NU1701 concernant `OpenCvSharp4.WpfExtensions` et MacCatalyst n'ont pas empêché le build Windows.

**Extraction de l'opérateur** via `Get-ChildItem "$env:LOCALAPPDATA\BestCrush\Logs" -Filter "bestcrush*.log" | Select-String "p3c10="` :

| Heure locale Europe/Paris | Ligne de diagnostic anonyme | Interprétation |
| --- | --- | --- |
| 18:59:18.535 | `status=enabled mode=debug-opt-in` | Opt-in Debug actif |
| 18:59:20.936 | `status=reset reason=manual-server-change` | Réinitialisation après sélection manuelle |
| 18:59:46.286 | `status=pending linked-windows=0 equal-roster-pairs=0 disjoint-roster-pairs=0` | Premier roster candidat `ksc` en attente |
| 18:59:51.400 | `status=linked linked-windows=1 equal-roster-pairs=0 disjoint-roster-pairs=0` | Premier `ksb` lié au premier roster |
| 19:00:17.496 | `status=pending linked-windows=1 equal-roster-pairs=0 disjoint-roster-pairs=0` | Deuxième roster candidat en attente, première fenêtre conservée |
| 19:00:20.765 | `status=linked linked-windows=2 equal-roster-pairs=0 disjoint-roster-pairs=1` | Deux fenêtres simultanément liées, ensembles candidats disjoints |
| 19:00:51.869 | `status=pending linked-windows=1 equal-roster-pairs=0 disjoint-roster-pairs=0` | L'ancien lien B a disparu pendant la reconnexion ; A subsiste |
| 19:00:54.488 | `status=linked linked-windows=2 equal-roster-pairs=0 disjoint-roster-pairs=1` | B reconnecté et relié, distinct de A |

Chaque sortie comportait `account=unverified server=unverified`. Aucune sortie `status=rejected` dans les lignes fournies. **Résultat P3c.10 : PASS, sur ce scénario réel** : le worker de BestCrush observe `ksc/ksb`, rattache deux groupes candidats disjoints à deux fenêtres Windows simultanées et retrouve le second groupe après sa reconnexion sans perdre le premier. Le retour de `linked-windows=2` constitue une preuve de fonctionnement local du diagnostic, mais **ne prouve ni des comptes Ankama ni l'identité du serveur réseau**. Le scénario n'a pas produit d'identifiants de compte validés, d'association de métiers, ni de validation du changement de personnage dans une même fenêtre. Il ne démontre pas non plus la disparition d'une fenêtre sans trafic ultérieur.

**Attention :** le nombre `disjoint-roster-pairs=1` provient d'ensembles de candidats numériques comparés, et n'autorise aucune fusion de métiers ou publication de `VerifiedAccountServerKey`. La sélection manuelle du serveur reste suffisante pour les fonctions économiques, sans vérification de serveur imposée.

**Étape suivante recommandée :** une petite intégration de présentation éphémère seulement, liée à l'inventaire de fenêtres OS existant, sans répliquer dans SQLite et sans transférer de données vers `CurrentCharacterState` ou `CurrentAccountProfessionsState` tant que les identités sémantiques ne sont pas indépendamment confirmées. Avant d'autoriser une identité métier, protéger aussi le changement de personnage dans la même fenêtre, la fermeture sans nouveaux paquets et les erreurs de visibilité du socket.
