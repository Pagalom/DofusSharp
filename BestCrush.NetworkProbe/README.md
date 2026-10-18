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
