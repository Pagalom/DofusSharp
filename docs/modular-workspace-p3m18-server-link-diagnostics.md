# P3m.18 — Propriétaire perdu sur Connection et diagnostic anonyme

## Déclencheur : test réel 24/10/2026

Capture visuelle de deux clients BestCrush v0.2.0 :
- Au départ, Pagalom ↔ Tylezia (ID 293) apparaissait ; Nahala était non identifié.
- Après un aller-retour à la sélection du serveur dans chacun des clients,
  **les deux affichent « Serveur : identification en cours »**.
- Les fenêtres, noms, niveaux et cartes restent visibles. Ce n'est **pas**
  une preuve que les messages Connection ne sont plus reçus.

L'ancien ServerId doit être révoqué lors d'un nouveau chargement ; les
observations de marché ne doivent jamais hériter d'un ancien contexte.

## Correction de robustesse

Avant P3m.18, `OnPacketArrival` appelait une première fois
`ObserveLiveDofusClient` pour le quadruplet TCP exact, puis
**forçait systématiquement une seconde interrogation** de la table
Windows TCP à la réception de `root.f1.f4.f1`, sans tenir compte
de la fenêtre trouvée dans la première interrogation.

Une connexion `Connection` brève pouvait disparaître entre les deux
lectures : un propriétaire TCP **déjà vérifié** était ainsi jeté.
La correction utilise la première preuve si elle existe, et ne réessaie
une lecture exacte que lorsqu'aucun propriétaire n'a été trouvé initialement.

Le correctif n'autorise **aucune** attribution par titre, focus, nom,
chronologie ou serveur global ; le `ServerId` reste toujours requis.

## Instrumentation (opt-in, Debug)

Lancer BestCrush en définissant `BESTCRUSH_SERVER_LINK_DIAGNOSTICS=1`.
Sur la page Personnages, un petit résumé compte pour la capture courante :

- **sélections reçues** : trames Connection contenant un ID structurellement
  décodé (pas forcément connu du catalogue).
- **propriétaire TCP identifié** : l'ID a été reçu pendant qu'une liaison
  TCP exacte vers une fenêtre était disponible.
- **propriétaire introuvable** : le message ID était décodable mais son
  quadruplet TCP n'a pas fourni de fenêtre propriétaire unique.
- **chargements avec sélection** : ksc a pu être associé à une unique
  sélection récente du **même client**, distincte du flux Game.
- **chargements sans sélection** : ksc a été reçu mais aucune sélection
  Connection suffisamment sûre n'était disponible.
- **associations confirmées** : ksb a validé une sélection ksc et un
  ServerId compatibles.

Il s'agit de compteurs de session agrégés, pas de statistiques par compte.
Aucun IP, port local, tuple, PID/HWND, nom de personnage ou jeton n'est
enregistré. Ces compteurs ne peuvent jamais autoriser une écriture SQLite.

Démarrer BestCrush **avant** Dofus. Seul Debug reconnaît le drapeau.

## Validation demandée sur Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0 -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
$env:BESTCRUSH_SERVER_LINK_DIAGNOSTICS = "1"
$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Le total attendu passe de **1 144 à 1 149 tests** (5 nouveaux).
Non vérifié sur Windows à la date du commit.

Scénario : fermeture de tous les clients, démarrage BestCrush,
connexion de Pagalom puis Nahala, relevé du compteur à la page
Personnages. Aucune observation économique pendant ce diagnostic.

## Lecture du résultat

- Sélections = 0 : la capture ne reconnaît pas le message ServerId
  (lancement trop tardif, protocole, Npcap, client en mémoire, etc.).
- Sélections > 0 et propriétaires introuvables > 0 : problème probable
  de durée/attribution TCP. Une partie des sélections peut toutefois
  appartenir à d'autres clients.
- Propriétaire trouvé, mais chargement sans sélection : sélectionner
  seulement depuis la fenêtre exacte échoue, ou la sélection n'était
  plus éligible/unique au chargement Game.
- Chargement avec sélection > 0, confirmé = 0 : vérifier la cohérence
  ksc/ksb, la fenêtre et les contraintes de décodage.

Si les connexions très brèves restent systématiquement sans propriétaire,
reprendre P3m.14 ETW TCP Connect (déjà réussi en probe administrateur)
**en envisageant soigneusement les privilèges et l'impact de la session
ETW**. Ne pas inventer un serveur par proximité temporelle.
