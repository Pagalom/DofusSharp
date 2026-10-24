# P3m.20 — Filtre temporaire de consultation par serveur (24/10/2026)

## Décision utilisateur

Revenir à la possibilité de **choisir un serveur**, mais uniquement pour
consulter les prix, coefficients, historiques et calculs déjà enregistrés.
Ce choix ne doit **jamais** :

- changer le serveur réellement attribué à un personnage/fenêtre ;
- réinitialiser ou masquer les fiches et leurs liaisons lors du choix ;
- arrêter/recommencer Npcap ou réinitialiser des caches de capture ;
- définir le serveur où des messages économiques sont persistés ;
- permettre d'écrire par mégarde des observations manuelles sur un serveur
  consulté temporairement.

Le serveur de référence redevient automatiquement celui du personnage quand
l'utilisateur **clique sur une fiche/un portrait**, même déjà ciblé, ou
**revient au premier plan de la fenêtre Dofus**. Deux fenêtres peuvent
pointer vers le même serveur ; leurs prix sont mutualisés par serveur,
et deux serveurs différents restent isolés.

## Comportement implémenté

### Bandeau et navigation

- La sélection de serveur revient dans le bandeau principal avec la mention
  explicite « **Consulter : <serveur>** » en mode temporaire.
- Les choix combinent le catalogue DoFocus et les serveurs présents dans les
  observations locales SQLite :
  `MarketPriceObservations`, `CoefficientObservations`,
  `CrushHistorySessions`. Il n'est pas nécessaire de posséder un compte sur
  le serveur pour consulter les données qui y ont déjà été collectées.
- Un bouton « Revenir au serveur du personnage ciblé » est disponible.
- Le filtre est **en mémoire seulement**, sans préférence de serveur
  réenregistrée au prochain lancement.
- Les URLs serveur existantes restent compatibles. Passer de
  `/servers/Tylezia/history` à Kourial conserve `/history` (idem
  équipements, runes, ressources et coefficients), ainsi que les paramètres
  de requête.
- Les pages SQL rechargent les observations à chaque changement de
  `ServerName`, sans montrer les prix précédents sous le nouvel intitulé.
  Les requêtes d'historique concurrentes rejettent les réponses obsolètes.

### Retour au personnage

- `ServerViewFilterState` sépare consultation manuelle et suivi
  automatique : `BrowseServer`, `FollowWindow`,
  `ReconcileTarget`. Un simple événement réseau ou rafraîchissement UI
  ne peut **pas** effacer un filtre manuel.
- `CurrentDofusClientsState.TargetRequested` est émis même pour un clic
  sur la fenêtre **déjà ciblée** ; cliquer sur une fiche ou son portrait
  rétablit immédiatement son serveur.
- `CurrentDofusClientsState.GameActivated` est émis au **retour
  réel au premier plan** d'une fenêtre Dofus, pas à chaque tick du polling.
  Si ce client est de nouveau celui actif, son serveur est restauré
  automatiquement.
- Pour une fiche d'identité confirmée, le `CharacterKey` et, s'il existe,
  le vrai lien OS fenêtre/PID sont exploités pour rétablir le serveur.
  Aucune attribution par nom (deux « Pagalom » sur des serveurs différents
  restent distincts).
- Lorsque la fenêtre ciblée n'a pas de serveur attesté, la vue revient
  à « Identification en cours » plutôt que de reprendre le serveur
  temporairement consulté. L'absence de preuve n'est pas comblée
  par le filtre.

### Capture et intégrité économique

- **Inchangé :** P3m.17 / P3m.19 : les écritures réseau passent uniquement
  par le `NetworkCaptureLease` attribué par
  `ServerId → tuple TCP exact → PID/HWND → ksc/ksb Game`.
- `CurrentServerState.GetCaptureLease()` continue de retourner
  `null` en mode automatique. `CurrentServerState.ServerName` est
  uniquement le contexte de consultation/affichage.
- Aucun changement des tables SQLite ; les observations sont **indexées
  par nom de serveur**, pas par fenêtre.
- Les pages de prix et coefficients `SourceValueInput` passent en
  **lecture seule** pendant la consultation temporaire. Les méthodes de
  sauvegarde refusent aussi explicitement les événements `onchange`
  obsolètes. Un filtre de consultation ne produit ainsi **aucune** nouvelle
  observation manuelle sur le serveur consulté.
- F8 et le clic molette, lorsqu'un Game est identifié, consultent d'abord
  le serveur du **vrai client Dofus concerné**, non celui du filtre SQL,
  puis demandent le retour à sa vue automatique. F8 ne se transforme pas
  en lecteur des prix d'un serveur sans personnage identifié.
- Les métadonnées des captures de debug en mode automatique n'utilisent
  plus le serveur du filtre SQL pour qualifier une capture réseau.

### Tests de régression

Ajouts de tests à la suite des 1164 tests attendus à P3m.19 :

- logique `ServerViewFilterState` : valeur par défaut, filtre temporaire,
  clic répété sur la même fenêtre, deux serveurs, même serveur sur deux
  fenêtres, absence de serveur, prévention de modification d'un personnage
  confirmé ;
- `CurrentDofusClientsState` : ciblage explicite, vrai retour au jeu,
  activation de la même fenêtre précédemment au premier plan ;
- `WorkspaceModules.ReplaceServerInRoute` : conservation des URL de
  consultation et de leurs paramètres, maintien de la fiche Personnages.

**1187 tests attendus si les 1164 de P3m.19 sont présents et réussissent**.
La compilation / exécution Windows de ces nouveaux changements reste à
confirmer par l'utilisateur. Aucun résultat automatisé ne doit être
annoncé comme acquis.

## Recette à exécuter sur Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0 -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Vérification manuelle :

1. Avec Pagalom Tylezia et Pagalom Kourial connectés, cibler Tylezia ;
   serveur affiché = Tylezia. Les deux fiches restent visibles.
2. Choisir manuellement Kourial dans « Consulter » ; afficher ses
   historiques et prix, sans réinitialiser les deux fiches.
3. Dans Kourial/historique, choisir Tylezia : rester sur l'historique
   mais avec uniquement les observations de Tylezia.
4. Vérifier le mode **lecture seule** pour les prix et coefficients
   consultés via le filtre temporaire.
5. Cliquer « Cibler » sur Pagalom Tylezia (déjà ciblé ou non) :
   retour immédiat à son serveur.
6. Choisir manuellement un autre serveur, cliquer la fenêtre Dofus
   Kourial : le bandeau doit revenir automatiquement à Kourial.
7. Pendant le filtrage de consultation, des observations réseau de
   l'une et l'autre fenêtre doivent continuer d'être enregistrées
   sous leurs véritables serveurs. Faire les essais de persistance
   uniquement sur une **copie/sauvegarde** de la base SQLite.

**Critère de non-régression critique :** aucun changement de filtre
manuel ne réinitialise la détection `ServerId`, la fiche personnage,
les états de session, les baux de Game ou la séparation SQLite.
