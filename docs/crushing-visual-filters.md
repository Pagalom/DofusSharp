# Filtres visuels de Concassage

Le premier regroupement des filtres conservait les anciens sélecteurs. Cette révision permet une sélection directement dans le panneau, sans ouvrir de listes supplémentaires.

- Métiers : boutons visibles, choix unique ou Tous ; le métier continue de limiter les types par les recettes du catalogue existant.
- Types : boutons avec les pictogrammes SVG déjà embarqués, sélection multiple et retour à Tous. Le mode habituel des composants JobFilter et EquipmentTypeFilter reste leur valeur par défaut ; Concassage active explicitement leur nouvelle présentation Inline.
- Runes : choix directs par famille (Caractéristiques, Dommages, Résistances, Toutes) et recherche dans toutes les familles. Chaque variante conserve son identifiant exact ; normale, Pa et Ra restent distinctes. Un deuxième clic retire le critère. Les modes Au moins une / Toutes sont conservés.
- Rentabilité : les critères existants restent disponibles. Les étiquettes et le compteur du panneau restent actifs quand celui-ci est replié.

Les nouveaux choix utilisent du HTML/CSS et des images locales ; les interactions ne déclenchent aucun accès réseau ni analyse supplémentaire. Les champs de recherche et de nombres restent sombres, les choix sont clairs et les sélections sont bleues avec une coche.

## Validation du 6 novembre 2026

25 tests ciblés réussis : sélection du métier et normalisation des types, calcul de concassage, stocks, filtres de craft et d'historique. Le nouveau test active les variantes normale et Pa d'une même rune, vérifie leurs identifiants distincts, conserve le mode Toutes et vérifie qu'un contrôle désactivé ne modifie pas les critères.

Compilation Windows réussie, avec cinq avertissements préexistants. Contrôle natif sur Kourial : Cordonnier réduit les types à Bottes et Ceinture ; sélection de Bottes ; recherche Sa montrant normale, Pa et Ra ; sélection de Rune Sa ; panneau replié affichant les trois étiquettes. Les critères de test ont ensuite été réinitialisés et l'application a été laissée ouverte sur les nouveaux filtres.
