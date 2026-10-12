# P1a — Exploration du protocole personnage sans payload

**Branche** : `feature/modular-workspace`.

**Pré-requis** : le checkpoint Windows M2/P0 n'a pas encore été communiqué. Ce lot
est strictement limité au projet `BestCrush.NetworkProbe` et à un composant
reusable `BestCrush.Network`. Il ne modifie pas BestCrush en exécution, son
stockage, le décodage métier, les captures de prix ou la gestion des overlays.

## Périmètre et protection des données

La correspondance des messages Dofus aux informations suivantes reste
**inconnue et non vérifiée** : identité du personnage, classe, niveau,
déconnexion logique, métiers, inventaire, kamas en poche, banque et kamas
en banque.

`--character-scan` affiche seulement l'heure UTC, un compteur anonyme de
session TCP, la direction, un opcode assaini et la taille du corps du message.
Ni adresse/port réseau, ni protobuf brut, ni nom, ni ID de compte, ni inventaire
ou banque ne sont affichés ou sauvegardés par ce mode.

Le traqueur utilise une paire de réassembleurs TCP par connexion (4-tuple
bidirectionnel), réinitialise les frames lors d'un SYN après trafic, marque
FIN/RST même sans charge utile et borne les flux à 128 entrées in-memory
(inactivité maximale : 30 min). **La fin TCP n'est pas synonyme de preuve de
déconnexion logique du personnage.** Les connexions persistantes ou basculées
par le jeu exigent une étude complémentaire.

Le nouveau mode d'analyse ne passe pas par `DofusCaptureProbe` et évite ses
diagnostics historiques susceptibles d'afficher des champs sensibles.

## Lancement Windows

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device <INDEX> --character-scan
```

Le `<INDEX>` est l'index de carte Npcap retourné par `--list`.
Le port utilisé est TCP/5555 (sauf `--port` explicitement renseigné).

## Session d'observation manuelle

Faire plusieurs essais en **une seule instance Dofus** pour commencer.
Noter précisément la seconde d'occurrence de chaque geste :

1. Attendre 10 secondes dans le jeu pour établir une base de bruit.
2. Entrer sur personnage A ; noter la seconde.
3. Ouvrir/caractéristiques de A ; noter la seconde.
4. Ouvrir ses métiers (J) ; noter la seconde.
5. Ouvrir l'inventaire (I), fermer puis rouvrir ; noter les secondes.
6. Ouvrir la banque (si accessible) puis fermer ; noter les secondes.
7. Retour à sélection et connexion sur B du même serveur ; ouvrir caractéristiques,
   métiers, inventaire, banque (si accessibles).
8. Retour sur A et répéter, puis déconnexion réelle de Dofus.
9. Refaire une session avec deux clients actifs seulement après validation
   qu'une seule connexion est reconstruite correctement.

Comparer les lignes des opcodes et leurs longueurs autour de chaque geste.
**Ne pas inférer le sens d'un opcode sur une seule corrélation temporelle.**
Faire plusieurs répétitions pour éliminer les messages de heartbeat et
chargement de carte.

Les observations suivantes devront être distinguées :

- écran de sélection (plusieurs personnages connus) vs personnage réellement
  connecté ;
- inventaire complet vs mises à jour partielles ;
- banque ouverte vs contenu déjà en cache ;
- kamas poche vs kamas banque ;
- métiers et niveaux vs XP de progression ;
- déconnexion TCP vs changement d'avatar dans la même connexion.

## Prochaines étapes avant lecture effective

- Extraire des structures protobuf candidates, localement, avec des
  **fixtures anonymisées** qui ne contiennent ni identifiant de compte ni
  données personnelles.
- Valider la sémantique sur plusieurs connexions, plusieurs personnages et
  plusieurs ouvertures d'interface.
- Ajouter les mapping versionnés, décodeurs, corrélation fiable à la fenêtre
  Dofus ciblée, garde-fous face aux clients multiples.
- Transmettre seulement ensuite des observations vérifiées au
  `CharacterIdentityCorrelator` et à `CurrentCharacterState` (RAM).
- Vérifier que le mode de debug historique de BestCrush ne conserve pas de
  données liées à un personnage après fermeture si l'exigence « zéro
  persistance du profil » doit également couvrir les diagnostics bruts.

**Arrêt au checkpoint Windows. Pas de M3 ni de synchronisation automatique
présumée tant que le protocole n'est pas caractérisé.**
