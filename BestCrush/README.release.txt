BestCrush v0.2.0
==================

BestCrush est un outil compagnon pour Dofus permettant de :

- suivre les prix locaux des runes, ressources et équipements ;
- calculer le coût réel minimum des crafts ;
- estimer la rentabilité du concassage ;
- suivre les runes réellement obtenues ;
- consulter l'historique du marché et des concassages ;
- comparer plusieurs évolutions de marché simultanément ;
- enregistrer des recherches et des analyses réutilisables.

NOUVEAUTÉS v0.2.0
-----------------

CAPTURE RÉSEAU PASSIVE

- remplacement des anciennes lectures OCR de marché par une capture réseau passive ;
- récupération automatique des prix HDV depuis le trafic Dofus ;
- mise à jour des prix des ressources et runes immédiatement après un achat lorsque le nouveau palier est reçu ;
- récupération directe des résultats de concassage, coefficients, runes et quantités ;
- conservation locale des observations et alimentation de l'historique ;
- aucun mécanisme d'injection dans le processus Dofus.

FOCUS ET RACCOURCIS

- clic molette : met en focus le dernier équipement identifié de façon fiable sur le réseau ;
- F8 : lit uniquement l'infobulle de l'équipement actuellement survolé par OCR et le met en focus ;
- F7 : masque ou restaure les overlays visibles ;
- F9 : désormais libre / non attribué ;
- un focus ouvre automatiquement l'overlay Rentabilité ;
- un concassage détecté ouvre automatiquement l'overlay Résultat concassage.

OVERLAYS

- ajout du niveau à côté du nom de l'équipement dans l'overlay Rentabilité ;
- ajout du scroll vertical lorsque le contenu dépasse la hauteur disponible ;
- liste des runes du résultat de concassage scrollable ;
- détails de marché scrollables ;
- bulles d'information de l'overlay Rentabilité repositionnées au-dessus ou en dessous de la ligne survolée afin de ne pas masquer le curseur ;
- bulles d'information longues elles-mêmes scrollables ;
- affichage du prix d'achat d'un équipement dans les résultats incomplets lorsque ce prix est connu.

NPCAP

La capture réseau passive nécessite Npcap sous Windows.

BestCrush ne distribue pas Npcap.

Au démarrage :
- si Npcap est détecté, la capture réseau démarre normalement ;
- s'il est absent ou incomplet, BestCrush reste utilisable mais les fonctions réseau sont désactivées ;
- un bandeau permet d'ouvrir la page officielle de téléchargement Npcap ;
- après installation, le bouton "Revérifier" permet de tenter de démarrer la capture sans redémarrer BestCrush.

INSTALLATION
------------

1. Installer Npcap depuis le site officiel si ce n'est pas déjà fait.
2. Extraire entièrement l'archive BestCrush dans un dossier.
3. Lancer BestCrush.exe.
4. Sélectionner votre serveur dans BestCrush.

Ne lancez pas BestCrush.exe directement depuis l'archive ZIP.

PRÉREQUIS
---------

- Windows 10 ou Windows 11 64 bits
- Microsoft Edge WebView2 Runtime
- Npcap installé séparément par l'utilisateur

Le runtime .NET et les dépendances Windows nécessaires sont inclus dans cette distribution.
Npcap n'est pas inclus dans l'archive BestCrush.

RACCOURCIS
----------

Clic molette : focus sur le dernier équipement identifié sur le réseau
F8           : lecture OCR de l'infobulle d'équipement
F7           : masquer ou restaurer les overlays
F9           : non attribué

DONNÉES
-------

Les équipements, ressources, recettes, caractéristiques et runes proviennent de DofusDB.

DoFocus peut être utilisé comme coefficient initial ou de repli lorsqu'aucun coefficient local actif n'est disponible.

Les prix, coefficients et résultats de concassage récupérables en jeu sont désormais principalement alimentés par la capture réseau passive.

LIMITATIONS
-----------

Le décodage réseau dépend du protocole de la version courante de Dofus.
Une mise à jour du jeu peut nécessiter une adaptation de BestCrush.

La lecture F8 repose encore sur une capture visuelle et de l'OCR ; elle peut donc être affectée par la résolution, l'échelle d'affichage ou des modifications de l'interface Dofus.

PROJET
------

Code source et nouvelles versions :
https://github.com/Pagalom/DofusSharp

BestCrush est un projet communautaire non officiel.
Il n'est ni développé, ni sponsorisé, ni approuvé par Ankama.
