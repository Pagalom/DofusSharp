# P3m.16 — Détection du serveur par fenêtre (premier jalon de migration)

## Décision utilisateur (23 octobre 2026)

Objectif cible : **serveur détecté automatiquement par son ID réseau,
attaché à la fiche du personnage de la bonne fenêtre**. Plus de choix
global ni de correction manuelle. Les prix, historiques et coefficients
devront être attribués au serveur réellement relié à chaque flux.

Ce commit est le **jalon 1 / caractérisation intégrée**. Il ne retire
pas encore le sélecteur historique ni ne change l'attribution de la
base SQLite. Cela est volontaire : en mode multicomptes, supprimer le
serveur global avant d'avoir un routage économique validé par fenêtre
risquerait de mélanger les marchés.

## Ce qui est effectivement développé

- Catalogue local public d'IDs : Ombre 50, Imagiro 291, Orukam 292,
  Tal Kasha 290, Tylezia 293, Hell Mina 294, Draconiros 295, Rafal 350,
  Brial 351, Salar 352, Dakal 353, Mikhal 354, Kourial 355.
  Référence communautaire :
  https://github.com/Pierre-ZACHARY/dofusguideweb/blob/main/src/dofus/servers.ts
  Aucun catalogue seul ne constitue une preuve d'appartenance d'une fenêtre.
- La capture Npcap (TCP/5555) inspecte désormais les trames d'amorçage
  non-Game **client→serveur** via le décodeur P3m.11 validé
  `ConnectionServerSelectionStudy.TryGetSelectedServerCandidate`
  (`root.f1.f4.f1`).
- Lorsqu'une trame de sélection est reconnue, une recherche explicite
  du **propriétaire Windows du quadruplet TCP exact** est tentée ;
  seuls un PID et une fenêtre HWND uniques permettent la mise en attente.
- Sur la **même fenêtre**, un flux Game ultérieur doit produire un
  `ksc` puis un `ksb` cohérents, sur le **même identifiant
  de connexion logique**. Une seule sélection préalable récente
  (moins de 2 minutes), liée sans ambiguïté à la fenêtre, est admise.
- Deux sélections distinctes avant le Game, un propriétaire manquant,
  une valeur ID non référencée, un message malformé, une reconnexion
  sans nouvelle sélection, une fenêtre fermée, une capture redémarrée,
  ou un décalage entre `ksc` et `ksb` => **pas de serveur attribué**.
- L'association est en RAM et révoquée par la surveillance Win32
  indépendante des paquets ; aucune identité de compte ou personnage
  n'est inventée. Aucune adresse, PID, HWND, token, nom ou valeur
  privée ne sort dans les journaux.
- La fiche Personnages affiche « Serveur : X — ID Y,
  association expérimentale », ou
  « Serveur : identification en cours » sans preuve.

## Limites connues, très importantes

1. Ce n'est **pas encore l'architecture multiserveur définitive** :
   `CurrentServerState`, `DofusNetworkMessageProcessor`,
   `NetworkObservationWriter` et les modules d'analyse utilisent
   encore le serveur historique sélectionné globalement. Ne pas
   utiliser deux serveurs différents pour relever des données de
   marché avec ce jalon.
2. Les connexions Connection sont très brèves. La lecture du
   propriétaire Windows peut les rater. Le code **n'emprunte jamais**
   un propriétaire depuis la fenêtre au focus ou depuis une
   connexion Game voisine, même si cela retarde la détection.
3. Les références d'IDs proviennent de catalogues publics ;
   leur couverture et validité pourront évoluer avec le jeu.
4. P3c.10 reste une **étude Debug opt-in séparée** et ses fiches
   candidates apparaissent uniquement avec
   `BESTCRUSH_P3C10_ROSTER_STUDY=1`. La liaison ServerId est
   conditionnée à une preuve réseau indépendante.
5. L'absence d'ID affiché peut résulter du manque de propriétaire
   d'une connexion courte ; elle ne démontre pas l'absence du
   message d'identification Dofus.
6. Aucun passage implicite des professions ni kamas aux fiches,
   sans preuves d'identité et compte nécessaires.

## Campagne Windows demandée

Avant d'ouvrir Dofus, mettre à jour et exécuter :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0 -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Après ouverture des deux comptes de serveurs différents,
observer les fiches et les indications de serveur.
Un ID manquant ne doit jamais être deviné. Fermer une
fenêtre et vérifier la disparition de son attribution.

Tests synthétiques ajoutés :
`Tests.BestCrush/Network/WindowServerAttributionStateTest.cs`
(16 cas, soit **1133 tests attendus** si les 1117 antérieurs
restent présents). **Compilation et tests Windows non encore confirmés
pour ce jalon.**

## Jalon 2 — À faire avant de retirer la sélection globale

1. Distinguer dans le moteur de capture un **bail de cadrage des trames**
   d'un **bail serveur économique**, délivré uniquement à la connexion
   Game précise et fenêtre dont `ksc/ksb` et `ServerId` sont liés.
2. Revalider le même bail par fenêtre avant chaque écriture SQLite
   et chaque notification ; interdiction d'écrire pour un client non
   identifié ou sous le serveur d'un autre client.
3. Tester deux clients sur deux serveurs, mêmes serveurs, reconnexion
   sans sélection attribuable, intercalage des paquets, fenêtre fermée
   et vieille observation asynchrone.
4. Une fois ces protections validées, faire dériver le serveur affiché
   par les modules depuis la fenêtre ciblée, supprimer le sélecteur
   global sans conserver d'ambiguïté de routage.
