# BestCrush — P0 : identification réseau fiable du personnage (multi-personnages)

## Statut et limite vérifiée (12 octobre 2026)

Le catalogue `BestCrush.Network/protocol-map.json` référence les événements de marché, d'inventaire, d'atelier et de concassage ; **aucun opcode de connexion, déconnexion, sélection du personnage, caractéristiques ou métiers n'est actuellement vérifié et cartographié**.

L'identification automatique n'est **pas activée dans l'application** par ce lot. Il serait dangereux de déduire le personnage à partir des seules observations d'inventaire, de prix ou du nom saisi manuellement. Les opcodes Dofus sont obfusqués et peuvent changer de build.

M2 (persistance du **serveur uniquement**) est publié mais attend toujours son checkpoint Windows. Les fiches des personnages, y compris les inventories et métiers, n'existent qu'en RAM et ne doivent jamais être enregistrées entre sessions. P0 doit rester isolé : pas de changement de capture Npcap, d'OCR F8, de base, de routage métier ou de persistences existantes.

## Architecture cible

### Identité et sessions

- **Personnage** : clé stable attendue `(Serveur, CharacterId)`, vérifiée depuis le protocole. Le nom, la classe et le niveau sont des attributs, pas la clé primaire.
- **Connexion** : clé canonique TCP spécifique à chaque client, avec génération de serveur, génération de capture et génération de connexion (reconnexion). Ne jamais fusionner deux clients simultanés.
- **Activation** : deux **familles d'observations indépendantes** portant le même identifiant et nom de personnage, accompagnées de classe et niveau valides. Deux répétitions d'un même message ne comptent pas comme deux preuves.
- **Déconnexion / changement / contradiction** : retrait immédiat de l'identité confirmée ; les observations de métiers restent non attribuées tant que l'identité n'a pas été à nouveau confirmée.
- **Messages tardifs** : ignorer ceux qui portent une ancienne génération de serveur, de capture ou de connexion, ou une date antérieure à la dernière observation acceptée.
- **Plusieurs connexions confirmées** : aucune sélection globale automatique ; associer explicitement le personnage à la fenêtre Dofus ou demander à l'utilisateur de choisir sa session.
- **Métiers** : n'accepter une observation que si elle appartient à la même session confirmée ; vérifier l'identifiant personnage lorsqu'il est présent. Les données seront conservées par `(Serveur, CharacterId, Métier)` seulement une fois le protocole validé.

Le `CharacterIdentityCorrelator` est une **machine à états préparatoire pure**, sans branchement réseau ni persistance. Ses sources sémantiques sont des catégories **à découvrir**, pas des mappings déjà démontrés.

### Limites à résoudre avant activation

1. Le flux `DofusWireMessage` actuel contient direction, opcode, corps, timestamp, bail serveur et captureEpoch ; il **ne contient pas de clé TCP de session**. Impossible de corréler de manière sûre des comptes simultanés avec ces seuls champs.
2. `DofusNetworkCaptureService.OnPacketArrival` ignore les paquets TCP sans données ; un FIN/RST ne remonte donc pas au routeur métier. Un mécanisme de déconnexion/reconnexion (avec timeout de session prudent) est nécessaire.
3. Le `NetworkProbe` existant utilise un réassembleur par direction et non par 5-tuple ; ne pas utiliser son résultat lors de **captures simultanées de plusieurs clients** sans corriger ce point.
4. `CurrentCharacterState` est un registre multi-personnages **éphémère** : aucune liste de personnages ni leur dernière sélection ne sont restaurées après fermeture. Son alimentation réelle par le protocole reste à implémenter.
5. `CurrentServerState` connaît le serveur **sélectionné par l'utilisateur** ; les paquets ne prouvent pas encore sur quel serveur Dofus est connecté. Un changement de personnage vers un autre serveur doit donc rester non attribué jusqu'à confirmation.

## Protocole expérimental P1 (Windows, une seule instance du jeu)

Utiliser `BestCrush.NetworkProbe` pour une **caractérisation locale**, sans recopier de payload en clair :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device <INDEX> --all
```

En gardant l'horloge visible, noter les **horodatages** de ces gestes :

1. Lancer la capture, puis se connecter sur **personnage A**.
2. Ouvrir les **caractéristiques** et le panneau des **métiers** (touche `J`) de A.
3. Fermer ces panneaux ; quitter A, revenir à l'écran de sélection.
4. Se connecter sur **personnage B du même compte et du même serveur**, avec un niveau ou une classe différents ; ouvrir ses caractéristiques puis ses métiers.
5. Déconnecter B, se reconnecter sur A, répéter les panneaux.
6. Relancer la capture et recommencer pour caractériser la reconnexion (ne pas présumer qu'une nouvelle connexion TCP est toujours créée).

Dans les sorties à partager, utiliser **seulement** les timestamps, les directions `C→S` / `S→C`, les clés de messages, les longueurs et les étapes manuelles. **Ne jamais publier** `wire.jsonl` brut, `bodyBase64`, jetons, identifiants privés ou autres trames sensibles ; le debug réseau existant peut contenir ces informations.

Corréler temporellement ces événements ne **prouve pas** encore la sémantique. Les messages candidats seront ensuite vérifiés localement sur plusieurs sessions, leur structure protobuf examinée avec prudence, puis des fixtures anonymisées seront ajoutées aux tests.

## Jalons

- **P0** : spécification et `CharacterIdentityCorrelator` isolé + tests sans toucher aux flux en production.
- **P1** : caractérisation des vraies trames identité / déconnexion / professions ; séparation des flux TCP dans le probe, données de diagnostic minimisées.
- **P2** : décodeurs versionnés et tests protobuf, propagation de la connexion et des resets dans `DofusWireMessage`, protection contre les messages tardifs ou mélangés.
- **P3** : fiches temporaires en RAM, niveau/classe/métiers/inventaires/kamas par identifiant stable, portraits de sélection manuelle et ciblage automatique de la fenêtre Dofus, avec indicateur de confiance. **Pas de persistance personnage.**
- **P4** : tests réels changement A→B→A, reconnexion, changement serveur, multi-compte simultané, ouverture des métiers, progression et F7/F8/concassage.

**Aucune synchronisation automatique active ne doit être prétendue fonctionnelle tant que les preuves P1/P2 et le checkpoint Windows ne sont pas validés.**

### Confidentialité et durée de vie

- Les données de personnage ne sont **jamais** écrites en Preferences, SQLite, exports, journal métier ni dans un cache durable. Le dernier joueur n'est pas repris au redémarrage.
- Seule la sélection du serveur (pas celle d'un personnage) peut rester mémorisée.
- Attention : le mode diagnostique historique `NetworkDebugWriter` peut enregistrer sur disque des corps réseau bruts (`wire.jsonl`), potentiellement porteurs d'informations de personnage. **Avant de brancher les décodeurs d'identité, il faudra empêcher ces données personnelles de se retrouver dans les captures conservées sur disque ou limiter/redacter ces diagnostics.** Le prototype actuel de fiche temporaire ne branche aucun nouveau décodage réseau.
- L'absence de réponse du serveur ne signifie pas « inventaire vide » ni « 0 kama ». Les deux inventaires et les deux soldes sont des champs observés indépendants ; `null` représente une donnée inconnue.
