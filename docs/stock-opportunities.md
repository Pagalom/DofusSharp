# Opportunités issues des modules métier

## Organisation

L'onglet Opportunités regroupe et classe les résultats des autres modules. Il ne remplace pas leurs écrans ni leurs règles de calcul. Les deux premières sources sont :

- Craft : fabriquer un équipement puis le revendre.
- Concassage : fabriquer un équipement puis concasser le lot avec le meilleur focus entièrement valorisé.

Chaque source implémente `IOpportunityProvider`. Le service commun charge un seul relevé du client ciblé et les données locales du serveur, puis appelle les sources. L'ajout d'un futur module ne nécessite pas de déplacer son calcul dans la synthèse.

Les routes sont `/modules/craft`, `/modules/opportunites` et `/servers/{serveur}/stock-crushing`. Le bouton « Avec mes stocks » du concassage mène à sa vue métier. Les liens de la synthèse conservent l'objet et la quantité demandée.

## Calcul économique

Le périmètre initial couvre les recettes d'équipements du catalogue local qui utilisent au moins un ingrédient présent dans les stocks. Les ingrédients ressources et équipements sont regroupés par identifiant avant de calculer le lot.

- La quantité réalisable sans achat est le minimum des quotients stock / besoin de chaque ingrédient.
- Les kamas à prévoir incluent les lots réellement nécessaires pour acheter les ingrédients manquants, même en cas de surplus. Les équipements ingrédients sont achetés à l'unité comme dans le calcul existant.
- Le stock consommé est valorisé à son prix de revente ; le posséder ne le rend pas gratuit.
- Le crédit du surplus est le plus bas entre sa valeur de marché et sa part du coût d'achat.
- Coût économique = valeur du stock utilisé + achats − crédit du surplus conservé.
- Gain estimé = recette estimée − coût économique ; ROI = gain / coût économique lorsque ce coût est positif.

Les gains sont bruts avant taxes. Le revenu du concassage utilise l'estimation de jet et la décote des paramètres existants, à coefficient constant pour le lot. Les quantités de runes sont valorisées par lots sur la quantité totale du lot fabriqué. Une stratégie dont certaines runes ne sont pas valorisées ne peut pas gagner le classement.

Les recettes sont des alternatives indépendantes : le même stock peut être proposé dans plusieurs lignes et leurs maxima/gains ne doivent pas être additionnés. Aucune fabrication, vente ou réservation réelle de stock n'est déclenchée.

## Données et interface

Le calcul nécessite un inventaire complet et non obsolète du client ciblé sur le serveur consulté. La banque est facultative : elle est incluse uniquement si son propre relevé est complet et non obsolète. Une banque absente, partielle ou obsolète est entièrement exclue, y compris ses lignes conservées. Les archives ne sont pas substituées au stock courant. Les résultats sont invalidés lors des changements de client, serveur, stock ou prix ; les relances sont regroupées et annulables. Un contrôle de la source et de sa date empêche la publication tardive d'un ancien calcul.

Les vues métier montrent aussi les estimations incomplètes et non rentables. La synthèse conserve uniquement les gains positifs entièrement valorisés, avec filtres d'origine, budget d'achat, gain, ROI et disponibilité sans achat. Elle exclut par défaut les données de plus de douze heures selon la règle existante ; le coefficient compte aussi dans la fraîcheur. La fraîcheur est réévaluée chaque minute.

Chaque résultat affiche sa source, les ingrédients utilisés/manquants, les lots achetés, le surplus, le coefficient et les dates/provenances des prix utilisés. Les prix extrapolés d'un lot sont signalés. Un prix inconnu reste inconnu, jamais zéro.

L'analyse globale est locale et ne récupère pas automatiquement des milliers de coefficients externes. Le calcul de concassage existant conserve sa recherche des seuils par défaut ; le balayage des stocks désactive uniquement cette recherche coûteuse, sans modifier la valorisation.

## Validation

Les tests ciblés couvrent les stocks possédés, recettes regroupées et lots multiples, surplus, ingrédients équipements, grandes quantités, prix manquants, isolation du serveur, annulation, synthèse de plusieurs origines, fraîcheur du coefficient et valorisation des runes par lots. La validation en jeu utilise désormais Pagalom l'Eniripsa ; le nom seul n'est pas un critère suffisant pour choisir la fenêtre de test.


Compilation Windows réussie. Suite complète au 28 octobre 2026 : 1 390 tests réussis, dont 18 tests de calcul/balayage et quatre tests supplémentaires de banque vide et de rejet des formes invalides.

Contrôle réel avec Pagalom l'Eniripsa, niveau 39, sur Tylezia : 128 lignes d'inventaire et banque vide, toutes deux complètes et synchronisées en SQLite et à l'écran. Les types d'objets et quantités par pile sont identiques avant/après les reconnexions ; les UID sont propres à la connexion et peuvent changer à la reconnexion. Le module Craft calcule 543 recettes liées à ces stocks et affiche les ingrédients disponibles/manquants. La synthèse reçoit 1 086 estimations des deux sources, toutes incomplètes avec les prix/coefficients actuellement présents sur Tylezia ; aucun gain n'est donc proposé. Le détail d'Abraknydi Vivitus montre notamment deux Laines de Ramane Strubien disponibles sur cinq requises, soit trois manquantes.

Une reconnexion rapide n'a pas fourni l'attribution de serveur attendue. Le calcul est resté bloqué, puis a fonctionné après retour au choix du serveur et sélection explicite de Tylezia, sans substitution d'archive ni modification manuelle du serveur des relevés. L'écran du module se rafraîchit désormais quand le filtre de consultation change.

## Recalcul et changement de fenêtre

Les notifications de sélection Windows comprennent aussi les changements de premier plan et de titre. Le composant conserve désormais son résultat ou son calcul en cours quand les entrées réelles sont identiques : fenêtre ciblée, source de stock avec ses conteneurs, serveur (sans tenir compte de la casse) et quantité demandée. Une simple alternance BestCrush / Dofus / BestCrush ne relance plus l'analyse.

Un changement de cible, de serveur ou de stock invalide toujours le résultat. Les nouveaux prix du serveur consulté et le bouton Recalculer forcent l'analyse même si les autres entrées restent identiques. Les prix d'un autre serveur ne provoquent pas de recalcul. Les nouveaux conteneurs sont comparés même lorsque leur horodatage est identique.

Validation : test d'intégration du composant Blazor rendu avec les vrais services de sélection et de stock ; rapport conservé à l'identique lors des changements de premier plan, puis nouveau calcul après modification du stock ou des prix du bon serveur, et retrait immédiat du rapport en ciblant une autre fenêtre sans relevé. Suite complète : 1 391 tests réussis.

## Mode inventaire seul — 28 octobre 2026

À la demande de l'utilisateur, une banque non synchronisée ne bloque plus les vues Craft, Concassage avec stocks et Opportunités. Une petite notification fermable indique que seul l'inventaire est utilisé et propose d'ouvrir la banque dans Dofus pour l'inclure. Elle n'a ni voile modal ni validation obligatoire. Une mention « Inventaire seul · banque exclue » reste attachée au résultat ; un retour de focus ou un recalcul manuel ne réaffiche pas une notification déjà fermée tant que la source et le mode restent identiques. La réception d'une banque valide, même vide, retire l'avertissement et relance automatiquement le calcul avec inventaire et banque.

Les prix, le serveur, l'inventaire synchronisé et la logique de rentabilité restent requis comme auparavant. Les archives n'interviennent jamais pour compléter la banque courante. Le sélecteur d'archives n'est pas modifié par cette demande : il reste un outil de consultation des derniers relevés des anciennes sessions, sans historique exhaustif de chaque mouvement.

Validation : 1 394 tests réussis. Trois cas d'intégration vérifient les banques absentes, obsolètes et partielles en contrôlant les quantités disponibles, achats manquants, gains et recettes sélectionnées : aucun objet d'une banque exclue n'est utilisé. Le test du composant rendu vérifie le calcul avec notification et inventaire seul, sa stabilité au changement de fenêtre, puis l'inclusion automatique d'une banque synchronisée et le retrait de la notification.

## Interface limitée à la session courante

À la demande de l'utilisateur, le sélecteur d'archives a été retiré de la page Banque et inventaire. Cette page lit exclusivement la source directe du personnage ciblé dans la session en cours. S'il n'existe aucun relevé courant, elle affiche l'attente de l'inventaire et ne substitue aucun ancien stock. La persistance interne est conservée pour un éventuel futur mode avancé, qui n'est pas implémenté ni exposé.

L'organisation du menu Concassage et la définition du bénéfice/coût économique ne sont pas modifiées dans cette intervention : les arguments sur la distinction entre dépenses à engager et valeur du stock consommé sont présentés à l'utilisateur avant décision.


## Concassage : une seule interface avec stocks facultatifs

Le menu séparé « Avec mes stocks » est remplacé dans le concassage principal par
« Prise en compte des stocks ». Les filtres de type, niveau, runes et critères
économiques restent disponibles. Le lien des opportunités ouvre cet écran avec
`stocks=true` et éventuellement l'équipement ciblé.

La recherche conserve tous les scénarios avant les filtres économiques et prépare
en parallèle de la lecture marché une lecture de trésorerie par équipement, sans
nouvelle requête de prix ni nouveau calcul de runes. La case sélectionne les résultats
préparés puis réapplique les filtres et le tri localement : elle ne lance pas Search,
ne consulte pas SQLite/le réseau et ne recalcule pas les recettes.

En mode stocks, les achats des ingrédients manquants sont recalculés avec les lots
disponibles. « Reste à payer (craft) » remplace « Coût craft » et la valeur de revente
des ingrédients possédés effectivement utilisés est affichée à titre indicatif.
« Gain après achats » = valeur ajustée des runes − achats restants, avant taxes.
La valeur de revente du stock utilisé n'est pas déduite de ce gain de trésorerie.
Un craft sans achat affiche « Sans achat » au lieu d'un ROI nul ou infini.
L'achat direct de l'équipement garde son coût marché ; les stocks s'appliquent ici
aux ingrédients de fabrication, y compris les équipements ingrédients.

Seuls les relevés complets et actuels du personnage ciblé et du même serveur sont
utilisés. L'inventaire suffit : une banque absente, incomplète ou périmée est exclue
avec un avertissement non bloquant. Sans inventaire utilisable, l'écran indique
explicitement qu'il affiche le calcul standard. Les changements réels de stocks
préparent les nouvelles projections sur un worker, avec protection contre les résultats
obsolètes ; un changement de focus seul ne les invalide pas. La lecture économique
de l'agrégateur Opportunités conserve sa valorisation du stock au prix de revente.

Validation : StockCrushingToggleTest vérifie les lots, le gain après achats, les
ingrédients possédés sans prix connu, la banque exclue, le changement de cible,
la restitution des résultats filtrés, le classement et 200 bascules sans accès au
planificateur ou aux services de prix/concassage, ainsi que le rendu des montants.


## Craft limité aux 100 meilleures recettes

L'onglet Craft affiche au maximum 100 recettes, sans bouton d'extension.
La limite est appliquée après l'ensemble des filtres et le tri choisi : gain,
rentabilité ou kamas à prévoir. Un changement de filtre peut donc faire entrer
une recette qui ne figurait pas dans les 100 résultats précédents.
Le classement filtré est évalué une seule fois par rendu, puis partagé entre le
compteur et le tableau, afin d'éviter plusieurs tris de la même liste.
L'agrégateur Opportunités conserve son comportement d'affichage existant.


## Fluidité de Craft et actualisation en arrière-plan

- Le scan de Craft ne lance que son fournisseur : aucun calcul de concassage,
  ni lecture des prix de runes et coefficients. L'agrégateur conserve les deux sources.
- Les lectures SQL et le planificateur s'exécutent dans un worker, toujours
  sérialisé par service pour protéger le contexte EF lors des annulations/navigation.
- Cache RAM borné à huit rapports et huit jeux de prix, catalogue réutilisé.
  Les clés incluent la session de stock complète, serveur, quantité, origine,
  révision de marché, priorité des sources et paramètres de concassage pertinents.
  Le bouton Recalculer force une relecture du catalogue et du marché.
- Les changements de prix invalident les caches concernés, même pour une vue
  fermée. Une mise à jour de runes ne relance pas Craft. Les rafales sont
  regroupées sur 350 ms ; une mise à jour pendant le calcul programme une passe
  supplémentaire, pour ne pas perdre la dernière observation.
- Le tableau conserve ses résultats pendant l'actualisation, avec un spinner
  discret et un libellé explicite de résultats précédents si le stock, les prix
  ou la quantité ont changé. Le rapport d'une autre session ou d'un autre serveur
  est immédiatement masqué et un worker dépassé ne peut pas le republier.
- StockOpportunityRow construit les ingrédients/prix détaillés uniquement à
  l'ouverture de sa fiche. Une ouverture actualise sa ligne seule. Les résumés
  inchangés ne sont pas rendus à nouveau et le classement filtré est mémorisé.
  La limite Craft reste de 100 lignes, appliquée après filtres et tri.

Validation automatisée : cache sans requête SQL sur réutilisation, invalidation
des prix/stocks/quantités/serveurs et recalcul forcé, Craft sans Concassage,
détails uniquement à la demande, filtres utilisables pendant un worker bloqué,
coalescence des notifications, dernière actualisation conservée et annulation
au changement de personnage. Suite complète : 1 408 tests réussis.

Mesure reproductible de rendu serveur (220 candidats, 100 lignes affichées,
8 ingrédients et 25 observations de prix par ligne) : HTML de 571 031 à environ
76 239 octets ; 800 lignes d'ingrédients et 2 500 observations masquées ne sont
plus construites au départ. Médiane des rendus locaux : 11,7 ms avant, entre
1,5 et 2,2 ms après selon la charge des tests. Ces chiffres mesurent le rendu
du composant, pas un gain garanti sur l'ensemble de l'application ou le GPU.
