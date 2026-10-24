# P3m.19 — Récupération du propriétaire TCP à l'ouverture (SYN)

## Retour utilisateur réel — 24 octobre 2026

Le diagnostic Debug de BestCrush indique :

```text
sélections reçues : 2
propriétaire TCP identifié : 0
propriétaire introuvable : 2
chargements avec sélection : 0
chargements sans sélection : 1
associations confirmées : 0
```

Deux fenêtres, Pagalom et Nahala, restent « Serveur : identification en cours » après retour à la sélection des serveurs. Le module Concassage affiche encore un message obsolète demandant à choisir manuellement un serveur.

**Conclusion précise :** le décodeur `root.f1.f4.f1` a vu deux sélections, mais aucune n'avait de propriétaire Windows exact (tuple TCP→PID→unique HWND) au moment de l'attribution. Un seul Game `ksc` sans sélection a été compté ; cela ne permet pas de conclure sur le second chargement Game. Ni `ServerId` ni Game à lui seul ne prouvent le serveur d'une fenêtre.

## Corrections livrées sur feature/modular-workspace

### Texte UI

- `Components/Pages/ModuleWorkspace.razor` : le Concassage annonce une identification automatique du serveur du personnage ciblé et propose un lien vers sa fiche.
- `Overlay/MarketCaptureOverlayPage.cs` : l'ancienne alerte F8 demandant de sélectionner un serveur devient « Serveur en cours d'identification ». Aucun menu manuel de serveur ne revient.

### Technique

- `WindowsTcpSocketTableReader.ReadConnectingOrEstablishedIPv4` accepte **SYN_SENT (état 3)** et ESTABLISHED (état 5), selon le comportement déjà utilisé dans la sonde P3m.13. La méthode ancienne `ReadEstablishedIPv4` reste inchangée.
- Sur un **SYN TCP client→port 5555**, la capture alloue l'identifiant logique de la connexion avant tout payload, inspecte le propriétaire OS par quadruplet TCP exact et l'unique fenêtre visible du même PID.
- `TcpHandshakeWindowEvidence` conserve uniquement, en RAM et au maximum 10 secondes, cette preuve `CaptureEpoch + ConnectionId + TCP tuple + PID/HWND`, bornée à 96 connexions. Le cache ne contient ni `ServerId` ni prix et ne peut jamais autoriser l'écriture SQLite tout seul.
- Au décodage du `ServerId`, le propriétaire validé directement dans ce callback est prioritaire. Sinon, une preuve SYN du **même tuple et de la même connexion** peut être récupérée après vérification de l'unicité et de la présence du même HWND/PID. En absence de preuve, l'OS est interrogé une dernière fois. Pas de focus, titre, IP distante, rapprochement temporel approximatif ou « dernier serveur ».
- **Correction essentielle :** un paquet SYN-ACK entrant sur la même connexion ne réinitialise plus son `ConnectionId` ! Seuls le SYN sortant et RST réinitialisent le contexte. Sans cela, le cache SYN deviendrait inutilisable avant la sélection du serveur.
- Une fenêtre fermée, un HWND/PID recyclé, un tuple/connexion incompatible, une session de capture redémarrée et un cache expiré **ne peuvent pas réutiliser** l'ancienne preuve.
- Si un propriétaire obtenu au moment du SYN est **contradictoire** avec un propriétaire retrouvé au décodage du message de sélection, la sélection est rejetée plutôt que déplacée sur une autre fenêtre.
- L'association serveur finale et la possibilité d'écrire SQLite exigent toujours le `ServerId` du protocole + `ksc/ksb` cohérents sur le Game de la même fenêtre. Les protections multiserveurs P3m.17 restent intactes.

### Diagnostic Debug étendu

Sous `BESTCRUSH_SERVER_LINK_DIAGNOSTICS=1` la page Personnages indique désormais :

- `preuves TCP à l'ouverture` : propriétaire HWND/PID trouvé sur un SYN, **Connection ou Game** ;
- `preuves réutilisées` : preuve SYN effectivement réutilisée pour une sélection de serveur après perte d'accès au socket ;
- compteurs antérieurs pour sélection reçue/propriétaire manquant/chargements/confirmation.

Tous les chiffres sont agrégés, sans identifiant sensible.

## Limitations

Ce jalon n'intègre pas ETW à l'application. Notre sonde P3m.14 ETW avait déjà démontré des captures correctes, mais exigeait un processus réellement administrateur pour activer le fournisseur noyau (sinon `0x80070005`). Il serait inapproprié d'imposer l'élévation à l'application BestCrush pour ce correctif sans décision explicite.

Lire la table TCP au SYN peut encore manquer une connexion trop brève ou un propriétaire non encore visible. Si les compteurs indiquent toujours `sélections > 0` et `propriétaires = 0`, il faudra revoir l'intégration d'une source de propriétaire indépendante de l'état courant de la table, par exemple ETW, avec gestion des privilèges, conflits, expirations et opt-in.

## Retour terrain — 24 octobre 2026, deux fenêtres sur le même serveur

Après la modification P3m.19, l'utilisateur a observé deux fiches simultanées :
- Pagalom, Eniripsa niveau 38 → **Tylezia (ID 293)**.
- Nahala, Iop niveau 2 → **Tylezia (ID 293)**.

Le diagnostic affiché sur la même capture est :
```text
sélections reçues=2 propriétaire TCP identifié=2 propriétaire introuvable=0
preuves TCP à l'ouverture=4 preuves réutilisées=1
chargements avec sélection=2 chargements sans sélection=0
associations confirmées=2
```

Le test de retour à l'écran de serveur puis de reconnexion de **Pagalom** a également été réalisé par l'utilisateur : l'ID serveur a été retrouvé à chaque reconnexion essayée. Nombre exact de répétitions non communiqué.

**Validé en fonctionnement réel pour ce scénario :** récupération d'un propriétaire TCP fiable pour deux sélections, attribution correcte du même ID à deux fenêtres indépendantes, absence de propriétaire manquant sur cette capture, et récupération de Pagalom après reconnexion. Le correctif a supprimé le symptôme observé dans les captures P3m.18 sans nécessiter ETW pour ces deux fenêtres.

**Non encore validé :** deux serveurs différents simultanément, écritures économiques des deux fenêtres dans SQLite, latences et cas limites de reconnexion, tests Windows automatisés après P3m.19 (aucun nouveau total communiqué dans ce message). Ne pas confondre confirmation d'association affichée et validation de persistance économique.

## Retour terrain — 24 octobre 2026, deux serveurs distincts

Nouvelle capture utilisateur, BestCrush v0.2.0, avec deux fenêtres actives et **deux personnages portant exactement le même nom** :

| Fenêtre | Personnage | Classe | Niveau | Serveur | ServerId |
| --- | --- | --- | ---: | --- | ---: |
| 1 | Pagalom | Eniripsa | 38 | Tylezia | 293 |
| 2 | Pagalom | Ecaflip | 182 | Kourial | 355 |

Les deux fiches montrent simultanément leur propre attribution serveur (association expérimentale). Cela confirme que le nom du personnage n'est pas utilisé comme identifiant unique pour choisir le serveur.

Diagnostic agrégé affiché :
```text
sélections reçues=3 propriétaire TCP identifié=3 propriétaire introuvable=0
preuves TCP à l'ouverture=6 preuves réutilisées=3
chargements avec sélection=3 chargements sans sélection=0
associations confirmées=2
```

**Confirmé en essai réel :** identification simultanée de deux fenêtres Dofus sur des serveurs différents, avec homonymie de personnages ; aucune des trois sélections ne manque de propriétaire TCP. Les deux associations affichées sont confirmées. Le compteur `3 chargements avec sélection / 2 associations confirmées` montre qu'un chargement additionnel n'a pas abouti à une confirmation comptabilisée ; la cause n'est pas établie et doit être surveillée.

**Non confirmé par la capture :** l'isolement réel des écritures SQLite par serveur, le basculement de contexte du bandeau après ciblage successif des fenêtres, la pérennité sous reconnexions répétées de deux serveurs et le succès d'un nouveau build/test complet. Ces vérifications restent nécessaires avant release.

## Contrôle Windows à effectuer

Les tests étaient à **1149 attendus** à l'issue du correctif P3m.18, mais le résultat n'a pas encore été transmis. P3m.19 ajoute **15 cas nouveaux** (9 faits + 6 théories). **1164 tests attendus**, à confirmer sur Windows ; aucun build ni essai Dofus ne sont déclarés réussis par anticipation.

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0 -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
$env:BESTCRUSH_SERVER_LINK_DIAGNOSTICS = "1"
$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Fermer BestCrush et les deux Dofus avant démarrage, lancer BestCrush **avant** Dofus. Charger chaque personnage une fois puis noter les compteurs. Éviter toute capture économique tant que les deux serveurs ne sont pas correctement attribués.

**Prochaine décision:** si le SYN ne produit pas de propriétaire fiable en test réel, diagnostiquer ETW avant tout contournement ; ne jamais écrire sous le mauvais serveur ni rétablir un choix de serveur manuel.
