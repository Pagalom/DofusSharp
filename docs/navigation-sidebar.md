# Navigation générale BestCrush

La navigation principale utilise une barre latérale, en conservant l'identité visuelle de BestCrush. Elle regroupe Routine et Opportunités sous « Au quotidien », les huit activités sous « Activités » et Analyses sous « Outils ». Chaque service reste accessible directement, sans sous-menu sur une fenêtre large.

Le bouton Données reste mis en valeur dans l'en-tête, avec le serveur, les personnages et les paramètres. Il remplace les activités par les cinq rubriques de données dans la même barre latérale. Le bouton devient Activités pour revenir au service précédent. Les mécanismes existants de chargement différé, de conservation des filtres et de restauration du défilement sont conservés.

Sous 992 px, la barre latérale devient un bouton Menu indiquant la rubrique courante. Le menu se déplie dans la page et se referme à la sélection d'une rubrique ou avec Échap. Les liens ont un état actif et un focus visible ; un lien de saut au contenu est accessible au clavier.

## Validation du 6 novembre 2026

- 80 tests ciblés réussis, dont 8 cas pour la nouvelle navigation : liens de chaque service, routes par serveur, rubrique active, cinq sélections de données, fermeture du menu et Échap.
- Compilation Windows réussie : aucune erreur ; cinq avertissements préexistants dans EquipmentPrices, CrushResultLine et OverlayService.
- Contrôle dans l'application native à 1426 px et en plein écran : tous les services sont visibles, état actif correct, accès à Routine et Forgemagie, conservation du serveur Kourial.
- Aller-retour Concassage → Données → Historique → Activités : recherche temporaire « Amulette » conservée, puis effacée après vérification.
- Contrôle à environ 964 px : menu compact, ouverture, accès à Concassage et fermeture automatique vérifiés.

Cette étape porte sur la navigation générale. Les interfaces internes et les calculs des services ne sont pas refondus.

## Navigation par icônes

La barre latérale large utilise désormais une colonne de 64 px avec des boutons de 44 px, les icônes Bootstrap déjà embarquées et une infobulle après 800 ms de survol. Les groupes restent séparés visuellement. Le nom de chaque destination reste présent dans son nom accessible ; au clavier, le focus affiche immédiatement le libellé et Échap ferme l'infobulle. Les petites fenêtres conservent un menu compact avec icône et texte.

Les infobulles utilisent uniquement le CSS, sans temporisateur .NET ou chargement réseau. Elles peuvent être survolées et ne sont pas coupées par un conteneur défilant. Sous 680 px de hauteur, la barre suit le défilement de la page pour garder les derniers accès atteignables.

Validation : 77 tests ciblés réussis, compilation Windows réussie avec les cinq avertissements préexistants. Vérification native des onze icônes d'activités, des cinq icônes de données, du libellé au survol (Opportunités et Historique), de sa fermeture avec Échap et de l'aller-retour Routine / Données.
