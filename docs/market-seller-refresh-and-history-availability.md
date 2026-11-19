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

## Prise en compte du cache dans Élevage — 19/11/2026

La route `market_mean_prices=isr` et ses champs figurent maintenant dans le profil commun, les preuves et la checklist.
Le décodeur exige une liste complète structurellement valide : entrées répétées f3, ID unique positif f1,
moyenne positive f2 pour les références disponibles. Un f2 omis ou nul correspond à une référence sans moyenne.
Une trame vide, tronquée, ambiguë ou contenant des doublons est refusée intégralement. Les messages client → serveur
et les leases de serveur expirés ne mettent rien à jour.

Le dépôt global conserve par serveur le dernier cache reçu : date et ensemble des références ayant un prix moyen.
Il ne conserve pas ces moyennes comme prix de vente et n’ajoute aucun point aux historiques ni aux ventes personnelles.
Un cache ancien n’écrase pas un récent. Une absence connue reste mémorisée jusqu’à un nouveau cache positif ou un
historique détaillé reçu plus récemment ; un simple délai ne rend pas le bouton du jeu disponible.

Le planificateur utilise une formule commune à toutes les espèces :

`potentiel hebdomadaire = marge unitaire estimée × quota de vente estimé`

Le quota ne peut être positif qu’avec un volume de ventes exploitable au prix visé, puis il tient compte des dépôts
du joueur. Les cas « historique manquant », « historique inaccessible » et « historique observé à zéro vente » restent
séparés. Les deux premiers donnent un quota prudent de zéro, sans inventer un relevé de zéro vente.
Il n’existe aucune liste noire de couleurs. La même demande alimente les objectifs, l’allocation des montures en stock
et la capture pour revente. Les parcours d’obtention de parents restent calculés indépendamment : une espèce sans
marché direct peut être utile pour produire une descendance qui a un débouché.

Les contrôles HDV gardent les prix d’offres utiles mais ne demandent plus d’ouvrir un historique signalé indisponible.
La réception du cache rafraîchit les conseils automatiquement. Après installation du correctif, une nouvelle réception
`isr` est nécessaire ; les anciennes traces privées sans attribution serveur ne sont pas importées dans la base du joueur.

Validation locale : trois trames déjà enregistrées ont été rejouées en lecture seule, sans attribution ni écriture en base :
8617 entrées / 8613 moyennes positives pour les deux anciennes trames ; 8626 / 8599 pour la plus récente.
Prune et Amande (33207) est présent et Prune et Turquoise (33215) absent dans les trois.
