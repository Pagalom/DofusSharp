# Objectifs Familiers et Élevage

## Familiers

- **Mes familiers** contient uniquement les exemplaires relevés dans l’inventaire et la banque synchronisés.
- **À faire** présente au maximum cinq opportunités d’achat, de nourrissage et de revente rentables, avec un volume de vente exploitable. Les consultations HDV concernent les exemplaires possédés et cette sélection, pas le catalogue entier.
- Plusieurs exemplaires peuvent avancer simultanément. Le stock de nourriture reste pris en compte, avec recalcul après consommation. Le stock possédé ne constitue pas une promesse de renouvellement par farm.
- Le budget de travail, facultatif et saisi par le joueur, limite les achats et leur nourriture. Il ne s’agit pas d’un solde bancaire détecté. Les recommandations restent des comparaisons indépendantes, pas des achats automatiques.

## Capture et capacité de production

La cadence de capture est modifiable dans À faire et les paramètres. En mode automatique, les valeurs de départ sont 20 / 40 / 60 / 120 par jour aux niveaux d’éleveur 1 / 100 / 150 / 200. Ce sont des hypothèses de planification choisies avec le joueur, pas des limites imposées par Dofus. Les filets débloqués aux mêmes paliers capturent une cible, la dupliquent, capturent une zone, puis dupliquent la zone. Le prix des filets reste exclu.

Le classement utilise la marge, les débouchés et la cadence : la production hebdomadaire d’un parcours est limitée par ses captures attendues, puis par le marché et le capital renseigné. Un parcours long indique son besoin de captures et le nombre de jours de capture ; cette durée ne remplace pas le temps de préparation/XP en enclos. Les achats n’ont aucun plafond de capture. Les cibles déjà possédées sont disponibles sans nouvelle capture.

Référence des filets : https://www.dofuspourlesnoobs.com/les-dragodindes.html#capturer

## Progression et encaissement

- Les acquisitions, l’XP et les jauges font progresser le suivi à partir des observations de jeu. L’atteinte d’un résultat ne prétend pas prouver les parents d’une naissance.
- Les dépôts confirmés sont corrélés à leur requête sur la même connexion et aux caractéristiques de l’exemplaire avant sa sortie de l’inventaire. L’UID de l’annonce est distinct de celui de l’objet.
- Un dépôt place l’exemplaire **en vente à 95 %**. Il ne valide ni vente encaissée ni bénéfice réalisé.
- L’historique personnel `kxt`, observé le 19/11/2026, contient des montants, dates, objets, quantités, effets éventuels et le statut d’invendu. Le relevé réel de 1 000 opérations a été confronté à l’interface : 989 ventes et 11 invendus.
- Un encaissement clôt le cycle uniquement après rapprochement avec un dépôt suivi : même objet, montant, chronologie et caractéristiques compatibles. Sans effets, des niveaux/pouvoirs différents ne sont pas confondus. Les exemplaires équivalents se rapprochent dans l’ordre des dépôts.
- Un invendu rend l’étape de remise en vente disponible et ne compte aucun cycle encaissé. La relecture du même historique ne compte aucune opération deux fois.
- Les journaux persistent par contexte de planification joueur/serveur, sans utiliser ce contexte comme identité réseau. Un journal endommagé est conservé.

La mise à jour des encaissements dépend de la réception de l’historique personnel par le client (notamment lors de son ouverture depuis l’inventaire). Aucun message individuel de vente en direct n’est supposé décodé sans observation.

## Validation

Tests de progression partielle et parallèle, conservation à la relance, quotas/capital, paliers de capture, dépôts, ventes, invendus, variantes, doublons et cloisonnement fenêtre/serveur. Le décodeur d’historique a également été rejoué hors ligne sur le relevé réel, conservé hors du dépôt.
