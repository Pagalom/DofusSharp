# P3e — Recherche des messages de progression XP métiers

## Contexte de l'observation

Étude expérimentale sur le client DOFUS `3.6.11.15`.
Le relevé manuel du joueur, sur un métier suivi :

| Mesure successive | Niveau | XP affichée |
| --- | ---: | ---: |
| avant fabrication 1 | 1 | 2 |
| après fabrication 1 | 2 | 22 |
| après fabrication 2 | 2 | 40 |

**Précision définitive de l'opérateur : exactement DEUX
fabrications : la première a rapporté +20 XP,
la seconde +18 XP.** Ne pas interpréter le nombre
de messages réseau comme un nombre de fabrications.

Les différences des valeurs affichées correspondent
aux gains déclarés : 2 → 22 (+20) avec passage
du niveau 1 au niveau 2, puis 22 → 40 (+18)
sans passage de niveau. **Il n'y a pas d'écart
inexpliqué.** La nature exacte du compteur affiché
(total ou progression propre à un niveau) reste
une question distincte, à ne pas déduire de ces
trois seuls relevés. Ces valeurs constituent
désormais deux références de terrain pour P3e.

## Capture P3e.0 — métadonnées uniquement

Le diagnostic existant `--character-scan` a été
exécuté pendant des opérations de fabrication.
**Une seule session TCP** porte les événements.

Fenêtre principale suspecte : 23:00:15–20 UTC
(timing de la capture, sans identification formelle
des deux fabrications).

- Six messages **`kbf` C→S** : trois de
  **7 octets**, puis trois de **8 octets** ;
- Six **`ith` S→C** de 4 octets ;
- Six **`iqq` S→C** de 6 octets ;
- Six **`jzx` S→C** de 5 octets ;
- Six **`lof` S→C**, de 25 puis 26 octets
  (une observation finale de 25 octets) ;
- Plusieurs **`kas` S→C** de 18 puis
  20 octets ; certaines opérations présentent
  des réponses `kas` supplémentaires ;
- **`iqx` / `irx` S→C** alternent ;
  leurs rôles ne sont pas connus.

Trois messages `kbf` de 7 octets apparaissent
vers 23:00:15.893 (deux), 23:00:16.896 (un).
Trois messages `kbf` de 8 octets apparaissent
vers 23:00:18.906 (deux), 23:00:19.915 (un).

Il y a **six messages `kbf` pour deux fabrications**.
Ces échanges pourraient correspondre à des requêtes
répétées ou à des sous-opérations, mais aucune
association un-pour-un ni trois-pour-un n'est
établie. Le passage 7→8 octets et 18→20 octets
est une **corrélation de tailles**, pas la preuve
d'une augmentation de niveau ou d'XP. Il pourrait
aussi provenir d'une recette, d'un objet,
d'une quantité ou d'un identifiant différent.

Deux occurrences `ipz` S→C de **12 octets**
apparaissent **hors de la séquence de fabrication**,
environ 22:59:07 et 23:00:38 UTC, après
des requêtes `kba` C→S. Leur forme n'a pas été
étudiée et elles ne sont pas assimilées aux
**instantanés `ipz` de 20 métiers / 294 octets**
déjà décodés en P3a/P3b. Le décodeur
`ProfessionIpzDecoder` exige exactement
20 entrées, donc ne décode pas ces messages
de 12 octets dans son mode actuel.

**Conclusion :** le parcours des deux fabrications
est associé temporellement à six `kbf` et à des
réponses répétées, mais la relation causale exacte
reste à caractériser. Aucune famille de messages
n'a encore été démontrée comme contenant l'XP
métier, le métier concerné ou un changement de
niveau. Ne pas assimiler six `kbf` à six crafts.

## Expérience suivante — étude des formes sans valeurs

Lancer sur un seul client DOFUS, connecté
à un compte et serveur connus de l'opérateur :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys kbf,kas,ith,iqx,irx,iqq,lof,jzx,ipz,kba
```

Le mode `--character-shape` existant sort
**uniquement les formes protobuf** (numéros et
types de champs, sans valeurs). Il peut supprimer
les structures identiques répétées, ce qui est
normal : les métadonnées horodatées et les
relevés manuels servent à identifier les épisodes.

Procédure :
1. relever le **niveau et l'XP exacts** avant
   la capture ; éviter d'ouvrir le panneau métier
   en cours de capture si possible ;
2. attendre quelques secondes sans fabriquer ;
3. réaliser **une seule fabrication** dont le
   gain d'XP est visible, puis attendre ;
4. relever le **niveau et l'XP** en dehors
   de l'intervalle de fabrication si nécessaire ;
5. réaliser une seconde fabrication, en notant
   son horaire approximatif, puis arrêter.

Partager seulement les lignes anonymisées
des opcodes ciblés et les moments manuels
approximatifs des fabrications. Ne pas publier
de noms de compte/personnage, PCAP,
payloads ni authentifiants.

La prochaine étape P3e.1 dépend de ces formes :
concevoir un décodeur **opt-in, en RAM, à sortie
catégorielle**, éventuellement fondé sur des
références XP saisies localement, sans créer
de métiers, sans attribution automatique
de compte et sans écriture dans la base.

## Critères de validation et restrictions

- Séparer messages de fabrication, butin/
  inventaire, métier et éventuel passage de niveau.
- Démontrer un changement compatible avec les
  relevés d'XP **à au moins deux moments indépendants**.
- Vérifier que le champ supposé XP ne suit pas
  seulement la recette, la quantité ou
  l'identifiant d'objet.
- Conserver `account=unverified server=unverified`
  jusqu'à P3c ; aucun métier ne doit être
  automatiquement associé à un compte.
- **Pas de nouveaux tests d'isolation multi-compte
  à ce stade**, conformément au périmètre reporté
  en P3c. Aucun changement de production.

Statut : **P3e.0 observé ; sémantique XP non vérifiée.**


## P3e.1 — Deux gains +16 XP, niveau constant, formes protobuf

Nouvelle observation fournie par l'opérateur :

| Étape | Niveau | XP affichée | Variation |
| --- | ---: | ---: | ---: |
| Début | 3 | 76 | — |
| Milieu | 3 | 92 | +16 |
| Fin | 3 | 108 | +16 |

Aucun changement de niveau entre les mesures :
c'est une référence plus simple que la première
séquence comprenant le passage niveau 1 → 2.

Capture `--character-shape --shape-keys
kbf,kas,ith,iqx,irx,iqq,lof,jzx,ipz,kba`
dans **une session**. L'unique groupe visible
à **23:10:41 UTC** est :

- `kba` C→S, 4 octets :
  `root=[1:v 2:v]` ;
- `irx` S→C, 15 octets :
  `root=[2:len 3:len]`,
  sous-champs candidats `?2.1:v ?2.4:v ?3.1:v ?3.3:v` ;
- `ipz` S→C, **12 octets** :
  `root=[1:len]`,
  sous-champs candidats
  `?1.1:v ?1.2:v ?1.3:v ?1.5:v ?1.6:v` ;
- `iqq` S→C, 6 octets :
  `root=[1:v 2:v]`.

### Limite majeure du diagnostic employé

Le code du probe `CharacterMetadataProbe`
déduplique les structures **par session + opcode**
et n'écrit que les **trois premières structures
distinctes**. Les répétitions exactes d'une
forme ne s'affichent **jamais**. Ainsi, le journal
ne prouve **ni l'absence de messages
ultérieurs** ni la présence d'un événement
propre à chacune des deux fabrications.
Les horaires précis des deux fabrications ne
sont pas indiqués dans la trace.

L'observation de `ipz root=[1:len]`
suggère une **ligne unique** par opposition
au format à vingt lignes précédemment décodé.
Cela fait de `ipz` un candidat intéressant
pour une réponse ciblée mais **ne prouve pas**
son rôle dans la progression XP ni les sémantiques
des champs `?1.1`/`?1.2`/etc.
Ce `ipz` suit immédiatement `kba` ; il pourrait
être déclenché par une demande de consultation,
et non par une fabrication.

### Décision et suite

Ne pas développer un décodeur XP métier sur cette
seule forme. Pour progresser, capturer toutes
les occurrences anonymes pendant les fabrications,
avec une fenêtre de repos avant/après et les horaires
manuels de chacune :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-scan
```

Le mode `--character-scan` rapporte chaque
message/opcode/taille/heure sans valeurs brutes ;
il n'applique **pas** la déduplication
`--character-shape`. Les prochaines analyses
pourront ensuite cibler un mode P3e opt-in
comparant des champs anonymement à des niveaux/XP
de référence, sans identité de compte présumée.
Aucun code de production ni test d'isolation ajouté.

Statut P3e.1 : **deux gains +16 contrôlés,
message d'XP non identifié**.


## P3e.2 — Capture non dédupliquée : deux réponses `ipz` après `kba`

Capture `--character-scan` sur une session TCP,
du 23:16:11 au 23:16:38 UTC, avec deux séquences
comparables espacées d'environ 13 secondes.

| Heure UTC | Requête C→S | Réponse S→C | Autres réponses S→C |
| --- | --- | --- | --- |
| 23:16:21.481–.483 | `kba` 4 octets | `ipz` 13 octets | `irx`×2, `kcx`×2, `irq`, `kco`, `ipa`, `jnj`, `iqq`, `krg` |
| 23:16:34.581–.582 | `kba` 4 octets | `ipz` 14 octets | `irx`×2, `kcx`×2, `irq`, `kco`, `iqq` |

Contrairement à P3e.1, **tous les messages** sont
visibles sans déduplication. Aucun `kbf`,
`kas`, `ith`, `lof` ou `jzx` n'apparaît
sur cet intervalle, même si ces opcodes étaient
présents dans la capture de fabrication P3e.0.

**Hypothèse :** `kba` sollicite un ensemble
d'informations contenant une réponse `ipz`
mono-entrée. La corrélation est répétée deux fois
avec un écart d'une milliseconde entre demande
et réponse, mais l'origine fonctionnelle de
`kba` reste non identifiée (consultation d'un
panneau ? autre requête de jeu ?).

La taille `ipz` augmente de 13 à 14 octets.
Une variation de longueur seule n'établit
ni la valeur ni le changement du compteur XP.
Elle peut refléter différents champs ou longueurs
de varints. Les valeurs exactes de niveau et XP
lors de **cette** capture n'ont pas été
communiquées ; ne pas lui attribuer les gains
précédents sans confirmation.

**Question expérimentale :** l'opérateur a-t-il
ouvert la fenêtre métiers `J` aux deux instants
`kba`, ou ces instants correspondent-ils aux
fabrications ? Avant d'écrire un décodeur,
distinguer consultation de métier, fabrication
et transmission spontanée de progression.

Statut : `ipz` mono-entrée lié
temporellement à `kba` sur deux occurrences ;
**XP sur le fil non validée.**
