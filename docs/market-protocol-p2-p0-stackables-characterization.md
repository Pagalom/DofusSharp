# P2.0 — caractérisation passive des prix des runes et ressources

**Branche :** `refactor/codebase-cleanup` — **Statut :** collecte de preuves, aucune modification production.

## Point de départ confirmé (P1)

- La capture réseau et le focus `jzs` ont été validés.
- Les offres de l'équipement **Tongues Wabbits** (ID 6493, groupe 11) portent un `f2` de quatre entiers non signés `[prix x1, 0, 0, 0]`.
- Sur cet équipement, le minimum du premier entier est le prix réel **4 899 K** affiché dans l'HDV (11 octobre 2026).
- Les 313 tests .NET ont été exécutés et validés sur Windows ; l'utilisateur confirme la persistance correcte des prix x1 des équipements en jeu.
- **Cela ne prouve rien sur les lots des runes ou des ressources**. Les positions 2/3/4 ne sont pas encore validées comme x10/x100/x1000. L'identifiant numérique du message `jzs` doit être confirmé sur ces catégories ; d'autres messages pourraient intervenir.

## Nouveau diagnostic (lecture seule)

L'outil existant `scripts/inspect_jzs_market.py` reçoit une option facultative `--lot-summary` :

1. Pour chaque réponse `jzs`, décoder le champ racine `f1` répété ;
2. pour chaque offre, vérifier l'identité d'objet `f1` et de groupe `f4`, les comparer à `root.f3` et `root.f2` ;
3. lire `offer.f2` comme **quatre entiers varint** dans l'ordre brut, sans leur assigner de quantité ;
4. si **toutes les offres** sont conformes, afficher le plus petit entier strictement positif et le nombre de valeurs non nulles par **position 1/2/3/4** ;
5. si une offre est incohérente, tronquée, d'une autre catégorie ou d'un nombre de positions différent, ne proposer **aucun minimum partiel** ;
6. ignorer les messages de sélection vides pour le calcul des prix.

Les minima affichés sont des **candidats**, pas des valeurs prêtes à être enregistrées dans SQLite. Un zéro signifie absence de candidat positif, et non un prix gratuit ou la confirmation d'un lot indisponible. Le script n'enregistre, n'exporte et n'envoie ni octets réseau ni Base64. Aucun fichier brut de session n'est committé.

## Checkpoint local du diagnostic

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
py -3 -m unittest discover -s scripts -p test_inspect_jzs_market.py -v
```

**17 tests Python attendus** (11 existants + 6 nouveaux), à confirmer sur Windows. Le checkpoint P2.0 ne nécessite **aucun nouveau build .NET** car aucun fichier .NET n'a été modifié.

## Procédure en jeu à transmettre

1. Lancer BestCrush avec F5, **sélectionner explicitement le serveur**, vérifier que Npcap tourne et que « Conserver les artefacts de debug » est actif.
2. Dans l'HDV, ouvrir **une rune** disposant d'offres visibles. Noter son nom, les prix minimums réellement affichés par quantité (x1, x10, x100 et éventuellement x1000), et joindre une capture de l'interface de l'HDV, **pas seulement l'infobulle avec le prix moyen**.
3. Dans le même parcours, ouvrir **une ressource** avec des offres visibles et noter les mêmes informations. Idéalement sélectionner deux objets différents pour provoquer de nouvelles réponses réseau.
4. Exécuter **après les deux consultations** :

```powershell
py -3 .\scripts\inspect_jzs_market.py --last 12 --lot-summary --rows 3
```

5. Envoyer la sortie **textuelle** de ce diagnostic et les captures/prix HDV. Pour approfondir un ID trouvé :

```powershell
py -3 .\scripts\inspect_jzs_market.py --item-id ID --last 2 --lot-summary --inspect-f2 --rows 12
```

Remplacer `ID` par l'identifiant numérique affiché par le diagnostic. Le script lit le `wire.jsonl` le plus récent, y compris après avoir quitté le jeu si le fichier existe encore ; si aucune donnée n'apparaît, vérifier les dates des sessions et l'activité de BestCrush avant d'en déduire un changement de protocole.

## Critères de sortie P2.0 (avant P2.1)

- Comparaison réussie de **plusieurs prix affichés** avec les champs candidats sur au moins une rune et une ressource.
- Identification réelle des quantités de lots et des cas non présents ; ne pas déduire qu'une position vaut x10/x100/x1000 d'après l'ancien `jzn`.
- Prise en charge des réponses vides, des minima positifs, des lignes aberrantes et des conversions (ulong → long).
- Seulement ensuite : modifier le décodeur de production et les tests de **routage réel** pour les runes/ressources, réutiliser `NetworkObservationWriter` et `MarketPriceService` sous lease serveur actif, et mettre à jour `protocol-map.json` avec l'état de vérification effectivement établi.

## Hors périmètre

Aucun changement de `BestCrush.Network`, du routage métier, des paramètres F8, du schéma SQLite, de `protocol-map.json`, de la version, de la release ou de `main` dans cette phase. Le chantier de robustesse de F8 est explicitement reporté conformément à la décision de l'utilisateur.
