# Vérifier BestCrush après une mise à jour de Dofus

Le [profil réseau](protocol-map.json) regroupe les codes, les champs et les preuves de validation des routes configurables. La checklist couvre aussi les messages que les décodeurs de stock, de FM, de métiers et de connexion lisent directement. Les codes indiqués servent de référence pour la comparaison avec le nouveau client.

Noter le build de Dofus, la version de BestCrush, la date et le serveur. Démarrer BestCrush avant la connexion au personnage. Activer temporairement **Conserver les artefacts de debug** si une comparaison des messages est nécessaire. Garder les captures et identifiants de session en local et ajouter seulement des cas synthétiques au dépôt.

## Faire la passe en jeu

| État | Action | Résultat à vérifier dans BestCrush | Route ou code de référence |
| --- | --- | --- | --- |
| ☐ | Connecter un personnage, changer de serveur ou de personnage, puis revenir. | La lecture devient active, le bon serveur apparaît et chaque fenêtre conserve ses stocks et niveaux de métier. | Connexion, `ksc` / `ksb`, [attribution serveur](../BestCrush/Services/WindowServerAttributionState.cs) |
| ☐ | Ouvrir la banque et déplacer une ressource dans les deux sens, en quantité partielle puis entière. | Inventaire et banque se synchronisent séparément. La pile prend la quantité du jeu et un retrait complet retire sa ligne. Vérifier aussi le jet d’un équipement en stock. | `irl` / `irp`, `iqx` / `iqz`, `irx`, `iqm` / `itf`, [décodeur de stock](Protocol/LiveStockProtocol.cs) |
| ☐ | Ouvrir les offres d’un équipement, d’une ressource, d’une rune et d’une pierre d’âme pleine. | Dans **Données**, l’équipement a son prix ×1. Les lots de ressource et de rune correspondent aux tailles présentes en jeu. Le prix d’une pierre pleine est lié au bon boss. Le focus pointe sur le bon équipement. | `market_selection_response` (`jzs`), `item_detail_current` (`kch`), [pierres d’âme](Protocol/SoulMarketDecoder.cs) |
| ☐ | Ouvrir le **Cours du marché** sur ces objets. | Prix, quantités vendues et historique 24 h / 30 j correspondent à la bonne référence, au bon serveur et, pour la pierre pleine, au bon boss. | `global_market_history_request` (`iqp`) et `global_market_history_response` (`ire`) |
| ☐ | Acheter un équipement à l’unité et un lot de ressource. Relever le prix payé et la taille du lot. | L’inventaire se met à jour. **Objets possédés** retrouve le prix payé sur l’exemplaire reçu. Les offres de la ressource évoluent lorsque le jeu communique leur nouvelle disponibilité. | `purchase_*_current` (`kbf` / `kas` / `jzx`), inventaire `iqx` |
| ☐ | Mettre en vente un seul lot et relever le nombre d’unités et les minima HDV avant et après. | La quantité mise en vente augmente de la taille du lot et le quota baisse une seule fois. Pour une ressource/rune, le rafraîchissement serveur actualise les quatre lots du marché. Le prix demandé ne devient pas directement un prix du marché. | `market_listing_request_current` (`jyq`), `market_listing_created_current` (`jxt`), `purchase_offer_current` (`kas`), [preuve et limites](../docs/market-seller-refresh-and-history-availability.md) |
| ☐ | Fabriquer un équipement et consulter les métiers du personnage. | Les ingrédients diminuent et l’équipement apparaît. Vérifier l’origine **Craft**, le coût estimé au prix des lots entiers les moins chers et les niveaux du bon personnage. | `craft_output` (`iuq`, route historique à confirmer), stock `iqx` / `irx` / `iqm`, métiers `ipz` |
| ☐ | Déposer un équipement à l’atelier de FM, passer des runes et fermer l’atelier. | Jet et puits se mettent à jour. Chaque rune consommée compte une fois, même en cas d’échec. L’historique affiche les effets et les coûts. | `jys`, `jxu`, `kch`, `kco`, `irx` / `iqm`, `kbr`, [décodeur d’atelier](../BestCrush/Services/LiveForgemagingProtocol.cs) |
| ☐ | Sur des objets prévus pour la FM, contrôler une résistance fixe et une résistance en %, puis des dommages mêlée/distance/armes/sorts en % si disponibles. | Les valeurs et signes sont conservés, sans division par 100. La rune est reconnue et sa consommation compte une seule fois. Les résistances élémentaires appartiennent à la catégorie d’effet 1 ; les dommages en % sont en catégorie 0. Le cache des effets doit être `fm-effects-v2` ou ultérieur. Une interruption écrit sa cause technique sans identifiant d’objet dans les journaux. | [Catalogue des effets](../BestCrush/Services/ActivityCatalogService.Forgemaging.cs), [tests de suivi](../Tests.BestCrush/ForgemagingResistanceTrackingTest.cs) |
| ☐ | Reconnecter le même personnage, puis concasser l’équipement de test. Contrôler aussi, si possible, un brisage sans rune et un brisage avec focus. | Avant le brisage, retrouver le prix d’obtention et la FM du même exemplaire. Après le brisage, comparer coefficient, focus et quantités de runes avec le jeu. Vérifier les écarts et la marge dans l’historique. | `item_detail_current` (`kch`), `crush_result_current` (`kag`), [rapprochement à la reconnexion](../BestCrush/Services/ForgemagingSessionStore.cs) |
| ☐ | Ouvrir Élevage et déplacer une seule monture de l’enclos vers l’étable, puis la remettre dans l’enclos. | Le relevé change de nombre de montures et retrouve l’exemplaire à son retour. Vérifier les jauges actives, le carburant, la sérénité, l’XP et la maturité. | `breeding_paddock_snapshot_current` (`hrf`), [décodeur d’enclos](Protocol/BreedingPaddockDecoder.cs) |
| ☐ | Relancer BestCrush avec une monture à l’étable, rouvrir Élevage, puis la remettre dans l’enclos. Consulter deux enclos déverrouillés si possible. | La monture garde le même numéro entre l’étable et l’enclos. Chaque enclos consulté garde son propre relevé. | Stable `hqv`, transferts `hqy` / `hqk`, sélection `hot` / `hpa` |
| ☐ | Consulter les offres d’un familier et d’un montilier à plusieurs niveaux, y compris une variante légendaire. | Le module Familiers / XP conserve chaque niveau et prix ; les pouvoirs spéciaux restent séparés. Le relevé suivant remplace les anciennes offres sans confondre un message vide de réinitialisation avec un marché vide. | `jzs`, effets 2821 / 2819, [décodeur des familiers](Protocol/PetMarketDecoder.cs) |
| ☐ | Sur un familier non maximal, prévisualiser un puis dix aliments identiques sans valider. Ensuite, uniquement si un repas est prévu, en consommer un. | L’XP et le niveau annoncés correspondent au jeu. Après validation, la quantité de nourriture baisse, l’XP de l’exemplaire augmente et le plan est recalculé. Contrôler aussi le familier rangé en banque et l’accès aux recettes selon les métiers. | Stocks `irl` / `irp` / `iqx` / `iqy` (objet modifié, enveloppe f1/f1) / `irx`, [règles et limites](../docs/pet-xp-planning.md) |

Marquer chaque ligne **OK**, **À corriger** ou **Non vérifié**. Pour un écart, relever l’action, l’heure, la valeur du jeu et celle de BestCrush. Corriger ensemble les routes affectées par le même changement.

## Mettre à jour et vérifier le correctif

1. Pour une route du [profil](protocol-map.json), modifier son code et les champs reçus dans `field_mappings`. Les clés sont les champs attendus par le décodeur et les valeurs `wire` sont ceux reçus du client. Renseigner `verified_routes` avec le build et la date de l’observation.
2. Si le format, le déroulement ou une route lue directement a changé, adapter le décodeur concerné et ajouter un cas synthétique qui reproduit l’écart. La [référence du profil](../docs/network-protocol-single-source-of-truth.md) décrit le format des champs imbriqués.
3. Depuis la racine du dépôt, exécuter les tests, fermer BestCrush, compiler puis le relancer pour charger le profil.

```powershell
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
```

Les tests rejouent des cas connus. La passe en jeu valide les messages que le nouveau client envoie effectivement.
