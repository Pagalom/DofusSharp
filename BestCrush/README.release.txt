BestCrush v0.1.5
==================

BestCrush est un outil compagnon pour Dofus permettant de :

- suivre les prix locaux des runes, ressources et équipements ;
- calculer le coût réel minimum des crafts ;
- estimer la rentabilité du concassage ;
- choisir entre un jet moyen ou un jet prudent pour l'estimation du concassage ;
- lire automatiquement certaines données en jeu ;
- suivre les runes réellement obtenues lors d'une session de concassage ;
- trier les résultats par bénéfice, rendement, nom ou coefficient.

NOUVEAUTÉS v0.1.5
-----------------

- prise en charge des équipements utilisés comme ingrédients de recette ;
- amélioration de la reconnaissance OCR des équipements en HDV ;
- remise à zéro cohérente des prix locaux ;
- remise à zéro d'un coefficient local avec retour au coefficient DoFocus disponible ;
- affichage détaillé des lots rune / ressource dans "Mise à jour marché" ;
- valeurs et prix cliquables dans les overlays ;
- détail du calcul de valorisation des runes pendant une session de concassage ;
- affichage de la date du coefficient utilisé dans l'overlay Rentabilité ;
- amélioration de l'affichage et de la copie des résultats de concassage.

INSTALLATION
------------

1. Extraire entièrement cette archive dans un dossier.
2. Lancer BestCrush.exe.
3. Sélectionner votre serveur dans BestCrush avant toute capture.

Ne lancez pas BestCrush.exe directement depuis l'archive ZIP.

PRÉREQUIS
---------

- Windows 10/11 64 bits
- Microsoft Edge WebView2 Runtime

Le runtime .NET et les dépendances Windows nécessaires sont inclus dans cette distribution.

RACCOURCIS
----------

Clic molette : lecture contextuelle
F7           : masquer / restaurer les overlays
F9           : démarrer / arrêter une session de concassage

COPIER / COLLER DANS LES OVERLAYS
---------------------------------

Principe général :
- clic sur un nom : copie le nom ;
- clic sur un prix ou une valeur : copie uniquement la valeur numérique,
  sans espaces ni "K".

Overlay Rentabilité :
- clic sur le coefficient : copie uniquement la valeur, sans le symbole "%" ;
- survol du coefficient : affiche la date de l'observation au format JJ/MM/AAAA ;
- clic sur cette date : copie la date ;
- les prix, valeurs de runes et coûts d'ingrédients affichés sont copiables.

Overlay Mise à jour marché :
- le nom de l'objet est copiable ;
- les prix détectés et les prix effectivement utilisés sont copiables ;
- les lots x1 / x10 / x100 / x1000 d'une rune ou d'une ressource sont affichés
  lorsqu'ils ont été reconnus.

Overlay Résultat concassage :
- clic sur le nom d'une rune : copie son nom ;
- clic sur sa quantité : copie la quantité ;
- clic sur sa valeur totale : copie la valeur numérique ;
- clic sur un terme du détail des lots : copie une formule Excel,
  par exemple "=2*99000" ;
- double-clic sur le détail des lots : copie la formule Excel complète,
  par exemple "=2*99000+4*9900+5*990+2*99" ;
- clic sur la valeur réelle totale : copie le total.

Les grands nombres peuvent être abrégés visuellement (k, M, Md), mais les valeurs
copiées restent toujours exactes.

DONNÉES
-------

Les équipements, ressources, recettes, caractéristiques et le catalogue des runes
proviennent de DofusDB.

Un équipement utilisé comme ingrédient de recette reste un équipement.
Pour le coût du craft parent, BestCrush utilise son prix local d'achat x1.

DoFocus est utilisé comme coefficient initial / de repli lorsqu'aucun coefficient
local actif n'est disponible.

IMPORTANT
---------

Lors d'une session F9, ne faites pas défiler le panneau de résultats.
Un scroll invalide volontairement la session afin d'éviter les doubles comptages.

BestCrush utilise de la reconnaissance visuelle/OCR. Certaines lectures peuvent
être affectées par la résolution, l'échelle d'affichage ou des modifications de
l'interface Dofus.

Les captures d'écran temporaires utilisées pour la reconnaissance sont supprimées
automatiquement une fois qu'elles ne sont plus nécessaires au traitement.

PROJET
------

Code source et nouvelles versions :
https://github.com/Pagalom/DofusSharp

BestCrush est un projet communautaire non officiel.
Il n'est ni développé, ni sponsorisé, ni approuvé par Ankama.
