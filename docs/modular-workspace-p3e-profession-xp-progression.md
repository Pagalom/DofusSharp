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
