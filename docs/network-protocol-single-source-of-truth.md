# Protocole réseau BestCrush — source unique de vérité

## Fichier à maintenir après une mise à jour de Dofus

**`BestCrush.Network/protocol-map.json`**

Il est partagé et copié tel quel dans les sorties de **BestCrush**, **BestCrush.NetworkProbe** et **Tests.BestCrush**. Il porte trois responsabilités :

1. Les **codes des messages** (ex. `market_selection_response = jzs`, `price_list = jzn`, `crush_result = kci`).
2. La **correspondance des champs Protobuf** (section `field_mappings`) pour les **19 routes** de capture actuellement connues.
3. L'**état de vérification** de chaque route (section `verified_routes`, même ensemble de 19 noms).

Ce fichier utilise `schema_version: 1`. Une version de schéma non supportée bloque le chargement.

### Convention `field_mappings`

Les noms des propriétés numériques correspondent à la numérotation *canonique* attendue par les décodeurs BestCrush. Leur valeur numérique correspond au champ *effectivement reçu de Dofus*.

```json
{
  "field_mappings": {
    "market_selection_response": {
      "2": 7,
      "3": 8
    }
  }
}
```

Dans cet **exemple fictif**, la version de Dofus envoie sa catégorie dans le champ **7** et l'ID équipement dans le champ **8**. Les décodeurs continuent à recevoir respectivement les champs canoniques **2** et **3**. Le vrai profil livré conserve `2→2` et `3→3`, tels qu'observés le 11/10/2026.

Pour les champs imbriqués ou répétés :

```json
{
  "1": {
    "wire": 7,
    "fields": {
      "1": {
        "wire": 8,
        "fields": {"1": 7, "3": 11}
      },
      "2": 9,
      "3": 10
    }
  }
}
```

Cela permet de remapper une liste de résultats de concassage, leurs runes et coefficients sans modifier le décodeur du résultat. Les champs inconnus ne sont pas interprétés et restent préservés dans la forme transmise au décodeur.

Les correspondances sont **par route sémantique**, pas par clé brute. Si l'identifiant `kci` change lors de la prochaine mise à jour, le même bloc `crush_result` continue à s'appliquer après mise à jour de la valeur `crush_result` en tête du fichier.

### Intégration — pas d'adaptations dispersées

- `DofusNetworkCaptureService` capture et assemble les paquets, et conserve les **messages bruts** pour le debug.
- `DofusNetworkMessageProcessor` appelle **une seule fois** `ProtocolMap.NormalizeBody(message.Key, message.Body)` avant de transmettre les données aux décodeurs existants.
- `BestCrush.NetworkProbe` appelle le **même normaliseur** avant ses décodeurs métier. Ses traces de diagnostic conservent les octets originaux.
- `SemanticDecoders` continue à travailler exclusivement avec les numéros *canoniques*, pour les prix, les équipements, les UID, les achats, les ventes, les transformations et le concassage.
- `NetworkObservationWriter`, la base SQLite, le serveur sélectionné, le clic molette, les overlays et les règles de prix/coefficient ne sont pas modifiés par la normalisation.

**Si le profil déclare une renumérotation mais que la structure attendue est mal formée**, la normalisation refuse le message (`[]`) au lieu d'alimenter la base avec une interprétation douteuse. Les champs explicitement imbriqués doivent réellement être des messages Protobuf. Une collision de codes `wire` dans un même objet de mapping est rejetée à la lecture du JSON.

### Couverture métier — à reprendre à chaque mise à jour

| Route du JSON | Fonctions concernées |
| --- | --- |
| `price_list` | prix du marché, lots, notification SQLite |
| `market_selection_response` | dernier équipement réseau et clic molette ; **pas de prix** |
| `crush_result` | résultats du concassage, runes, coefficients |
| `item_detail`, `inventory_add`, `craft_output` | UID → ID équipement, stats, corrélation concassage |
| `purchase_request`, `purchase_offer`, `purchase_receipt` | achats et rafraîchissement de prix |
| `market_listing_request`, `market_listing_created` | dépôt/vente HDV |
| `inventory_quantity`, `inventory_remove` | inventaire (NetworkProbe) |
| `workshop_slot_put`, `craft_prepare` | atelier/craft (NetworkProbe) |
| `smithmagic_request`, `smithmagic_batch_request`, `smithmagic_result`, `smithmagic_aux` | forgemagie (NetworkProbe et/ou BestCrush) |

`craft_prepare` ne lit pas de champ métier aujourd'hui ; son mapping est intentionnellement `{}`. Les messages purement diagnostiques du tableau `diagnostic_messages` n'ont pas de parseur métier et restent bruts.

Les valeurs de `verified_routes` commencent par :
- `observed-...` : comportement corrélé à une capture réelle datée ;
- `legacy-...` : uniquement un comportement historique, **non confirmé sur le client actuel**.

**Situation au 11/10/2026** : seule `market_selection_response` / `jzs` est confirmée sur le protocole récent ; les autres routes conservent des structures historiques `3.6.11.15` et doivent faire l'objet d'une caractérisation. Le présent refactor ne prétend PAS avoir restauré les prix ou le concassage sur les nouveaux messages.

### Procédure lors de la prochaine mise à jour du jeu

1. Activer temporairement `Conserver les artefacts de debug`. Capturer des événements connus et comparer les **clés, directions, tailles et signatures** aux états attendus, sans committer les paquets privés.
2. Identifier la ou les routes affectées : sélection HDV, prix d'un équipement et d'une ressource, achat, inventaire, concassage, etc.
3. **Modifier uniquement `BestCrush.Network/protocol-map.json`** : code du message et numérotation réelle (valeurs `wire` de `field_mappings`). Mettre à jour l'état `verified_routes` pour chaque événement effectivement caractérisé.
4. Ajouter/mettre à jour des **fixtures synthétiques de non-régression** ; `ProtocolWireNormalizerTest` vérifie que toutes les routes figurent dans le fichier commun. Ne jamais ajouter des données de session ou identifiants utilisateurs aux fixtures.
5. Exécuter build/test Windows et F5 ; contrôler focus, prix/lot, coefficients, runes, server lease, notifications et observations SQLite. La configuration est chargée au démarrage : **redémarrer BestCrush** après la modification.

### Limite explicite — tout changement ne peut pas être réduit à du JSON

Cette abstraction gère les **identifiants de messages et la renumérotation des champs**, y compris les objets imbriqués. Elle **ne peut pas deviner la nouvelle signification** d'un champ, remplacer automatiquement un float par un prix en kamas, déchiffrer un nouveau transport, ni reconstruire une nouvelle logique d'agrégation d'offres. Pour ce type de rupture sémantique, il faudra investiguer et adapter le parseur concerné, avec tests, puis documenter la nouvelle correspondance ici.

Le but est **zéro oubli de couverture et zéro renumérotation dispersée**, pas de garantir un protocole auto-adaptatif sans observation humaine.

## Checkpoint Windows

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Les **264 tests précédents** étaient attendus mais pas encore tous certifiés sur Windows. Ce lot ajoute **11 tests**, soit **275 attendus** si toutes les étapes antérieures sont passées.

Checkpoint manuel : démarrage, sélection de serveur, focus molette HDV `jzs`, F8, capture réseau/brut, prix historiques, rendu overlays, résultats de concassage (si possible). Ne pas toucher à `main`, aux releases, au schéma SQLite ou à la version applicative.
