# S4b — Bornage des caches réseau en mémoire

Base : `ad24334` sur `refactor/codebase-cleanup`. Le correctif F8 a été validé fonctionnellement par l'utilisateur ; le checkpoint Windows complet S4a/F8 reste à confirmer séparément.

## Constat

Deux caches vivaient tant que le service de capture réseau restait actif :

- `DofusNetworkCaptureService._streams` : un `TcpReassembler` par 5-tuple et par interface de capture. Chaque nouvelle connexion augmentait le dictionnaire sans éviction hors arrêt et changement de serveur.
- `DofusNetworkMessageProcessor._itemDetails` : les UID et statistiques d'équipements reçus via les messages réseau étaient mémorisés sans limite jusqu'au changement de génération.

Ces caches sont indépendants de la base de données SQLite et ne doivent pas modifier les prix ou les coefficients déjà enregistrés.

## Modification S4b

### Flux TCP

`NetworkFlowCache<TKey,TReader>` remplace le dictionnaire permanent des assembleurs.

- Maximum **256 flux simultanément mémorisés** par service.
- Sur le retour d'un flux récemment vu, conserver son **même assembleur** (séquence TCP et framing intacts).
- Pour un flux nouveau, retirer les entrées d'une autre génération ou inactives depuis **une heure** ; au plafond, éliminer le flux le moins récemment utilisé.
- Un flux expiré ou évincé recrée un assembleur lors du prochain paquet : ses éventuelles données de framing incomplètes sont perdues. Aucun flux n'est artificiellement sélectionné comme serveur ; la sélection BestCrush demeure la source d'autorité.
- Synchronisation inchangée sous `_streamLock` ; `Stop()` et le changement de serveur vident toujours le cache.

### Cache des UID d'équipement

`RecentItemDetailsCache` remplace le dictionnaire UID sans limite :

- Maximum **16 384 UID** ; mise à jour/lecture d'un UID le rend récent.
- Au plafond, évincer le plus ancien UID inutilisé, sans toucher aux observations persistées.
- Les quatre types de messages alimentant le cache (item detail, inventory add, craft output, smithmagic result) sont conservés.
- Le chemin `PersistCrushAsync` consomme les valeurs conservées dans le dictionnaire en lecture seule ; les UID inconnus restent ignorés comme avant.
- La purge sur sélection de serveur ou reprise de capture (S4a) reste inchangée.

## Tests sans Npcap

Huit nouveaux tests synthétiques :

1. Conservation de l'assembleur d'un flux actif.
2. Recréation après expiration.
3. Éviction LRU au plafond de flux.
4. Réinitialisation sur changement de lease/clear.
5. Éviction d'un UID ancien.
6. Protection d'un UID récemment consulté.
7. Remplacement d'un UID mis à jour.
8. Nettoyage de toutes les corrélations sur `Clear`.

## Limites explicitement hors S4b

- La **file `Channel<DofusWireMessage>` demeure non bornée** : fixer sa taille risquerait de perdre des mises à jour et des résultats de concassage si le consommateur prend du retard.
- Les buffers internes d'un assembleur TCP (`_pending` et `FrameBuffer`) et les éventuels paquets capturés sur plusieurs cartes réseau ne sont pas encore bornés/dédupliqués.
- Il n'y a pas d'instrumentation du volume mémoire ni de télémétrie ajoutée.
- En cas de plus de 16 384 UID ou 256 flux distincts entre deux réutilisations, une corrélation ancienne peut manquer ; c'est le compromis explicite qui permet l'éviction.

## Checkpoint Windows demandé

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Attendu si les 245 tests de la base S4a/F8 passent : **253 tests réussis**.

F5 : lancement, capture passive des prix HDV, concassage, F7/F8, molette dernier équipement réseau, sélection de serveur. Éventuel arrêt/reprise de capture dans le même processus si accessible ; la simulation des tests ne remplace pas Npcap sur Windows.

Ne pas merger `main`, publier de release, changer la version, les règles métier ou le schéma de données.
