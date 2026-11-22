# Rendements XP des aliments

Les rendements sont une donnée calculée commune aux serveurs. `BestCrush/Data/pet-food-yields.json` contient le catalogue 3.7.14.18 : 3 521 entrées, dont 2 749 à rendement positif. Il est chargé une fois en mémoire, sans téléchargement, puis partagé par les pages, l’overlay et les calculs de rentabilité. Le rapport XP/K reste calculé avec le prix choisi sur le serveur consulté ; les stocks et les choix de prix J/M continuent de fonctionner.

Le catalogue remplace les anciennes valeurs manuelles et celles apprises par nourrissage. Ces repas donnent un résultat entier, parfois plafonné ou bonifié, qui ne doit pas remplacer le rendement de base. Les colonnes historiques de provenance et de date restent présentes pour la compatibilité de la base, mais sont vidées et ne pilotent plus les calculs. Aucun service de saisie ou d’apprentissage XP n’est enregistré. Le suivi réseau de l’inventaire et du niveau des familiers continue via le service de stocks.

## Calcul et précision

Pour les ressources non fabriquées, le champ brut de recyclage reçoit un facteur 5 si sa provenance non vide ne contient que des boss résolus par le catalogue. Une provenance mixte ne reçoit pas ce facteur. Pour un résultat fabriqué, la recette est décomposée jusqu’aux composants sans recette ; les quantités sont regroupées, les facteurs de provenance appliqués composant par composant, puis un facteur 1,5 est appliqué. Les références de monstre absentes sont ignorées comme dans le client.

Le recyclage ajusté est tronqué à cinq décimales après conversion float32 du produit par 100 000. Il est ensuite multiplié par 10 pour les familiers ordinaires. Les effets XP explicites 2820 sont utilisés directement ; les aliments portant les effets 2862/2864 ne sont pas traités comme de la nourriture XP ordinaire.

Le coefficient conservé n’est pas arrondi au gain d’un exemplaire. `PetExperience.MealExperience` convertit le produit quantité × coefficient en float32 avant l’arrondi inférieur. Exemple : une Bidoche Goûtue donne 1 569 XP, mais dix donnent 15 693 XP. Le calcul de base exclut les bonus temporaires du jeu. Le catalogue est lié à cette version du client ; une mise à jour du jeu nécessitant une nouvelle règle doit être vérifiée avant sa régénération.

## Régénération

`tools/build_pet_food_yields.py` prend les exports JSON des tables items, monstres, recettes et types d’objets, ainsi qu’un catalogue de noms français. Les quatre tables utilisent le format `references.RefIds` des données du client ; les noms sont une liste d’objets `id`/`name.fr`. Le script fonctionne hors ligne et ne modifie ni le jeu ni la base utilisateur.

Passer `--items`, `--monsters`, `--recipes`, `--types`, `--names`, `--output`, `--version 3.7.14.18-feeding-v1` et `--client-version 3.7.14.18`. Mettre à jour la version du catalogue dans `FixedPetFoodCatalog` avec la donnée générée. Aucun fichier exécutable du client ni export assembleur n’est nécessaire à l’exécution de BestCrush.
