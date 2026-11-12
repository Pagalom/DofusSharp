# Palette commune des fiches

Les variables `--bc-*` définissent une palette commune : panneaux #222e34, surfaces secondaires vert grisé, texte ivoire, accents dorés #e4c27a. Le fond clair est conservé autour du contenu. La feuille `wwwroot/css/character-palette.css` est chargée après les styles isolés et adapte aussi les variables Bootstrap locales pour que les composants imbriqués restent lisibles.

Couverture : tableaux de prix et historiques, cartes de résultats et détails, filtres et choix directs, Routine, FM, graphiques et panneaux Analyses, menus et paramètres. Les sélections et focus utilisent le doré ; les gains, pertes et avertissements gardent des couleurs sémantiques. Les séries graphiques utilisent une palette plus claire sur les nouveaux fonds sombres.

Les grands blocs Kamas et Banque ont été retirés de CharacterSheetCard. Les seuls montants connus sont présentés en une ligne compacte dans Stocks, pour la fenêtre du personnage effectivement ciblé et liée à son profil vérifié. Un montant inconnu n’affiche aucune case ; zéro reste une valeur valide.

Validation : 60 tests ciblés réussis, dont l’isolation des montants entre personnages et la stabilité des graphiques. Les contrastes des couples principaux texte/fond dépassent 4,5:1. Contrôle natif des tableaux, de Concassage, Achat / Vente, Routine, FM et des panneaux Analyses.
