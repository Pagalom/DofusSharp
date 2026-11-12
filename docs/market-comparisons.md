# Comparaisons de marché

Analyses est un espace indépendant de l’Historique. Il explore les évolutions de prix locaux ; il ne duplique pas les classements économiques des autres activités et ne prétend pas identifier une cause de marché.

## Composants

- `MarketAnalyticsService` : catalogue léger (sans recettes ni graphes d’entités suivies), résolution des sélections et une lecture des observations pour toute une comparaison. « Tout l’historique » ajoute une recherche de la première date utile.
- `MarketAnalyticsCalculator` : calcul pur, testable avec un jeu de relevés et une horloge explicites.
- `MarketAnalyticsProjection` : courbe fusionnée ou courbes individuelles à partir des mêmes résultats, sans SQL.
- `AnalyticsWorkspace` : configurations, chargement de fond annulable, groupe par critères ou sélection libre, exploration des membres. Les nouveaux relevés signalent qu’une actualisation est disponible ; ils ne déclenchent pas de recalcul automatique au retour sur l’application.
- `MarketAnalyticsChart` : géométrie préparée lorsque les données ou l’échelle changent. Seuls les créneaux avec de nouveaux relevés et une valeur calculable sont affichés comme points. Un trait continu relie ces points, même lorsqu’il manque des créneaux entre eux : cette liaison est uniquement visuelle, sans échantillon ajouté ni prix interpolé consultable. Son survol précise les dates et l’absence de relevés intermédiaires, ou l’insuffisance des données si des relevés existaient mais ne permettaient pas de calculer le groupe. Aucune courbe n’est prolongée avant le premier point ou après le dernier ; des prix seulement reportés ne créent pas de courbe. Un groupe mêlant nouveaux relevés et prix conservés reste affichable et le détail du point le précise. Au plus 8 courbes sont montées à la fois et tous les objets restent consultables par pagination.

## Interface

Le graphique occupe la zone principale et « Mes courbes » reste à gauche (au-dessus sur un écran étroit). Le bandeau des stocks est masqué dans cette activité, sans arrêter la capture. La période et l’échelle sont dans une barre compacte ; l’ajout, la modification, les membres d’un groupe, les options, le nom et la suppression d’une configuration utilisent un panneau latéral. Les menus « … » se ferment au clic extérieur, après une action et avec Échap ; ouvrir un menu ferme les autres. Les panneaux peuvent être fermés par Échap ou par leur fond, leur focus initial vise le bouton de fermeture et la tabulation reste dans le panneau.

Afficher/masquer une courbe et fusionner/séparer ses membres réutilisent la comparaison chargée, sans requête. La visibilité est conservée dans la configuration, avec `true` par défaut pour les anciennes sauvegardes. Le calcul garde tous les groupes pour ne pas modifier la référence commune lorsqu’on masque une courbe. Les couleurs de la liste et du graphique sont partagées. La légende du graphique n’apparaît que pour les groupes dépliés. Les options globales peuvent conserver des réglages différents selon chaque courbe ; une application sans changement ne relance pas la lecture. Un observateur de largeur adapte le SVG et ses libellés à l’espace disponible ; il est déconnecté à la destruction du composant.

## Sélections

Une courbe peut viser un objet, tous les objets d’une famille, un groupe par critères (famille, caractéristique positive, types d’équipement, niveaux, nom), ou une sélection personnelle pouvant mélanger plusieurs familles. Les membres sont identifiés par `(famille, ID)` et dédupliqués. Le raccourci par caractéristique crée un groupe de runes et un groupe d’équipements correspondants. La présence d’une caractéristique positive est un critère de catalogue, pas une prédiction de rendement de brisage.

## Calcul

1. Tous les groupes partagent la même période et la même grille (au plus 160 instants dans l’interface).
2. Les lots sont convertis en prix unitaire sauf demande explicite d’un prix de lot précis. Chaque couple lot/source produit une valeur par objet et par intervalle ; les lots et sources présents sont ensuite agrégés sans multiplier leur poids par leur nombre de relevés.
3. Les relevés antérieurs au début peuvent fournir un prix de référence pendant la durée de fraîcheur autorisée (72 heures par défaut, réglable). Pour chaque couple lot/source, la valeur agrégée de l’intervalle est conservée jusqu’au prochain relevé, sans revenir au dernier prix brut ; sa date de fraîcheur reste celle du dernier relevé réel. Un prix reporté n’est jamais compté comme un nouveau relevé. Une réinitialisation annule seulement son propre lot et sa source. Les prix invalides et les observations futures sont exclus.
4. La référence base 100 est commune : parmi les instants où chaque groupe observé possède au moins un prix utilisable, choisir celui qui maximise la couverture minimale des groupes puis la couverture moyenne. En cas d’égalité, conserver le premier. Les membres sans aucune observation ne peuvent fournir une référence. Aucune référence n’est inventée pour des marchés sans période commune.
5. À cette référence, la composition de chaque groupe est figée. Chaque objet est normalisé individuellement (`prix / prix de référence × 100`) **avant** la fusion. La fusion utilise une moyenne à poids égaux ou une médiane des objets.
6. Si un membre de cette composition n’a plus de prix utilisable, le point du groupe est absent. Un membre absent à la référence n’entre pas silencieusement dans l’indice plus tard. Les effectifs et les exclusions restent visibles et les prix réels individuels restent consultables.

Un indice de groupe décrit donc les objets couverts à sa date de référence, pas nécessairement tout le catalogue. La résolution et la fraîcheur choisies déterminent les points disponibles ; une période très longue ou des relevés très espacés peuvent laisser des trous. La liste des membres permet de contrôler les absences.

## Persistance et compatibilité

Les nouvelles sauvegardes utilisent `Data/analytics-presets.json` dans le répertoire local de BestCrush, avec remplacement atomique du fichier. Tant que ce fichier n’existe pas, la clé de préférences `Analytics.MarketPresets.v1` reste lisible : les anciens objets deviennent des sélections unitaires par défaut, sans réécriture au chargement. La première sauvegarde copie toutes les configurations lisibles vers le fichier et conserve les préférences d’origine. Un fichier JSON illisible bloque l’écriture plutôt que de remplacer silencieusement les analyses par une liste vide. Les réglages de groupe sont clonés en profondeur ; seule la configuration est enregistrée, jamais les relevés ni les résultats figés. L’échelle est commune à tout le graphique, ce qui évite de mélanger kamas et indices sur le même axe.

## Vérifications

Tests : normalisation avant fusion, absence de pondération par fréquence, lots et sources, composition fixe, référence commune et représentative, report de la valeur calculée sans changement artificiel, actualisation partielle des membres d’un groupe, expiration, réinitialisations, observations futures, catalogue avec bonus négatifs, sélection mixte, une lecture SQL pour plusieurs groupes, compatibilité des configurations, bascules sans requête, rejet d’une lecture obsolète, liaisons entre les seuls points avec relevés et arrêt au dernier point. Une comparaison de 1 000 objets et 25 000 relevés mesure également la lecture/calcul groupés.
