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
