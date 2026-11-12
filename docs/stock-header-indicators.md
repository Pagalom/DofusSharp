# Indicateurs de synchronisation dans l’en-tête

Le bandeau global a été retiré. Sous chaque avatar de fenêtre Dofus, deux emplacements discrets représentent l’inventaire puis la banque. Un cercle rouge est visible tant que le conteneur n’a pas un relevé complet et courant ; il disparaît dès la synchronisation, même si le conteneur est vide. Sans personnage détecté, aucun indicateur n’est affiché. Ils ne sont jamais placés sous le bouton Personnages.

Le survol de chaque cercle affiche son intitulé après 800 ms. Le focus clavier l’affiche immédiatement et Échap permet de fermer l’infobulle. Les cercles ont aussi des libellés accessibles précisant leur état.

`LiveStockService.ForWindow` lit le stock courant de chaque fenêtre indépendamment de la sélection globale. Il conserve la vérification de connexion et refuse les fenêtres fermées. Aucun état d’archive n’est utilisé pour ces indicateurs.

Chaque fiche de personnage porte son propre bouton Voir les stocks. Il cible explicitement la fenêtre de cette fiche avant d’ouvrir les stocks ; aucune action générale n’est affichée en dehors des fiches.

Validation : 16 tests ciblés réussis, incluant deux personnages aux états distincts, un relevé partiel, une banque vide synchronisée, un changement de sélection, la fermeture des fenêtres et la protection contre les anciennes connexions.
