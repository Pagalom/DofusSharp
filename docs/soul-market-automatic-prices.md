# Prix automatiques des âmes de boss

État au 29 octobre 2026. Le paramètre butin est supprimé du modèle, des écrans et des calculs. Une capture est identifiée par son objet HDV et son boss ; les offres sont regroupées sur chaque serveur.

## Utilisation et prix de référence

BestCrush doit être ouvert et la connexion Dofus attribuée à son serveur. Dans l’HDV des âmes, sélectionner « Âme de gardien de donjon », puis ouvrir les offres du boss. Le prix est capturé sans achat. Cette fonction relève les offres consultées ; elle ne parcourt pas tous les boss automatiquement.

Le prix de revente est le plus bas des offres à l’unité du boss. À défaut d’offre à l’unité, le plus petit lot observé fournit un prix unitaire indicatif ; quantité et prix total restent dans les détails. Ainsi, Batofu x1 à 29 996 k reste la référence si x10 à 250 000 k est également proposé. Le formulaire de saisie manuelle est retiré de l’interface. Les anciens relevés manuels restent conservés en base.

Les offres d’un même objet sont remplacées atomiquement lors d’un nouveau relevé complet. Une notification vide n’efface pas le relevé précédent. Les dates et les serveurs restent isolés. Les prix d’âmes ne contribuent pas aux minima génériques de ressources.

## Pierre vide et calcul

Les pierres vides de type 83 sont identifiées par l’effet 705 à 100 %, même sans ouverture préalable du module. Le prix est relevé en consultant les offres « Pierre d’âme » du même HDV. Le coût utilise la pierre compatible avec le niveau du boss la moins chère parmi les prix connus. Les autres monstres du groupe ne constituent pas un critère de calcul.

Gain brut = prix de revente de la capture − valeur de la pierre vide. Les montants sont avant taxes. Une pierre déjà possédée conserve sa valeur économique, mais sa dépense d’achat devient nulle. Sans prix de pierre vide, la revente reste affichée et le gain reste inconnu. Pour une pierre vide uniquement disponible en lots, le coût est estimé par unité et le budget d’achat couvre le lot entier.

Le Cours du marché du boss fournit désormais la demande utilisée par la cinétique, sous une clé dédiée `boss:<id>`. Cette clé reste séparée des ressources génériques. Ventes par jour = ventes sur 7 jours / 7 ; indice de classement = gain × ventes par jour / quantité proposée. Cela compare les marges et volumes de marché, sans prédire un délai ou revenu personnel.

## Capture et persistance

Sur Dofus 3.7.12.12, `jzs` catégorie 327 contient l’objet racine, des offres, leurs prix totaux par lots 1/10/100/1000, un identifiant d’offre, le groupe (effet 4057) et le boss (effet 4058). Les données de format transmises par le jeu sont ignorées. La carte des champs reste dans `BestCrush.Network/protocol-map.json`.

Le décodeur conserve les limites de taille et de lignes, les vérifications de catégorie, d’objet, de doublons, de prix, de débordements et de messages tronqués. Le catalogue configuré confirme les correspondances objet/boss/groupe et les boss capturables ; le serveur vient de la connexion vérifiée. Le réglage de capture Ressources et les baux actifs restent obligatoires.

Dans `Data/global-sales.db`, `soul_market_prices` contient serveur, objet, boss, quantité, prix, groupe et date. `soul_capture_quotes` contient un relevé manuel par serveur et boss. Les anciennes tables sont converties en transaction : minimum des offres du dernier relevé par objet/lot, dernière saisie manuelle par boss, conservation des historiques et résumés. La migration est idempotente et supprime les anciennes colonnes de format. Une sauvegarde cohérente de la base réelle a été faite avant le déploiement.

## Stocks et validation

Le message « Banque non synchronisée · inventaire seul » est centralisé dans le bandeau de stock commun. Il disparaît pour une banque complète et récente, même vide, et revient si son relevé devient partiel ou périmé. Les messages et fenêtres flottantes redondants des modules ont été retirés. Le calcul reste accessible avec l’inventaire seul.

Suite complète : **1 446 tests réussis, zéro échec**. Les contrôles couvrent les captures réelles Ancestral/Batofu, minima entre offres, lots et priorité des saisies manuelles, direction/connexion/serveur, actualisation des calculs, utilisation de l’historique du boss, migration et bandeau commun (banque absente, partielle, vide et changement de personnage).

Fixtures publiques : `soul-market-ancestral.bin` et `soul-market-batofu.bin`, sans données de compte.


Contrôle réel après déploiement : le schéma de la base réelle ne comporte plus la colonne de format. Tous les minima, historiques et résumés ont été comparés à la sauvegarde et conservés ; intégrité SQLite `ok`. Après reconnexion de Pagalom, les 128 lignes d’inventaire et leurs quantités sont identiques au relevé initial. Le badge de banque non synchronisée apparaît dans le bandeau supérieur, sans doublon dans Pierres d’âme. L’Ancestral affiche le dernier minimum relevé de 80 000 k, coût 4 992 k, gain 75 008 k et 10,7 ventes/jour. Le Batofu affiche 29 996 k, gain 25 004 k et 1,1 vente/jour. Le sélecteur de format a disparu. Compilation Windows : zéro erreur, avertissements préexistants.


Présentation allégée : les détails des âmes n’affichent plus la provenance horodatée du prix, le paragraphe détaillé des ventes globales, ni le formulaire de relevé manuel. L’indice de classement et son explication restent affichés. La cinétique et les dates des observations continuent d’alimenter les calculs et le signalement des prix anciens. Les neuf tests existants des activités passent ; aucun calcul modifié.


Indice compact : les détails affichent uniquement « Indice : valeur ». Au survol ou au focus, l’infobulle montre le gain, les ventes globales par jour et la quantité de vente réellement utilisée par le classement, sous la forme « valeur (Gain) × valeur (ventes globales par jour) ÷ valeur (quantité mise en vente) ». Les nombres conservent jusqu’à neuf décimales utiles pour rendre le calcul contrôlable.
