# Recherche et filtres des services

## Portée

L'interface reprend les principes de recherche visible, filtres repliables et détails à la demande, avec les couleurs et les filtres de BestCrush. Les calculs économiques, sources de prix, stocks et niveaux de métier restent ceux des services existants.

- Concassage : recherche dans une liste d'équipements, sans sélection ni autocomplétion. Le texte filtre immédiatement les résultats déjà calculés, sans requête de prix ou nouvelle analyse. Le bouton Analyser recherche les équipements correspondant au texte et aux critères. Pour étendre la recherche au-delà des résultats calculés, il faut relancer cette analyse.
- Concassage : panneau repliable regroupant métier, types, niveau, runes et rentabilité ; compteur et étiquettes retirables ; filtres enregistrés dans une section séparée repliable. Les résultats complets, partiels et non chiffrables utilisent des fiches compactes paginées par 30. Les détails et leurs composants ne sont montés qu'à l'ouverture.
- Craft : recherche et tri visibles ; métier, types, rentabilité, budget et niveaux de métier dans le panneau commun. Le classement précède toujours la limite de 100 recettes.
- Achat / Vente, Pierres d'âme, Farming et Opportunités : panneau commun avec leurs critères existants, compteur et étiquettes retirables. Ouvrir ou retirer un filtre agit sur le rapport en mémoire et ne relance pas son calcul.
- FM : recherche d'un équipement avec huit suggestions illustrées au maximum ; seuls les types acceptés par la FM sont proposés. La sélection reste propre à ce service. Le suivi manuel/en jeu, le jet et les conseils ne sont pas modifiés.
- Données : champ de recherche commun dans Équipements, Runes, Ressources et Historique ; étiquettes communes dans l'historique. Les tableaux, leur pagination et leurs chargements en arrière-plan sont conservés.
- Routine et Analyses conservent leur organisation spécifique.

## Composants

`ServiceSearch`, `ServiceFilters`, `FilterChip` et `ServiceResult` mutualisent les interactions. `MarketItemPicture` utilise le cache d'images existant ; l'absence d'image ne bloque jamais un résultat. `EquipmentSearch`, réservé à la FM, charge un catalogue léger dans un contexte indépendant et abandonne son travail à la destruction du composant. Les icônes de commande utilisent la police Bootstrap déjà embarquée.

Le champ de Concassage notifie explicitement la page après chaque saisie : modifier le modèle dans un sous-composant ne suffisait pas à actualiser sa liste parente. Le test de parcours utilise cette notification réelle et vérifie que la saisie n'ajoute pas de requêtes ni de nouveaux calculs.

## Validation du 6 novembre 2026

- 131 scénarios ciblés vérifiés : recherche de concassage progressive, filtre par métier, stock activé/désactivé, top 100 du craft après filtres et tri, réactivité des filtres pendant une actualisation, activités, historique, données et conservation des espaces de travail. Les tests de rendu ont été adaptés aux panneaux initialement repliés et à un marqueur de nom stable dans les lignes.
- Après les dernières corrections de recherche : cinq scénarios Concassage/FM puis six scénarios Concassage/métiers ont été exécutés avec succès. Le dernier test vérifie l'affichage de la valeur du champ et la notification de la saisie au parent.
- Compilation Windows réussie, aucune erreur, cinq avertissements préexistants dans EquipmentPrices, CrushResultLine et OverlayService.
- Contrôle natif : recherche Bouftou sur Kourial, affichage de 15 équipements, ouverture/fermeture d'un détail, puis saisie Marteau affichant immédiatement un seul résultat et un compteur correct sans relancer l'analyse.
- Contrôle natif : huit suggestions FM illustrées, sélection du Bouclier du Bouftou déjà présent en session ; aucune rune consommée et aucun jet modifié.
- Contrôle natif : panneau de filtres des pierres d'âme, application d'un critère, fermeture du panneau et retrait de son étiquette ; recherche d'équipements dans Données et retour à Concassage.
- Le craft a été contrôlé avec les fixtures de stock des tests, aucun inventaire de personnage n'étant connecté lors de la vérification native.
