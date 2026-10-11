# P3.0 — caractérisation passive des résultats de concassage (11 octobre 2026)

**Branche :** `refactor/codebase-cleanup` — **statut : collecte de preuves seulement**.

## Contexte constaté dans le dépôt

- `BestCrush.Network/protocol-map.json` déclare toujours `crush_result = kci`, avec un état **`legacy-3.6.11.15`** : cette clé et ses champs ne sont **pas** validés pour le client actuel.
- L'ancien `SemanticDecoders.TryDecodeCrush` attend des lignes répétées dans le champ racine 1 : par ligne, `f1` contient les runes obtenues, `f2` une fraction `float32` de coefficient, `f3` l'UID d'instance de l'objet détruit, et `f4` une autre valeur `float32` de signification inconnue. Dans chaque rune, `f1` est l'ID de rune et `f3` la quantité. **Tout cela est historique et non confirmé aujourd'hui.**
- `NetworkDebugWriter` produit un `wire.jsonl` par session (`%LOCALAPPDATA%\BestCrush\DebugCaptures\Network\session-*\wire.jsonl`) si `Conserver les artefacts de debug` est activé et qu'un serveur a été sélectionné. Le fichier contient des corps réseau Base64 **bruts** : **ne jamais les partager ou les committer**.
- Les prix HDV issus de `jzs` sont opérationnels (équipements x1, runes/ressources x1/x10/x100/x1000), et l'utilisateur a validé P2.1. **P3.0 ne doit modifier aucun de ces comportements.**

## Diagnostic ajouté

`scripts/inspect_crush_network.py` utilise uniquement Python standard et la fonction de lecture Protobuf locale déjà présente dans `scripts/inspect_jzs_market.py`. **Aucun Npcap supplémentaire** et **aucune modification du produit**.

- Sélectionne, par défaut, le `wire.jsonl` le plus récemment modifié. `--file` permet de choisir une autre session.
- Lit le fichier deux fois de manière séquentielle ; la fenêtre `--last-seconds N` est ancrée au **dernier horodatage des paquets capturés**, et non à l'horloge du PC au moment de l'analyse.
- Résume, pour les deux directions C→S/S→C, les **clés réseau**, le nombre de messages et les longueurs minimum/maximum, sans imprimer les données brutes.
- Produit une chronologie par tranches `--bin-seconds 5` pour corréler un clic de concassage à une apparition de codes.
- `--key CODE` (répétable) permet d'inspecter un nombre borné de messages de ce code avec `--samples N` et `--depth N` : uniquement tags Protobuf, entiers, **hypothèses** de floats `float32` ; aucun champ chaîne, hexadécimal ou corps Base64 n'est affiché.
- Tous les identifiants de messages/équipements, valeurs numériques et floats sont à ce stade des **pistes**, jamais un résultat métier validé.

## Procédure Windows P3.0

Commencer par récupérer le commit puis vérifier les tests **sans Dofus** :

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
py -3 -m unittest discover -s scripts -p 'test_inspect_*.py' -v
```

**27 tests Python attendus** (= 17 P2.0 + 10 P3.0), à confirmer Windows. **Pas de rebuild .NET nécessaire à P3.0**, puisqu'aucun fichier du code production n'est changé.

1. Démarrer BestCrush via F5. Sélectionner explicitement le serveur. Vérifier que Npcap fonctionne et que **Conserver les artefacts de debug** est activé. Dans Dofus, ouvrir l'interface de concassage. Si un résultat de concassage ne peut pas être annulé, choisir un équipement de **faible valeur**.
2. Facultatif, mais utile pour comparaison : avant de concasser, exécuter la commande de diagnostic suivante pour établir les codes présents en situation normale :

   ```powershell
   py -3 .\scripts\inspect_crush_network.py --last-seconds 25
   ```

3. Effectuer **un seul concassage** (idéalement un équipement connu, quantité x1). Noter l'heure approximative, le **nom de l'équipement détruit**, le **coefficient affiché** et les **noms/quantités des runes obtenues**, ou envoyer une capture de l'écran de résultat. Distinguer ces valeurs observées en jeu des éventuels nombres du paquet.
4. Immédiatement après :

   ```powershell
   py -3 .\scripts\inspect_crush_network.py --last-seconds 45
   ```

   Envoyer **la sortie de cette commande**, et si possible celle de l'étape 2. Ne **pas** envoyer `wire.jsonl` : il pourrait contenir d'autres données réseau personnelles.
5. Après identification de deux ou trois codes suspects et seulement **sur instruction**, afficher leur structure locale (remplacer `CODE1` / `CODE2` par les codes réellement présents) :

   ```powershell
   py -3 .\scripts\inspect_crush_network.py --last-seconds 45 --key CODE1 --key CODE2 --samples 2 --depth 3
   ```

   Ces champs sont anonymes d'un point de vue sémantique ; on doit comparer **les nombres réellement visibles en jeu**, notamment le coefficient (ancien format float fraction ou autre), les quantités de runes et l'ID d'équipement/UID.
6. Si aucun paquet attendu n'apparaît, confirmer que BestCrush enregistrait les paquets dans une session active, et vérifier si un autre `wire.jsonl` est sélectionné. Ne pas inventer d'opcode ni écrire en base.

## Critères de sortie P3.0

- Identifier le(s) **opcode(s) réels** du concassage sur la version actuelle, avec direction(s) et ordre des messages.
- Identifier la structure répétée des résultats, la **conversion exacte** du coefficient, les runes obtenues, leurs IDs et quantités, la manière de corréler l'équipement détruit (UID/ID) et le cas multi-objets.
- Comparer au moins **deux résultats de concassage réels**, si le premier reste ambigu.
- Documenter le mécanisme dans `protocol-map.json` **seulement après** validation observée. Ne pas transformer une coïncidence numérique en vérité de protocole.
- P3.1 pourra ensuite reprendre l'ancien circuit `DofusNetworkMessageProcessor → NetworkObservationWriter.PersistCrushAsync → CrushSessionService.ApplyNetworkCrushAsync` sous lease serveur actif, sans modification des overlays.

## Hors périmètre

Aucune modification de `DofusNetworkCaptureService`, `NetworkObservationWriter`, des décodeurs de production, `protocol-map.json`, de SQLite, des coefficients, de F8, du focus réseau, des overlays, des versions, releases, `main` ou du stash local du NetworkProbe.
