# Cinétique des ventes globales et nouveaux modules

État au 29 octobre 2026. Développement intégré ; suite complète : 1 446 tests réussis. Le contrôle visuel des nouveaux onglets a été effectué, avec la limite de session détaillée ci-dessous.

## Fonctionnement

Achat / Vente compare les tailles de lots, les offres au prix moyen pondéré des ventes globales sur 7 jours, et la revente des stocks de la session courante. Les crafts plus intéressants sont signalés avec leur recette consultable sur le nom. Leur comparaison valorise tous les ingrédients.

Farming valorise un volume choisi de ressources : récolte, ressources de combat, et transformation en crafts. Les autres ingrédients sont déduits de la recette de revente. Les monstres concernés sont consultables dans les détails. Aucun revenu par combat, quantité de loot attendue, prospection ou temps de combat n’est supposé.

Pierres d’âme propose les boss de donjon explicitement capturables, sans archimonstres. La marge déduit la pierre vide compatible la moins chère. Les offres sont regroupées par objet de capture et boss : prix le plus bas à l’unité, sinon estimation unitaire depuis le plus petit lot. Le paramètre butin est supprimé. L’historique global de cette âme sert maintenant à la cinétique, sans mélange avec les ressources génériques. La saisie manuelle reste un complément. Voir `soul-market-automatic-prices.md` pour le schéma, la migration et les vérifications.

Opportunités rassemble ces résultats et les propositions existantes Craft et Concassage. Les pistes qui réutilisent les mêmes ressources ne s’additionnent pas. La cinétique du concassage reste inconnue tant que celle des runes produites n’est pas calculée ; celle de l’équipement d’entrée ne la remplace pas.

## Calcul de cinétique

L’utilisateur ne retrouve pas l’ancienne formule. Sa consigne est de tenir compte de la vitesse potentielle de revente à partir des ventes globales. Le classement implémenté utilise :

- Volume journalier = articles réellement vendus sur 7 jours / 7.
- Équivalent d’écoulement du marché = quantité proposée / volume journalier.
- Gain = revente estimée − coût connu.
- Indice comparatif = gain / équivalent d’écoulement du marché.

Les gains positifs précèdent les pertes. Parmi les propositions rentables, l’indice combine marge et volume global. La quantité écoulée par le joueur, sa concurrence et son prix futur sont inconnus : cet indice ne garantit aucun délai personnel ni revenu journalier. Les montants sont présentés avant taxes. Un prix absent reste inconnu. Coût nul ne produit pas de ROI infini. Un historique absent, ancien ou sans ventes correspond à trois états distincts.

## Capture et stockage

Sur Dofus 3.7.12.12, les routes `iqp` / `ire` ont été caractérisées à partir de captures réelles. La requête identifie l’objet ; la réponse porte les points horaires sur 24 h et journaliers sur 30 jours. Chaque point contient le prix unitaire, l’identifiant d’objet, une date ISO UTC et un nombre d’articles vendus. Le prix moyen est pondéré par les quantités.

La capture vérifie le sens, la même connexion, le client et son serveur, l’époque de capture, l’identité de l’objet et un délai de réponse maximal de 15 secondes. Elle n’assimile pas des annonces à des ventes. Les limites de taille, les dates, les doublons et les débordements numériques sont contrôlés.

La base dédiée `Data/global-sales.db` contient les historiques bruts, les résumés 1/7/30 jours et les relevés manuels de pierres pleines. Les fenêtres qui se chevauchent ne sont pas additionnées. La relecture d’une période ne la rend pas récente. La capture déclenche l’invalidation des analyses du serveur concerné.

## Interface et performance

Calcul en arrière-plan, indicateur de chargement, annulation et cache borné ; filtres locaux et 100 résultats maximum après filtrage. Les détails de monstres sont chargés à leur ouverture. Un simple retour de focus ne relance pas les analyses. La case « Tenir compte de mes niveaux de métier » masque les activités dont le niveau connu est insuffisant ; les niveaux inconnus restent à vérifier.

Seuls les conteneurs complets et récents du client courant, sur le serveur consulté, contribuent aux stocks. Une banque non synchronisée produit une information sans bloquer l’inventaire. Les archives ne remplacent pas la session courante.

## Validation effectuée

- Suite principale : 1 434 tests réussis, zéro échec, dont 26 nouveaux tests de cinétique, capture, persistance, catalogues, calculs, isolation et filtrage.
- Catalogue public réel : 118 activités de récolte, 100 boss capturables. Capacités des pierres vérifiées à partir des effets des objets.
- Connexion de Pagalom, Eniripsa 39, Tylezia : 128 lignes d’inventaire récupérées automatiquement.
- Capture automatique en jeu le 28 octobre vers 19 h 30, heure Europe/Berlin : Laine de Ramane Strubien, identifiant 881. Dofus affiche 7 443 ventes sur 24 h et 32 426 sur 7 jours ; SQLite contient exactement ces volumes et 103 877 sur 30 jours. Intégrité SQLite : `ok`.
- Achat / Vente s’est actualisé automatiquement et affiche 4 632,3 articles/jour (32 426 / 7), les prix anciens signalés et la notice de craft.

## Contrôle du 29 octobre et reste à faire

La version Windows habituelle a été recompilée avec les derniers changements : zéro erreur, 16 avertissements déjà présents. Elle a été ouverte et contrôlée sur le marché réel de Tylezia, sans client Dofus connecté ; aucune archive de stock n’a été réutilisée.

- Farming : 100 résultats affichés sur 1 573 propositions ; le filtre de niveaux inconnus conserve les résultats avec une notice, sans relancer l’analyse. Les détails de la laine chargent Ramane Strubien, niveau 30, et rappellent que la valeur ne constitue pas un revenu par combat.
- Pierres d’âme : 100 boss, montants inconnus affichés comme tels, détails de capture et pierre compatible visibles. Aucun relevé fictif n’a été enregistré.
- Opportunités : filtre d’origine Achat / Vente, Farming, Pierres d’âme ; le filtre Farming restitue les 1 573 propositions avec les mêmes montants et les liens vers le module, toujours plafonnés à 100 après filtrage. Les filtres ne changent pas la date d’analyse.

La capture automatique des prix des pierres pleines par boss et places de butin est désormais implémentée et vérifiée en jeu ; voir `soul-market-automatic-prices.md`. Un contrôle supplémentaire du regroupement Craft/Concassage avec un inventaire de session fraîchement connecté reste utile : cette session de contrôle n’avait aucun client Dofus ouvert. La capture d’inventaire et de cinétique avec Pagalom a été vérifiée la veille.


Complément du 29 octobre, 13 h 51 : capture des prix de quatre pierres vides vérifiée dans le même HDV. Les marges Ancestral et Batofu sont affichées dans Pierres d’âme et Opportunités. L’historique de boss est capturé séparément pour tous les formats (Ancestral : 75 ventes/7 jours), sans lui attribuer une cinétique par places de butin. Version Windows compilée et relancée ; suite complète à 1 444 tests réussis. Détails : `soul-market-automatic-prices.md`.


Mise à jour finale du 29 octobre : suppression du paramètre butin et regroupement des offres. Les anciens relevés et historiques sont migrés ; la cinétique des âmes exploite leur historique de boss. Le message de banque non synchronisée est centralisé dans le bandeau des stocks pour tous les modules. Suite complète : 1 446 tests réussis. Les contrôles précédents par format ci-dessus sont des étapes historiques, remplacées par ce fonctionnement.
