# Routine économique hebdomadaire

Implémentation du 5 novembre 2026, à partir de la transcription et du schéma fournis par l’utilisateur pour « La ROUTINE en MONOCOMPTE sur DOFUS », HUZ REPLAY : https://www.youtube.com/watch?v=s1B8Oo2Pcow . Les métadonnées publiques ont été lues ; les sous-titres n’étaient pas récupérables par YouTube et les points parlés proviennent donc de la transcription utilisateur, pas d’un visionnage revendiqué.

## Périmètre retenu

Six objectifs économiques hebdomadaires de départ : valorisation des ressources accumulées, vérification des missions de guilde, comparaison des échanges de Guildatons, choix d’une récolte/craft, examen du concassage/FM, puis ventes et invendus. Les boutons ouvrent les services existants. Le bouton Concassage respecte le serveur de consultation. Les missions, soldes, quotas et achats sont vérifiés/effectués dans Dofus, sans lecture supposée de ces données ni opération automatisée.

Le contenu Quêtes Dofus/Tour du monde, les avis/archimonstres et la coordination sociale ne deviennent pas de nouveaux services. Le joueur peut ajouter un objectif personnel sans créer de suivi automatique de quête. Aucune liste de quêtes journalières étrangères à la transcription n’a été ajoutée au catalogue initial.

## Organisation

Routine personnelle indépendante du personnage/serveur consulté, clairement indiquée dans l’interface. Nom et niveau facultatif configurables, priorités 1/2/3, mise de côté réversible, notes, estimation nette par réalisation, ajout d’objectifs hebdomadaires ou quotidiens. Le classement correspond aux priorités choisies et à la progression, pas à une prédiction automatique de bénéfice. Les gains sont inconnus initialement, jamais inventés. Les gains négatifs restent possibles dans les estimations ; les montants ne représentent ni des ventes encaissées ni des modifications des stocks.

Planning mardi → lundi par défaut, jour de départ modifiable. Ce calendrier est celui du suivi personnel et ne fixe pas une heure de réinitialisation des quêtes du jeu. Dates calculées dans Europe/Paris (mêmes décalages actuels que le fuseau utilisateur Europe/Berlin), avec prise en compte de l’heure d’été. Horloge réévaluée chaque minute tant que le composant est ouvert ; une nouvelle semaine présente de nouvelles coches sans supprimer les validations passées. Les semaines précédentes sont consultables en lecture seule ; les futures ne peuvent pas être validées. Les objectifs quotidiens gardent une coche par date.

## Persistance et fluidité

Fichier Data/weekly-routine.json : configuration et dates de validation uniquement, sans identité réseau, kamas du personnage, inventaire, banque ou historique de ventes. Chargement/sauvegarde hors rendu, écriture atomique et copie de retour en cas d’échec. Un fichier corrompu n’est ni effacé ni réinitialisé. L’interface signale une sauvegarde impossible et revient à la dernière configuration conservée.

Le chargement et la personnalisation ne déclenchent aucun calcul des autres onglets ni accès réseau. La routine est utilisable sans Dofus ouvert ni serveur choisi. Une estimation manquante reste « À renseigner » ; une somme partielle est identifiée comme incomplète.

Validation : 101 tests ciblés réussis, couvrant routine, navigation et régression FM. Tests spécifiques : semaines aux changements d’année et en heure d’été, validation/dévalidation, pas de coche future, priorités et niveau, conservation de l’historique, montants inconnus/négatifs, sauvegarde atomique et protection des fichiers endommagés, rendu avec les liens serveur et sans dépendance au jeu/réseau.
