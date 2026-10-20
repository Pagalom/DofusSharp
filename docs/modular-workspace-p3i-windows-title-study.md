# P3i — Fenêtres Dofus et sélecteur de clients (étude préalable)

## Décision de produit

BestCrush doit supporter plusieurs clients/comptes Dofus simultanément
lorsqu'ils sont autorisés dans le contexte du jeu. Le projet **ne doit
pas** être réduit à un personnage unique. Les profils sont exclusivement
reconstruits en RAM et disparaissent à la fermeture de BestCrush.

Le composant `BestCrush/Components/Layout/MainLayout.razor` dispose
déjà de boutons de personnage en haut à droite, bordure active et
silhouette provisoire. `CurrentCharacterState` conserve plusieurs
fiches éphémères, mais le code réseau de production ne valide pas
encore leur rattachement.

## Essai 1 — Titres des fenêtres, PID et focus Windows

```powershell
powershell -NoProfile -File .\scripts\probe_dofus_windows.ps1 -WatchSeconds 30
```


Ce test n'utilise que les API Windows `EnumWindows`,
`GetWindowText`, `GetWindowThreadProcessId` et
`GetForegroundWindow`. Il ne lance pas Npcap et n'écrit
aucun fichier ou profil.

Scénarios :

1. Ouvrir un client et charger un personnage ; vérifier sur son PC que
   le **titre réel** de la fenêtre contient bien le nom du personnage.
2. Pendant que le test surveille les fenêtres, ramener Dofus au
   premier plan. Vérifier l'événement `focus-dofus`.
3. Revenir à la sélection du personnage, puis en charger un autre ;
   observer si le **titre change**, et si le PID/HWND demeure ou non.
4. Si deux clients Dofus sont disponibles et autorisés sur le serveur,
   ouvrir le second client, puis alterner leur focus. Vérifier que
   deux fenêtres sont recensées et que les PID/HWND différencient
   les deux clients (sans supposer un PID par client avant observation).
5. Fermer/recréer un client : vérifier que le nouveau cycle est bien
   distingué de l'ancien.

Pour transmettre une sortie dépourvue de noms personnels :

```powershell
powershell -NoProfile -File .\scripts\probe_dofus_windows.ps1 -WatchSeconds 30 -RedactTitles
```


**Confidentialité :** le titre affiché localement peut comprendre un
nom de personnage. Ne pas coller les titres réels dans les tickets,
logs, documents ou conversations. L'outil ne conserve aucun titre.
La version masquée permet seulement de comparer nombre et longueur
des titres ; l'opérateur confirme lui-même le contenu localement.

## Portée des résultats

- **Fenêtre ouverte en dernier** : la seule énumération des fenêtres
  ne donne PAS leur heure de création. Ne pas confondre ordre des
  fenêtres et chronologie d'ouverture.
- **Fenêtre au premier plan** : Windows fournit un indicateur explicite
  via `GetForegroundWindow`, utile pour basculer la sélection UX.
- **Titre/PID/HWND** : indices d'une instance locale et de son
  personnage affiché, PAS une preuve du compte Dofus ni du serveur.
  Une même fenêtre peut changer de personnage et un PID peut être
  réutilisé après fermeture.
- **Correspondance réseau ↔ fenêtre** : doit être prouvée
  indépendamment pour chaque session TCP, notamment lorsque plusieurs
  clients tournent ensemble. Un titre ne suffit jamais à attribuer
  un `ipz`, des kamas ou un inventaire.
- L'existant `DofusWindowService.GetActiveDofusWindow()` retourne
  aujourd'hui la première fenêtre énumérée en l'absence de Dofus
  au premier plan : ce comportement **ne doit pas servir de preuve**
  automatique en multicomptes. Une version future devra distinguer
  fenêtre réellement active et sélection manuelle.

## Interface cible, après test

Un bouton compact par **client identifié** dans l'angle supérieur
droit : illustration de classe ou de personnage uniquement avec une
source d'image réellement disponible et autorisée, bordure indiquant
le client ciblé et infobulle avec le personnage.

Cliquer choisit explicitement le client/session affiché par BestCrush ;
cela ne réattribue jamais rétroactivement des paquets réseau à cette
session. Toutes les données observées restent séparées par connexion.
La liaison `AccountServerKey` n'est activée qu'après preuve
indépendante de compte et serveur. Des personnages successifs peuvent
appartenir au même compte : une fenêtre n'est pas égale à un compte.

Aucune persistance de profil. Ne pas toucher à `main`,
ne pas merger/publier, ne pas supprimer les garde-fous multiclients.


## Essai 1 — observation réelle (20 octobre 2026, heure de Paris)

Le test de 30 secondes a été exécuté avec **un seul client Dofus**.
Aucune fenêtre n'existait au démarrage du probe, puis une fenêtre
est apparue et est restée associée à un même PID et HWND pendant
les transitions de son titre.

Chronologie locale confirmée, **sans recopier le nom du personnage,
PID ou HWND** :

```text
aucune fenêtre → apparition: "Dofus"
→ "Dofus 3.7.12.12 - Release"
→ "[nom personnage] - Ecaflip - 3.7.12.12 - Release"
```


Le focus sur la fenêtre Dofus a été détecté, perdu lors d'un passage
sur une autre application, puis détecté à nouveau sur la même
fenêtre. Les événements de focus sont donc cohérents avec l'usage
observé. **La lecture du titre et le suivi du focus sont validés
pour ce scénario à fenêtre unique.**

Restent **non testés** :
- plusieurs clients simultanés et le basculement entre leurs fenêtres ;
- le changement de personnage dans une même fenêtre ;
- le rattachement sécurisé des flux réseau à chaque HWND/PID ;
- le statut compte/serveur, toujours non vérifié.

**Changement de build observé : `3.7.12.12`** alors que les
études de protocole et de mapping `ipz` utilisaient
`3.6.11.15`. Les résultats de P3f/P3g ne constituent
pas à eux seuls une validation du décodage des métiers sur ce nouveau
build. Un contrôle de compatibilité distinct sera nécessaire avant
de brancher le décodage réseau sur des données de production.

**Décision de conception :** le titre apporte un indice local
nom/classe/build et le HWND identifie une fenêtre vivante dans son
cycle courant. Aucun de ces éléments n'est un identifiant de compte
et aucun ne garantit l'origine d'un message réseau. Le sélecteur
en haut à droite peut s'appuyer sur les fenêtres pour l'UX, mais
les données de métier, d'inventaire et de kamas attendront une
attribution réseau ↔ client vérifiée.
