# P1 — prix x1 des équipements via jzs (11 octobre 2026)

## Validation croisée capture/HDV

- BestCrush actif, `jzs` S→C : `item root.f3 = 6493`, `root.f2 = 11`, `root.f1` contient 34 enregistrements.
- HDV Dofus : **Tongues Wabbits**, 34 offres, prix x1 minimum **4 899 K** ; les premiers prix visibles comprennent **4 900, 5 000, 5 197, 5 198, 5 199, 5 249, 5 250**, etc.
- Chaque ligne contient `f1=6493`, `f4=11`, `f3` numérique non corrélé directement à l'écran (signification opaque), et `f2` length-delimited contenant **quatre entiers varint successifs**.
- `f2` sur les 34 offres : `[prix x1,0,0,0]`. Plusieurs correspondances exactes observées ; en particulier la ligne #20 porte **4 899 K**. Le minimum des prix positifs sur ces lignes est bien **4 899 K**.
- Une seconde réponse `jzs` pour la même sélection, avec **zéro offre**, suit les données détaillées. Ce message ne prouve pas une chute du prix à zéro et ne doit pas persister.
- Le minimum de `f3` sur ces offres est **7 529** et ne correspond pas au prix minimum réel : le lire comme un prix est **interdit**.

## Implémentation volontairement limitée

Le code conserve le mapping partagé `market_selection_response = jzs`. Il ne prétend pas que `jzs` partage le format de l'ancien `jzn`.

1. `SemanticDecoders.TryDecodeMarketSelectionItemId` reste la source du **focus réseau**, sans mutation de prix.
2. `SemanticDecoders.TryDecodeJzsEquipmentPrices` fournit un **nouveau décodeur sémantique spécifique**, appelé sur la même réponse :
   - exige un identifiant d'objet racine strictement positif et convertible en `long` ;
   - exige une catégorie racine positive, une liste non vide, des sous-lignes décodables et alignées sur le même identifiant/catégorie ;
   - exige sur **toutes** les lignes un seul champ `f2` length-delimited avec **exactement quatre varints**, dont un **prix x1 strictement positif, <= `long.MaxValue`**, et **trois zéros** ;
   - rejette **l'observation entière** si une ligne diverge, est tronquée, utilise un autre lot ou présente un champ incohérent ;
   - garde les références de ligne `f3` sans les interpréter comme des prix.
3. `DofusNetworkMessageProcessor` traite d'abord le focus puis le nouveau décodage, avant son `return` sur `market_selection_response`. Le logger brut préexistant conserve la capture originelle.
4. `NetworkObservationWriter.PersistMarketAsync` utilise le circuit **existant** (server lease, catégorie résolue depuis SQLite, indicateurs de capture, observation x1 et notifications). Un nouveau garde-fou `requireEquipment: true` interdit la persistance des ressources/runes via cette voie non validée. L'ancien `jzn` garde toutes ses règles x1/x10/x100/x1000.
5. `protocol-map.json` conserve le nom sémantique `market_selection_response` et sa correspondance de champs, mais signale `observed-x1-equipment-2026-10-11`.

**Aucune migration SQLite, aucune nouvelle table ni capture OCR automatique.** Les changements n'affectent ni F8 ni le clic molette, hormis les observations de prix x1 autorisées lors de la sélection.

## Tests à exécuter sur Windows

- Régression du décodeur avec 34 montants représentatifs du relevé (notamment 4 899/4 900/5 000), liste vide, champs/ID/catégorie incorrects, longueur packed différente, lots supplémentaires non nuls, dépassement de `long.MaxValue`, contenu malformé.
- Régression de la **véritable route métier** `jzs` sans Npcap, avec SQLite de test : persistance x1 au minimum, notification, focus conservé, message vide sans écriture, runes/ressources exclues, capture équipement désactivée, serveur absent/lease périmé, observation invalide refusée.
- Non-régression historique `jzn` et suite complète .NET.

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Le dernier total annoncé avant ce lot était **289 tests .NET attendus**, sans preuve de sortie Windows. Ce lot ajoute **24 cas .NET** (16 cas de décodage, 8 cas de routage) : **313 attendus**, sous réserve du nombre effectif découvert et de la compilation Windows.

### Checkpoint manuel

1. Lance BestCrush avec F5 ; sélectionne explicitement le serveur ; active la **capture des prix des équipements** dans ses paramètres.
2. Dans l'HDV, consulte **Tongues Wabbits** avec des offres x1 visibles. Vérifie le montant minimum **actuel** à l'écran ; les offres peuvent avoir changé depuis 17:19 UTC.
3. Vérifie dans BestCrush que le prix automatique x1 suit le minimum du moment, qu'une notification est émise et que le focus molette reste fonctionnel.
4. Passe à un autre équipement, puis ferme l'HDV et teste F8 ; vérifie l'absence de régression.
5. Contrôle la table/historique de prix dans l'interface BestCrush. En cas d'erreur, **arrêter la capture des équipements** et conserver la dernière capture brute ; ne pas purger SQLite par défaut.

Les autres lots ou objets n'ont **pas** été validés. Il faudra des relevés d'une rune/ressource en x1 puis en x10 et x100, et caractériser séparément la nouvelle voie de concassage.

**Ne pas publier, changer la version, fusionner vers `main` ni restaurer le stash `NetworkProbe` sans décision de l'utilisateur.**
