# Modes de suivi FM

Le suivi en jeu est disponible uniquement avec un objet d’atelier correctement relevé, un jet exploitable, le personnage ciblé et une connexion/attribution serveur courante. L’existence du service dans l’application ne suffit plus. L’option indisponible est désactivée et son libellé grisé ; son survol précise ce qui manque.

L’observation est passive et automatique : une demande de dépôt provenant de la fenêtre ciblée et de son flux vérifié amorce le suivi ; le retour d’atelier valide et la lecture du jet activent le mode en jeu. Aucun clic ni rune n’est envoyé au jeu. Un simple résultat isolé ou les messages d’une autre fenêtre ne peuvent pas amorcer une session.

Chaque activation complète d’atelier possède une révision transitoire. Une nouvelle activation choisit automatiquement le suivi en jeu. Lorsque l’utilisateur choisit manuellement le mode manuel alors que les deux modes sont disponibles, les mises à jour ordinaires ne lui reprennent pas ce choix. La détection continue en arrière-plan. Fermer l’atelier, retirer l’objet, perdre le jet exploitable ou la connexion rend le suivi en jeu indisponible et restaure la session manuelle indépendante.

Le choix manuel n’arrête pas la capture passive et les enregistrements du jeu n’écrasent pas les sessions manuelles. Le jet manuel est conservé avant la transition automatique. Les contrôles de propriétaire de fenêtre, de connexion TCP et d’attribution serveur restent requis.

Validation : 51 tests FM réussis, dont une simulation intégrée des modes avec dépôt d’objet, lecture du jet, préférence manuelle, fermeture et réouverture du même objet, et révocation du serveur. Les relectures des essais FM existants continuent de vérifier les résultats, quantités et coûts. Aucune rune consommée dans le jeu pour cette modification.
