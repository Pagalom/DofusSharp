## Familiers : catalogue XP intégré

- BestCrush embarque désormais les rendements XP de **3 521 aliments**, dont **2 749 à rendement positif**, calculés pour le catalogue DOFUS 3.7.14.18. Ces rendements sont communs aux serveurs ; le rapport **XP/K** dépend toujours des prix du serveur choisi.
- Les coefficients fractionnaires sont conservés pour les ressources qu’il faut donner en plusieurs exemplaires. L’arrondi du repas est appliqué au total de la quantité prévue.
- Les valeurs XP sont désormais en lecture seule dans Données, le module et l’overlay. Les anciennes saisies et l’apprentissage par nourrissage ne remplacent plus le catalogue intégré. Le suivi des stocks, du niveau et de l’XP des familiers reste actif.
- Le catalogue est chargé une seule fois et partagé par les calculs. Les choix J/M pour les prix et l’utilisation des stocks restent disponibles.

## Familiers : présentation et choix de nourriture

- Dans l’overlay, l’image et le nom du familier sont alignés à gauche. Une seule ligne regroupe **Nourrissage**, l’aliment, sa quantité et le montant à débourser.
- Dans le module, les cartes présentent les prix, puis trois étapes : **achat / niveau initial**, **nourriture / quantité / coût**, **revente / niveau conseillé**. Les listes d’achats redondantes et la ligne « Objectif » sont retirées.
- Les boutons précédent, suivant et retour au meilleur aliment utilisent la même sélection que celle placée en haut de la page et de l’overlay.
- Le survol de la nourriture affiche les prix connus par lots **×1, ×10, ×100 et ×1000**. Un clic continue à copier le nom de la ressource.

## Nouvel overlay : classement des runes

- Un bouton **hexagonal**, placé à gauche de la barre de contrôle, ouvre un tableau compact des **cinq meilleures runes selon leur ratio kamas / point de poids**.
- L’œil d’une ligne masque la rune et laisse entrer la suivante dans le classement. L’œil en haut permet de retrouver les runes masquées ; le bouton de réinitialisation les réaffiche toutes.
- Les choix de masquage sont conservés par serveur, même après un nouveau relevé de prix.
- Le classement emploie les mêmes poids et prix retenus que Données → Runes. La fenêtre reste déplaçable et redimensionnable, avec affichage piloté par la barre de contrôle et F7.

## Rentabilité : recherche globale et synchronisation

- Une barre de recherche est ajoutée à gauche de **Mes équipements**. Elle recherche dans le catalogue local, y compris les équipements sans recette.
- Les filtres avancés regroupent **métier, types d’objet, runes et niveaux**, avec des icônes et les noms au survol. Les visuels de repli sont disponibles si les images locales de métier sont absentes.
- **Entrée** affiche une liste conservée pendant la session. Un nom exact ou la sélection d’un résultat place l’objet en focus avec son **jet moyen**.
- La recherche et les filtres communs sont synchronisés dans les deux sens avec le module **Concassage**, sur le même serveur. Entrée dans l’overlay déclenche également l’analyse du module lorsqu’il est ouvert ; la demande est retrouvée à son ouverture sinon.
- Après analyse, la liste de l’overlay reprend les résultats et leur ordre. Elle suit les actualisations du module. Les résultats d’une ancienne recherche ne remplacent plus ceux d’une recherche plus récente.
- Le filtre par rune emploie une règle commune : une ligne de caractéristique nulle ou négative ne suffit pas à rendre l’objet candidat pour cette rune. Cela corrige les écarts de compteurs dus aux malus.

## Installation et mise à jour

Fermez BestCrush, extrayez **BestCrush-v0.2.9-win-x64.zip**, puis lancez **BestCrush.exe**. L’archive Windows x64 inclut .NET et le Windows App SDK. **Npcap** reste nécessaire pour les relevés réseau.

Vos prix, coefficients de concassage et réglages locaux sont conservés. Les rendements XP des aliments sont remplacés par le catalogue intégré. Cette archive n’inclut ni données personnelles ni captures de jeu ; les images de développement non distribuées disposent de visuels de repli.
