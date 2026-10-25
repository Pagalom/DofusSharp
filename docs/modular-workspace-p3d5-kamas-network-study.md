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

## P3d.5.6 — Retrait complet confirmé par diminution du roster bancaire (24/10/2026)

Nouvelle capture `--character-shape --shape-keys irp,irl,iqx,iqz,irx,iqm`
avec une seule session TCP #1 ; expérience contrôlée annoncée par
l'opérateur : ouvrir banque, **retirer une pile entière**, rouvrir
banque, remettre la pile, rouvrir banque.

| Heure UTC | Opcode | Taille | Forme/résultat |
|---|---|---:|---|
| 03:32:42.758 | `irp` | **11 698** octets | `root=[1:v 2:lenx559]` |
| 03:32:45.789 | `iqx` | **16** octets | `root=[2:len]` avec sous-champs objet `?2.1:len ?2.4:v ??2.1.3:v ??2.1.4:v ??2.1.5:v` |
| 03:32:49.831 | `irp` | **11 682** octets | `root=[1:v 2:lenx558]` |
| 03:32:51.844 | `iqz` | **15** octets | `root=[1:len]` avec sous-champs analogues |
| 03:32:51.844 | `iqm` | **5** octets | `root=[1:v]` |

**Preuve expérimentale forte du roster du coffre :**
retirer une pile complète d'une banque supposée produire
559 lignes entraîne exactement la diminution attendue
`559 → 558` des champs répétés `irp.f2`. La longueur
diminue également de 16 octets. Une autre observation
indépendante avait déjà établi la forte corrélation
de `irp` avec l'ouverture du coffre bancaire.

Ce résultat **étaye fortement** que `irp.f2[]` est la
collection des lignes de banque. Il ne prouve toujours
pas la signification exacte de chaque sous-champ : UID,
ItemId, quantité et stats sont **non vérifiés**.

La restauration de la pile a déclenché `iqz` puis
`iqm`, mais aucun troisième `irp` n'apparaît dans
la sortie de forme. Le mode `--character-shape`
**déduplique les formes identiques** dans une session ;
un `irp` final à 559 lignes identique à l'initial
serait masqué. Cela ne démontre ni absence de
transmission ni restauration confirmée par le réseau.

### Suite P3d.5.7 : corroborer le snapshot de l'inventaire

L'ancien chargement Dofus a montré
`irl root=[2:lenx503 4:v]`. Son sous-message répété
`?2.1:len ...` a la même forme structurelle
que `irp.f2[]`, `iqx` et `iqz`.

Pour tester si `irl.f2[]` représente les lignes
d'inventaire, choisir une pile entière initialement
**absente de l'inventaire**, puis comparer deux
connexions complètes du même personnage :

1. Capture **A**, avant transfert :
   `--device 3 --character-shape --shape-keys irl,irp`
   démarrée avant connexion ; noter le compte `irl.f2[]`.
2. Transférer l'intégralité d'une pile de banque vers
   l'inventaire et la laisser **dans l'inventaire**.
   Quitter complètement le client Dofus.
3. Capture **B** avec la **même** commande,
   et une nouvelle ouverture du client : noter le
   compte `irl.f2[]`. S'il s'agit d'une nouvelle
   ligne, attendre une variation de **+1**.
4. Replacer la pile en banque à la fin de l'essai.
   Ne pas supposer que le nombre total d'objets
   reste invariant si des actions du jeu ont modifié
   le stock entre les captures.

**Ne pas** envoyer les identifiants de la ressource,
les quantités, valeurs scalaires ou payloads ; seuls
les comptes `irl`/`irp`, heures et conditions
suffisent à cette étape. Une stabilité de `irl.f2[]`
peut être compatible avec une pile déjà existante
dans l'inventaire ou un autre mécanisme de snapshot ;
il faut alors changer le contrôle.

**Pour interpréter les champs numériques eux-mêmes**,
il faudra ensuite un diagnostic local explicite et
testé, qui confronte les valeurs à des références
opérateur en RAM et ne renvoie que les chemins
correspondants, jamais les valeurs privées.

Aucune modification du routeur métier, de
`CharacterSessionProfile`, du craft ou de SQLite.

## P3d.5.7 — Validation croisée des lignes inventaire + banque (24/10/2026)

Nouvelle capture `--character-shape --shape-keys irl,irp`
d'un **même personnage**, avec fermeture intégrale de Dofus
et nouvelle connexion après un transfert d'une pile entière
de banque vers inventaire. Un seul client Dofus en cours
d'essai, probe Npcap actif pendant la reconnexion.

| TCP du probe | Heure UTC | Sens | Opcode | Taille | Nombre d'enregistrements |
|---|---|---|---|---:|---:|
| #2 avant transfert | 03:35:08.078 | S→C | `irl` | 12533 | `root.f2`: **502** |
| #2 avant transfert | 03:35:13.849 | S→C | `irp` | 11616 | `root.f2`: **559** |
| #4 après transfert | 03:35:31.112 | S→C | `irl` | 12545 | `root.f2`: **503** |
| #4 après transfert | 03:35:38.010 | S→C | `irp` | 11669 | `root.f2`: **558** |

Les variations sont **opposées et exactement égales à
une ligne** : inventaire `502 → 503`, banque `559 → 558`.
La forme interne `f2[].f1` est identique dans
`irl` et `irp` pour les chemins échantillonnés.

### Conclusion étayée

- `irl.f2[]` et `irp.f2[]` sont désormais des **collections
  de lignes de stock très fortement étayées** par un contrôle
  croisé positif et négatif et une reconnexion.
- Cette expérience ne démontre **ni** les quantités totales,
  **ni** la signification de chaque champ numérique,
  **ni** la façon dont les piles fusionnent,
  ni les champs de Kamas.

### Correspondance à contrôler avec le décodeur `kch` réel

Le protocole `kch` utilisé pour corréler les concassages
(`Tests.BestCrush/Network/NetworkPayload.cs` et
`BestCrush.Network/protocol-map.json`, route
`item_detail_current`, observée le 11/10/2026)
dispose d'une instance imbriquée sous `root.f3.f1`
avec :

- `f3` : quantité, déjà validée pour **l'objet de `kch`** ;
- `f4` : `ItemId`, déjà validé pour **l'objet de `kch`** ;
- `f5` : `ItemUid`, déjà validé pour **l'objet de `kch`**.

Dans les lignes de stock `irl.f2[].f1` et
`irp.f2[].f1` et les deltas `iqx.f2.f1`,
`iqz.f1.f1`, les champs `f3`, `f4`, `f5`
ont précisément les mêmes **types protobuf**.
**Hypothèse forte de réutilisation d'un objet réseau**, mais
il serait incorrect de transférer sans vérification
les significations `kch` aux messages `irl/irp/iqx/iqz`.

`f7:len` apparaît dans certaines lignes et certains
deltas ; c'est un **candidat opaque de caractéristiques**
éventuelles (et pas encore une preuve).
L'étude doit conserver chaque instance `UID`
et ses effets, sans écraser les différences de jet.

### Prochaine étude P3d.5.8 — Équipement avec effets

Effectuer dans un seul client une capture **forme seule**,
avant la connexion :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys irp,irl,iqx,iqz,irx,iqm,kch
```

1. Ouvrir la banque ; choisir un seul équipement avec des
   caractéristiques non vides (pas une ressource), connu
   comme une seule instance et absent de l'inventaire.
2. Transférer l'équipement entier banque → inventaire,
   noter l'heure locale **à la seconde**.
3. Attendre au moins cinq secondes et retransférer le
   même équipement inventaire → banque, noter l'heure.
4. Arrêter par `Ctrl+C` et ne transmettre que les
   `shape=[...]`, jamais les statistiques/UID/ID bruts.

Chercher si les deltas porteurs de la ligne affichent
`...7:len` de manière reproductible et si les mêmes
champs structuraux `3:v,4:v,5:v` sont présents.
Le mode actuel n'explore pas la profondeur des
`f7` et **ne valide aucune valeur** ; une étude locale
à références numériques anonymisées sera nécessaire
avant toute intégration métier.

**Statut : détection de deux listes de stock confirmée
expérimentalement ; champs numériques et statistiques
non décodés ; pas de migration, SQLite ni UI.**

## P3d.5.8 — Corrélation approfondie avec les tests historiques (24/10/2026)

Cette section compare les **formes réseau réelles**
des captures P3d.5.1 à P3d.5.7 avec
les tests et décodeurs existants. Les champs ci-dessous
restent des **hypothèses P3d.5** tant qu'une
valeur connue en jeu n'a pas été comparée
localement à un champ identifié.

### Hypothèse de structure commune (très forte)

Le protocole `kch` actuel a déjà été caractérisé en P3.0,
testé dans `Tests.BestCrush/Network/CurrentCrushProtocolTest.cs` :

```
kch.root.f3.f1:
  f3 = Quantity       [prouvé pour kch]
  f4 = ItemId         [prouvé pour kch]
  f5 = ItemUid        [prouvé pour kch]
  f7 = ?              [non caractérisé]
```

Nos nouveaux **enregistrements anonymisés** ont exactement
les champs `f3:v, f4:v, f5:v` dans une sous-structure
`f1:len` :

```
irl.root.f2[] .f1: { f3:v, f4:v, f5:v, f7?:len }
irp.root.f2[] .f1: { f3:v, f4:v, f5:v, f7?:len }
iqx.root.f2  .f1: { f3:v, f4:v, f5:v, f7?:len }
iqz.root.f1  .f1: { f3:v, f4:v, f5:v, f7?:len }
```

Les `f7` viennent de l'échantillonnage P1b de
`irl` (`??2.1.7:lenx35`) et des premiers deltas
`iqx`/`iqz` (présence variable selon objet) :
**présence et répétition compatibles avec des effets
d'équipement**, mais les valeurs et sous-champs
d'effets restent **inconnus**. En particulier,
`ProtoShapeSummarizer.MaximumDepth = 2` et
`MaxSamplesPerPath = 3` : les diagnostics existants
**ne décodent pas l'intérieur de f7** et les trois
premières lignes de banque peuvent n'avoir aucun effet.
Les tests d'un équipement avec stats sont indispensables.

### Réutilisation des décodeurs d'objets et deltas

| Nouveau opcode | Ancien test / méthode | Correspondance à vérifier |
|---|---|---|
| `iqx` | `ItemDecoderTest` / `TryDecodeItemDetail` | Enveloppe actuelle root f2.f1 → canonique root f2.f5 ; objet f3→quantité, f4→ItemId, f5→UID |
| `iqz` | idem | Enveloppe actuelle root f1.f1 → canonique root f2.f5 ; mêmes champs internes |
| `irx` | `ItemDecoderTest.InventoryQuantityAllowsZeroAndRemovalOnlyNeedsUid` / `TryDecodeInventoryQuantity` | Actuel root f3.len avec inner f1 et f3 ; ancien root f3.len avec inner f2=quantité, f3=UID ; vérifier la permutation f1→f2 |
| `iqm` | idem / `TryDecodeInventoryRemove` | Actuel root f1 varint ; ancien root f1=UID ; **aucune preuve par forme seule** |
| `irl` | `CurrentCrushProtocolTest` + `ItemDecoderTest` | Itérer les lignes root f2[] et réutiliser un parseur d'instance commun |
| `irp` | mêmes tests | Idem, dans un conteneur **bancaire distinct** |

Le vieux `ItemDecoderTest` vérifie les effets
`ItemStatObservation(EffectId, signed Value)` sur
le **format canonique historique** : effets sous f3
avec `f1=EffectId` et `f10=Value`. Ce schéma
n'est **pas** confirmé pour l'actuel `f7`.
Ne pas ajouter de `f7 → f3` dans
`protocol-map.json` sans expérimentation.

`ProtocolWireNormalizerTest.ConfigOnlyCanRemapDeepItemAndStatFields`
prouve déjà que le normaliseur sait adapter des numéros
de champs et des enveloppes **profondément imbriquées**.
La solution visée réutilise cet outil et un seul décodeur
d'objets, sans coder quatre lectures divergentes.

### Contraintes d'intégration / raccourcis sûrs

1. Le profil actuel conserve les routes historiques
   `inventory_add=isa`, `inventory_quantity=isf`,
   `inventory_remove=irz`, dont le statut est
   `legacy-3.6.11.15`. Ne **pas remplacer ces clés**
   en prétendant qu'elles sont validées actuellement.
   Créer des routes actuelles distinctes une fois
   confirmées, sans régression des anciennes.
2. `Tests.BestCrush/Network/ProtocolWireNormalizerTest.cs`
   impose un mapping et un statut par route ;
   à adapter avec le fichier de mapping commun et la
   copie utilisée par les tests.
3. `CurrentCharacterState.CharacterInventoryItem`
   ne comprend actuellement que `ItemId`,
   `InstanceUid` et `Quantity`, **pas les effets** :
   le modèle RAM devra accueillir les caractéristiques
   par instance sans les confondre avec les
   caractéristiques **théoriques** de recette ou du
   catalogue `DofusDbItemEffect`.
4. Le cache `RecentItemDetailsCache` est un
   cache LRU de **corrélation de concassage**,
   pas un inventaire ni une source de vérité du stock.
5. Les champs scalaires `irl.root.f4`,
   `irp.root.f1` et `iqq.f1/f2`
   ne sont **pas** des Kamas validés.
6. Tous les futurs snapshots et deltas seront
   liés strictement au bon flux Game/fenêtre
   et à sa génération de capture, sans
   mélange entre personnages ni rémanence après
   reconnexion.

### Tests synthétiques recommandés ensuite

- `irl` et `irp` à zéro / une / plusieurs lignes
  avec mêmes `ItemId` et `ItemUid`, mais
  containers séparés ;
- ajout `iqx/iqz`, ajustement `irx`,
  suppression `iqm`, répétitions idempotentes,
  corruption ou trames coupées ;
- équipement portant plusieurs effets, des
  effets identiques répétés, valeurs négatives,
  et deux copies de même `ItemId` de jets différents ;
- changements A/B simultanés, même nom, UID
  réutilisé, bail expiré, flux non lié ;
- inventaire non observé `null` distinct
  de snapshot valide vide ; banque non ouverte
  et deltas arrivant avant un snapshot complet.

La caractérisation réelle **précède** les
modifications de `BestCrush` : aucun mapping,
solde, inventaire ni affichage n'est activé
sur cette seule corrélation.

## P3d.5.9 — Capture équipement (03:39–03:40 UTC) et mutualisation atelier/HDV

### Observation expérimentale

Nouvelle capture d'un équipement avec caractéristiques,
avec **deux essais effectués par l'opérateur** :
l'un conserve la banque ouverte entre les transferts,
l'autre la ferme entre les transferts. Les
`--character-shape` ont été **dédupliqués par
opcode + forme + session**, de sorte que les sorties
ne séparent pas toutes les manipulations.

| Heure UTC | Opcode | Taille | Champs racine et sous-champs pertinents |
|---|---|---:|---|
| 03:39:57.897 | `irl` | 12 541 | `f2:lenx502`, `f4:v` ; lignes `f2[].f1.f3/f4/f5:v`, parfois `f7:len` |
| 03:40:03.753 | `irp` | 11 659 | `f2:lenx559`, `f1:v` ; échantillons de lignes sans `f7` (pas preuve d'absence) |
| 03:40:07.786 | `iqx` | 27 | `root.f2.f1` : `f3:v f4:v f5:v f7:lenx2` |
| 03:40:13.832 | `iqz` | 28 | `root.f1.f1` : `f3:v f4:v f5:v f7:lenx2` |
| 03:40:13.833 | `iqm` | 4 | `root.f1:v` |
| 03:40:22.919 | `irp` | 11 632 | `f2:lenx558`, `f1:v` |

**Indice fort nouveau :** lors du transfert d'un équipement
avec caractéristiques, `iqx` et `iqz` contiennent
**tous les deux deux occurrences** de `f7:len`.
Cette répétition est cohérente avec des effets d'équipement,
mais il ne faut **pas** assimiler deux occurrences à deux
statistiques avant comparaison avec l'affichage en jeu et
examen des sous-messages.

Le `irp` final à 558 après l'initial à 559 ne permet
**pas**, sans un chronogramme action par action, d'affirmer
une restitution finale de l'équipement ni un état attendu
du coffre. Aucun `irl` après le premier chargement dans
cette trace : on ne peut pas déduire son effectif final.
Les comportements banque ouverte vs fermée ne
peuvent pas encore être distingués avec des formes
dédupliquées.

### Comparaison du code déjà existant

- `SemanticDecoders.TryDecodeItemObject` est déjà
  **un parseur privé mutualisé** entre
  `TryDecodeItemDetail`, `TryDecodeInventoryAdd`,
  `TryDecodeCraftOutput`, `TryDecodeSmithmagicStack`
  et `TryDecodeSmithmagicResult`. Son objet canonique
  est `ItemDetailObservation(ItemUid,ItemId,Quantity,Stats)`.
  Il lit les effets canoniques depuis `item.f3[]`
  avec `effect.f1` et `effect.f10`.
- `TryDecodeWorkshopSlot` existe aussi, mais ne
  renvoie actuellement que **UID + delta** :
  un atelier peut référencer un objet déjà connu
  sans contenir ses données complètes.
- `TryDecodeMarketListingRequest` traite
  **UID d'instance + quantité + prix** sur
  l'ancien schéma ; `TryDecodeMarketListingCreated`
  traite **UID d'annonce, ItemId, quantité, prix et
  effets**. **UID d'annonce ≠ UID d'instance**.
- Les `jzs` actuels sont **validés pour le prix
  des équipements x1 et les lots de ressources/runes**,
  ainsi que pour l'item sélectionné ; ils
  ne constituent **pas** un flux validé d'instances
  ou de caractéristiques des offres.
- Les routes historiques `kec` (atelier),
  `iuq` (craft), `kbu` (forgemagie),
  `kcr/kda` (annonce HDV) sont couvertes
  par des tests **synthétiques**, mais ne
  sont pas validées avec les opcodes du jeu
  actuel. Ne pas leur attribuer le statut
  `observed` sans preuves.

### Proposition d'architecture transversale (future, pas encore implémentée)

```text
Protocole actuel / ancien / futur (opcodes et champs versionnés)
    |
    v
Décodeurs d'enveloppe par message et famille
    |
    v
Parseur commun d'INSTANCE physique d'objet
  ItemId, UID?, quantité?, effets?, complétude / fiabilité
    |
    +--> Snapshots inventaire personnel (personnage / fenêtre / connexion)
    +--> Snapshots banque (identité compte+serveur si vérifiée)
    +--> Places/slots d'atelier et opérations de craft/forgemagie
    +--> Corrélation concassage (UID -> ItemId, aucune régression)
    +--> Annonces HDV qui contiennent réellement les données d'instance
```

En parallèle, une **branche distincte** pour les
`jzs/jzn` (prix, offres, lots, références de marché)
doit rester indépendante : toutes les offres HDV
ne portent pas un UID réel ni des effets d'objet.
Un `MarketListingUid` ne doit **jamais** être
stocké comme `ItemUid`.

Distinctions indispensables :

1. **Instance vs type** : `ItemId` = type/catégorie
   de bien, `ItemUid` = identité de l'exemplaire
   ou de la pile. Deux équipements d'un même `ItemId`
   peuvent avoir des jets et UID distincts.
2. **Données complètes vs delta vs référence** :
   ne pas inventer une quantité 1 ou des effets vides
   si le message ne donne qu'un UID ou un prix.
   Les effets non observés (`null`) diffèrent
   des effets observés vides (`[]`).
3. **Lieu vs contenu** : même parseur d'instance,
   mais les mouvements, slots, stocks et offres
   relèvent d'observateurs métier différents.
   Une annonce publique n'est pas un stock du personnage.
4. **Ateliers** : différents types de métiers
   peuvent partager le modèle d'objet tout en
   ayant des événements, identifiants de
   recette, positions et résultats différents.
   Ne pas considérer le `kec` historique
   comme un événement du client courant.
5. **Craft et coûts** : `CraftCostService`
   calcule actuellement les coûts d'achat à partir
   des recettes et prix de marché ; tenir compte
   plus tard du stock personnel et, de façon
   **optionnelle**, du stock en banque. Ne jamais
   soustraire un inventaire inconnu comme s'il
   était vide.
6. **Comptes/sessions** : aucun inventaire
   inter-fenêtres par simple nom. Les données
   restent en RAM ; elles sont effacées ou rendues
   inconnues à l'expiration du bail, à la
   déconnexion ou en cas d'attribution ambiguë.
   Aucun affichage des objets sur la fiche personnage.

### Travail minimal avant développement

- Contrôle humain sur le nombre de lignes
  d'effets de l'équipement testé ; confronter
  au nombre de `f7:len` **sans déduire les
  valeurs**.
- Ajouter au probe un mode **opt-in borné
  d'exploration structurale intérieure de f7**
  pour **une ligne de delta `iqx` / `iqz`** ;
  0 valeur scalaire, 0 texte, 0 UID, 0
  payload brut enregistré ; tests de
  confidentialité et de limites.
- Comparer ensuite localement 2 équipements
  de jets différents, et une ressource
  sans effets. Ne modifier le mapping versionné
  qu'après validation.
- Tester les adaptateurs pour stocks et
  inventaires, ateliers, HDV en modules séparés,
  avec `kch/kag` inchangés ; protéger le
  routing des prix et l'absence de persistance
  du profil.

**Statut : documenté, aucune modification de
production ou du probe ; compilation/tests
Windows non exécutés dans ce lot.**

## P3d.5.10 — Contrôle visuel de l'équipement aux deux f7 (24/10/2026)

L'opérateur a communiqué la capture d'écran de l'équipement utilisé
dans le test P3d.5.9 : **Kwakanneau de Flammes**, anneau **niveau 42**.
La section « EFFETS » affichait :
- **+99 Vitalité** ;
- **+20 Intelligence** ;
- **0 % Résistance Feu**, en grisé.

La capture réseau P3d.5.9 de ses deux transferts montrait
`iqx ... ??.1.7:lenx2` et `iqz ... ??.1.7:lenx2`
(les chemins complets dépendent des enveloppes `iqx.f2.f1`
et `iqz.f1.f1`).

**Corrélation expérimentale : deux effets affichés non nuls,
deux occurrences f7**, contre une troisième ligne affichée
à zéro. Hypothèse de travail : `item.f7[]` serait la collection
des effets effectivement portés sur l'instance ; la ligne à
0 % pourrait n'être qu'une présentation de l'interface, sans
entrée réseau. **Ce n'est pas encore un décodage confirmé** :
les deux `f7` pourraient être d'autres informations, et aucune
valeur numérique, ID d'effet, ni signe n'a été observé à ce
stade. Le nombre de champs `f7` sur un seul équipement ne
suffit pas à prouver un mapping sémantique.

### Test décisif conseillé

Comparer anonymement un **second équipement**
affichant **trois caractéristiques non nulles** à sa liste
`item.f7[]`, ou un objet sans caractéristique.
La même corrélation de cardinalité sur un autre objet
indépendant renforcerait le modèle. Pour aller au-delà,
ajouter dans `BestCrush.NetworkProbe` un mode de
diagnostic spécifique **borné**, explorant uniquement
les schémas internes de `f7[]` sur un delta d'objet
et **aucune valeur** brute, nom, UID, effet ou prix ;
inclure tests de confidentialité, bornes taille/profondeur,
et cas erronés. Puis comparer les valeurs et effets
localement avec des références saisies par l'opérateur,
sans afficher ni exporter ces valeurs.

Ne pas encore mapper `item.f7` vers l'ancien
`DecodeStats(item,3)`, ni persister les caractéristiques.
Les ateliers de craft/forgemagie et les offres HDV restent
des consommateurs futurs de l'instance normalisée si
leur message comporte réellement un objet complet,
et non une simple référence/annonce/offre.

## P3d.5.11 — Préparation du diagnostic d'effets f7 (24/10/2026)

**Nouvel outil de recherche non activé en production :**

`BestCrush.Network/Protocol/ItemEffectsWireShapeStudy.cs`
propose `Describe(opcode, body)` sur **trois** messages
S→C courants : `iqx`, `iqz` et `kch`.
Le nouveau commutateur `--item-effects-shape`
est branché dans `BestCrush.NetworkProbe` et le
`CharacterCaptureFlowTracker` avec option explicite,
et affiche par ligne les métadonnées habituelles puis
`ITEM_EFFECTS=[effects=N wire-shapes=[...]]`.

Chemins examinés **sans exporter les valeurs** :

- `iqx.root.f2.f1.f7[]`
- `iqz.root.f1.f1.f7[]`
- `kch.root.f3.f1.f7[]`

Les enveloppes ne sont prises en compte que si
l'objet interne possède les champs numériques positifs
`f3,f4,f5` ; leur **valeur n'est jamais affichée**.
Le résumé n'imprime que des **numéros et types de champs**
(`v`, `len`, `32`, `64`) ainsi que
la cardinalité/forme des éventuels enregistrements f7.

Limites : message maximal **4 Kio**, entrée f7
maximale **1 Kio**, maximum **64 entrées f7**
par instance, **4 formes distinctes** affichées,
**24 lignes** affichées par session. Un
résultat `effects=too-large` ou `over-limit`
**n'est pas** une estimation d'effets : diagnostic borné,
ne jamais interpréter une sortie absente comme un
objet sans effets. Aucun dump brut, sauvegarde ou
modification de SQLite.

**Tests C# synthétiques ajoutés** :
`Tests.BestCrush/Network/ItemEffectsWireShapeStudyTest.cs`.
Ils vérifient la forme dans les trois enveloppes,
le cas zéro, les limites, les messages malformés,
le masquage des noms, UID, ID et valeurs privées,
le filtre S→C et l'absence de diagnostic par défaut.

### Checkpoint Windows obligatoire

Se positionner sur `feature/modular-workspace`
**sans réinitialiser ses modifications locales**.
Examiner `git status --short --branch` ; si
l'arbre est propre, récupérer les commits par
`git fetch origin feature/modular-workspace` puis
`git merge --ff-only FETCH_HEAD`.

```powershell
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Aucun de ces tests Windows n'a été exécuté par
l'assistant ; ne pas déclarer de réussite avant
constat sur l'ordinateur de l'opérateur.**
Si une erreur de compilation apparaît, corriger
uniquement les nouvelles modifications de P3d.5.11
avant de reprendre les captures.

### Expérience suivante (un seul client)

Après build et tests réellement réussis :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --item-effects-shape
```

1. Fermer toutes les fenêtres Dofus et BestCrush ;
   lancer le probe, puis **un** client Dofus.
2. Ouvrir la banque. Choisir le **Kwakanneau de Flammes**
   déjà testé : visible +99 Vitalité, +20 Intelligence,
   0 % Résistance Feu grisée.
3. Transférer une fois banque→inventaire, attendre 5 s,
   puis transférer inventaire→banque et noter les secondes.
4. Refaire avec un deuxième équipement connu,
   idéalement **3 effets non nuls**, sinon un objet
   connu sans caractéristique.
5. Transmettre seulement les lignes
   `ITEM_EFFECTS=[...]` et les heures des actions ;
   ne pas partager de PCAP, payload brut ou UID.
   Le mode n'affiche pas les valeurs des effets.

**Critères d'analyse :**

- Si 2 occurrences `f7` de l'anneau apparaissent
  sous une structure homogène et 3 pour l'autre
  équipement, l'hypothèse `f7 = effets d'instance`
  est renforcée.
- Une forme protobuf interne répétée dans `f7`
  peut suggérer les **champs** à comparer localement
  aux IDs/valeurs d'effets, mais ne les prouve pas.
- Si l'outil renvoie `unrecognized` ou `null`,
  pas de conclusion ; il faudra inspecter seulement
  un autre schéma borné avec fixtures.
- Une fois les champs confirmés par références
  saisies **localement**, construire le normaliseur
  et les décodeurs de snapshots/deltas ; ateliers
  et HDV restent des consommateurs séparés.

### Exigence d'architecture rappelée

Le parseur commun d'objets doit fonctionner pour
inventaire, coffre de banque, artisanat, ateliers de
forgemagie, concassage et les **messages HDV qui
portent réellement les instances et leurs jets**.

Ne pas confondre l'**UID d'une instance** et
l'**UID d'une annonce HDV** ; `jzs` prix/
lots doit continuer d'utiliser son moteur de
prix existant. Ne pas modifier les routes
historiques tant que la correspondance du
client actuel n'est pas contrôlée.

**Aucune implémentation de stock ou d'effets
activée en production dans P3d.5.11.**

## P3d.5.12 — Structure f7 et protocole de comparaison des valeurs (24/10/2026)

### Capture réelle — Kwakanneau de Flammes

Après validation Windows des deux projets réseau et de **1248 tests
réussis / 0 échec** (état **antérieur** aux ajouts de comparaison
décrits ci-dessous), un probe
`--item-effects-shape` a produit :

```text
[21:30:25.216 UTC] Session #1 S→C iqx 28 octets ITEM_EFFECTS=[effects=2 wire-shapes=[root=[10:v 5:v]x2]]
[21:30:36.321 UTC] Session #1 S→C iqz 27 octets ITEM_EFFECTS=[effects=2 wire-shapes=[root=[10:v 5:v]x2]]
```

Le même anneau affichait **99 Vitalité**, **20 Intelligence**
et une ligne de **0 % Résistance Feu** grisée.

**Confirmé structurellement :**
deux sous-messages `item.f7[]` par transfert ;
chacun comporte les champs protobuf
`f5:varint` et `f10:varint` ;
les formes sont identiques en `iqx` (banque→inventaire)
et `iqz` (inventaire→banque).

**Hypothèse de mapping, non encore vérifiée par la valeur :**
`f7[].f10` pourrait porter la **valeur de l'effet**
(comme le champ `f10` de l'ancien `DecodeStats`),
et `f7[].f5` son **identifiant d'effet**. Le format
historique canonique utilisait `f1=effectId`,
`f10=value` ; si confirmé, le remapping serait
`f5→canonical.f1`, `f10→canonical.f10`.
Le simple nombre de champs/leur forme ne prouve PAS
les valeurs 99 et 20 ni la sémantique de f5.

### Nouveau diagnostic anonyme à référence locale

Afin d'éviter un dump privé et plusieurs captures
inutiles, le mode exclusif
`--item-effects-compare` a été ajouté sur
`feature/modular-workspace`.

- Il demande localement **1 à 16 entiers séparés
  par des virgules** avant d'ouvrir le probe.
- Ces nombres sont uniquement conservés en RAM
  le temps de la session du processus.
- Sur `iqx/iqz/kch` S→C ayant une instance candidate,
  il compare sans ordre (multiensemble, y compris
  doublons) les champs `f5` et `f10` des
  `f7[]` avec les références saisies.
- Il affiche seulement `match=[f5:yes/no/... f10:yes/no/...]`,
  le nombre d'effets et les **types de champs** ;
  il n'affiche ni références, ni valeurs du paquet,
  ni UID ni ItemId. `incomplete` et `unrecognized`
  sont explicitement distincts de `no`.
- Nécessite l'option explicite ; le mode normal reste
  inchangé. Bornes d'origine préservées
  (4 Kio message, 1 Kio par effet, 64 effets,
  24 rapports/session), isolation par flux.

Des tests C# **synthétiques** ont été ajoutés pour
le matching sur `f10`, l'ordre, les doublons,
les entiers signés, les champs absents et les
invariants de confidentialité / opt-in. Ces nouveaux
tests et la compilation **restent à valider sur
Windows** ; ne pas présenter les 1248 tests
précédents comme couvrant le nouveau code.

### Test suivant, après checkpoint Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Si ces vérifications passent :
fermer Dofus, démarrer

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --item-effects-compare
```

Quand le probe demande des références, taper **`99,20`**,
puis connecter **un seul** client, ouvrir la banque
et transférer le **Kwakanneau de Flammes** dans
chaque sens, avec au moins cinq secondes entre
les actions. Attendre `iqx` et `iqz`, puis
`Ctrl+C`. Partager uniquement les
`ITEM_EFFECTS=[...]`, pas les données réseau.

**Critère positif prioritaire :**
`effects=2 ... match=[f5:no f10:yes]`
sur les deux directions, ce qui corroborerait
la correspondance `f10=valeur`, mais **ne
validerait pas à lui seul** `f5=effectId`.
Tester ensuite indépendamment un second
équipement doté d'autres valeurs d'effets.
Les champs inconnus ne sont pas mappés
en production avant la validation croisée.

Cette étape ne touche **ni** au décodage
métier de `kch/kag` ou des prix HDV
**ni** au stock SQLite, aux ateliers, à
la molette, à F7/F8, ou au serveur choisi.

## P3d.5.13 — Correspondance f5 observée et validation des pourcentages

### Capture réelle du 24/10/2026, 21:39 UTC

L'utilisateur a confirmé l'équipement **Kwakanneau de Flammes,
niveau 42**, avec **+99 Vitalité**, **+20 Intelligence**,
et une troisième ligne **0 % Résistance Feu**, affichée en gris.
Deux séquences `--item-effects-compare` :

1. Références saisies `99,20,0` : `iqx` et `iqz`
   indiquent `effects=2`,
   `match=[f5:incomplete f10:incomplete]`.
   Le probe exige une comparaison bijective de **tous**
   les effets ; trois références ne peuvent être
   comparées à deux enregistrements. **Ceci ne démontre
   pas qu'il existe un effet à zéro.**
2. Références saisies `99,20` :
   - 21:39:20.985 : `iqz`, 17 octets,
     `effects=0 ... incomplete/incomplete` ;
     message distinct, sémantique non prouvée,
     **ne pas l'utiliser comme photographie d'inventaire**.
   - 21:39:23.008 : `iqx`, 28 octets,
     `effects=2 wire-shapes=[root=[10:v 5:v]x2]
     match=[f5:yes f10:no]`.
   - 21:39:25.032 : `iqz`, 28 octets,
     `effects=2 wire-shapes=[root=[10:v 5:v]x2]
     match=[f5:yes f10:no]`.

**Conclusion solide :** pour cet exemplaire, les **deux
valeurs non nulles affichées** forment exactement le
multiensemble des valeurs `item.f7[].f5`, dans les
deux directions du transfert. C'est la correction
**expérimentale** de l'hypothèse P3d.5.12 :
**`f5` est le candidat à la valeur des caractéristiques**,
et non `f10`. Ne pas renommer `f10`
« identifiant d'effet » avant preuve spécifique ;
il peut être un ID d'effet ou un autre code associé.

La ligne de résistance à **0 %** n'a pas d'entrée
supplémentaire `f7` dans ce test, mais on ne peut
pas encore affirmer que tous les effets nuls sont
toujours absents du protocole. Les nombres de prix
moyens présentés dans l'infobulle ne sont pas des
caractéristiques d'instance et n'entrent pas dans
la comparaison.

### Contrôle prioritaire : statistique en pourcentage non nulle

Utiliser une **autre pièce d'équipement** dans la banque,
avec au moins une caractéristique en **% strictement
différente de zéro**. Relever les valeurs visibles de
TOUTES les caractéristiques non nulles ; inclure
les valeurs en % sous forme de **nombre entier sans
le symbole %** (par exemple `7` pour `+7 %`),
sans `0` pour une ligne grisée à zéro.

Si possible, utiliser une pièce avec :
une caractéristique absolue (p. ex. +99 Vitalité)
et une caractéristique pourcentage non nulle (p. ex.
+7 % Résistance), afin que le comparateur
ne confonde pas la famille des effets. Exemple
**illustratif**, pas une description d'objet réellement
possédé : `99,7`.

Après la synchronisation Git et les validations Windows,
démarrer le mode **déjà existant** :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --item-effects-compare
```

Saisir les valeurs observées ; ouvrir un client Dofus
après le démarrage du probe, ouvrir la banque et
transférer une fois vers l'inventaire, puis une fois
vers la banque en notant les instants. Partager
uniquement le résumé anonymisé `ITEM_EFFECTS`.

Interprétation :
- `effects=N` égal au nombre d'effets non nuls,
  et `f5:yes` dans les deux sens : renforce le
  mapping `f5=valeur`, y compris pour le type
  pourcentage et son unité affichée.
- `f5:no` avec `effects=N` : une ou
  plusieurs valeurs de pourcentage peuvent être
  codées dans une autre unité (p. ex. facteur
  décimal), ou d'autres valeurs différer.
  **Ne pas changer les références au hasard** :
  relever l'objet et analyser par étude locale
  ciblée avant de conclure.
- `incomplete` : dénombrement incompatible ;
  corriger la liste des caractéristiques non
  nulles et vérifier si l'objet a des effets
  spéciaux non numériques.

### Régression/tests

Deux tests synthétiques ajoutés à
`ItemEffectsWireShapeStudyTest` :
- reproduire `f5:yes f10:no` en `iqx`
  et `iqz` avec références non nulles,
  et `incomplete` avec une référence
  de zéro supplémentaire ;
- vérifier que le comparateur peut rechercher
  une valeur en pourcentage **sans effectuer
  lui-même de conversion d'unité**, et que
  les références incorrectes ne correspondent pas.

Les valeurs artificielles des tests **ne prouvent
rien de plus** sur le protocole réel. Le
décodeur canonique métier, la base SQLite,
les observations d'atelier, le calcul de craft
et les HDV sont inchangés. Aucune exécution
Windows du nouveau code n'est déclarée validée.

## P3d.5.14 — Résistances en pourcentage non nulles : succès contrôlé

### Observation réelle, 24/10/2026 à 21:45 UTC

L'utilisateur a testé un **deuxième équipement indépendant**
portant notamment **+5 % de résistance Feu**.
Références saisies localement dans
`--item-effects-compare` : `24,26,5,3`.

```text
[21:45:50.660 UTC] Session #1 S→C OPENED
[21:45:53.716 UTC] Session #1 S→C iqx 42 octets ITEM_EFFECTS=[effects=4 wire-shapes=[root=[10:v 5:v]x4] match=[f5:yes f10:no]]
[21:45:54.738 UTC] Session #1 S→C iqz 42 octets ITEM_EFFECTS=[effects=4 wire-shapes=[root=[10:v 5:v]x4] match=[f5:yes f10:no]]
```

Cette capture reproduit la correspondance exacte des valeurs
d'effets via `item.f7[].f5` dans **les deux sens**,
sur **quatre** occurrences `f7`, avec au moins
un effet de résistance en **pourcentage non nul**.

Comparaison des deux équipements réels étudiés :

| Équipement | Valeurs non nulles visibles | `f7[]` | Match f5 (iqx/iqz) | Match f10 |
|---|---|---:|---|---|
| Kwakanneau de Flammes | 99 Vitalité, 20 Intelligence | 2 | yes / yes | no / no |
| Deuxième équipement (dont 5 % rés. Feu) | 24, 26, 5, 3 | 4 | yes / yes | no / no |

**Conclusion :** hypothèse `f7[].f5 = valeur numérique
d'effet` **fortement validée sur deux équipements,
deux tailles de liste et deux sens de transfert**.
Le comparateur est un **multiensemble** : il
n'associe pas individuellement `5` à l'effet
« Résistance Feu » et ne prouve pas les unités de
tous les effets. Dans l'ensemble de référence, la
valeur `5` correspond à l'effet de résistance Feu
rapporté par l'utilisateur ; le comportement est
compatible avec un encodage entier directement en
points de pourcentage.

Le champ `f7[].f10` existe comme varint dans
toutes les lignes analysées mais **ne correspond pas**
au multiensemble des valeurs. Il constitue un
**candidat ID d'effet**, pas une preuve de
sémantique. La présence ponctuelle d'un delta
`iqz` sans effets dans l'expérience antérieure
reste inexpliquée : ne pas fusionner aveuglément
l'ensemble des messages `iqz` avec les snapshots
de stock.

### Travail suivant (une seule question expérimentale)

Confirmer `f10 = type/identifiant d'effet`.
Piste non intrusive : comparer deux instances du
**même type d'équipement**, avec les **mêmes
caractéristiques présentes mais des jets différents**,
et vérifier localement que le multiensemble des
`f10` est identique tandis que les `f5`
diffèrent. Cette égalité ne suffira pas à elle
seule à assigner les noms des effets ; elle
permettra de distinguer « identifiant stable par
type d'effet » de « valeur de l'effet ». Autre
piste : comparer de vrais IDs d'effet publics,
issus du catalogue versionné du jeu, aux
`f10` en RAM via un mode de référence local.
Ne pas afficher les `f10` bruts ni les
empreintes d'UID de personnage.

Maintenir séparation entre modèle d'instance commun
(UID, ItemId, quantité, liste d'effets),
adaptateurs d'enveloppe propres à chaque opcode
et contexte métier (inventaire, banque, atelier,
forgemagie, concassage et HDV lorsque le message
porte réellement des données d'instance).

**Aucun code de production, mapping d'effets,
SQLite ou UI modifié lors de ce checkpoint de capture.**

## P3d.5.15 — Contrôle f10 commun entre deux équipements

### Référence visuelle — Kwakture de Flammes (24/10/2026)

Infobulle communiquée par l'utilisateur :

- **Kwakture de Flammes**, ceinture niveau **41**, panoplie Kwak de Flammes.
- **+24 Vitalité**
- **+26 Intelligence**
- **+5 % Résistance Feu**
- **+3 Fuite**

La liste confirme précisément les références `24,26,5,3` de la
capture P3d.5.14 (`f7[].f5:yes` pour `iqx/iqz`). Le prix
moyen, le poids, le niveau et les informations de panoplie ne sont
pas des effets d'instance et ne font pas partie du jeu de références.

### Hypothèse testable sans décoder les codes privés

La **Kwakture** et le **Kwakanneau de Flammes** possèdent
en commun **Vitalité** et **Intelligence** avec valeurs différentes :

| Type de caractéristique | Kwakanneau A | Kwakture B |
|---|---:|---:|
| Vitalité | 99 | 24 |
| Intelligence | 20 | 26 |
| Résistance Feu % | 0 (grisée, exclue) | 5 |
| Fuite | absent | 3 |

L'ancien `DecodeStats` historique utilisait
`effect.f1` comme type d'effet et `effect.f10` comme valeur,
mais la **capture actuelle démontre l'inverse concernant la valeur :
`effect.f5`**, pas `f10`. Le numéro `f10` reste **inconnu**.
Hypothèse : `f10` identifie le type d'effet, invariant
sur les deux équipements pour Vitalité et Intelligence.

Un **mode opt-in dédié** a été ajouté :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --item-effects-type-study
```

Il demande deux lignes locales :

```text
Objet A : 99,20
Objet B : 24,26,5,3
```

**Convention stricte** : A doit contenir toutes ses caractéristiques
non nulles ; B doit commencer par les caractéristiques communes
**dans le même ordre**, puis ajouter les siennes. A et B doivent
avoir des cardinalités différentes, toutes les valeurs au sein
de chaque objet doivent être distinctes pour l'attribution fiable.

Le diagnostic lit seulement `iqx/iqz/kch` **S→C**, en RAM,
sur **un même flux TCP/session**. Il associe les entrées `f7[]`
par leur valeur `f5`, puis vérifie la stabilité du `f10`
pour chaque paire `99↔24` (Vitalité) et `20↔26`
(Intelligence). Il ne journalise **jamais** les codes f10,
les valeurs brutes réseau, les identifiants d'instance,
le compte, les endpoints ou les payloads.

Exemples de sorties **attendues si l'hypothèse est juste**
(non encore observées sur le jeu réel) :

```text
ITEM_EFFECTS=[item=A other=pending]
ITEM_EFFECTS=[item=B common-f10=[1:yes 2:yes] distinct-f10=yes]
```

`common-f10=[1:yes 2:yes]` indique que
les codes des deux effets communs coïncident entre
équipements malgré leurs jets différents.
`distinct-f10=yes` exige que les codes soient
distincts entre effets du même équipement, un garde-fou
contre une constante systématique. Si un booléen est
`no`, ne pas normaliser `f10` comme identifiant d'effet
avant une nouvelle comparaison. Même si tous sont `yes`,
il s'agit d'une **validation de stabilité**, pas encore
d'une correspondance formelle vers les IDs publics.

### Checkpoint Windows préalable

Après validation de `git status --short --branch`,
si les changements locaux ne sont pas recouverts,
faire `git fetch origin feature/modular-workspace`
puis `git merge --ff-only FETCH_HEAD`.
Aucune restauration des modifications locales.

```powershell
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Les nouveaux tests couvrent correspondance stable/non stable,
messages malformés, codes d'effets égaux à tort, isolation de
session et absence d'affichage des identifiants. **Ils n'ont pas
encore été exécutés sur Windows** ; attendre la sortie utilisateur.

### Manipulation manuelle (si checkpoint validé)

La déconnexion complète de Dofus **n'est pas obligatoire**.
Lancer le probe avant le premier transfert, donner les références
ci-dessus, **garder le même personnage sur le même flux** et
transférer **une fois chacun** des deux équipements.
L'ordre des objets et le sens des mouvements ne changent pas
le calcul. Si Dofus recrée un flux TCP entre les deux,
recommencer les deux transferts dans une même session.
`Ctrl+C` pour terminer puis partager les seuls résumés
`ITEM_EFFECTS`.

Aucune modification de `SemanticDecoders`, des tables SQLite,
des stocks, des prix, des filtres multicomptes, des ateliers ou de
l'affichage des fiches personnages.

## P3d.5.16 — Corrélation f10 validée pour Vitalité et Intelligence

### Captures confirmées le 24/10/2026 à 21:57 UTC

Après le checkpoint Windows rapporté par l'opérateur
**1260 tests réussis, 0 échec, 9,1 s**, il a exécuté
`--item-effects-type-study` avec les deux références
locales :

- A — **Kwakanneau de Flammes** : `99,20`, dans
  l'ordre Vitalité, Intelligence ;
- B — **Kwakture de Flammes** : `24,26,5,3`,
  dans l'ordre Vitalité, Intelligence, 5 % Résistance
  Feu, Fuite.

Résultat issu de la capture réelle :

```text
[21:56:59.313 UTC] Session #1 S→C OPENED
[21:57:07.422 UTC] Session #1 S→C iqx 27 octets ITEM_EFFECTS=[item=A other=pending]
[21:57:10.447 UTC] Session #1 S→C iqx 42 octets ITEM_EFFECTS=[item=B common-f10=[1:yes 2:yes] distinct-f10=yes]
```

Il s'agit **deux fois de `iqx`**, dans **la même
session TCP #1**, sans relance de Dofus entre les
observations.

**Ce qui est expérimentalement établi :**

1. La paire `f7[].f10` associée à la Vitalité
   est identique entre A (+99) et B (+24).
2. Celle associée à l'Intelligence est identique
   entre A (+20) et B (+26).
3. Les codes `f10` sont distincts pour les
   différents effets d'un même objet dans les
   deux captures (`distinct-f10=yes`).
4. L'identification des effets a été faite
   par valeur `f7[].f5` dans des ensembles
   comportant chacun des valeurs distinctes
   ; les noms proviennent des infobulles
   observées en jeu, pas du paquet.

**Hypothèse fortement corroborée :**
`f7[].f10` identifie le **type d'effet**,
et `f7[].f5` porte sa **valeur courante**.
Cela ne prouve **pas encore** que le nombre `f10`
est exactement le `EffectId` (ou l'ID de
caractéristique) de DofusDb. Aucune valeur de
`f10` n'est diffusée dans la sortie du probe.

### Optimisation de la suite

Le code `BestCrush.Network/Protocol/SemanticDecoders.cs`
possède déjà un modèle `ItemDetailObservation`
avec `ItemStatObservation(ulong EffectId,long Value)`
et un parseur `TryDecodeItemObject` utilisé
par l'ancienne chaîne d'inventaire, la
forgemagie, les crafts et la corrélation
du concassage. L'ancien schéma **canonique**
emploie `effect.f1 = EffectId` et
`effect.f10 = Value`.

Le nouveau schéma expérimental est **différent** :
`wire.item.f7[].f10` est candidat à l'identifiant
de type et `wire.item.f7[].f5` est la valeur.
Le normaliseur de champs du dépôt peut en théorie
convertir `wire.f7 → canonical.f3` et,
à l'intérieur, `wire.f10 → canonical.f1`,
`wire.f5 → canonical.f10`. **Ne pas
appliquer ce remapping à la production tant
que f10 n'a pas été confronté aux IDs
DofusDb actuels.**

Priorité suggérée : comparer, sur un nombre
réduit d'effets connus (Vitalité, Intelligence,
Résistance Feu %, Fuite), les valeurs `f10`
uniquement **dans la machine locale**
avec le catalogue DofusDb en version compatible.
En cas de discordance, enregistrer un mapping
distinct `wire-effect-code ↔ DofusDb effect id`
en protocole versionné sans supposer une identité
numérique.

Ensuite, mutualiser un décodeur d'instance
portant `UID, ItemId, quantité, effets`
sur les enveloppes `kch/iqx/iqz/irl/irp`,
en maintenant **inconnue** (`null`)
la quantité ou la liste des effets si
une observation ne les transmet pas.
Les adaptateurs de slots d'atelier, craft,
concassage et annonces HDV doivent réutiliser
ce type, sans assimiler `MarketListingUid`
à un UID d'instance et sans réécrire `jzs`
(prix) ni les routes legacy avant leur
caractérisation.

**Statut :** observation réelle documentée ;
aucune modification de production, de SQLite,
d'interface ou de schéma de stock effectuée
à ce checkpoint.

## P3d.5.17 — Comparaison directe avec le catalogue DofusDb

**Objectif :** vérifier avec le moins de captures possible si
`item.f7[].f10`, stable entre Kwakanneau et Kwakture,
est un identifiant **numériquement identique** à l'un des
champs de référence DofusDb :
`DofusDbItemEffect.EffectId`,
`.Characteristic` ou `.ElementId`.
La preuve de stabilité **ne** suffisait **pas** à
prouver l'égalité numérique avec ces références.

### Implémentation limitée au probe

Nouveau commutateur opt-in :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --item-effects-dofusdb-study
```

Avant d'activer la capture, le processus récupère à
travers le client DofusDb **existant dans le dépôt**
les fiches publiques :

| Alias | Équipement | ID public | Références de valeurs d'instance |
|---|---|---|---|
| A | Kwakanneau de Flammes | 2418 | 99,20 |
| B | Kwakture de Flammes | 2427 | 24,26,5,3 |

Sources d'identification publique des objets :
https://dafous.app/objet/2418/kwakanneau-de-flammes
et https://dafous.app/objet/2427/kwakture-de-flammes ;
ces pages **ne prouvent pas** les identifiants internes
des effets ni la compatibilité avec le client en cours.

Le programme ne demande ni nom de personnage, ni
UID, ni données privées à DofusDb : uniquement ces
deux identifiants d'articles de catalogue.
`DofusDbClient.Production().Items().GetAsync(...)`
fait les requêtes, avec délai borné à dix secondes
et validation du nom français exact des deux objets.
En cas d'échec, de réponse absente, de nom différent
ou de données incohérentes, l'outil affiche
`DOFUSDB=[catalogue-indisponible-ou-incompatible]`
et **n'exécute pas de capture**. Les références
de catalogue restent en **RAM**, ne sont ni
journalisées ni persistées.

Lorsque DofusDb répond :

```text
DOFUSDB=[catalogue-charge]
```

Le probe reconnaît `iqx`, `iqz`, `kch`
par le **multiensemble des valeurs `f5` déjà
validées** et compare localement leurs `f10`
avec les trois listes de codes publics.
Il affiche uniquement, par alias A ou B :

```text
ITEM_EFFECTS=[item=A f10-vs-dofusdb-effectId=all|partial|none|unknown characteristicId=... elementId=... value-ranges=... catalog-effects=N wire-effects=N catalog-version=unverified]
```

L'indicateur `value-ranges` n'est qu'un
contrôle auxiliaire pour les correspondances
d'IDs d'effets, car le catalogue pourrait
représenter certaines valeurs dans une autre
unité et **n'est pas verrouillé sur la version
du jeu**. Avec un matching incomplet des IDs,
`value-ranges=incomplete-id-match` empêche
une conclusion trompeuse. Les effets publics
absents ou incomplets sont traités comme
`unknown`, pas comme une preuve négative.

**Protection de confidentialité :** aucun
`EffectId`, `Characteristic`, `ElementId`,
`f5`, `f10`, itemUID, payload, compte ou
endpoint n'est affiché. Seuls les statuts
d'égalité et les cardinalités sont produits.
Les plafonds et filtres du probe existant
restent actifs ; option exclusive, un seul
client Dofus.

### Checkpoint Windows indispensable

Dans le dépôt `feature/modular-workspace`,
préserver les modifications locales de
`DofusSharp.sln`, `.vscode/`, backups et
fichiers baseline.

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git diff --name-only HEAD FETCH_HEAD
```

Si aucun des chemins à modifier par le
distant ne chevauche des changements locaux :

```powershell
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Ensuite seulement, lancement du nouveau mode
`--item-effects-dofusdb-study` **sans devoir
redémarrer Dofus**. Si Dofus est déjà ouvert,
démarrer le probe avant les transferts ;
transférer le Kwakanneau puis la Kwakture
une fois chacun (sens indifférent), puis
`Ctrl+C`. Partager la sortie de ce mode.
Le test compare chaque objet indépendamment :
si seul A ou B apparaît, seule la comparaison
correspondante est exploitable.

### Garde-fous métier

- `f7[].f5` = valeur : observations réelles
  P3d.5.13–14 cohérentes.
- `f7[].f10` = code stable de type :
  observation réelle P3d.5.16 cohérente.
- **Égalité numérique** avec l'un des IDs
  DofusDb : **nouvelle hypothèse à tester**.
- Catalogue DofusDb potentiellement
  différent du build actuel de Dofus Unity :
  un `none` ne suffit **pas** à invalider
  la nature « identifiant stable de type » ;
  il peut imposer un mapping de codes distinct.

Aucune modification de `SemanticDecoders`
en production, du profil protocolaire, des
stocks (RAM/SQLite), des fiches, des prix,
du traitement de craft/atelier/HDV, de F7/F8
ou de la molette. Les nouveaux tests
synthétiques ne constituent **pas** une
validation sur Windows ; seuls les résultats
fournis par l'opérateur peuvent la valider.

## P3d.5.18 — Effets de forgemagie hors catalogue (24/10/2026)

### Capture réelle : catalogue DofusDb vs deux instances

`--item-effects-dofusdb-study` a obtenu
`DOFUSDB=[catalogue-charge]` et **six** deltas
S→C dans **la même session TCP**, horodatés
**22:10 UTC** :

```text
22:10:44.775 iqz B f10-vs-dofusdb-effectId=all characteristicId=none elementId=none value-ranges=all catalog-effects=4 wire-effects=4
22:10:45.784 iqz A f10-vs-dofusdb-effectId=partial characteristicId=none elementId=none value-ranges=incomplete-id-match catalog-effects=2 wire-effects=2
22:10:46.786 iqx B f10-vs-dofusdb-effectId=all characteristicId=none elementId=none value-ranges=all catalog-effects=4 wire-effects=4
22:10:48.801 iqx A f10-vs-dofusdb-effectId=partial characteristicId=none elementId=none value-ranges=incomplete-id-match catalog-effects=2 wire-effects=2
22:10:49.806 iqz A f10-vs-dofusdb-effectId=partial characteristicId=none elementId=none value-ranges=incomplete-id-match catalog-effects=2 wire-effects=2
22:10:50.819 iqz B f10-vs-dofusdb-effectId=all characteristicId=none elementId=none value-ranges=all catalog-effects=4 wire-effects=4
```

La **Kwakture de Flammes (B)** valide numériquement,
pour ses **quatre effets réels**, `f7[].f10 ==
DofusDbItemEffect.EffectId` (sur ce catalogue).
L'identité numérique n'est pas observée avec
`Characteristic` ou `ElementId`.

**Le Kwakanneau de Flammes (A)** montre une
correspondance `EffectId` partielle,
de façon parfaitement répétée. Ce constat
n'invalide pas le schéma `f10=effectId` :
la référence catalogue de l'anneau décrit
les **jets naturels**, pas nécessairement
l'instance forgemagée.

Les fiches publiques contemporaines :
- https://dafous.app/objet/2418/kwakanneau-de-flammes
  donne **16–20 Intelligence**, **4–5 % Résistance Feu**,
  **pas de Vitalité naturelle**.
- https://dafous.app/objet/2427/kwakture-de-flammes
  donne **21–30 Vitalité**, **21–30 Intelligence**,
  **5 % Résistance Feu**, **2–3 Fuite**.

L'anneau observé dans la capture personnelle
présente **+99 Vitalité**, **+20 Intelligence**,
et **0 % Résistance Feu** grisée. Hypothèse
particulièrement plausible : la forgemagie a
**ajouté de la Vitalité** et **fait tomber la
résistance naturelle à zéro**. Ce diagnostic
reste une inférence : aucune séquence de
forgemagie de cet anneau n'a été interceptée.

La capture précédente P3d.5.16 a déjà confirmé que
le code f10 de la Vitalité est **stable entre A
et B**, or B valide ses EffectId sur DofusDb.
Le décalage A est donc **attendu pour une
instance possédant un effet hors catalogue
de base**, plutôt qu'une preuve d'un numéro
de code entièrement différent.

### Ajustement du diagnostic et tests

`ItemEffectDofusDbCatalogStudy` rapporte désormais
trois indicateurs agrégés (sans révéler aucun code) :

- `unlisted-on-item=N` : effets réellement reçus
  dont le `f10` ne figure pas parmi les
  `EffectId` naturels de cet objet ;
- `unlisted-known-on-other=N` : combien de ces
  effets sont connus comme types valides dans
  le catalogue **de l'autre objet** ;
- `catalog-codes-unobserved=N` : nombre de
  codes `EffectId` de la fiche théorique non
  présents dans le delta d'instance.
  Une absence peut correspondre à un effet
  tombé à zéro, mais ne le prouve pas.

Dans le cas de la forgemagie suspectée,
l'anneau devrait produire
`unlisted-on-item=1 unlisted-known-on-other=1
catalog-codes-unobserved=1`. Cette sortie
est une **prévision à vérifier**, pas une
capture réelle à ce jour. La ceinture devrait
produire `0/0/0`.

Des tests synthétiques ont été ajoutés
pour un anneau avec Vitalité ajoutée,
résistance disparue, et cas témoin où
l'effet hors catalogue est véritablement
inconnu. Pas de changement de la production
`BestCrush` ni de SQLite.

### Validation Windows et contrôle minimal

Exécuter le checkpoint habituel après
un `git status` prudent :
`git fetch origin feature/modular-workspace`
puis `git merge --ff-only FETCH_HEAD` seulement
si aucun chemin modifié localement n'est
recouvert. Compiler `BestCrush.NetworkProbe`,
puis `dotnet test .\\Tests.BestCrush\\Tests.BestCrush.csproj`.
Ne pas affirmer que les nouveaux tests sont
validés avant leurs résultats Windows.

Relancer le **même mode**
`--item-effects-dofusdb-study`.
Un seul transfert de l'anneau et, si utile,
de la ceinture suffit pour tester
ces indicateurs, sans redémarrer Dofus.
Ne pas activer de règle de décodage produit
tant que le cas de FM et l'identification
numérique des effets n'ont pas été
explicitement confirmés.

## P3d.5.19 — Confirmation utilisateur : Kwakture forgemagée

L'utilisateur confirme explicitement : **« La kwakture a été fm en effet »**.
Il s'agit d'une confirmation de **forgemagie de la Kwakture
de Flammes** (B), et non d'une preuve supplémentaire sur
l'historique du Kwakanneau (A). Le type exact de
modification, les valeurs avant forgemagie et
l'éventuel effet de la forgemagie sur chaque jet
ne sont **pas connus**.

**Correction de l'interprétation P3d.5.17–18 :**
Le résultat B `f10-vs-dofusdb-effectId=all`,
`value-ranges=all` et `catalog-effects=4 wire-effects=4`
n'a **jamais** signifié « non forgemagé ». Il démontre
uniquement que les quatre types d'effets observés sont
répertoriés comme effets naturels de ce type d'objet,
avec des valeurs comprises dans les bornes du catalogue
consulté à ce moment-là. Une forgemagie peut changer
les valeurs d'un effet sans en changer l'identifiant ni
nécessairement sortir des intervalles théoriques.

Le Kwakanneau observé (+99 Vitalité,
+20 Intelligence, 0 % Résistance Feu) **semble**
comporter une Vitalité ajoutée hors des caractéristiques
naturelles et une Résistance Feu absente du flux.
Cela reste une **inférence**, sans preuve directe
de son historique de forgemagie.

**Invariants d'implémentation :**

- `f7[].f5` : valeur **observée** de chaque effet
  d'instance, compatible avec les objets forgemagés.
- `f7[].f10` : code de type d'effet, correspondant
  aux `EffectId` du catalogue sur la Kwakture
  (4/4), et stable pour les deux types communs entre
  Kwakture et Kwakanneau.
- Les `DofusDbItem.Effects` et `From/To`
  décrivent le modèle **théorique** de l'objet,
  jamais le jet d'une instance possédée.
- `unlisted-on-item` et
  `catalog-codes-unobserved` sont des diagnostics
  de **différence au catalogue**, pas des indicateurs
  fiables « FM oui/non ». Une modification des valeurs
  sans apparition ni disparition d'effet peut laisser
  ces compteurs à zéro.

**Pas de nouveau test ou changement de code métier**
requis par cette seule confirmation. Poursuivre la
caractérisation du décodeur commun et garder les
caractéristiques **réelles** distinctes des
références théoriques du catalogue.

## P3d.6.0 — Ne pas reproduire les anciens tests ItemId/UID/HDV

### Preuve historique retrouvée — test de septembre 2026

L'utilisateur a rappelé qu'un autre agent avait déjà caractérisé
la continuité des identifiants d'objets pendant une mise en vente
et un rachat HDV. **Ne pas recommencer cette campagne de tests.**
Les observations antérieures les plus précises établissent :

- Sur un objet `ItemId=8533`, UID d'inventaire initial
  `841526881`, annonce HDV `MarketListingUid=9126175`,
  retrait manuel, puis UID d'inventaire `841687195`.
  Le **type ItemId** et les effets sont conservés,
  mais l'**UID d'instance est remplacé**, même sans vente.
- Un essai d'achat de son propre équipement a également
  maintenu les ItemId des objets testés et généré un
  nouvel UID à réception en inventaire. Les traces
  documentaient des ItemId `8533` et `8534`.
- Ces tests **ne** constituent **pas** une preuve
  de crédit des kamas après vente à un tiers ;
  ne pas généraliser au règlement financier d'une
  vente réellement exécutée.

**Niveaux d'identité à conserver distincts :**

1. `ItemId` = identifiant public du **type** d'objet
   et clé pour le catalogue DofusDb ; stable pour ce type.
2. `ItemUid` = identifiant de l'**incarnation d'instance
   côté client/serveur** ; utile pour les transitions et
   la corrélation, mais peut **changer après HDV**.
3. `MarketListingUid` = identifiant propre à l'annonce ;
   ne jamais l'utiliser comme `ItemUid`.
4. Identité **logique suivie par BestCrush** : ne pas
   l'inférer du seul ItemId (plusieurs copies peuvent
   coexister) ; pour relier deux incarnations, exiger
   des observations de transaction/transfert et une
   chaîne de provenance suffisamment vérifiée.

### Priorité P3d.6.1 rectifiée

**Ne pas tester à nouveau « l'ID survit-il au HDV ? »**
ou « l'UID d'instance est-il éternel ? ».

Étudier exclusivement l'**appartenance et la quantité
des stocks** :

- décoder, en lecture seule dans la sonde, les collections
  `irl.root.f2[]` (inventaire) et `irp.root.f2[]`
  (banque), déjà corroborées par les comptes
  `502→503` / `559→558` ;
- déterminer, sur ces deux enveloppes précises,
  comment `f3` (quantité candidate), `f4`
  (`ItemId` candidat) et `f5`
  (`ItemUid` candidat) correspondent au schéma
  de `kch`. Le fait que leurs **types** protobuf
  coïncident ne prouve pas encore leur **sémantique
  dans `irl/irp`** ;
- caractériser la variation lors des transferts
  `iqx/iqz`, de l'ajustement `irx` et de la
  suppression `iqm`, en se concentrant sur
  quantités, fusion/scission de piles et direction ;
- respecter le contexte multicomptes et distinguer
  un snapshot `null` non reçu d'un snapshot vide
  effectivement complet ; ne pas connecter un
  inventaire non vérifié au calcul de craft.

**Règle métier de l'utilisateur** : un objet **équipable**
peut voir ses caractéristiques réelles évoluer via la
forgemagie ; un **familier** peut voir ses caractéristiques
évoluer lorsqu'il est nourri. L'identification des types et
des stocks ne dépend pas des caractéristiques, mais toute
modification d'effets reçue doit rester séparée des
valeurs théoriques DofusDb et rattachée à la bonne instance.
Ne pas supposer l'immuabilité des valeurs d'effet en
fonction d'une reconnaissance d'ItemId.

## P3d.6.1 — Décodage candidat irl/irp en lecture seule

### Implémentation

Ajout de `BestCrush.Network/Protocol/InventoryBankSnapshotDecoder.cs` :
décodeur **pur** et borné des deux formes déjà observées
`irl.root.f2[].f1` et `irp.root.f2[].f1`.
Il renvoie les lignes candidates séparées en RAM :
`f3` (quantité **candidate**), `f4` (ItemId
**candidat**), `f5` (UID d'instance **candidat**),
ainsi que le **nombre** de champs `f7[]`
sans en décoder le contenu.

Pour `kch`, le sens de `f3/f4/f5` est validé ;
pour `irl/irp`, leur numérotation est identique
mais la correspondance **reste à vérifier par
références ou par opérations réseau ciblées**.

États du décodeur :

- `Malformed` : structure invalide, trame excessive,
  plafond dépassé (256 Kio, 4096 lignes, 8192 octets
  par ligne), ou Protobuf non valide ;
- `Incomplete` : métadonnée attendue manquante,
  ligne candidate incomplète, champs ambigus ou
  UID candidat répété ;
- `EmptyCandidate` : zéro ligne **avec** le champ de
  métadonnées constaté auparavant (`irl.root.f4`,
  `irp.root.f1`) ; **pas** une preuve suffisante
  d'inventaire véritablement vide ;
- `CompleteCandidate` : toutes les lignes ont une
  structure complète et des scalaires uniques
  compatibles avec le schéma **candidat**. Cela ne
  signifie **pas** que les quantités, l'identité du
  personnage et le conteneur sont prouvés sémantiquement.

`Decode` renvoie `null` pour tout autre opcode
(non-observation), distinction fondamentale avec
une liste vide. Aucun état n'est publié dans
`CurrentCharacterState`, `RecentItemDetailsCache`,
les calculs de craft, les dossiers de banque ou SQLite.

### Probe opt-in anonymisé

Nouveau mode exclusif :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --stock-snapshot-study
```

La sortie ne présente que les **comptages** de
lignes, de triplets f3/f4/f5 présents, de lignes
ayant au moins un f7 et d'UID candidats dupliqués
au sein d'un même snapshot.

Exemple **synthétique** non issu du jeu :

```text
S→C irl ... STOCK_SNAPSHOT=[container=inventory status=complete-candidate rows=2 fields345=2 incomplete-rows=0 effects-present=1 duplicate-f5=0]
S→C irp ... STOCK_SNAPSHOT=[container=bank status=complete-candidate rows=1 fields345=1 incomplete-rows=0 effects-present=0 duplicate-f5=0]
```

Aucun ItemId, UID, quantité, effet, nom de personnage
ou contenu protobuf n'est imprimé. Le traitement est
limité au sens S→C et à 12 rapports maximum par session
TCP. L'option est mutuellement exclusive avec
les autres modes de diagnostic. Ce mode fonctionne
avec le même contrôleur de sessions TCP ; ne pas
mélanger les captures de plusieurs personnages.

### Tests synthétiques

Ajout de `Tests.BestCrush/Network/InventoryBankSnapshotDecoderTest.cs` :
conteneurs séparés, interprétation structurale,
liste vide candidate vs non-observation, corruption,
tailles plafonnées, champs absents ou répétés,
collision UID, ajout/évolution d'effets sans changer
l'identité candidate, confidentialité du rapport et
activation expresse S→C de la sonde.

Règle métier : **équipements équipables** susceptibles
de modification de jets via forgemagie et
**familiers** via nourrissage. Ces modifications
ne sont pas requises pour connaître le type d'objet
ou la quantité en stock. Les caractéristiques
réelles des instances doivent toutefois rester
distinctes des bornes DofusDb et être préservées
dans un futur enrichissement optionnel.

### Test réel minimal proposé APRÈS build/test Windows

1. Démarrer `--stock-snapshot-study` puis se connecter
   à **un seul** personnage pour recevoir `irl`.
2. Ouvrir la banque afin de déclencher `irp`.
3. Ne pas déplacer d'objet lors de ce **premier**
   essai : il valide seulement la structure
   des deux collections, leur caractère complet
   ou incomplet, et la disponibilité de `f3/f4/f5`.
4. `Ctrl+C` et partager seulement les lignes
   `STOCK_SNAPSHOT`, et le résumé de build/tests.

**Ne pas demander un nouveau test HDV.**
L'historique déjà caractérisé distingue ItemId
stable, UID d'instance éventuellement renouvelé,
et `MarketListingUid` indépendant.

### Checkpoint Windows

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Préserver les fichiers de travail locaux, y
compris les modifications de solution et les
baselines. Ne jamais effectuer de `git reset`,
`git clean` ou écrasement des changements locaux.
La validation Windows n'est **pas encore reçue**
pour ce checkpoint. En cas d'échec au build ou
aux tests, arrêter l'essai réseau et examiner
l'erreur avant de continuer.

**Étape suivante si les lignes sont structurellement
validées** : confronter `f3/f4/f5` aux variations
réelles de stock et aux deltas `iqx/iqz/irx/iqm`,
sans tester à nouveau la pérennité du UID HDV
ni alimenter les profils par supposition.

## P3d.6.2 — Banque : validation réelle et corrélation aux deltas

### Checkpoint Windows reçu le 25/10/2026

Le précédent développement P3d.6.1 a passé **1276 tests,
0 échec, 0 ignoré, en 10,0 secondes** (rapport utilisateur).

Capture `--stock-snapshot-study` réelle :

```text
[01:46:38.429 UTC] Session #1 S→C OPENED
[01:46:39.461 UTC] Session #1 S→C irp 11682 octets STOCK_SNAPSHOT=[container=bank status=complete-candidate rows=560 fields345=560 incomplete-rows=0 effects-present=157 duplicate-f5=0]
```

**Constats exacts** :

- `irp` porte **560 lignes**, toutes avec les
  trois champs protobuf candidats `f3/f4/f5`,
  **aucune collision de valeur candidate `f5`** ;
- **157 lignes** contiennent au moins un champ
  `f7` de caractéristiques, sans preuve que les
  valeurs aient été systématiquement lues ;
- `complete-candidate` décrit uniquement la
  **cohérence structurelle** du décodeur,
  **pas** une validation sémantique des champs ;
- aucun `irl` observé pendant cette capture :
  l'absence d'un message **n'est pas un
  inventaire vide**. Un snapshot d'inventaire
  peut avoir été reçu lors de la connexion
  du personnage, avant le début de la capture.

### Suite implémentée : comparaison passive banque / transferts

`BestCrush.Network/Protocol/StockSnapshotDeltaStudy.cs`
maintient un instantané **candidat** de
chaque conteneur `irl` / `irp`,
indépendamment **par session TCP**.

Les formes `iqx.root.f2.f1` et
`iqz.root.f1.f1` sont interprétées comme
des triplets **candidats** `f3/f4/f5`
sans attribuer pour l'instant une
signification définitive. Elles sont
comparées au snapshot précédent de
**la même session** en calculant en RAM :

- `uid-candidate-found` / `uid-candidate-not-found`
  pour l'existence du `f5` du delta ;
- `itemId-candidate=same/different`
  pour la comparaison des `f4` ;
- `quantity-candidate=same/different`
  pour la comparaison des `f3`.

Les lignes de retour ne contiennent **jamais**
les quantités, ItemId, UID, caractéristiques,
payloads ou identifiants de compte. Si aucun
snapshot n'a été observé dans cette session,
`baseline-unobserved` est affiché plutôt
que de conjecturer un stock nul. Les snapshots
incomplets **invalident** les anciennes
références du conteneur. Les sessions sont
vidées à la fermeture/expiration et la mémoire
de la sonde est bornée à 16 sessions stockées.

Le mode CLI **ne change pas** :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --stock-snapshot-study
```

Les tests synthétiques couvrent les
correspondances positives et négatives,
l'isolation inventaire/banque et entre
sessions, l'invalidation après corruption,
les deltas malformés et les effets
modifiés sans changement de type/UID.

### Test réel minimal — sans relancer Dofus

**Après** validation Windows de la nouvelle
compilation et suite de tests :

1. Garder **un seul personnage** pour ce test ;
   fermer la banque si elle est ouverte.
2. Lancer le probe avant de **rouvrir** la
   banque, de manière à observer `irp`.
   Si le jeu ne renvoie pas de `irp`,
   noter `baseline-unobserved` et ne
   pas interpréter cela comme un échec.
3. Déplacer une **pile ou un équipement
   identifiable** de banque vers inventaire,
   puis le replacer en banque ; éviter
   toute autre manipulation entre-temps.
4. Arrêter `Ctrl+C` et fournir les seules
   sorties `STOCK_SNAPSHOT`, avec les
   opcodes et horaires utiles.

`iqx/iqz` peuvent apparaître dans les
**deux sens** ; le seul fait de reconnaître
le même triplet ne prouve **pas** quel
conteneur a perdu ou gagné l'objet.
Les opcodes courts `irx/iqm` n'ont
pas encore de décodage sémantique dans
ce nouveau mode. Il ne faut **pas**
attribuer l'objet à une fiche personnage,
alimenter le craft ou muter le stock
avant validation indépendante.

Ne **pas** répéter les tests précédents
de stabilité d'ItemId/UID après HDV.

### Checkpoint Windows de P3d.6.2

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Ces nouveaux tests **ne sont pas encore
exécutés sous Windows**. Ne pas effacer
les modifications locales, baselines,
ou fichiers non suivis. Aucun changement
de SQLite, des flux réseau métier ou
des stocks applicatifs n'a été activé.

## P3d.6.3 — UID candidat absent : comparaison f4/f3 sans divulgation

### Trace réelle du 25/10/2026 — P3d.6.2

Nouvelle exécution de `--stock-snapshot-study`
par l'utilisateur, avec une banque puis deux
transferts d'un équipement :

```text
[01:53:04.184 UTC] Session #1 S→C OPENED
[01:53:15.309 UTC] Session #1 S→C irp 11682 octets STOCK_SNAPSHOT=[container=bank status=complete-candidate rows=560 fields345=560 incomplete-rows=0 effects-present=157 duplicate-f5=0 baseline=stored]
[01:53:17.351 UTC] Session #1 S→C iqx 84 octets STOCK_SNAPSHOT=[delta=fields345-candidate bank=[uid-candidate-not-found] inventory=[baseline-unobserved]]
[01:53:22.403 UTC] Session #1 S→C iqz 84 octets STOCK_SNAPSHOT=[delta=fields345-candidate bank=[uid-candidate-not-found] inventory=[baseline-unobserved]]
```

**Conclusion strictement autorisée par les données :**

1. `irp.root.f2[]` est de nouveau lisible
   intégralement (560 lignes / 560 triplets candidats).
2. `iqx` et `iqz` contiennent chacun un
   triplet f3/f4/f5 admissible structurellement.
3. Pour ces **deux** deltas, le `f5` n'appartient
   pas à l'ensemble des 560 `f5` de la banque
   avant transfert.
4. Le `irl` n'a pas été observé pendant cette
   session ; il est **interdit** d'en inférer
   un inventaire vide.
5. **Ne pas conclure** que l'UID d'un équipement
   change lors du transfert ! Il reste possible
   qu'il y ait une particularité du protocole,
   une mauvaise hypothèse de champ, un décalage
   entre les snapshots ou un objet absent du
   conteneur de départ.

### Implémentation P3d.6.3

Le même mode `--stock-snapshot-study` établit
dorénavant deux séries supplémentaires de
comparaisons **locales en RAM uniquement** :

- En cas de `uid-candidate-not-found`,
  comparer `f4` du delta aux 560 `f4`
  candidats de la banque : rapporter les
  comptes `same-type-rows` et
  `same-type-quantity-rows` (intersection
  `f4`/`f3`), ainsi que
  `same-type-effects-count-rows` (égalité
  du nombre d'effets `f7`, pas des valeurs).
  Plusieurs copies du même type peuvent
  exister : ces comptes n'établissent
  **pas une identité d'instance**.
- Comparer chaque delta au **delta
  immédiatement précédent dans la même
  session TCP**, sans présumer qu'il s'agit
  de la même transaction. Les indicateurs
  `previous-delta` exposent seulement
  `f3=same/different`, `f4=same/different`,
  `f5=same/different` et
  `effects-count=same/different`,
  avec l'opcode précédent `iqx`/`iqz`.
  Une nouvelle liste `irl/irp` remet
  cette comparaison à zéro.

La sonde n'affiche **jamais** les valeurs
brutes de `f3/f4/f5`, les IDs d'objets,
UID, détails `f7`, endpoints ou
identités de personnages. Les baselines
sont cloisonnées par session TCP,
révoquées à la fermeture ou sur snapshot
invalide. Pas d'activation métier.

Des tests synthétiques couvrent :
`same-type-rows` de cardinalité variable,
les collisions d'UID de type inconnu,
les deux directions `iqx/iqz` identiques
ou différentes, le réarmement sur nouveau
snapshot ou fermeture de session, et la
confidentialité de toutes les sorties.

### Contrôle Windows requis, une seule manipulation ciblée

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Ne pas effacer les fichiers locaux** ; toute
collision avec un changement local doit être
résolue explicitement sans `reset` ou
`clean`.

Une fois les tests passés :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --stock-snapshot-study
```

Laisser Dofus ouvert. Avec **un seul client**,
lancer le probe **avant d'ouvrir** la banque,
observer `irp`, puis déplacer **un seul
équipement de banque vers inventaire et
inversement**. `Ctrl+C` et fournir uniquement
les lignes `STOCK_SNAPSHOT`.

Le comportement des champs `f3/f4/f5`
sur les snapshots de `irl/irp` demeure
**candidat**, même si celui de `kch` a été
établi. Aucun changement d'ItemId/UID
lors du passage par HDV n'est à tester
de nouveau : les travaux historiques
distinguent déjà UID d'instance, ItemId
et UID d'annonce.

## P3d.6.4 — Corrélation des effets réels sans exposer les jets

### Capture réelle : 25/10/2026, 01:57 UTC

La banque a fourni 560 lignes structurellement complètes,
dont 157 avec champs `f7` ; aucun doublon de
`f5` candidat détecté.

```text
01:57:48 irp rows=560 fields345=560 effects-present=157 duplicate-f5=0 baseline=stored
01:57:53 iqx bank=[uid-candidate-not-found same-type-rows=1 same-type-quantity-rows=1 same-type-effects-count-rows=1] inventory=[baseline-unobserved] previous-delta=[none]
01:57:59 iqz bank=[uid-candidate-not-found same-type-rows=1 same-type-quantity-rows=1 same-type-effects-count-rows=1] inventory=[baseline-unobserved] previous-delta=[opcode=iqx f3=same f4=same f5=different effects-count=same]
```

**Interprétation strictement étayée** :

- Un **unique** enregistrement de banque possède le
  même `f4` que chacun des deltas, et son `f3`
  et le **nombre** d'effets sont identiques.
- Le `f5` des deux deltas ne correspond à aucun
  `f5` du snapshot bancaire ; les `f5` des
  deux deltas sont eux-mêmes différents.
- `f4`, `f3` et nombre d'effets des deux deltas
  sont identiques.
- Cela **ne prouve pas** une réattribution d'UID,
  la continuité d'une instance, ni la direction
  du transfert ; l'inventaire initial `irl` n'a
  pas été observé.
- Les tests antérieurs de renouvellement d'UID
  lors du passage par HDV ne doivent **pas** être
  extrapolés automatiquement à un transfert
  interne banque ↔ inventaire.

### Extension du même mode, sans IDs imprimés

Le décodage candidat `StockItemWireCandidate`
conserve en RAM (borné à 64 entrées de 1024 octets)
les **paires exactes** `(f10 code type, f5 valeur)`
pour chaque `f7[]` interprétable. L'ordre des
effets est neutralisé par une comparaison
de **multiensembles** préservant les doublons.

Un effet non décodable produit un état
`unrecognized` ; une liste sans effets
est `no-effects`. Aucun de ces cas ne doit
être assimilé à un match exact de jets.

Quand le `f5` candidat n'est pas retrouvé
dans la banque, la sonde ajoute :

- `exact-effects-status=decoded/no-effects/unrecognized`
- `same-type-exact-effects-rows=N`
- `same-type-quantity-exact-effects-rows=N`

Pour deux messages de transfert consécutifs,
`previous-delta` inclut maintenant
`exact-effects=same/different/unrecognized/not-applicable`.
En cas de `f5` candidat trouvé,
`exact-effects` complète aussi la comparaison
avec la ligne de référence.

L'égalité des jets **renforce** la corrélation
entre les messages mais ne prouve pas à elle
seule une identité d'instance (deux copies du
même équipement peuvent posséder les mêmes jets).
Une modification légitime des caractéristiques
d'un objet équipable par FM ou d'un familier
par nourrissage peut également conduire à
`exact-effects=different` sans changement
de type d'objet.

Les valeurs, identifiants d'effets, UID,
ItemId, noms, comptes et charges utiles
restent absents de la sortie texte et de
SQLite. Les paires sont gardées uniquement
dans les références éphémères du probe,
cloisonnées par session TCP.

### Tests synthétiques et étape suivante

Des tests supplémentaires couvrent :
un exemplaire correspondant par ses jets
parmi deux du même type, ordre f7 modifié,
paires d'effets répétées ou négatives,
effets modifiés ou non décodables, comparaison
iqx↔iqz avec f5 différents, absence de
divulgation des codes/valeurs, et invalidation
de la comparaison précédente si un delta
malformé intervient entre deux messages.

**Pas encore de validation Windows pour ce patch.**
Le mode CLI reste identique :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --stock-snapshot-study
```

La validation du code précède le lancement du probe ;
préserver les modifications locales. Pour le test réel,
un seul client, démarrer la sonde avant l'ouverture
de banque, faire **un aller-retour** de l'équipement
déjà utilisé, puis `Ctrl+C`. Partager uniquement les
lignes `STOCK_SNAPSHOT`.

Ne pas relancer les anciens tests HDV ni attribuer
les stocks à un personnage avant d'avoir validé
les champs `f3/f4/f5` et l'identité de sa session.

## P3d.6.5 — Capture confirmée : effet exact malgré f5 différent

### Référence réelle validée (25/10/2026, 02:09 UTC)

La capture d'un transfert banque → inventaire → banque d'un équipement
dont les caractéristiques ont été déterminées donne :

```text
[02:08:54.687 UTC] Session #1 C→S OPENED
[02:09:29.052 UTC] Session #1 S→C irp 11684 octets STOCK_SNAPSHOT=[container=bank status=complete-candidate rows=560 fields345=560 incomplete-rows=0 effects-present=157 duplicate-f5=0 baseline=stored]
[02:09:37.147 UTC] Session #1 S→C iqx 49 octets STOCK_SNAPSHOT=[delta=fields345-candidate bank=[uid-candidate-not-found same-type-rows=1 same-type-quantity-rows=1 same-type-effects-count-rows=1 exact-effects-status=decoded same-type-exact-effects-rows=1 same-type-quantity-exact-effects-rows=1] inventory=[baseline-unobserved] previous-delta=[none]]
[02:09:46.265 UTC] Session #1 S→C iqz 49 octets STOCK_SNAPSHOT=[delta=fields345-candidate bank=[uid-candidate-not-found same-type-rows=1 same-type-quantity-rows=1 same-type-effects-count-rows=1 exact-effects-status=decoded same-type-exact-effects-rows=1 same-type-quantity-exact-effects-rows=1] inventory=[baseline-unobserved] previous-delta=[opcode=iqx f3=same f4=same f5=different effects-count=same exact-effects=same]]
```

**Nouveauté démontrée** : parmi les 560 lignes de la banque,
une **seule** ligne possède le même `f4` que les deux deltas
et son `f3` et ses **paires exactes d'effets f7 (f10 code/f5 valeur)**
correspondent. Les deux deltas ont eux-mêmes les mêmes effets réels,
malgré des `f5` objets différents. Le `f5` d'aucun des deux
deltas ne figurait dans les 560 lignes bancaires au moment du snapshot.

**Évaluation prudente** : forte preuve de **continuité logique de
l'équipement** à travers le transfert, mais pas une preuve d'UID
d'instance réellement renouvelé : l'interprétation de `f5` en
`irl/irp/iqx/iqz` reste une hypothèse issue de son sens déjà
établi dans `kch` ; deux objets peuvent en théorie partager
un même type et des jets strictement identiques.

**Ne pas** prétendre que `ItemId` est un identifiant d'exemplaire :
il désigne le type. Les tests historiques HDV établissaient déjà
un renouvellement d'UID après retrait/rachat, mais pas nécessairement
pour ce mouvement de conteneurs. Aucun `irl` reçu pendant cette
capture (la non-observation n'est pas une liste vide).

La caractérisation des **effets identiques** est désormais suffisante
pour cet équipement : **ne plus demander une nouvelle répétition
de ce test sans hypothèse distincte**. Priorité suivante :
messages courts `irx`, `iqm`, `iqq` et rattachement des
mouvements au conteneur et à la session, ou capture `irl`
indépendante pour confirmer l'inventaire. Toujours en mode
lecture seule, sans présumer la direction ou appliquer de mutations.

## P3d.6.5b — Signaux courts irx/iqm/iqq en lecture seule

La sortie précédente `irp/iqx/iqz` a démontré une
**correspondance unique des effets réels** entre
la banque et les deux messages de transfert,
tout en montrant des `f5` candidats différents.
Cette analyse de jet est **terminée** pour
l'équipement utilisé. Ne pas la refaire.

### Nouveau code (même option CLI)

`BestCrush.Network/Protocol/StockShortMovementStudy.cs`
est activé uniquement avec
`--stock-snapshot-study` en réception **S→C**.

Objectif : caractériser des signaux courts
potentiellement liés à un mouvement, sans
leur attribuer prématurément la sémantique
d'ajout, de retrait, de quantité ou d'UID :

- `irx` : enveloppe candidate `root.f3`,
  avec `inner.f1` et `inner.f3` numériques ;
- `iqm` : `root.f1` candidat ;
- `iqq` : `root.f1/f2` candidats si présents.

Le diagnostic compare chaque scalaire au
triplet f3/f4/f5 du **dernier** `iqx/iqz`
de la **même session TCP**, et imprime
uniquement `same/different` ou
`no-delta-baseline`. Aucun scalaire réel,
identifiant, quantité, jeu de caractéristiques,
nom, adresse IP ou payload n'est imprimé
ou sauvegardé. Tout reste en RAM et le
mode ne modifie ni inventaire, ni banque,
ni le cache de concassage, ni SQLite.
Un message `irx/iqm/iqq` n'est
**jamais** considéré à lui seul comme une
preuve de changement de stock.

Tests synthétiques :
formes valides et malformées, références
absentes, séparation sessions, comparaison
sans données privées, sonde inactive par
défaut et uniquement S→C. L'ancien test
qui ignorait `irx` a été adapté au
diagnostic opt-in. Pas de résultat de
validation Windows pour ces ajouts à ce jour.

### Validation Windows

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Ne pas résoudre une divergence de Git
en effaçant les modifications locales.

### Essai réel différent du précédent

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --stock-snapshot-study
```

Sur **un seul** client Dofus :

1. Garder Dofus connecté et la banque **fermée**
   avant le démarrage du probe. Ouvrir ensuite
   la banque pour recevoir une référence `irp`.
2. Prendre une **pile de ressources** (plusieurs
   unités, idéalement au moins 5) en banque.
   Déplacer **une partie seulement** vers
   l'inventaire et noter ce mouvement.
3. Déplacer le reste (ou une seconde fraction)
   puis retransférer une partie vers la banque
   si la manipulation est simple.
4. Finir avec `Ctrl+C` et transmettre les
   lignes `STOCK_SNAPSHOT`, y compris celles
   qui mentionnent `short=irx/iqm/iqq`.

Si aucun message court n'apparaît,
ne pas interpréter cela comme une erreur :
le protocole peut émettre d'autres opcodes
selon la fusion/scission des piles et
l'état des conteneurs. Le but est de
discriminer la **sémantique des ajustements**,
pas de redémontrer l'identité des jets
ou le comportement des UID en HDV.

Le décodage métier de l'inventaire/banque
dans `CurrentCharacterState` reste
**désactivé** jusqu'à validation des
quantités et association fiable à la
session personnage/serveur/compte.

## P3d.6.5c — Capture de retrait fractionné, P3d.7 — regroupement logique des stocks

### Capture réelle du 25/10/2026, 02:20–02:21 UTC

Le test opérateur a consisté à retirer une
partie d'une pile de ressources banque →
inventaire, à retirer le reste, puis à
retransférer la pile vers la banque :

```text
02:20:50 irp rows=560 fields345=560 effects-present=157 duplicate-f5=0 baseline=stored
02:21:01 iqx bank=[uid-candidate-not-found same-type-rows=1 same-type-quantity-rows=0 ...] inventory=[baseline-unobserved]
02:21:01 iqz bank=[uid-candidate-found itemId-candidate=same quantity-candidate=different] previous-delta=[opcode=iqx f3=different f4=same f5=different]
02:21:01 iqq short=scalar-candidates f1/f2 different du dernier delta
02:21:10 irx short=f3.f1+f3.f3 valeurs differentes du dernier delta
02:21:10 iqz bank=[uid-candidate-found itemId-candidate=same quantity-candidate=different] previous-delta=[opcode=iqz f3=different f4=same f5=same]
02:21:10 iqq short=scalar-candidates f1/f2 different du dernier delta
02:21:19 iqz bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same] previous-delta=[opcode=iqz f3=different f4=same f5=same]
02:21:19 iqm short=f1 different du dernier delta
02:21:19 iqq short=scalar-candidates f1/f2 different du dernier delta
```

**Observé** :

- Les **trois** `iqz` S→C après mouvements
  trouvent un `f5` déjà présent dans le snapshot
  bancaire `irp` ; `f4` est égal, `f3` varie
  lors des deux premières transitions puis
  retrouve sa valeur initiale après le retour.
- `iqx` lors du premier retrait ne retrouve
  pas son `f5` dans la banque ; `f4` reconnaît
  un unique type bancaire, et le `f3` diffère
  de la quantité bancarisée initiale.
- `irx`, `iqm`, `iqq` sont effectivement
  émis pendant des mouvements de piles.
  Leur comparaison au **dernier `iqz`**
  n'a détecté aucune égalité : cela ne
  valide ni n'invalide leur sens. Ils
  pourraient viser la pile côté inventaire
  (`iqx`) ou employer d'autres champs.
- **Aucun `irl` initial** observé ; les
  quantités de l'inventaire restent inconnues
  pour ce probe.
- On ne connaît pas les **valeurs numériques
  exactes** dans la capture, uniquement les
  égalités/différences, donc ne pas attribuer
  artificiellement des nombres aux champs.

**Interprétation forte, non encore normative** :
`iqz` semble représenter une mise à jour
de la pile bancaire existante (UID candidat
persistant ; quantité candidate ajustée
puis rétablie). `iqx` semble porter une
autre incarnation de l'objet lors de son
transfert. **Ne pas conclure à un UID
renouvelé sur tout transfert** ; la notion
d'identité permanente est d'ailleurs non
nécessaire au métier.

### Décision de conception utilisateur : identité fonctionnelle

L'utilisateur préfère traiter comme **fongibles**
les objets ayant le **même type d'objet** et
**exactement les mêmes caractéristiques** :
il n'est pas nécessaire de tracer l'UID
d'instance à travers tous les conteneurs
ni de ré-identifier `A` vs `B`.

Distinguer :
1. L'UID réseau local, utile temporairement
   pour choisir la pile à incrémenter ou
   retirer au sein d'une session ;
2. La clé de regroupement métier
   `ItemId + multiensemble exact des effets réels`,
   avec **somme des quantités** ; la clé n'inclut
   ni UID ni identifiant d'annonce HDV ;
3. Les stocks restent séparés par **conteneur,
   serveur, personnage et compte vérifié**.
   Ne jamais fusionner les stocks intercomptes
   ni considérer une banque comme appartenant
   à un personnage sur la seule présence d'une
   fenêtre ou sur un nom visible ;
4. Un équipement peut changer de jets via FM,
   un familier via nourrissage : il change
   alors de **groupe fonctionnel** après
   une observation réellement validée.
   Conserver séparément toute information
   particulière non capturée dans `f7`
   pouvant rendre deux biens non fongibles
   (ex. liens ou contraintes d'échange).

### P3d.7 : code ajouté, purement candidat

Nouvelle classe
`BestCrush.Network/Protocol/StockCandidateGrouping.cs` :
prend un snapshot `irl/irp` de statut
`CompleteCandidate` et le regroupe en RAM
par `f4` candidat et les effets `f7`
entièrement décodés (`f10` code,
`f5` valeur signée). Les effets répétés
et leur ordre sont correctement gérés.

Les quantités `f3` **candidates** sont
additionnées sous contrôle de débordement.
Aucun UID n'entre dans la clé regroupée ;
une pile de type identique mais de jets
différents n'est jamais écrasée.

Le regroupement refuse de produire un
stock pour les snapshots malformés,
incomplets, vides candidats, valeurs
hors limites ou effets non décodables.
Il distingue par construction la banque
de l'inventaire ; les propriétaires restent
hors champ. Il n'écrit ni dans
`CurrentCharacterState` ni dans SQLite
et n'alimente aucun calcul de craft tant
que les champs et l'identité ne sont pas
correctement vérifiés.

Le probe `--stock-snapshot-study` montre
désormais uniquement :
`grouping=candidate groups=N source-rows=M
groups-with-effects=K` ou
`grouping=not-ready`. Aucun ID, UID,
quantité, jet, personnage ou prix ne
figure dans ce rapport.

Nouveaux tests synthétiques : objets
identiques fusionnés sans UID permanent ;
jets distincts restant séparés ;
négatifs, ordre des effets et doublons ;
effets non décodables => fail-closed ;
quantités hors limites/débordement ;
absence de mélange inventaire/banque ;
confidentialité des lignes de probe.

### Prochaine validation

Un seul checkpoint Windows : 

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Tests Windows du nouveau code à recevoir** :
ne pas les présenter comme déjà passés.
Aucune nouvelle capture ni nouveau test HDV
ne sont nécessaires pour caractériser à nouveau
l'identité/fongibilité des objets.

L'étape d'intégration production future doit
d'abord fournir une preuve d'identité de la
session/du compte/du serveur et une distinction
des snapshots réellement complets ; un regroupement
`CompleteCandidate` ne suffit pas à lui seul.

## P3d.8 — Modèle métier des stocks fongibles : checkpoint 1304 tests

### Point de validation Windows — 25/10/2026

L'utilisateur confirme la suite complète **1304 tests réussis,
0 échec, 0 ignoré**, durée **9,2 s**, après l'ajout de
`StockCandidateGrouping` (P3d.7).

La compilation des projets n'a pas été jointe en tant
que résultat séparé ; le résultat de `dotnet test`
est cependant positif. Ne pas gonfler ce checkpoint
en prétendant que P3d.8 a déjà été testé.

### P3d.8 — Préparation sans réseau du modèle métier

Objectif : porter la règle métier validée
`ItemId + liste **exacte** des caractéristiques réelles
→ somme des quantités` vers les modèles de
session caractère, sans créer de clé permanente
fondée sur `ItemUid`.

- `CharacterInventoryItem` conserve sa signature
  constructeur existante `(ItemId, InstanceUid,
  Quantity)`; une propriété `Effects` init-only,
  de type `IReadOnlyList<CharacterInventoryEffect>?`,
  est ajoutée. `Effects=null` = effets **non observés**,
  `Effects=[]` = effets **observés absents**.
- `BestCrush/Services/CharacterStockGrouping.cs`
  expose un regroupement **pur** avec la clé
  `ItemId + multiensemble de (EffectId, Value)`
  et calcule la somme des quantités. Il n'inclut
  pas l'UID dans cette clé et n'essaie pas de
  raccorder les UID après HDV/transfert.
- Le regroupement refuse les données inconnues,
  les quantités négatives/nulles, les types
  malformés et les débordements. Une liste
  vide connue reste une liste vide.
- Il faut l'appeler **par conteneur et par propriétaire
  déjà vérifié** : le service ne peut pas
  prouver un rattachement de compte/personnage
  en inspectant seul une liste d'objets.
- Les objets aux jets différents ne sont pas
  fusionnés ; la FM d'un équipement ou le
  nourrissage d'un familier crée un autre
  groupe métier dès lors qu'un nouveau jet
  est **observé et validé**.

Nouveaux tests synthétiques : jet identique
et UID différents, ordre et répétition d'effets,
valeurs négatives, ressources sans effets,
évolution des jets, null vs vide,
quantités invalides/débordement, et
conteneurs gardés séparés.

**Pas encore de raccordement au réseau de production**,
à `CurrentCharacterState.ApplyObservedData`
ni à l'UI. En particulier, les champs numériques
`f3/f4/f5` de `irl/irp` restent des
**candidats sémantiques**, et l'attribution de
la banque au compte/serveur doit avoir
une preuve indépendante. Cela ne constitue
pas une lecture fiable des stocks actuels
en production.

### Prochain checkpoint Windows

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Si échec, fournir les erreurs de build/test
sans nettoyer ni réinitialiser le dépôt.

**Étape suivante après tests** :
raccorder uniquement des **snapshots
sémantiquement validés** et des preuves
d'appartenance à une session/compte/serveur
au modèle métier, en refusant toute
attribution fondée sur le nom, le focus
ou la présence d'un `f5` candidat.

## P3d.9 — Vue de stocks restreinte aux connexions et comptes vérifiés

### Point de contrôle P3d.8 validé le 25/10/2026

La sortie Windows communiquée par l'utilisateur :

```text
Récapitulatif du test : total : 1310; échec : 0; réussi : 1310; ignoré : 0; durée : 8,5s
```

Confirme le succès des **1 310 tests** après le
modèle de regroupement métier P3d.8.
Il n'y a aucune capture Dofus complémentaire requise
à ce checkpoint.

### Changement P3d.9 : projection en lecture seule

`CurrentCharacterState.GetScopedStockGroups(CharacterKey)`
et le modèle `CharacterScopedStockGroups` permettent
désormais de consulter des groupes fonctionnels
**déjà observés et acceptés par l'état de session**.
Aucun accès réseau, aucun calcul de craft et aucune
écriture SQLite ne sont ajoutés.

Règles de sécurité :

- Le caractère doit exister, posséder une identité
  confirmée, une session `CharacterConnectionScope`
  active et non révoquée ; le serveur actif doit
  être celui de la clé demandée.
- Les personnages de **même nom** restent séparés
  par `CharacterKey(ServerName, CharacterId)` ;
  le focus ou le nom affiché ne déterminent
  jamais implicitement le propriétaire.
- `Inventory = null` signifie non observé,
  `Inventory = []` signifie observé vide.
  Effets non lus ⇒ regroupement inconnu,
  pas regroupement forcé.
- Le stock **banque** de la vue est `null`
  tant que `VerifiedAccountServerKey` manque,
  est incomplet ou diverge du serveur de
  `CharacterKey`. Le simple fait d'ouvrir
  une banque sur une connexion ne prouve
  pas le titulaire du compte.
- Une fiche conservée après déconnexion
  ne doit **pas** exposer ses stocks comme
  courants ; une reconnexion doit vider
  les anciennes observations avant toute
  nouvelle application.
- `CharacterStockViewProjection.FromVerifiedProfile`
  est une fonction pure pour appliquer
  cette vérification aux données de
  session confirmées ; elle **ne peut
  pas** confirmer à elle seule le caractère,
  le compte ou la sémantique des champs
  réseau.

Tests nouveaux dans
`Tests.BestCrush/Network/CharacterScopedStockGroupsTest.cs` :
cohabitation de deux comptes/personnages de
même nom, données cloisonnées, preuve de
compte+serveur pour la banque, profil
déconnecté, changement de serveur et
reconnexion, absence d'effets et
distinction vide vs non observé.

### Restrictions restantes importantes

**Ne pas brancher `irl/irp` au
`CurrentCharacterState` en production
sur la seule base de `CompleteCandidate`.**
Les messages réseau ont été décrits par
équivalences anonymes et corrélations :
`f3/f4/f5` du snapshot sont encore
candidats, et la preuve d'identité
du compte/serveur (P3c) manque.

Le modèle est prêt pour ingestion **lorsque**
une source indépendante valide à la fois
le propriétaire et les champs sémantiques.
Ce checkpoint ne débloque ni
`VerifiedAccountServerKey` ni
une copie du stock bancaire à un
personnage au hasard.

### Checkpoint Windows

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Ne pas détruire les modifications locales.
La suite P3d.9 reste à valider sous Windows.

## P3d.10 — Différences entre snapshots de stocks fongibles

Checkpoint précédent : 1315 tests réussis, 0 échec, 0 ignoré (8,6 s), communiqué le 25/10/2026.

Nouveau code : `StockSnapshotGroupChangeStudy` compare deux snapshots candidats successifs du même conteneur et de la même session TCP. Il utilise ItemId candidat et les effets exacts pour déterminer les groupes fonctionnels, jamais un UID permanent. Les quantités sont comparées en RAM, mais ne sont pas affichées.

Sortie ajoutée : `snapshot-change=no-prior-snapshot`, `snapshot-change=not-comparable` ou `snapshot-change=compared` avec seulement les nombres de groupes augmentés, diminués, nouveaux, supprimés et inchangés. Le résultat est candidat, non autoritatif. Un snapshot invalide révoque la référence précédente. Les conteneurs et sessions sont isolés. Aucune écriture dans le profil du personnage, SQLite ou le moteur de craft.

Tests ajoutés : groupes fonctionnels avec plusieurs UID, évolution des quantités, jets différents, isolation entre sessions et conteneurs, invalidation des données malformées, confidentialité.

À valider sous Windows :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Un test longitudinal ultérieur pourra comparer deux messages `irp` de la même connexion après modification d'une pile de ressources. La sémantique de `f3/f4/f5` et l'identité du compte/serveur restent à établir avant activation du stock métier.
