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
