# P3m.2 — Isolation des corrélations métiers par connexion TCP

## État précédent

L'essai P3m.1 a été confirmé par l'utilisateur : les deux clients
Dofus sont reconnus par BestCrush et les indicateurs de liaison
fenêtre ↔ TCP fonctionnent. Le focus, la sélection manuelle et la
disparition des boutons avaient déjà été validés en P3l.

Dans le processeur de production, les caches `RecentItemDetailsCache`
et `_pendingPurchaseRequest` étaient communs à tous les flux
du worker. Deux personnages peuvent ainsi présenter le même UID
d'équipement ou identifiant d'offre. La séparation des flux au niveau
TCP ne suffisait pas à protéger la corrélation métier.

## Changement P3m.2

- Un identifiant de connexion **opaque, positif et volatil** est
  associé au quadruplet TCP normalisé, identique pour les deux sens
  d'une même connexion.
- `DofusWireMessage.ConnectionId` transporte cet identifiant au
  processeur, sans y exposer d'adresse, de port ou de nom de compte.
- `DofusNetworkMessageProcessor` utilise un cache **par
  identifiant de connexion** : éléments par UID, demande d'achat en
  cours, horodatage de demande.
- La corrélation entre `kei`, `kef`, `kbd` reste limitée
  au même flux, tout comme `kch` et `kag`.
- La table des corrélations est bornée à **64 connexions actives ou
  récentes** avec élimination des entrées inactives. Elle est effacée
  au changement de serveur/capture.
- Le prochain SYN ou RST et une erreur de réassemblage invalident
  l'ancien identifiant du tuple ; une reconnexion avec le même
  port local ne réutilise pas sciemment les anciennes corrélations.
- Un message sans identifiant de connexion valide (<=0) est rejeté
  par le processeur, sans écriture en base.
- Les tests de replay historiques utilisent une **connexion fictive
  unique** par défaut (`connectionId=1`), et peuvent fournir deux
  identifiants différents pour les essais multiclients.

### Ce qui ne change PAS

- Aucun profil persistant et aucune modification de la base.
- Le prix de marché et les coefficients restent les observations
  globales existantes, selon le serveur sélectionné et les protections
  déjà présentes.
- Le focus de l'overlay, les raccourcis, le suivi des fenêtres et les
  marqueurs réseau de P3l/P3m.1 ne changent pas.
- Aucun attribut compte/serveur n'est déduit d'un numéro de flux.
- Les messages de jeu ne sont **pas** encore attribués aux fiches
  personnages. Le décodage métier 3.7.12.12 reste non validé.

Le log d'erreur de saturation de réassemblage ne contient plus
le quadruplet d'adresses/ports.

## Tests de non-mélange ajoutés

Quatre tests d'intégration via le vrai worker/routage protègent :
1. `kei` depuis A suivi de `kef` sur B (même ID d'offre)
   ne crée aucun prix ; la réponse sur A reste valide.
2. `kbd` reçu sur B ne clôture pas l'achat ouvert sur A.
3. Un `kch` de A ne peut jamais résoudre le `kag` de B,
   même si l'UID du jeu est identique.
4. Un message sans scope TCP ne peut écrire un prix marché.

Deux tests unitaires de la table des flux vérifient également que
les deux sens d'une même connexion partagent un identifiant et
qu'un SYN réinitialise le token après réutilisation du quadruplet.

## Checkpoint Windows requis

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```


Vérifier ensuite sur deux clients que le choix de personnage et les
marqueurs de liaison TCP de P3m.1 restent actifs.

Pas de modification de main, version, release ni persistance de
nouveaux profils. Le décodage réseau métier reste à revalider sur
Dofus 3.7.12.12 avant de connecter les cartes aux métiers/XP.
