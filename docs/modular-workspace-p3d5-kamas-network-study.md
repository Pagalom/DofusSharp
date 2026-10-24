# P3d.5 — Kamas, inventaire et banque : caractérisation du réseau

## Objectif

Étudier les **quatre flux de patrimoine** d'une session Dofus :

1. Kamas en poche.
2. Objets de l'inventaire personnel.
3. Kamas en banque.
4. Objets du coffre de banque.

**Décision de produit :** conserver les objets en mémoire pour les modules
fonctionnels (craft, calcul de ressources déjà possédées, rentabilité),
sans afficher de liste d'objets ni leurs statistiques dans la fiche
personnage. Celle-ci ne conserve que la présentation des deux montants
de Kamas, quand ils sont vérifiés. Aucun objet n'est sauvegardé dans
SQLite par cette phase.

Les deux stocks restent indépendants : une ressource est disponible
`inventaire` ou `banque` uniquement après observation positive
de ce conteneur. Pour le craft, une quantité non observée vaut
**inconnue**, jamais zéro.

Étudier séparément les deux cases de richesse déjà présentes dans
`CharacterSheetCard` :

- **Kamas du personnage** (portefeuille / inventaire)
- **Kamas de la banque**, dont la propriété compte + serveur doit être
  prouvée indépendamment avant toute mutualisation

Aucun solde ne doit être déduit d'une dépense théorique ou de la simple
sélection d'un personnage. Les champs `PocketKamas` et `BankKamas`
existent déjà dans `CharacterSessionProfile`, mais aucun décodeur Game
ne les alimente actuellement. Les fiches expérimentales liées à une fenêtre
n'ont pas d'identité de personnage confirmée.

Les prix observés en HDV ne sont **pas** un solde de Kamas. L'absence
d'observation doit rester `null`, jamais `0`.

## Tests et implémentations déjà retrouvés

Cette phase doit **réutiliser** les bases existantes plutôt que créer
un deuxième décodeur d'objets incompatible.

### Décodage des instances

`BestCrush.Network/Protocol/SemanticDecoders.cs` définit déjà :

- `ItemDetailObservation(ItemUid, ItemId, Quantity, Stats)` ;
- `ItemStatObservation(EffectId, Value)` ;
- `InventoryQuantityObservation(ItemUid, NewQuantity)` ;
- `InventoryRemoveObservation(ItemUid)`.

Le détail d'une instance doit distinguer **trois éléments** :

| Champ | Sens | Usage |
|---|---|---|
| `ItemId` | Référence d'objet commune aux copies du même type | Recettes, affichage dans les modules métiers |
| `ItemUid` | Instance/lot concret observé sur le flux | Mises à jour et identification d'équipements distincts |
| `Quantity` | Nombre possédé dans cette instance/lot | Calcul de ressources disponibles |
| `Stats` | Effets `EffectId` et valeurs signées propres à l'instance | Évaluation d'un équipement, craft/forgemagie et statistiques |

Les caractéristiques peuvent être absentes ou répétées ; **ne pas
dédupliquer** les effets par `EffectId`. Un `ItemUid` n'est pas
l'identifiant du type d'objet et ne doit pas être mélangé avec l'UID
de cotation d'une annonce HDV.

`Tests.BestCrush/Network/ItemDecoderTest.cs` contient déjà des
scénarios `kdb`/`isa` (enveloppe objet, UID, ID, quantité, stats
avec valeurs négatives et répétées), `iuq` (sortie craft),
`kbu` (résultat FM), `isf` (quantité modifiée) et `irz`
(suppression d'un UID). Ses fixtures sont synthétiques et ne
valident **pas** à elles seules les opcodes sur Dofus 3 courant.

`Tests.BestCrush/Network/RecentItemDetailsCacheTest.cs` protège
l'association bornée UID → détail d'objet (LRU et éviction) ;
**ce cache sert aujourd'hui à la corrélation de concassage, pas à
un inventaire exhaustif**.

`Tests.BestCrush/Network/DofusNetworkRoutingTest.cs` protège le
routage d'objets `kdb/isa/iuq/kbu`, le couplage actuel
`kch → kag` (instance → équipement concassé), la séparation
de deux connexions et le refus de recycler un même UID sur un autre
client. `kch/kag` sont corroborés par des observations réelles
(11/10/2026), contrairement aux autres routes historiques.

### Modèle temporaire des personnages, non alimenté en production

`BestCrush/Services/CurrentCharacterState.cs` contient déjà
`CharacterInventoryItem(ItemId, InstanceUid, Quantity)` et des
champs distincts `Inventory`, `BankInventory`, `PocketKamas`,
`BankKamas`. Il ne conserve **pas encore les `Stats`** au
niveau de `CharacterInventoryItem`.

`Tests.BestCrush/Network/CharacterSessionStateTest.cs` vérifie :

- stocks des personnages A et B indépendants ;
- inventaire inconnu (`null`) distinct d'un inventaire observé
  vide (`[]`) ;
- refus des valeurs négatives, des connexions étrangères
  et des messages tardifs ;
- purge des montants et stocks lors d'une nouvelle connexion.

**Cette couche ne reçoit pas aujourd'hui de snapshots d'inventaire
réseau vérifiés.** Elle est un prototype de stockage temporaire,
pas la preuve que le solde ou la banque sont synchronisés.

### Craft : données attendues et limite actuelle

`BestCrush.Domain/Services/CraftCostService.cs` utilise
actuellement les recettes et les observations de prix du marché ;
`Tests.BestCrush/CraftCostServiceTest.cs` valide le coût minimal
par lots x1/x10/x100, y compris les équipements ingrédients,
sans consulter le stock possédé.

L'intégration future devra exposer au calculateur de craft un **snapshot
de stock en lecture seule** (séparé du détail de fiche) avec :

- `ownedInventory` et `ownedBank` par `ItemId`, sans détruire
  les lignes d'équipements par `ItemUid` et `Stats` ;
- `required`, `available`, `missingToBuy`, calculés seulement
  sur des stocks réellement connus ;
- un choix explicite d'inclure la banque ou non, car un objet en banque
  n'est pas immédiatement dans l'inventaire du personnage ;
- le coût de **ce qui reste à acheter**, sans modifier les règles
  de prix, la combinaison optimale de lots ni les observations SQLite.

L'inventaire personnel est rattaché à une fenêtre/connexion validée ;
l'éventuelle mutualisation de la banque entre plusieurs personnages
requiert toujours une **preuve distincte de compte + serveur** (P3c).
Ne pas inférer qu'une banque inconnue est vide ou partagée.

## Phase 1 : localisation d'opcodes sans toucher au programme

Utiliser le mode **existant** de `BestCrush.NetworkProbe` :
`--character-scan`. Ce mode affiche les métadonnées des messages
(opcode obfusqué, direction, taille, numéro de session, heure),
sans imprimer les charges réseau ni leurs valeurs.

Sur Windows, depuis la racine du dépôt :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device <INDEX_A_VERIFIER> --character-scan
```

Remplacer uniquement le numéro d'interface détecté. Le probe doit être
**démarré AVANT Dofus** pour ne pas perdre le début des messages Game.

### Recette de capture

Faire l'expérience sur **un seul client Dofus** à la fois, dans une
session de capture neuve ; ne pas activer d'autres modes de recherche.

1. Lancer le probe, puis ouvrir un seul personnage et attendre la fin
   complète du chargement ; noter localement l'heure de cette étape.
2. Ouvrir son inventaire, relever localement le solde en poche, puis
   refermer ; noter l'heure. **Ne pas publier le solde.**
3. Effectuer une action bénigne **qui modifie réellement les kamas en
   poche** (par exemple achat d'un objet bon marché), attendre le
   rafraîchissement de l'inventaire et noter l'heure de l'action.
4. Dans l'inventaire personnel, ajouter un objet (ramassage
   ou fabrication) puis retirer ou consommer un objet, en notant
   les heures : identifier ajout, quantité et suppression séparément.
   Choisir si possible une ressource empilable et un équipement
   porteur de caractéristiques.
5. Ouvrir l'interface de banque, sans changer de compte ni de
   personnage. Noter l'heure de l'ouverture. Si possible, effectuer
   un petit dépôt de kamas contrôlé, puis noter l'heure.
6. Déplacer une petite pile de ressources inventaire → banque,
   puis banque → inventaire. Vérifier l'impact sur **les deux**
   conteneurs ; une mise à jour ne vaut pas snapshot intégral.
7. Fermer la banque et refaire une action d'inventaire pour vérifier
   que son flux n'est pas attribué par erreur à la banque.
8. Arrêter le probe par `Ctrl+C`.

Comparer les séries de messages **S→C**, par session et par tranche
horaire. L'identification d'un opcode fréquent pendant l'action n'est
**pas** une preuve qu'il contient le solde. Une fois 1–3 familles
plausibles identifiées, les examiner à l'aide du mode de forme
`--character-shape --shape-keys <OPCODES>` dans une seconde capture
séparée. Ce mode n'affiche que les formes protobuf, pas leurs valeurs.

### Informations à partager

Uniquement les lignes de métadonnées `Session #N S→C opcode taille`
pertinentes pour chaque étape et les noms des actions (« inventaire »,
« achat », « banque »), **sans publier les montants exacts**.

Ne transmettre **aucun** PCAP, dump, corps de message, adresse IP,
nom/ID de personnage ou de compte, jeton, cookie, nom de fenêtre ou
capture affichant des informations privées. Ne pas activer `--all`.

## Conditions avant implémentation dans BestCrush

- Démontrer un champ numérique de solde par comparaison locale avant /
  après, puis par au moins un second changement de solde indépendant.
- Caractériser le **snapshot complet** de l'inventaire et celui de la
  banque ; ne pas construire une liste complète à partir de seuls
  ajouts ou d'un cache LRU d'objets.
- Vérifier séparément l'ID du type, l'UID d'instance, la quantité et
  la structure d'effets; conserver les caractéristiques des équipements.
- Appliquer les deltas quantité/suppression seulement après le
  snapshot fiable de **leur** conteneur, et conserver la cohérence
  après dépôt/retrait banque et crafting.
- Identifier la provenance et le type de chaque message (Game S→C
  du client correct ; banque distincte du portefeuille).
- N'accepter qu'un message associé au **bail TCP Game + fenêtre +
  époque actif**, jamais au pseudo, au focus seul ou au serveur en
  consultation SQL.
- Montants en `long` contrôlés ; zéro explicite différent d'inconnu.
  Éviter toute interpolation du prix d'un objet acheté.
- Lors de la reconnexion, de l'éviction ou du changement de personnage :
  remettre les montants à `null` tant que la nouvelle session n'apporte
  pas une donnée fiable.
- Tester : fenêtres A/B avec le même nom, Game non lié, données tardives,
  fermeture sans nouveaux paquets, banque non ouverte, et changement
  d'époque de capture.
- **Ne jamais transférer la banque au modèle commun compte+serveur** sans
  validation P3c indépendante de cette identité.
- Ne modifier ni les prix HDV / coefficients, ni SQLite, ni les règles
  de sélection manuelle de serveur.

## État

**P3d.5.0 : protocole d'observation élargi, aucun opcode de
richesse ni de snapshot complet inventaire/banque validé.**
Aucun nouveau solde ni item n'est affiché ou utilisé dans le craft.
Les décodeurs unitaires existants restent réutilisables sous
réserve de vérification sur la version courante de Dofus.
La phase suivante sera construite à partir des observations
contrôlées de l'opérateur.

## P3d.5.1 — Premier relevé réel : chargement puis ouverture d'inventaire (24/10/2026)

L'opérateur a exécuté `--device 3 --character-scan` avec un seul client
Dofus et a transmis un relevé **de métadonnées uniquement**. Le
rattachement TCP est stable : session #1 ouverte puis fermée,
session **#2** active, `ksc` puis `ksb` reçus.

Parmi les messages **S→C** du chargement de session #2 :

| Opcode | Taille de corps | Heure UTC | Constat |
|---|---:|---|---|
| `mdp` | 5 857 octets | 03:14:52.802 | Gros message initial |
| `ica` | 2 485 octets | 03:14:52.802 | Message initial |
| `irl` | 12 532 octets | 03:14:52.803 | Gros message initial |
| `ico` | 26 472 octets | 03:14:52.819 | Gros message initial |
| `jei` | 6 002 octets | 03:14:52.819 | Message initial |
| `ifg` | 4 322 octets | 03:14:53.836 | Message initial |
| `isr` | 79 574 octets | 03:14:54.544 | Gros message, juste après un `jgm` C→S |

À 03:14:54.526, un premier `jgm` C→S précède `jhb` ; un
second `jgm` C→S est observé à 03:14:54.544, immédiatement
avant `isr`. **Il manque l'horodatage de l'action humaine exacte**.
Cette proximité n'établit donc ni que `jgm` signifie « ouvrir
l'inventaire », ni que `isr` en est le snapshot.

Des messages `icj` répétés suivent `ico`; sans preuve,
**ne pas** les interpréter comme des lignes d'objets.
Aucune visite de banque, modification contrôlée de quantité ni
comparaison avant/après sur un même solde n'est validée par cette trace.
Aucun opcode de Kamas, d'inventaire ou de banque n'est prouvé.

**Lien aux recherches P1b** :
`docs/modular-workspace-p1b-protobuf-shape-study.md` avait déjà repéré
`mdp,irl,ico,jei,isr` dans les rafales de chargement, sans
signification métier établie. Le relevé P3d.5.1 reproduit
leurs présences, sans lever cette incertitude.

### Étape suivante — formes protobuf (zéro valeur de compte ou objet)

Reprendre une **nouvelle** capture, probe lancé avant le client :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys mdp,ica,irl,ico,jei,isr
```

Charger le personnage, attendre le calme pendant environ 10 secondes,
noter l'heure locale de l'ouverture de l'inventaire,
ouvrir/fermer, attendre 5 secondes et arrêter avec `Ctrl+C`.
Ne transmettre que les lignes `shape=[...]` et les heures des
actions, sans payload brut.

Le compteur complet des champs **racine** sera distinct des
**échantillons** imbriqués, comme prévu par P1b. Les champs
`?f...` ne sont que des hypothèses de sous-messages, pas
des objets ni des montants de Kamas.

**Aucune modification du routeur métier, des vues, ni de SQLite.**

## P3d.5.2 — Deuxième relevé réel : formes uniquement (24/10/2026)

L'opérateur a relancé un unique client sur l'interface Npcap 3 avec
`--character-shape --shape-keys mdp,ica,irl,ico,jei,isr`. Capture
locale à 05:17, horodatages des messages en UTC (Europe/Paris UTC+2).
Tous les messages ci-dessous proviennent de la **session TCP #2 S→C**.

| Heure UTC | Opcode | Octets | Champs racine | Formes imbriquées échantillonnées |
|---|---|---:|---|---|
| 03:17:03.488 | `mdp` | 5857 | `1:lenx494` | `?1.1:vx3 ?1.2:vx3` |
| 03:17:03.496 | `ica` | 2475 | `2:len 5:lenx173 6:lenx43` | `?5.1:vx3 ?5.2:vx3 ?6.1:vx3 ?6.3:lenx3` |
| 03:17:03.496 | `irl` | 12495 | `2:lenx503 4:v` | `?2.1:lenx3 ?2.4:vx3` |
| 03:17:03.505 | `ico` | 26472 | `2:lenx170` | `?2.1:vx3 ?2.2:vx3 ?2.3:lenx3 ?2.4:lenx3 ?2.5:lenx3` |
| 03:17:03.505 | `jei` | 6002 | `1:lenx88` | `?1.3:vx3 ?1.4:lenx3` |
| 03:17:05.298 | `isr` | 79574 | `3:lenx8806` | `?3.1:vx3 ?3.2:vx3` |

L'opérateur déclare avoir ouvert son inventaire « à 5h17 »,
**sans secondes**. Cette indication situe l'ouverture dans la même
minute que la rafale initiale, sans permettre d'attribuer l'un
de ces messages à l'ouverture de l'interface.

**Interprétation limitée aux formes :**

- `ico` présente des objets imbriqués variés, `irl` et `jei`
  contiennent des collections répétées, `mdp` et `isr` des
  paires scalaires ; leur sens métier demeure **inconnu**.
- La présence de 8806 entrées `isr` **ne prouve pas**
  8806 objets en inventaire ou en banque.
- Aucun champ n'est validé comme ID, UID, quantité, effet,
  solde de Kamas ni identifiant de compte.
- Le mode `--character-shape` déduplique par forme et opcode
  dans chaque session : une réémission identique peut être masquée.
  L'absence de nouvelle ligne après l'ouverture ne démontre donc
  **pas** l'absence de réponse réseau.

### P3d.5.3 — Prochaine étape : différence temporelle contrôlée

Revenir au mode `--character-scan` pour observer **tous**
les opcodes, sans valeurs :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-scan
```

Depuis un seul client Dofus, lancé **après** le probe :

1. Charger le personnage et attendre **15 secondes sans action**.
2. Noter une heure avec les **secondes** puis ouvrir l'inventaire.
3. Attendre 5 secondes, le fermer, attendre 5 secondes, l'ouvrir
   une deuxième fois et noter de nouveau l'heure avec les secondes.
4. Après 5 secondes, arrêter (`Ctrl+C`).

Partager seulement les lignes **S→C/C→S** des périodes de repos
et d'ouverture, et deux heures d'action ; aucun PCAP ni valeur.
Un clic inventaire peut ne générer **aucun trafic** si le
jeu possède déjà le snapshot en cache. Ne jamais déclarer
un opcode « inventaire » à partir de sa seule proximité temporelle.
Si les deux ouvertures ne produisent rien de distinct, comparer
dans une phase ultérieure une modification réelle de pile d'objet,
puis l'ouverture et les transferts de banque.

**Statut : caractérisation structurale confirmée ; sémantique
inventaire, banque et Kamas toujours non validée.**

## P3d.5.4 — Transferts réels banque ↔ inventaire (24/10/2026)

L'opérateur a exécuté le mode `--device 3 --character-scan`
puis a effectué cinq actions, heures locales **approximatives**
(UTC+2). Les métadonnées proviennent d'une **nouvelle**
session TCP numérotée **#1** dans cette capture :
les numéros de session du probe ne sont pas des identifiants
persistants de personnage ou de compte.

| Action déclarée (Europe/Paris) | Trace observée (UTC) | Observation |
|---|---|---|
| Ouvrir banque à ~05:23:07 | 03:23:02.222–03:23:03.256 : `ilt` 18 (C→S) puis `ilx` 16, `ilf` 26, `kis` 2, `lof` 7, `ith` 5, `jyf` 8, `irp` **11 677** (S→C) | Raffale liée temporellement au début du dialogue bancaire ; heure déclarée décalée d'environ 5 s, sémantique inconnue |
| Transférer 1 unité banque → inventaire à ~05:23:34 | 03:23:33.525–.526 : `jys` 16 (C→S) puis `iqx` 27, `iqz` 28, `iqq` 6 (S→C) | Première séquence de transfert candidate |
| Transférer 2 unités supplémentaires banque → inventaire à ~05:23:51 | 03:23:50.669–.670 : `jys` 16 (C→S) puis `irx` 8, `iqz` 28, `iqq` 6 (S→C) | Même requête de 16 octets, retour différent (`irx` au lieu de `iqx`) |
| Transférer 3 unités inventaire → banque à ~05:24:10 | 03:24:09.835–03:24:10.848 : `jys` **6** (C→S), puis `iqz` 28, `iqm` 4, `iqq` 6 (S→C) | Forme de requête plus courte dans le sens inverse et retour `iqm` au lieu de `iqx/irx` |
| Fermer banque à ~05:24:25 | 03:24:24.960 : `kbr` 2 (S→C) | Simple coïncidence temporelle candidate, non confirmée |

Des `kne` / `knb` périodiques se produisent durant les pauses,
indiquant un trafic de fond à exclure de toute inférence métier.

### Bilan des hypothèses

- **Forte corrélation temporelle** : `jys` correspond
  aux trois actions de transfert déclarées.
  La première et deuxième requête font 16 octets ;
  la troisième (sens inverse) fait 6 octets. Ce sont
  des **hypothèses d'usage**, pas un décodage prouvé.
- `iqz` 28 + `iqq` 6 arrivent après chaque
  transfert candidat, quel que soit son sens.
- `iqx` 27 n'apparaît qu'au premier transfert,
  `irx` 8 au deuxième, `iqm` 4 au troisième.
  **Ne pas affirmer** qu'il s'agit respectivement d'ajout,
  de quantité ou de suppression sans validation du contenu.
- `irp` 11 677 octets est un candidat de chargement
  de banque, mais la différence d'environ 5 secondes entre
  l'heure de l'action et la trace empêche de conclure.
  Sa taille ne suffit pas à établir qu'il s'agit d'une liste
  d'objets ou du solde bancaire.
- Aucun opcode n'est encore démontré porter `ItemId`,
  `ItemUid`, `Quantity`, `Stats` ou des Kamas,
  et aucune écriture en stock n'est permise sur cette seule base.

### Capture suivante : formes contrôlées

Faire une capture **nouvelle**, démarrée avant le jeu pour
éviter la perte de la structure TCP de début de connexion :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys jys,iqx,irx,iqz,iqm,iqq,irp,ilt,ilx,ilf,kbr
```

Un seul client. Après chargement, ouvrir la banque, noter l'heure ;
transférer 1 unité (banque → inventaire), 2 unités supplémentaires,
puis les 3 unités dans l'autre sens, avec quelques secondes entre
chaque geste ; fermer la banque. Communiquer uniquement les lignes
`shape=[...]`, jamais les contenus de messages, valeurs, ID, UID,
identifiants de compte ou captures d'inventaire.

**Limite du mode :** seulement les **trois formes distinctes**
par opcode et session sont émises ; une forme répétée ne produit
pas une ligne à chaque transfert. Ne pas interpréter cette
déduplication comme l'absence de message réseau.

Ensuite comparer **localement**, sans export des valeurs,
les candidats structuraux avec les opérations réellement faites.
Ne modifier le fichier `BestCrush.Network/protocol-map.json`
et les décodeurs qu'après preuve sur version courante.

## P3d.5.5 — Formes des messages de transfert réels (24/10/2026)

Nouvelle capture `--character-shape --shape-keys jys,iqx,irx,iqz,iqm,iqq,irp,ilt,ilx,ilf,kbr` réalisée avec une unique session TCP **#1** ; heures de l'opérateur
en Europe/Paris UTC+2, à la seconde près mais légèrement
approximatives.

| Étape | Heure signalée | Message observé UTC | Forme protobuf |
|---|---|---|---|
| Banque ouverte | 05:27:22 | 03:27:19.884 `ilt` C→S | `root=[1:v 2:v 3:v]` |
| Banque ouverte | 05:27:22 | 03:27:19.911 `ilx` S→C | `root=[3:v 5:v]` |
| Banque ouverte | 05:27:22 | 03:27:19.912 `ilf` S→C | `root=[1:lenx2 2:len 3:v]` |
| Banque ouverte | 05:27:22 | **03:27:21.923 `irp` S→C, 11 677 o** | `root=[1:v 2:lenx559]` ; sur trois lignes échantillonnées `?2.1:len ?2.4:v ??2.1.3:v ??2.1.4:v ??2.1.5:v` |
| Retrait 1 | ~05:27:39 | 03:27:41.095 `jys` C→S, 16 o | `root=[1:v 3:v]` |
| Retrait 1 | ~05:27:39 | 03:27:41.096 `iqx` S→C, 28 o | `root=[2:len]` ; `?2.1:len ?2.4:v ??2.1.3:v ??2.1.4:v ??2.1.5:v ??2.1.7:len` |
| Retrait 1 | ~05:27:39 | 03:27:41.096 `iqz` S→C, 28 o | `root=[1:len]` ; `?1.1:len ?1.4:v ??1.1.3:v ??1.1.4:v ??1.1.5:v ??1.1.7:len` |
| Retrait 1 | ~05:27:39 | 03:27:41.097 `iqq` S→C, 6 o | `root=[1:v 2:v]` |
| Retrait 2 | ~05:27:51 | 03:27:52.184 `irx` S→C, 9 o | `root=[3:len]` ; `?3.1:v ?3.3:v` |
| Dépôt 3 | ~05:28:04 | 03:28:05.290 `iqm` S→C, 5 o | `root=[1:v]` |
| Fermeture heure perdue | inconnue | 03:28:16.363 `kbr` S→C, 2 o | `root=[1:v]` |

Les `jys`, `iqz` et `iqq` associés aux étapes 2/3
ne sont **pas tous visibles** : le mode forme déduplique les
structures identiques d'un opcode **sur une même session**.
Le premier relevé `--character-scan` confirmait néanmoins
trois `jys`, trois `iqz`, trois `iqq`.

### Comparaison structurale la plus prometteuse

- Le chargement personnage contenait `irl` avec
  `root=[2:lenx503 4:v]` et un objet échantillon
  `?2.1:len, ?2.4:v, ??2.1.3:v, ??2.1.4:v, ??2.1.5:v`.
- L'ouverture bancaire contient `irp` avec
  `root=[1:v 2:lenx559]` **et les mêmes chemins
  d'enregistrements pour ses éléments racine f2**.
- `iqx` (racine f2) et `iqz` (racine f1) ont
  des sous-messages encore similaires, avec en plus
  un champ opaque `...7:len`.

**Hypothèse de travail :** `irl` = liste initiale
de l'inventaire, `irp` = liste du coffre,
`iqx/iqz` = lignes de modification liées aux
transferts, `irx/iqm` = messages courts d'ajustement
ou de retrait. **Aucun de ces sens n'est prouvé.**
En particulier, `559` et `503` sont des **nombres
de champs protobuf répétés**, pas des quantités
confirmées d'objets possédés.

### Prochain test : contrôle différentiel d'une pile entière

Sans écrire de décodeur sur un champ supposé, vérifier
si `irp` reflète bien un stock bancaire :

1. Dans la banque, choisir une **pile entière de ressource
   sans valeur**, présente comme une ligne distincte,
   qu'il est sûr de retirer et de remettre.
2. Avec `--character-shape --shape-keys irp,irl,iqx,iqz,irx,iqm`
   démarré **avant le client**, ouvrir la banque, noter
   le nombre `2:lenxN` du premier `irp`.
3. Transférer **la pile entière** dans l'inventaire.
   Fermer/réouvrir la banque. Si un second `irp` est
   reçu, comparer `N` et `N-1`. L'absence d'un
   second `irp` peut simplement signifier un cache UI.
4. Retransférer toute la pile dans la banque, puis
   rouvrir et chercher un nouvel `irp` avec
   le nombre initial `N`.
5. Noter les **secondes exactes** des manipulations ;
   ne transmettre que les formes et counts anonymes.

Ne pas présumer que la banque possède un snapshot
réémis à chaque ouverture : si rien ne revient,
effectuer une nouvelle session distincte **après**
la fermeture/reconnexion complète du jeu pour voir
si le compteur du chargement varie comme prévu.
À défaut de contrôle positif et négatif, conserver
la sémantique `bank=unverified`, ne jamais exposer
des calculs de craft fondés sur `irp`.

Aucune modification de production, aucun nouvel
enregistrement d'objets, aucun solde de Kamas prouvé.
