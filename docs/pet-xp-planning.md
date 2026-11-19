# Familiers / XP

Le module compare les familiers évolutifs possédés dans l’inventaire et la banque, ainsi que les achats HDV rentables. Les stocks doivent être complets, actuels et rattachés à la fenêtre et au serveur concernés. Les archives d’une ancienne connexion ne deviennent jamais un stock disponible.

## XP et nourriture

Référence : client Dofus **3.7.14.18**, `EvolutiveItemType` 1 et données publiques de production DofusDB. Le familier commence au niveau **0**, atteint le niveau 100 à **179 592 XP**, et une ressource rapporte `recyclingNuggets × 10`. Les ressources du super-type 9 et les nourritures ayant explicitement l’action XP 2820 sont retenues. Les croquettes légendaires ne sont pas assimilées à de la nourriture d’XP. Le catalogue de production est aussi utilisé dans le build Debug : les actions des DTO bêta sont incomplètes.

Un repas est arrondi à l’entier inférieur après multiplication par la quantité. Conserver les valeurs binaires du client, notamment pour les faibles rendements des runes. Les quantités d’une même nourriture doivent être données ensemble. Validation de l’aperçu en jeu : un Bec de Dodox vaut 101,25 XP, soit **101 XP / niveau 23** à l’unité et **1 012 XP / niveau 44** par dix sur une Serpète non nourrie. Le 18 novembre 2026, un repas réellement consommé a confirmé le passage de 0 à 101 XP / niveau 23 (`iqy`) et la diminution de 10 à 9 Becs (`irx`). `iqy` remplace l’objet de même UID dans l’inventaire, effets bruts compris.

Le plan compare achat, stock, fabrication accessible et mélanges. Les achats sont évalués au coût minimal de lots entiers 1/10/100/1 000, surplus compris. Les fabrications respectent le niveau de métier synchronisé. Un ingrédient ne peut pas être consommé simultanément comme nourriture et dans une recette. Les achats communs sont regroupés avant de chiffrer les lots.

Le montant à débourser est séparé de la valeur de revente du stock consommé. Un stock non valorisable produit un gain incomplet, jamais une valeur gratuite implicite. Les comparaisons utilisent les prix de moins de 72 heures. Les besoins de relevé sont copiables via la file Ctrl+V existante.

L’achat de lots pour un besoin donné est exact. La recherche de nourriture compare chaque aliment et recette accessible, des préfixes de stock ordonnés par coût/XP et des finitions combinant deux aliments. Elle ne certifie pas l’optimum combinatoire de tous les mélanges possibles. Les simulations de plusieurs familiers sont indépendantes ; il ne s’agit pas d’une réservation commune de leurs ressources.

## Catalogue et tableau des nourritures

La migration `AddPetFoodExperience` ajoute `Resources.PetExperience` (nullable, XP non arrondie) et `PetFoods`, catalogue commun aux ressources, runes et nourritures spéciales. La synchronisation maintient la valeur de la ressource et le catalogue utilisés par le module et l’onglet **Données → Nourriture familiers**. Une rune ne devient pas une ressource dupliquée.

Le fichier utilisateur **XP Familiers.ods**, daté du 18 novembre 2026, fournit le premier apport de 210 aliments distincts. Les prix sont unitaires sur Kourial, importés comme observations manuelles avec la date du fichier, une seule fois. Tout objet disposant déjà d’un relevé plus récent garde ce relevé. La Fraise n’a pas de prix dans le fichier et reste sans prix importé. Les colonnes Nombre et Prix total sont ignorées. Les 53 lignes de clefs et d’essences sont exclues à la demande de l’utilisateur ; les deux lignes Pétale Malibout sont regroupées en conservant le prix le moins cher. La ligne Gelée royale est exclue car son nom ne permet pas de choisir entre les gelées royales existantes. Les corrections orthographiques sont rapprochées d’identifiants vérifiés dans le catalogue de production.

`Data/pet-food-seed.json` conserve le nom et le SHA-256 du fichier, les lignes d’origine, les XP du fichier et les prix. Le rendement précis du catalogue est prioritaire lorsqu’il existe ; la valeur du fichier reste conservée dans `SheetExperience`. Sinon la valeur du fichier est utilisée avec la provenance **Fichier utilisateur**, sans prétendre avoir été vérifiée en jeu.

Le tableau montre aussi les aliments du catalogue déjà utilisé par BestCrush, y compris ceux absents du fichier. Il conserve les cases inconnues. Il calcule, depuis zéro XP, les quantités pour **29 819 XP (niveau 80)** et **179 592 XP (niveau 100)** avec l’arrondi réel du repas. Le prix unitaire est celui du lot d’achat le moins cher par unité ; le coût total optimise les lots entiers nécessaires et leur surplus. Le détail des lots et la provenance des XP sont disponibles au survol. Les anciens prix sont signalés et les relevés en jeu actualisent automatiquement les coûts.

## Vérification par les repas réels

La requête `irb` désigne le familier et l’aliment : f2 contient l’UID du familier, et l’unique f3 contient la quantité (f1) et l’UID de nourriture (f3). Le suivi exige ensuite la consommation exacte (`irx` ou `iqm`) et l’XP du même exemplaire (`iqy`), dans les dix secondes, sur la même connexion vérifiée et avec un inventaire complet. Les repas mélangés, les arrivées au niveau maximal et les rapprochements ambigus sont ignorés. Le cas réel Serpète/Bec de Dodox du 18 novembre 2026 a été rejoué localement ; les captures restent hors dépôt.

`PetFeedingObservations` conserve durablement les quantités, les XP avant/après et le build du client, sans identifiant d’exemplaire. Les observations sont séparées par build. Une variation de 101 XP pour un aliment ne signifie pas un rendement exact de 101 : elle définit l’intervalle [101, 102). Dix aliments donnant 1 012 XP réduisent cet intervalle à [101,2, 101,3), compatible avec 101,25. La référence précise n’est jamais remplacée par une division arrondie.

Dans les données, une icône indique si les repas confirment la référence, la contredisent ou ne permettent pas encore de connaître la valeur exacte. Les explications restent au survol. Une référence contredite ou des observations incompatibles excluent l’aliment des conseils et masquent ses quantités/coûts de montée, tout en conservant les données d’origine. Une XP encore inconnue reste inconnue ; l’intervalle mesuré est consultable sans inventer une valeur exacte. Les pages se recalculent après enregistrement d’un repas.

## Prix et revente

Les offres `jzs` des catégories 18 et 121 conservent leur niveau, XP et variantes spéciales. Les effets 2821 et 2819 utilisent le sous-message f9, dont f4 contient respectivement le niveau et l’XP. L’absence de f4 signifie zéro dans ces variantes reconnues. Les pouvoirs légendaires sont séparés pour éviter de valoriser un familier ordinaire au prix d’un familier légendaire. Le module ne simule pas une transformation légendaire.

Chaque consultation complète remplace le dernier relevé de l’espèce pour le serveur. Le `jzs` vide de réinitialisation est ignoré, conformément au décodeur du marché existant. Les octets et identifiants des captures réelles restent hors dépôt ; les tests sont synthétiques.

Le prix conseillé est **l’offre comparable la moins chère − 1 K**, avec un plancher de 1 K. Seuls les niveaux effectivement proposés sont évalués : aucun prix de niveau supérieur n’est extrapolé. Les repas qui dépasseraient le niveau étudié sont rejetés. Un prix au niveau actuel manquant empêche de chiffrer le gain supplémentaire d’un familier possédé.

Pour un achat : marge = revente conseillée − achat du familier − nourriture achetée − valeur du stock consommé. Pour un familier possédé : gain supplémentaire = revente conseillée − revente au niveau actuel − nourriture achetée − valeur du stock consommé. Les deux sont avant taxes.

Les quotas réutilisent les historiques et mises en vente de l’espèce entière ; ils ne constituent pas une demande mesurée pour chaque niveau ou pouvoir. Une espèce saturée ne génère pas de nouveau conseil d’achat. L’absence d’historique donne un quota inconnu et explicite.

## Actualisation

Le module se recalcule lorsque changent les stocks, métiers, prix, offres de familiers, historiques ou mises en vente. Le calcul s’exécute hors du thread d’interface et les résultats d’une ancienne session sont écartés. Les caches de calcul ne vivent que pendant une analyse, afin de ne pas conserver un ancien prix après un nouvel événement.
