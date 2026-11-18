# Prix actualisés pendant les mises en vente et disponibilité de l’historique

Observation locale du 18/11/2026, client Dofus 3.7.14.18. Les octets et identifiants de session restent dans les diagnostics locaux. Les tests utilisent des objets synthétiques.

## Rafraîchissement public `kas`

Le message serveur déclaré par `purchase_offer_current` n’est pas réservé aux achats. Il est aussi envoyé après la confirmation d’un dépôt HDV `jyq` / `jxt`.

Structure observée sur 401 rafraîchissements concernant 115 références :

| Champ | Contenu |
| --- | --- |
| 1, packed varints | Minima courants des lots ×1, ×10, ×100 et ×1000, zéro si absent |
| 2, varint | ID de l’objet |
| 3, varint | Catégorie réseau |
| 4, varint | Référence d’offre |

Exemple de séquence observée : prix ×1 à 347 K dans la sélection initiale, dépôt à 346 K confirmé, puis prix `346 / 3970 / 44882 / 349980 K`, avec les autres tailles identiques à celles de la sélection publique. Les 383 premiers dépôts corrélés du relevé ont été effectués au minimum.

Contrôle complémentaire à 00:42:13 UTC : l’utilisateur a déposé 10 Noix de Pécan (392) à **1301 K**, au-dessus du minimum affiché de **1300 K**. `jyq` puis `jxt` indiquent bien 10 unités à 1301 K ; `kas` renvoie **114 / 1300 / 9496 / 139998 K**. La case ×10 conserve donc le minimum existant de 1300 K. Un test de routage synthétique rejoue cette distinction. Le prix demandé dans `jyq` et le prix confirmé dans `jxt` ne deviennent jamais directement des observations du marché.

`CurrentPurchaseDecoder.DecodeStackableMarket` valide cette structure et `DofusNetworkMessageProcessor` route uniquement les messages serveur vers la persistance existante des quatre lots. Celle-ci résout l’ID dans le catalogue et accepte uniquement ressources/runes, avec les mêmes réglages de capture, attribution de serveur, notifications et effacement des lots absents que `jzs`. Un message entièrement nul ou mal formé n’écrit rien. Un équipement ou une monture n’alimente pas cette route : un rafraîchissement d’une offre avec un jet particulier ne prouve pas le minimum de toutes les offres de l’objet.

Le suivi d’acquisition conserve son décodage et sa corrélation avec la confirmation d’achat et l’objet reçu. Un rafraîchissement public ne constitue pas une vente réalisée ni une nouvelle acquisition.

## Bouton « Cours du marché » grisé

Le client reçoit aussi `isr` avant l’ouverture d’un historique. Dans les deux relevés disponibles, sa racine contient 8617 entrées répétées en champ 3. Chaque entrée observée associe un ID d’objet au champ 1 à un prix moyen au champ 2. Le Volkorne Prune et Amande (33207), dont le bouton est actif, y figure à 373874 K. Le Volkorne Prune et Turquoise (33215), dont le bouton est grisé, est absent des deux relevés. Les 97 références dont un historique a été demandé dans la capture figurent toutes dans cette liste.

Cela soutient l’hypothèse que le client utilise son cache de prix moyens pour rendre ce bouton disponible. Le code de décision du client n’a pas été confirmé. L’absence d’une entrée n’est pas enregistrée comme zéro vente, ni comme preuve d’une durée précise sans ventes.

Les sélections de ces deux montures n’ont émis aucune requête `iqp`, et n’ont reçu aucun historique `ire`. Dans la capture, les historiques détaillés avec dates et volumes arrivent à la suite des requêtes explicites `iqp`. La présence préalable d’un prix moyen et la réception de l’historique détaillé sont deux informations différentes. Le prix moyen n’est pas substitué à un minimum HDV relevé.

## Vérifications après une mise à jour

- Mettre en vente un lot prévu, comparer les quatre minima affichés avant/après et les prix enregistrés. Vérifier un dépôt qui change le minimum puis un dépôt qui ne le change pas.
- Vérifier l’absence d’écriture pour le sens client → serveur, un serveur expiré, la capture désactivée et un objet inconnu/équipement.
- Comparer une référence avec historique actif et une référence avec bouton grisé, sans cliquer. Puis ouvrir uniquement l’historique actif et vérifier la séquence requête/réponse.
