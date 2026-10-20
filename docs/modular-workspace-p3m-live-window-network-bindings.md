# P3m.1 — Lien flux Npcap ↔ fenêtre Dofus dans BestCrush (RAM)

## Validation expérimentale préalable

P3k a démontré le 20 octobre 2026, en conditions réelles, deux
associations distinctes, chacune fondée sur l'égalité exacte des
quatre éléments de la connexion TCP observée par Npcap et par Windows,
puis sur le propriétaire PID et une fenêtre HWND correspondante :

- flux 1 → fenêtre 2 ;
- flux 2 → fenêtre 1 ;
- `flow-matched=2 unique-windows-linked=2`.

P3l a également été validé visuellement par l'opérateur : suivi du
dernier personnage utilisé, choix manuel du client, disparition du
bouton lorsque sa fenêtre Dofus se ferme.

## Étendue du changement P3m.1

Le service de capture Windows actif construit désormais **uniquement
une association en mémoire** entre un quadruplet TCP et un HWND/PID,
via le même principe que P3k, sans publier de valeurs réseau.

- `WindowsTcpSocketTableReader` lit les sockets IPv4 Windows.
- `LiveDofusClientNetworkBindings` refuse les liens ambigus,
  distingue les fenêtres, isole les cycles de capture et révoque les
  associations lors de la perte de leur socket ou fenêtre.
- `DofusNetworkCaptureService` inspecte le tuple des paquets
  au cours de la capture active ; les listes de fenêtres et sockets
  sont rafraîchies au maximum une fois par seconde.
- `MainLayout.razor` affiche un petit point vert sur une
  fenêtre dont un flux TCP a été reconnu. L'infobulle précise que
  **le compte reste non vérifié**.
- Le serveur choisi manuellement ne prouve pas le serveur du flux.
- Aucun profil, ID de compte, titre de fenêtre, IP, tuple TCP,
  inventaire ou métier n'est sauvegardé.

Cette phase est **une préparation de routage, sans attribution** :
la liste des liens ne sert pas encore à diriger les objets du jeu
vers les fiches personnages. Il faut au préalable isoler les caches
du `DofusNetworkMessageProcessor` par flux (actuellement
communs pour les achats et détails d'équipements) et revérifier le
décodage métier sur Dofus **3.7.12.12**, alors que les validations
antérieures portaient sur **3.6.11.15**.

Aucun changement à la persistance existante des prix marché, aux
overlays, à la gestion des raccourcis, à la version produit ou à
la branche main.

## Validation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```


Ouvrir deux clients Dofus et BestCrush, avec la capture réseau
active. Après un échange réseau sur chaque client, vérifier que les
deux boutons peuvent afficher un petit indicateur vert. Fermer une
fenêtre et vérifier la disparition du bouton; arrêter la capture
et vérifier la remise à zéro des liens. La présence d'un voyant
représente seulement le lien réseau local, PAS la vérification
des données métiers, de compte ou de serveur.

Sept nouveaux tests unitaires synthétiques vérifient isolation,
absence de propriétaire, collision, fermeture de socket/fenêtre,
changement interdit de propriétaire, redémarrage et réinitialisation.
