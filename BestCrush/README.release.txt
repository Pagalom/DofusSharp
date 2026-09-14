BestCrush v0.1.7
==================

BestCrush est un outil compagnon pour Dofus permettant de :

- suivre les prix locaux des runes, ressources et équipements ;
- calculer le coût réel minimum des crafts ;
- estimer la rentabilité du concassage ;
- suivre les runes réellement obtenues ;
- consulter l'historique du marché et des concassages ;
- comparer plusieurs évolutions de marché simultanément ;
- enregistrer des recherches et des analyses réutilisables.

NOUVEAUTÉS v0.1.7
-----------------

ANALYSES DE MARCHÉ

- ajout de l'onglet Analyses dans la page Historique ;
- analyse simultanée de plusieurs runes, ressources ou équipements ;
- possibilité d'ajouter plusieurs variantes du même élément ;
- périodes disponibles : 24 heures, 7 jours, 30 jours ou tout l'historique ;
- agrégations disponibles : médiane, moyenne, Q1 et Q3 ;
- sélection des données capturées en jeu, manuelles ou toutes sources ;
- transformation en Base 100 utilisée par défaut ;
- valeur brute toujours disponible comme transformation alternative ;
- conversion obligatoire en prix unitaire lorsque tous les lots sont regroupés ;
- enregistrement et réutilisation des analyses sous forme de presets ;
- cartes interactives avec les véritables icônes Dofus ;
- couleur stable permettant d'associer chaque carte à sa courbe ;
- mise en évidence de la carte et de la courbe sélectionnées ;
- affichage d'un tooltip lors du survol des points ;
- affichage simultané de l'indice et de la valeur réelle en Base 100 ;
- copie des noms et des valeurs pertinentes.

RENTABILITÉ ET RECHERCHE

- ajout d'une décote configurable sur la valeur théorique des runes ;
- décote de 5 % appliquée par défaut ;
- conservation de la décote utilisée dans l'historique des concassages ;
- calcul du prix maximum acceptable à l'achat et au craft ;
- calcul du coefficient minimum nécessaire selon le ROI cible ;
- ajout de filtres économiques avancés ;
- filtres multi-runes avec modes TOUTES et AU MOINS UNE ;
- filtres de complétude, de fraîcheur et de coefficient ;
- ajout de presets de recherche.

HISTORIQUE DU MARCHÉ

- conservation temporelle des observations de prix ;
- historique propre à chaque serveur ;
- regroupement temporel des observations proches ;
- limitation des doublons pour les prix identiques rapprochés ;
- conservation des confirmations de prix espacées dans le temps.

INSTALLATION
------------

1. Extraire entièrement cette archive dans un dossier.
2. Lancer BestCrush.exe.
3. Sélectionner votre serveur dans BestCrush avant toute capture.

Ne lancez pas BestCrush.exe directement depuis l'archive ZIP.

PRÉREQUIS
---------

- Windows 10 ou Windows 11 64 bits
- Microsoft Edge WebView2 Runtime

Le runtime .NET et les dépendances Windows nécessaires sont inclus dans cette distribution.

RACCOURCIS
----------

Clic molette : lecture contextuelle
F7           : masquer ou restaurer les overlays
F9           : démarrer ou arrêter une session de concassage

COPIE DES DONNÉES
-----------------

- clic sur un nom : copie le nom ;
- clic sur un prix ou une valeur : copie uniquement la valeur numérique ;
- clic sur un coefficient : copie sa valeur sans le symbole "%" ;
- les détails de valorisation des runes peuvent être copiés sous forme de formules Excel.

DONNÉES
-------

Les équipements, ressources, recettes, caractéristiques et runes proviennent de DofusDB.

DoFocus est utilisé comme coefficient initial ou de repli lorsqu'aucun coefficient local actif n'est disponible.

IMPORTANT
---------

Lors d'une session F9, ne faites pas défiler le panneau de résultats.
Un scroll invalide volontairement la session afin d'éviter les doubles comptages.

BestCrush utilise de la reconnaissance visuelle et de l'OCR.
Certaines lectures peuvent être affectées par la résolution, l'échelle d'affichage ou des modifications de l'interface Dofus.

PROJET
------

Code source et nouvelles versions :
https://github.com/Pagalom/DofusSharp

BestCrush est un projet communautaire non officiel.
Il n'est ni développé, ni sponsorisé, ni approuvé par Ankama.