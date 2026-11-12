# Achat / Vente : interface

La présentation dédiée est activée uniquement pour `ActivityOpportunities.Module == "achat-vente"`.

- Recherche dans toutes les propositions déjà calculées, avant la limite de 100 résultats.
- Trois choix directs, désélectionnables : écarts entre lots, prix sous l’historique, revente du stock.
- Filtres avancés repliables : gain, ROI, budget, métiers, dépenses, données et fraîcheur.
- Tri accessible à côté du nombre de résultats ; cartes résumant coût, revente, gain et cinétique/indice.
- Détails montés uniquement à l’ouverture ; recette des alternatives de craft accessible au survol du lien.
- Les autres modules conservent le tableau ; les formules et la sélection économique sont inchangées.

La recherche, l’approche, le tri et les filtres de vue réutilisent le rapport mémorisé. La revente évalue le stock entier, sans plafond de quantité ni filtre sur le niveau des objets. Le bouton Actualiser force le renouvellement du rapport. Les changements de serveur, personnage, stock ou relevés continuent d’invalider les données concernées.

Validation : 80 tests ciblés réussis (ActivityOpportunity, ActivityRanking, ModularWorkspace et CrushingJobFilters), compilation Windows, contrôle natif de la sélection/désélection, recherche et détail. Le test de vue vérifie explicitement le même rapport et les mêmes entrées, sans requête catalogue, après utilisation des filtres et ouverture/fermeture du détail. Les notices de craft sont vérifiées dans les deux présentations.

Retrait des limites : 18 tests ciblés réussis, dont une valorisation de stocks de 40, 350, 25 000 et 3 milliards d’unités. Les modules utilisant encore une quantité de scénario conservent leur fonctionnement.
