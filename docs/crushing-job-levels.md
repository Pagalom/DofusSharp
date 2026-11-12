# Concassage : choix implicites, images locales et niveaux de métier

## Sélections

Les boutons Tous les métiers, Tous les types et Tous du métier sont retirés des choix visuels. Une sélection vide signifie tous les métiers ou tous les types autorisés par le métier choisi. Cliquer de nouveau sur un choix le désélectionne. Les autres usages des composants en liste dépliante conservent leur présentation existante.

## Images de runes

Les 104 runes du catalogue sont couvertes par 103 PNG distincts, embarqués dans `BestCrush/wwwroot/img/runes` (environ 1,85 Mo). Certaines runes partagent la même image. Les sources sont les identifiants d'image publics de la base locale et le service d'images DofusDB déjà utilisé par le projet : `https://api.dofusdb.fr/img/items/{iconId}.png`. 47 images proviennent du cache local existant et 56 ont été récupérées pour ce lot.

Le filtre utilise des chemins locaux ; aucune récupération d'image à distance n'est nécessaire à son affichage. Un pictogramme local de remplacement est prévu pour une image absente après une future évolution du catalogue.

## Prise en compte niveau métier

La case est disponible lorsque toutes les conditions suivantes sont satisfaites :

- Le personnage ciblé, son niveau et des niveaux de métier sont détectés pour la même fenêtre et le serveur consulté.
- Le maximum de niveau d'objet est vide.
- Le minimum est vide ou strictement inférieur au niveau du personnage.

Une modification des bornes rendant ces conditions fausses désactive et décoche la case. Une infobulle indique la raison de son indisponibilité. Les tests incluent un minimum égal au niveau du joueur et un maximum renseigné sans minimum.

Lors de l'analyse, le filtre utilise le métier et le niveau requis indiqués par chaque recette, comparés aux niveaux relevés dans la fenêtre Dofus ciblée. Les prérequis inconnus sont exclus. Le catalogue public des prérequis est chargé en arrière-plan uniquement lorsque cette option est utilisée, puis mis en cache. Le modèle des filtres et ses préréglages conservent cette option.

Un changement de personnage ou de niveaux invalide les résultats calculés avec ces informations, ainsi que leurs projections de stock en mémoire. Les anciennes informations d'une autre fenêtre ne sont pas réutilisées. Si les niveaux deviennent indisponibles, la case est également décochée.

## Validation du 6 novembre 2026

- 44 tests ciblés réussis pour la politique d'activation, les sélections de runes/types/métiers, les filtres de craft et d'historique et le calcul de concassage.
- Après renforcement de l'invalidation de session : 26 tests Concassage/niveaux/métiers réussis, dont un nouveau scénario vérifiant l'effacement des résultats et du cache de projection lors de la perte du personnage. Cela représente 45 scénarios distincts vérifiés.
- Compilation Windows réussie, sans nouvelle erreur ni nouvel avertissement de code.
- Contrôle natif sur Kourial : disparition des boutons Tous, sélection/désélection de Cordonnier et retour implicite à tous les types, affichage des vraies images de runes, case grisée et infobulle explicative sans personnage connecté. Les règles lorsque des niveaux sont disponibles sont vérifiées avec les tests de composant et de politique ; aucune session Dofus n'a été ouverte pour cette validation.
