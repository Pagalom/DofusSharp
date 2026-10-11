# P0 — caractérisation des prix HDV du protocole courant (lecture seule)

**Objectif :** identifier les champs donnant les prix effectivement affichés en HDV, puis seulement après preuve mettre à jour le `protocol-map.json` partagé pour restaurer l'enregistrement automatique des prix dans SQLite.

## État initial

Les traces locales du 11 octobre 2026 ont confirmé :

- `kcy` (C→S) émet l'ID d'équipement demandé dans le champ 1 ;
- `jzs` (S→C) reprend cet ID dans le **champ racine 3** (exemples 12851, 1356) et un entier supplémentaire dans le champ racine 2 (exemples 9, 3) ;
- le champ racine 1 de `jzs` contient **plusieurs enregistrements imbriqués**, chacun avec notamment `f1`, `f2` opaque, `f3` et `f4` ;
- les valeurs `f3` des offres sont **compatibles avec l'hypothèse** d'un prix en kamas (ex. 28116, 66305, 108414), mais **cette hypothèse n'est pas encore validée** par comparaison avec les prix de vente réellement affichés en jeu ;
- le champ racine 2 et le champ imbriqué 4 semblent corrélés, mais leur sens métier exact (type, lot, catégorie...) est **inconnu**.

Attention : le prix moyen (« PRIX MOYEN ») visible sur une simple infobulle n'est pas le **minimum des offres HDV**. Ne pas valider un prix de vente contre ce seul indicateur.

Le décodeur historique `jzn` traite l'ancien format `f1 itemId; f2 offres; f6 lot`. Le nouveau `jzs` est déjà reconnu par `market_selection_response` pour le clic molette, **sans écriture de prix**. Il ne faut surtout pas rebaptiser `jzs` en `price_list` ni traiter ses sous-champs comme l'ancien `jzn`.

## Outil de diagnostic reproductible

`scripts/inspect_jzs_market.py`, Python standard 3.13, **en lecture seule** :

- localise le `wire.jsonl` le plus récent de `%LOCALAPPDATA%\BestCrush\DebugCaptures\Network\session-*` ; accepte aussi `--file` ;
- affiche les **ID de sélection** `jzs`, le nombre d'offres et, pour les entrées imbriquées, `f1`, `f3`, `f4`, ainsi que les longueurs des champs `f2` — jamais leur contenu ;
- **ne sauvegarde pas**, n'exporte pas et ne publie aucune trame, Base64, octets opaques ou identifiant de session ;
- peut lire les derniers messages (`--last 8`) ou attendre de nouveaux messages (`--watch`, arrêt Ctrl+C) ;
- présente `minimum f3?` avec la mention « NON VALIDE comme prix » pour éviter toute interprétation prématurée.

### Tests locaux du script (sans données utilisateur)

```powershell
py -3 -m unittest discover -s scripts -p test_inspect_jzs_market.py -v
```

### Relevé à effectuer sur Dofus

1. BestCrush lancé, **serveur explicitement sélectionné**, « Conserver les artefacts de debug » actif, et interface Dofus ouverte.
2. Démarrer le diagnostic en attendant les prochains événements :

```powershell
py -3 .\scripts\inspect_jzs_market.py --watch
```

3. Dans l'HDV en jeu, consulter successivement **un équipement avec des offres visibles**, une **rune ou ressource en lot x1**, puis, si disponible, le **même produit en x10 ou x100**. Noter le **prix de vente minimum affiché** et le nom de l'objet, pas seulement son prix moyen.
4. Après chaque consultation, comparer `jzs item f3`, `row f3?`, `row f4`, `root f2` avec l'écran. Arrêter avec Ctrl+C et transmettre au développeur **uniquement les lignes textuelles du diagnostic et les prix minimums visibles**. Ne jamais fournir le fichier `wire.jsonl` complet sans l'avoir explicitement choisi : il inclut des paquets de session.
5. Si le script n'affiche rien, sélectionner **un nouvel objet différent**, et vérifier que `wire.jsonl` se met à jour et que l'option debug est activée. Il n'y a pas de promesse de prix tant que la structure n'est pas prouvée.

Pour examiner une session existante sans attendre de nouveaux événements :

```powershell
py -3 .\scripts\inspect_jzs_market.py --last 8
```

## À faire après confirmation (P1, NON réalisé dans P0)

1. Caractériser `jzs` avec données **constatables** : champ réel prix, corrélation item ID, éventuel ID d'offre, lots x1/x10/x100/x1000, absence d'offre, grands nombres, mode vente/achat, sens C→S/S→C.
2. Adapter le **fichier mutualisé** `BestCrush.Network/protocol-map.json` et son état de vérification (distinguer `jzs` de `jzn`) ; n'ajouter une voie de décodage métier spécifique que si les données sont sémantiquement différentes, comme la présence d'un prix unitaire au lieu d'une échelle de lots.
3. Écrire les tests de décodeur et de **routage réel** (server lease, dédoublonnage, minimum strict positif par lots, conversions, notification et persistance).
4. Brancher seulement ensuite l'observation fiable sur `NetworkObservationWriter.PersistMarketAsync` et `MarketPriceService`, sans inventer de valeur ni polluer la base SQLite.
5. Effectuer checkpoint Windows (build, suite .NET, F5) et vérification des prix réellement persistés.

## Hors périmètre

P0 ne modifie **aucun service produit**, aucune clé `protocol-map.json`, aucun prix, aucune base de données, aucun raccourci F8, aucune version ni branche `main`. Il ne prétend pas corriger les captures de concassage, qui nécessitent une étape ultérieure dédiée.

## Observation corroborée en jeu — Tongues Wabbits (11/10/2026)

Capture HDV fournie avec la sélection `jzs item f3=[6493]` (34 offres) :

- **Nom de l'objet :** Tongues Wabbits, bottes niveau 60 ;
- **premier prix réellement affiché pour un lot x1 : 4 899 kamas** ;
- autres prix visibles : 4 900, 5 000, 5 197, 5 197, 5 198, 5 199, etc. ;
- minimum du champ **`offer.f3` : 7 529**, clairement **différent** de 4 899 ;
- `offer.f2` est un `bytes` de 5 ou 6 octets dans les entrées observées.

**Conclusion confirmée :** il est faux d'enregistrer directement `offer.f3` comme prix affiché minimum. Il pourrait s'agir d'un identifiant d'offre, mais sa signification n'est pas établie. La présence du prix et de la quantité dans les 5–6 octets de `offer.f2` reste une **hypothèse**.

### Lecture numérique facultative du champ `f2`

La version suivante de `scripts/inspect_jzs_market.py` accepte :

```powershell
py -3 .\scripts\inspect_jzs_market.py --item-id 6493 --inspect-f2 --last 2 --rows 16
```

- `--item-id` filtre les messages du journal local **avant de retenir les deux derniers**.
- `--inspect-f2` affiche, pour chaque entrée, une **hypothèse** de champs Protobuf numériques imbriqués, ou des varints non structurés si la forme Protobuf échoue.
- Les octets bruts, les contenus Base64 et les éventuels sous-champs binaires ne sont **jamais affichés ni exportés**.
- Les libellés `nested?` / `raw_varints?` restent **exploratoires** : des numéros égaux à 4 899 (ou à un autre prix de l'écran) ne suffiraient pas, seuls, à confirmer le sens métier. Il faudra comparer plusieurs lignes et au moins deux objets, puis vérifier les lots.

Tests synthétiques : `py -3 -m unittest discover -s scripts -p test_inspect_jzs_market.py -v`. Aucun changement dans le décodage production de `jzs`, le fichier mutualisé, F8 ou SQLite.
