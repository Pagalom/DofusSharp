# A4 — Référence manuelle Windows : raccourcis et overlays

## Référence, portée et état de validation

Code examiné : `7b420616e1ec0080cdded46cf4ff4013812daea4`, branche exclusive
`refactor/codebase-cleanup`, après A3. A4 ne modifie que la documentation.
La v0.2.0 reste la référence comportementale ; ce protocole décrit les branches
actuellement implémentées, pas des comportements cibles corrigés.

Checkpoint A3 **déclaré validé par l'utilisateur sous Windows** : build réussi
avec 16 avertissements, 111 tests réseau et 203 tests au total, aucun échec.
Ces exécutions n'ont pas été reproduites dans l'environnement de rédaction :
`dotnet` y est absent, ainsi que l'environnement graphique Windows nécessaire.
À la publication A4, tous les scénarios ci-dessous restaient à exécuter.

Retour utilisateur du 7 octobre 2026, avant B1, sur la référence `c92d1ca` :
les scénarios critiques suivants sont **déclarés conformes sous Windows** :
`A4-F7-01`, `A4-F7-02`, `A4-F8-01`, `A4-F8-02`, `A4-F8-03`, `A4-MID-01`,
`A4-FOCUS-01`, `A4-FOCUS-03`, `A4-CR-01`, `A4-CR-05`, `A4-INT-01`,
`A4-INT-02`, `A4-UI-02`, `A4-UI-05`. Ce retour autorise le lot B1 ; il ne
valide pas les autres scénarios ni ne ferme les lacunes de routage A3.
Ces essais n'ont pas été reproduits dans l'environnement de préparation.
Le [checkpoint B1](codebase-cleanup-b1-obsolete-market-ocr.md) précise les cas
à rejouer après suppression du seul bloc privé de marché OCR.

Les références `[S1]` à `[S12]` renvoient au tableau des sources en fin de document.
Les observations manuelles ne remplacent pas les tests de routage encore requis
avant la Phase E. Aucun rejeu, paquet injecté, automatisme de jeu ou parcours
OCR/F9 historique n'est nécessaire à ce protocole.

## Préparer une exécution reproductible

1. Depuis la racine du dépôt, relever la branche, le HEAD et les changements
   locaux. Récupérer uniquement le commit documentaire A4 par avance rapide.
   Conserver les modifications indépendantes de `DofusSharp.sln` et
   `DofusSharp.DofusDb.Filter.slnf` ; ne pas les réinitialiser.

   ```powershell
   git branch --show-current
   git status --short
   git pull --ff-only origin refactor/codebase-cleanup
   git rev-parse HEAD
   dotnet --version
   ```

   Si la branche active n'est pas `refactor/codebase-cleanup`, s'arrêter avant
   le `pull`. Ne pas utiliser `reset --hard` pour résoudre un conflit local.

2. Lancer cette version de BestCrush sous Windows. Si les binaires A3 validés
   ne sont plus disponibles, les reconstruire avec la commande habituelle :

   ```powershell
   dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
   ```

   A4 n'ajoute aucun test automatisé et n'exige pas de répéter la suite pour
   valider un changement de documentation. Pour un contrôle de l'environnement :

   ```powershell
   dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --filter "FullyQualifiedName~Tests.BestCrush.Network" --logger "console;verbosity=normal"
   dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
   ```

3. Relever Windows, SDK, version/build DOFUS, résolution, échelle d'affichage,
   moniteurs et version de Npcap. Utiliser un seul client DOFUS, en fenêtre,
   visible et non minimisé, sauf scénario contraire. Terminer le chargement du
   catalogue BestCrush avant les essais. Le choix de fenêtre privilégie le
   client DOFUS au premier plan, sinon le premier client visible énuméré [S3].

4. Définir un serveur **S** correspondant au client DOFUS et, pour les essais
   de changement de serveur, un serveur **T** différent. Relever leurs noms
   exacts. Préparer deux équipements **A** et **B** de noms distincts, présents
   dans le catalogue local et dont on peut afficher l'infobulle. Noter noms,
   ItemId si disponibles, niveau et emplacement en jeu. Pour le concassage,
   utiliser uniquement des équipements que le testeur a décidé de détruire.

5. Dans la page serveur, ouvrir **⚙ Paramètres**. Relever les quatre options
   de capture, la priorité des données, le mode de jet, la décote et le ROI
   cible. Les garder constants, sauf changement explicitement demandé par un
   scénario. Activer **Conserver les artefacts de debug** pour les cas nécessitant
   une preuve de capture ou de message ; restaurer le réglage initial à la fin.
   Ne pas éditer SQLite, les messages ou le catalogue pour fabriquer un cas.

6. Avant chaque scénario, attendre la fin des F8 et des calculs précédents.
   Sauf essai réseau explicite, ne pas acheter, crafter ou concasser pendant
   l'observation. Une réception réseau peut modifier le dernier équipement ou
   rouvrir l'overlay de concassage. Un redémarrage signifie fermer BestCrush
   par sa fenêtre principale puis le relancer, pas appeler `Stop/Start` au debugger.

### Fenêtres, boutons et données à relever

| Repère | Fenêtre / commande | Comportement utile au test |
|---|---|---|
| R | **Rentabilité**, bouton **◈** | Affiche l'équipement en focus et ses calculs |
| M | **Mise à jour marché**, bouton **↻** | Reçoit les diagnostics F8 ; ils ne l'ouvrent pas automatiquement |
| C | **Résultat concassage**, bouton **⚒** | Reçoit le dernier résultat réseau accepté |
| Barre | Poignée **⋮**, puis les trois boutons et **⚙** | Reste visible pendant F7 ; ses boutons masquent/montrent les fenêtres |
| ⚙ de la barre | « Paramètres » | Active la fenêtre principale ; n'ouvre pas lui-même le panneau Paramètres |

Les trois overlays ont un en-tête **⋮⋮** déplaçable et des bords/coins de
redimensionnement personnalisés. Ils n'ont pas de bouton X dédié. « Fermer
depuis la barre » signifie **masquer**, sans effacer leur contenu [S1, S2, S6].

**Relevé commun E0, à joindre à tout échec :** ID du scénario, HEAD, date/heure
avec fuseau, serveur BestCrush et serveur DOFUS, réglages, équipement, ordre
exact des actions, état visible/masqué de R/M/C, capture écran ou courte vidéo,
texte exact du diagnostic, durée d'attente. Attendre 10 s à titre de repère
opératoire, puis noter si le traitement continue ; ce délai n'est pas une
garantie du produit, notamment si le fallback DoFocus attend une réponse.

| Élément | Emplacement Windows / preuve |
|---|---|
| Logs applicatifs | `%LOCALAPPDATA%\BestCrush\Logs\bestcrush*.log` |
| Images F8 conservées | `%LOCALAPPDATA%\BestCrush\DebugCaptures\Tooltip\f8-tooltip-*\` : `dofus.png`, puis crops et `tooltip-title-ocr.txt` selon la branche atteinte |
| Traces réseau conservées | `%LOCALAPPDATA%\BestCrush\DebugCaptures\Network\session-*\` : `session.txt`, `wire.jsonl`, `events.log` selon les événements reçus |
| Disposition des fenêtres | `%LOCALAPPDATA%\BestCrush\Settings\overlay-layout.json` ; lecture/copie uniquement pour le relevé |
| Historique | Page serveur → **Historique** → **Concassages** → **Rafraîchir** → **▶** sur la ligne de l'opération |

Conserver les extraits correspondant au créneau du test, notamment
`[LAST-EQUIPMENT] ItemId=...` et les messages `kci` si le debug est actif.
L'absence de fichier, seule, ne prouve pas qu'une branche a été exécutée.

Statuts du compte rendu : **Conforme**, **Écart au code**, **Bloqué** (précondition
indisponible), **Partiel** (effet visible sans preuve de la branche), **Non exécuté**.
Une particularité attendue mais indésirable reste conforme à cette référence ;
une correction éventuelle exige un lot séparé.

## F7, état initial et touches globales

### A4-F7-01 — Démarrage et premier F7 [S1, S2, S8]

- **Préconditions :** BestCrush fermé ; aucun concassage en cours dans DOFUS.
- **Actions :** lancer BestCrush ; attendre la stabilisation des fenêtres sans
  action de focus ; appuyer puis relâcher F7 deux fois, avec une seconde entre
  les appuis. Ouvrir ensuite R, M et C une fois avec la barre.
- **Attendu :** au démarrage, la barre est visible, R est masqué après sa
  création, M et C ne sont pas ouverts. Sans état F7 mémorisé et sans overlay
  visible, F7 ne montre rien. À leur première ouverture sans données : R indique
  « Aucun équipement en focus », M « Aucune capture récente », C attend un
  résultat réseau. Les libellés initiaux M « Clic molette — prêt à lire » et
  « Les captures de runes, ressources et prix HDV apparaîtront ici » sont
  historiques ; ils ne décrivent pas une capture OCR par molette.
- **Échec :** E0 + vidéo du démarrage ; distinguer un bref affichage de création
  de R d'une visibilité persistante après son masquage différé de 100 ms.

### A4-F7-02 — Ensemble complet, ensemble partiel et maintien de touche [S1, S2]

- **Préconditions :** R/M/C visibles, aucun message de concassage ni focus attendu.
- **Actions :** F7 puis F7 ; masquer M avec ↻ ; F7 puis F7. Maintenir ensuite F7
  deux secondes, relâcher, puis appuyer une fois pour restaurer.
- **Attendu :** première paire : tout masquer puis restaurer R/M/C. Deuxième
  paire : masquer puis restaurer R/C seulement. Barre toujours visible. Un
  maintien produit une seule bascule, pas une répétition à chaque keydown.
  Les boutons de la barre reflètent la visibilité (vert lorsque visible).
- **Échec :** E0 + séquence des triplets R/M/C ; relever les événements réseau
  ou focus intercalés avant de conclure à une erreur de restauration.

### A4-F7-03 — Intervention de la barre ou d'un focus après masquage [S1]

- **Préconditions :** R/M/C visibles ; aucune opération réseau en attente.
- **Actions :** F7 ; cliquer ◈ pour ouvrir R puis ◈ pour le masquer ; F7.
  Ensuite ouvrir R/M/C, F7, faire un focus explicite de A, puis F7 deux fois.
- **Attendu :** les clics de barre annulent l'ancien ensemble de restauration :
  le premier F7 après les deux clics ne montre rien. Un focus explicite annule
  lui aussi cet ensemble et ouvre R. La dernière paire F7 restaure donc R seul.
  Le bouton **Overlay** de la page serveur appelle directement `Toggle` : il
  ne passe pas par l'effacement d'état F7 des boutons de la barre ; ne pas
  l'utiliser à leur place dans ce scénario.
- **Échec :** E0 + bouton précis utilisé et état avant chaque action.

### A4-KEY-01 — Portée globale et F9 libre [S1, S3]

- **Préconditions :** R visible ; DOFUS visible ; serveur S sélectionné ; une
  autre application, par exemple Bloc-notes, peut être mise au premier plan.
- **Actions :** depuis Bloc-notes, F7 deux fois. Faire apparaître une infobulle
  unique dans DOFUS, revenir à Bloc-notes sans la faire disparaître si possible,
  puis F8. Attendre la fin ; appuyer sur F9.
- **Attendu :** F7 est global. F8 n'exige pas que DOFUS soit au premier plan :
  il capture la fenêtre sélectionnée par `DofusWindowService` et suit le résultat
  réel de sa détection. F9 ne déclenche rien dans BestCrush. Les hooks appellent
  toujours `CallNextHookEx` : le comportement de ces touches dans le jeu ou
  l'autre application peut aussi avoir lieu. Aucun scan F9 ne doit démarrer.
- **Échec :** E0 + application au premier plan et ses propres raccourcis ; si
  l'infobulle a disparu, appliquer le résultat attendu de A4-F8-03.

## F8 : capture et reconnaissance d'infobulle

Pour voir les diagnostics pendant ces essais, ouvrir M avec ↻ avant F8, sauf
mention contraire. Les erreurs et ambiguïtés ci-dessous ne changent pas le
focus existant et ne montrent pas R. Le diagnostic M est mis à jour même si M
est masqué ; l'ouvrir ensuite révèle le dernier diagnostic, pas un journal [S4].

### A4-F8-01 — Aucun serveur choisi dans cette session [S1, S8]

- **Préconditions :** redémarrage de BestCrush, catalogue chargé, écran de
  sélection des serveurs sans sélection ; debug conservé si déjà configuré.
- **Actions :** ouvrir M ; relever les dossiers F8 existants ; F8 puis clic
  molette dans DOFUS ; sélectionner ensuite S et refaire F8 sur A.
- **Attendu :** avant sélection, F8 s'arrête avant recherche de fenêtre et
  capture : « ⚠ Sélectionnez d'abord un serveur », « Aucune capture effectuée ».
  Le détail mentionne encore « lecture clic molette ». La molette sans snapshot
  ne fait rien et ne produit pas ce diagnostic. Après sélection, F8 suit son
  parcours normal. Le serveur est un état de session ; revenir sur une autre
  page après l'avoir choisi n'est pas équivalent à l'effacer.
- **Échec :** E0 + page au moment de l'appui, nouveaux dossiers F8, diagnostic.

### A4-F8-02 — Infobulle unique, équipement connu [S1, S3, S4, S5]

- **Préconditions :** S choisi ; A connu ; une seule infobulle lisible entièrement
  visible ; R et M masqués ; artefacts conservés.
- **Actions :** relever le presse-papiers ; F8 sur A ; attendre ; ouvrir M avec
  ↻. Refaire l'essai avec M visible et maintenir F8 deux secondes.
- **Attendu :** capture visuelle, détection, OCR, reconnaissance puis
  `FocusEquipmentAsync`. R s'ouvre et affiche A avec son niveau et les résultats
  calculables. M reste masqué jusqu'au clic ; il affiche « ✓ Équipement en
  focus », A et le pourcentage de reconnaissance. Le nom exact normalisé est
  reconnu à confiance 1 si l'OCR le lit correctement. Un maintien ne crée
  qu'une demande F8. Ce parcours ne copie pas le nom au presse-papiers et ne
  scanne ni prix HDV ni coefficient. Les valeurs de R viennent des services
  métier existants, avec leurs sources, valeurs manquantes et fallback DoFocus.
- **Échec :** E0 + `dofus.png`, crops, texte OCR, nom/niveau réels et reconnus ;
  pour un résultat économique incomplet, relever les données manquantes plutôt
  que classer automatiquement la reconnaissance comme échouée.

### A4-F8-03 — Fenêtre présente, aucune infobulle détectée [S1, S3, S4]

- **Préconditions :** focus A établi ; M visible ; aucune infobulle dans DOFUS.
- **Actions :** déplacer le pointeur sur une zone vide, attendre la disparition
  des infobulles, F8.
- **Attendu :** une capture a bien lieu. Si le détecteur retourne zéro candidat,
  M affiche pourtant « ⚠ Dofus non détecté », « Lecture annulée » et « BestCrush
  n'a pas trouvé de fenêtre Dofus active. ». Ce texte est trompeur dans cette
  branche. A reste en focus ; aucune nouvelle identification n'est appliquée.
- **Échec :** E0 + image complète, nombre de candidats si observé au debugger ;
  noter les faux positifs éventuels de détection.

### A4-F8-04 — DOFUS absent ou capture impossible [S1, S3, S4]

- **Préconditions :** S choisi, M visible, aucun autre client DOFUS.
- **Actions :** fermer DOFUS ; F8. Relancer DOFUS, le minimiser ; F8.
- **Attendu :** sans fenêtre détectée, même diagnostic « Dofus non détecté »,
  mais aucune capture lancée. Si la fenêtre minimisée est encore sélectionnée,
  la capture échoue avec « ⚠ Lecture impossible », « Capture non exploitable »
  et « Dofus est minimisé. ». Si elle n'est pas énumérée, c'est la branche
  fenêtre absente. Le focus antérieur reste conservé dans les deux cas.
- **Échec :** E0 + état/fenêtre sélectionnée, message exact de l'exception ; ne
  pas confondre absence de fenêtre et échec WinRT/Direct3D après sélection.

### A4-F8-05 — Plusieurs infobulles [S1, S3, S4]

- **Préconditions :** focus A ; M visible ; deux infobulles réellement visibles
  dans DOFUS, par exemple via sa fonction de comparaison. Noter la commande
  DOFUS et la disposition qui permettent de reproduire cette présentation.
- **Actions :** garder ces deux infobulles, F8 ; relever l'image et le diagnostic.
- **Attendu :** si le détecteur trouve plus d'un candidat : « ⚠ N infobulles
  détectées », « Lecture ambiguë ». A reste en focus. Cette branche retourne
  avant l'OCR de titre et ne propose pas de choix d'équipement.
- **Échec :** E0 + image et nombre de candidats ; deux fenêtres visuellement
  présentes ne prouvent pas que le détecteur en a trouvé deux. Si un seul
  candidat est trouvé, noter une couverture partielle et son résultat réel.

### A4-F8-06 — Équipement inconnu ou texte non reconnu [S1, S3, S4]

- **Préconditions :** focus A ; M visible ; infobulle réelle unique dont le nom
  est absent du catalogue local, ou titre mal reconnu. Noter l'objet et le
  contenu OCR ; ne pas altérer le catalogue ni substituer une image au pipeline.
- **Actions :** F8 ; comparer le titre OCR, le nom réel et le diagnostic.
- **Attendu :** si `Recognition` est nul, « ⚠ Équipement non reconnu », titre
  OCR s'il existe, et texte indiquant une certitude insuffisante ; A reste en
  focus. **L'absence du nom exact dans le catalogue ne garantit pas le rejet** :
  le rapprochement approximatif peut accepter un autre équipement. Dans ce
  cas, le code le focalise ; relever cette mauvaise identification séparément.
- **Échec :** E0 + images, OCR, équipement réellement visé et équipement accepté,
  confiance si disponible. Sans stimulus réel disponible : **Bloqué**, pas Conforme.

### A4-F8-07 — Ambiguïté de reconnaissance avec une seule infobulle [S3, S4]

- **Préconditions :** cas réel conservé où un titre OCR non exact rapproche
  plusieurs noms du catalogue ; M visible ; focus A.
- **Actions :** reproduire la même infobulle et F8. Si nécessaire, observer les
  scores dans `DofusItemRecognitionService.FindBestMatch` au debugger, sans
  changer les valeurs ni appeler artificiellement des méthodes.
- **Attendu :** la reconnaissance applique ses filtres niveau/type, un seuil
  de 0,82 et un écart minimal de 0,05, avec acceptation anticipée du meilleur
  score ≥ 0,999 ; une seconde tentative peut retirer un artefact de type.
  Si aucune tentative n'aboutit, le diagnostic est **Équipement non reconnu**,
  pas « N infobulles détectées ». Aucun sélecteur de candidats n'est affiché.
- **Échec :** E0 + candidats/scores, filtres et retour final. Ce cas n'est pas
  garanti par un geste générique ; sans preuve d'ambiguïté effectivement atteinte,
  le déclarer Partiel/Non exécuté et conserver le besoin de caractérisation.

### A4-F8-08 — Demandes F8 successives [S1, S3]

- **Préconditions :** A/B reconnus isolément ; debug actif ; aucun autre focus.
- **Actions :** afficher A, F8 et relâcher ; afficher B, F8 et relâcher sans
  attendre la fin d'analyse de A. Attendre la fin des deux demandes.
- **Attendu :** les captures commencent immédiatement et peuvent se chevaucher,
  mais leur analyse respecte l'ordre des demandes. Si les images capturées
  contiennent respectivement A puis B et que les deux réussissent, B reste le
  focus final. La capture asynchrone n'est pas une garantie d'image à l'instant
  exact du keydown : vérifier les images avant d'imputer une erreur d'ordre.
- **Échec :** E0 + les deux dossiers horodatés et leur contenu. Ne pas extrapoler
  ce résultat à un mélange F8/UI/molette : un ancien F8 encore en file peut
  appliquer son focus après un autre geste explicite.

## Molette et focus explicite

### A4-MID-01 — Dernier équipement réseau, indépendant du survol [S1, S7]

- **Préconditions :** S choisi ; debug actif ; A/B connus ; aucun F8 en attente.
- **Actions :** consulter A dans l'HDV pour obtenir une observation réseau ;
  vérifier `[LAST-EQUIPMENT] ItemId=A`. Faire F8 sur B par un simple survol qui
  ne produit pas une nouvelle identification réseau. Vérifier que la dernière
  trace reste A. Relever les dossiers Tooltip, puis cliquer une fois la molette
  dans une zone vide de la fenêtre DOFUS hors overlays.
- **Attendu :** R montre A, même si B était le focus OCR et même sans infobulle.
  Aucun nouveau dossier F8, aucune nouvelle analyse OCR, aucun diagnostic de
  capture dans M. L'observation réseau ne focalisait pas A à elle seule.
  Le snapshot n'expire pas après un délai : le dernier équipement mémorisé du
  serveur courant reste éligible tant qu'il n'est pas remplacé.
- **Échec :** E0 + dernière trace avant le clic, ItemId, dossiers avant/après.
  Si la préparation a généré une observation de B, rétablir A et recommencer.

### A4-MID-02 — Aucune identification, ressource ou rune observée [S1, S7]

- **Préconditions :** redémarrage, S sélectionné, aucun équipement identifié par
  le réseau ; absence vérifiée de `[LAST-EQUIPMENT]`. R masqué, M visible.
- **Actions :** clic molette sur DOFUS. Puis établir A comme dernier équipement ;
  consulter uniquement une ressource ou rune dans l'HDV et refaire le clic.
- **Attendu :** sans snapshot : aucune action, aucun avertissement, aucun OCR.
  Les ressources/runes ne remplacent pas le dernier équipement fiable : le
  second clic reprend A si aucune autre observation d'équipement n'est intervenue.
  Un ItemId doit être résolu comme équipement dans le catalogue pour être retenu.
- **Échec :** E0 + chronologie `wire.jsonl`/`[LAST-EQUIPMENT]` ; une connexion
  DOFUS peut déjà avoir fourni des équipements d'inventaire, auquel cas la
  précondition « aucun équipement » n'est pas remplie.

### A4-MID-03 — Zones exclues et rotation de molette [S1]

- **Préconditions :** dernier équipement réseau A ; focus B ; R/M/C et barre
  placés en partie au-dessus du rectangle DOFUS ; aucune demande F8 en cours.
- **Actions :** clic molette successivement sur R, M, C, la barre puis à
  l'extérieur du rectangle DOFUS. Faire tourner la molette sans cliquer dans
  DOFUS. Enfin cliquer dans DOFUS hors overlays.
- **Attendu :** seules les deux notifications bouton milieu intéressent le hook ;
  les zones BestCrush visibles et l'extérieur du rectangle sont exclus. Le focus
  reste B jusqu'au dernier clic, qui reprend A. La rotation ne lance aucun scan
  et n'invalide pas le résultat réseau. Le clic est propagé au jeu.
  Le test de zone DOFUS est géométrique : il ne vérifie pas qu'une autre fenêtre
  ordinaire ne recouvre pas ce point ; ne pas supposer cette protection.
- **Échec :** E0 + position du pointeur, rectangles et applications superposées.

### A4-FOCUS-01 — Bouton Focus de la page serveur [S1, S5, S8]

- **Préconditions :** A dans les résultats de recherche de la page S ; R masqué.
- **Actions :** cliquer **Focus** sur A ; attendre ; coller dans Bloc-notes.
  Refaire avec un équipement dont certaines données économiques manquent.
- **Attendu :** R s'ouvre et montre l'équipement explicitement choisi. Le bouton
  UI copie aussi son nom exact, sans niveau, au presse-papiers ; F8/molette
  n'effectuent pas cette copie. Un calcul incomplet conserve l'identité du focus
  et montre les mentions calculées « indisponible », « incomplet » ou « Résultat
  partiel ». Un coefficient DoFocus est bleu ; absent, « Coefficient : À scanner ».
  Ces libellés ne demandent pas de relancer l'ancien scanner.
- **Échec :** E0 + ligne Focus choisie, données locales/sources et texte collé.

### A4-FOCUS-02 — Notification de marché et visibilité [S1, S5, S7]

- **Préconditions :** focus A ; prix ou coefficient effectivement utilisé par A
  susceptible de changer via une nouvelle observation réseau sur S ; priorité
  connue. Ne pas prendre un prix manuel prioritaire comme preuve de non-rafraîchissement.
- **Actions :** masquer R avec ◈ ; provoquer une observation réseau pertinente
  par consultation HDV ou concassage ; après traitement, ouvrir R avec ◈.
- **Attendu :** une notification du serveur courant déclenche le recalcul du
  focus existant, sans changer l'équipement et sans ouvrir R. Un concassage
  peut ouvrir C séparément. Les valeurs visibles après réouverture suivent les
  données effectives et les règles A2, même si une source prioritaire les garde
  identiques. M n'est pas un affichage automatique des prix reçus par le réseau.
- **Échec :** E0 + données avant/après, sources prioritaires, serveur de la
  notification et traces réseau. Une valeur identique ne prouve pas l'absence
  de recalcul ni l'absence de notification après déduplication.

### A4-FOCUS-03 — Recalculs concurrents et résultat périmé [S1, S5]

- **Préconditions :** A/B accessibles par **Focus** dans l'UI, données chargées,
  aucune demande F8 ; si possible calculs assez longs pour se chevaucher.
- **Actions :** cliquer Focus A puis Focus B rapidement ; répéter cinq fois
  en terminant par B. Refaire pendant une mise à jour réseau pertinente.
  Facultativement, poser des tracepoints sans arrêt dans
  `RefreshFocusedProfitabilityAsync` à la demande, avant/après `CalculateAsync`
  et avant `ShowProfitability` ; relever version, ItemId et serveur.
- **Attendu :** le sémaphore sérialise les calculs et la génération écarte les
  demandes périmées, avec nouvelle vérification avant mise à jour UI. Après le
  traitement de B, un résultat A ancien ne doit pas remplacer B ni mélanger son
  titre et les valeurs de A. Une transition A avant que B soit demandé est normale.
- **Échec :** E0 + vidéo et ordre des demandes/résultats, versions si disponibles.
  Sans chevauchement observé, noter **Partiel** : des clics rapides seuls ne
  démontrent pas l'exercice de la protection. Ne pas bloquer tous les threads au
  debugger puis prétendre avoir provoqué une concurrence ; ne pas modifier le code.

## Concassage réseau, valorisation et historique

Pour ces cas : S sélectionné avant l'action en jeu, capture Npcap fonctionnelle,
debug actif, aucun F8 nécessaire. Vérifier que les UID des équipements sont déjà
associés à des ItemId dans le traitement réseau ; les messages réels conservés
permettent de contrôler cette précondition. L'absence de résultat après un `kci`
dont tous les UID sont inconnus est un retour anticipé actuel, pas une preuve
d'échec de l'ouverture de C [S7].

### A4-CR-01 — Résultat automatique et snapshot initial [S6, S7, S8]

- **Préconditions :** un équipement identifié, prix des runes attendues présents
  sur S, coefficients activés, décote relevée ; C masqué et R focalisé sur B.
- **Actions :** effectuer manuellement un concassage dans DOFUS. Ne presser ni
  F8 ni F9. Photographier le résultat DOFUS et C. Ouvrir l'historique des
  concassages, rafraîchir, développer la nouvelle ligne.
- **Attendu :** C s'ouvre automatiquement : « ● Résultat reçu par le réseau »,
  « Objets détruits : N », « Types de runes : K ». R conserve B. Les quantités
  positives des runes sont agrégées par ItemId et affichées par nom ; les
  valeurs et lots utilisent les observations locales effectives de S.
  Valeur ajustée = valeur brute × (1 − décote/100), affichage des totaux à zéro
  décimale. Les coefficients ne figurent pas dans C : ils sont enregistrés
  s'ils sont positifs et activés, et apparaissent dans les détails d'historique
  avec la source « Jeu ». La sauvegarde d'historique est asynchrone : C visible
  ne prouve pas qu'elle est terminée ; rafraîchir la page pour la vérifier.
- **Échec :** E0 + `kci`, associations UID, runes/quantités, coefficient réel,
  prix/lots/source/date effectifs, décote et détail historique ; relever le
  délai d'apparition. Une exception d'historique est actuellement avalée par
  `PersistHistoryAsync`, donc une absence de log ne suffit pas à exclure l'échec.

### A4-CR-02 — Lignes multiples, même équipement et UID inconnu [S6, S7]

- **Préconditions :** transaction réelle multi-équipements ; si disponible,
  deux exemplaires du même ItemId avec UID distincts. Pour la variante inconnue,
  disposer d'une séquence réelle montrant l'absence d'association d'un UID.
- **Actions :** concasser en une transaction ; relever l'ordre des lignes `kci`,
  C et les détails d'historique.
- **Attendu :** N compte les lignes acceptées par le routeur, pas les types
  uniques. Les runes d'un même ItemId sont additionnées. Dans l'historique, les
  équipements sont indexés par ItemId : une seule entrée par type, le dernier
  coefficient et la position de sa dernière ligne font foi. Les lignes d'UID
  inconnu sont ignorées intégralement, runes comprises ; si toutes sont ignorées,
  aucun nouveau résultat/historique n'est fourni et l'ancien C peut rester affiché.
  La persistance des coefficients suit l'ordre des lignes acceptées.
- **Échec :** E0 + ordre `kci`, UID/ItemId avant résultat, compte accepté et
  coefficients successifs. La simple concordance du total ne valide pas l'ordre
  des écritures ; sans séquence inconnue ou doublon réel, marquer ces variantes
  **Non exécutées**, sans forcer les caches au debugger.

### A4-CR-03 — Coefficients désactivés, résultat toujours visible [S6, S7, S8]

- **Préconditions :** S, UID connus, au moins une rune positive ; prix présents ;
  noter l'état des observations de coefficient de l'équipement avant le test.
- **Actions :** Paramètres → désactiver **Coefficients de brisage**, fermer le
  panneau ; masquer C puis concasser. Consulter C, l'historique des concassages
  et les observations de coefficient. Restaurer le paramètre.
- **Attendu :** C s'ouvre, runes et valorisation sont présentes. Aucune nouvelle
  observation de coefficient n'est écrite par ce `kci` et sa notification de
  coefficient n'est pas envoyée. Le snapshot d'historique du concassage contient
  néanmoins le coefficient reçu et sa source « Jeu ». Le flag ne supprime ni
  les lignes de résultat ni ces informations historiques. Les flags de prix
  ne servent pas non plus de commutateur général pour l'affichage de C.
- **Échec :** E0 + paramètre au moment du message, historiques avant/après.
  L'absence de notification exige un test de routage futur ; l'UI seule ne
  permet pas de la certifier.

### A4-CR-04 — Prix incomplets, lots estimés ou résultat sans rune [S6, S8]

- **Préconditions :** cas réel avec une rune sans prix effectif sur S ; ou
  reliquat dont seuls des prix de lots supérieurs sont disponibles. Pour la
  variante sans rune, disposer d'un résultat accepté contenant zéro rune positive.
  Ne pas vider artificiellement les données pour atteindre ces branches.
- **Actions :** concasser ; relever les lignes et développer l'historique.
- **Attendu :** rune sans valorisation : « prix manquant » en rouge, totaux
  brut/ajusté « — » si au moins une ligne est inconnue. Reliquat estimé : terme
  orange préfixé `~`, division par la taille du lot si > 1 ; l'algorithme suit
  les lots disponibles du plus grand au plus petit, pas un coût d'achat minimal.
  Sans rune positive mais avec au moins une ligne acceptée : C s'ouvre, N > 0,
  zéro type de rune, totaux « — » et aucun snapshot sauvegardé.
  Particularité du détail historique : sa ligne de lot affiche actuellement
  `Count × LotQuantity = Count × LotPrice`, même pour un reliquat estimé où
  `Count` représente des unités. Cette ligne peut donc contredire la valeur
  sauvegardée ; voir le suivi séparé, ne pas « corriger » l'attendu dans A4.
- **Échec :** E0 + prix/tailles de lots disponibles et champ estimé. Coller les
  formules uniquement dans Bloc-notes si nécessaire ; aucune comparaison Excel.
  Classer séparément chaque variante qui n'a pas pu être produite réellement.

### A4-CR-05 — Remplacement, masquage et immutabilité de l'historique [S6, S8]

- **Préconditions :** résultat CR-01 sauvegardé ; relever sa date, ses prix,
  runes, coefficients, décote et valeurs ; paramètres stables.
- **Actions :** masquer/rouvrir C avec ⚒. Consulter de nouveaux prix de runes en
  HDV ; rafraîchir l'historique. Effectuer une deuxième transaction distincte ;
  consulter C puis les deux entrées historiques.
- **Attendu :** le masquage conserve le résultat. La deuxième transaction
  remplace la première dans C, sans cumul permanent, et produit son propre
  snapshot si elle contient des runes. L'ancien historique garde ses valeurs
  initiales malgré les prix ultérieurs. Dans le parcours réseau actuel, C n'est
  pas abonné aux notifications de prix : une consultation HDV ultérieure ne
  revalorise donc pas à elle seule le résultat affiché. L'abonnement se trouve
  uniquement dans l'ancien `StartNew`, non appelé par ce parcours.
- **Échec :** E0 + deux messages et deux snapshots, avant/après consultation.
  Ne pas demander un recalcul de l'ancien snapshot comme résultat attendu.

## Manipulation des overlays

### A4-UI-01 — Barre : ouvrir, masquer, réactiver la fenêtre principale [S1, S2, S4, S6]

- **Préconditions :** focus et diagnostic/résultat disponibles si possible ;
  aucune action automatique en attente.
- **Actions :** pour chaque bouton ◈/↻/⚒, cliquer deux fois en observant sa
  fenêtre ; répéter. Mettre DOFUS au premier plan puis cliquer ⚙ dans la barre.
- **Attendu :** chaque bouton agit seulement sur son overlay ; le contenu est
  conservé après masquage. Ouvrir C vide donne l'attente réseau, sans lancer une
  session OCR. Les overlays restent au-dessus des fenêtres ordinaires. ⚙ active
  la fenêtre principale ; son panneau Paramètres ne s'ouvre qu'en cliquant le
  bouton ⚙ de la page serveur. La barre elle-même ne se masque pas avec F7.
- **Échec :** E0 + bouton et fenêtre ciblés, ordre des fenêtres au premier plan.

### A4-UI-02 — Déplacement et redimensionnement [S1, S2, S4, S6, S9]

- **Préconditions :** les trois overlays ouverts ; écran assez grand ; relever
  leur disposition initiale et celle de la barre.
- **Actions :** maintenir le bouton gauche sur l'en-tête ⋮⋮ de chacun et déplacer
  d'environ 100 px ; relâcher. Déplacer la barre par ⋮. Agrandir puis réduire
  chaque overlay par un bord puis un coin ; tenter de réduire sous le minimum
  et de déplacer hors de la zone de travail. Lire le JSON après relâchement.
- **Attendu :** déplacements et tailles suivent le geste et sont enregistrés
  à sa fin. Minimum natif : R 260×170, M 260×180, C 300×220, limité à la zone
  de travail si elle est plus petite. La barre se déplace, mais reste 205×50
  et n'a pas de redimensionnement prévu. La contrainte utilise le moniteur le
  plus proche et sa zone de travail, sous réserve des appels Windows réussis.
  Ne pas assimiler une mesure visuelle en DIP à un pixel natif à DPI différent.
- **Échec :** E0 + échelle Windows, bord/coin utilisé, JSON avant/après, limites
  des moniteurs et éventuel recouvrement par une fenêtre de détail.

### A4-UI-03 — Défilement et détails de Rentabilité [S5, S6]

- **Préconditions :** R avec un équipement à détails multiples ; C avec
  plusieurs types de runes ; réduire la hauteur pour obtenir un dépassement.
- **Actions :** faire défiler le corps de R et la liste de C avec la molette.
  Survoler dans R **Coefficient**, **Valeur runes**, **Craft**, puis l'indication
  de données manquantes si présente ; entrer dans le détail et y faire défiler
  les longues listes, puis déplacer le pointeur en dehors.
- **Attendu :** les corps défilent dans leurs zones dédiées, sans déplacer
  toute la fenêtre. Les détails disponibles s'ouvrent au survol dans R ; leur
  fermeture utilise un délai de 250 ms pour permettre d'y entrer. La rotation
  de molette ne lance aucun OCR et n'invalide pas C. M possède aussi une zone
  de défilement pour ses détails, mais les diagnostics F8 peuvent être trop
  courts pour la faire déborder ; ne pas appeler un ancien affichage OCR pour
  fabriquer du contenu.
- **Échec :** E0 + zone sous le pointeur, hauteur et contenu ; si une zone ne
  déborde pas, sa variante de défilement reste Non exécutée.

### A4-UI-04 — Copie et retour du feedback [S4, S5, S6]

- **Préconditions :** R valorisé, M avec diagnostic F8 et C avec plusieurs
  termes de lots ; aucun rafraîchissement attendu ; Bloc-notes ouvert.
- **Actions :** cliquer le nom d'équipement dans R, sa valeur runes puis son
  coefficient ; coller après chaque clic. Dans M, cliquer le nom et coller.
  Dans C, copier le nom d'une rune, sa quantité, sa valeur et les deux totaux.
  Cliquer un terme de lot, attendre au moins 400 ms et coller ; attendre une
  seconde puis double-cliquer ce terme et coller. Observer les pieds de page.
- **Attendu :** R copie le nom sans niveau ; valeurs monétaires arrondies en
  entier sans unité/groupement ; coefficient sans `%`, à deux décimales maximum
  selon la culture. M copie son libellé d'objet. C copie nom, quantité et valeurs
  numériques ; valeur manquante : pas de copie numérique. Un simple clic sur
  un terme copie `=Count*LotPrice` (ou `=Count*LotPrice/LotQuantity` si estimé) ;
  le double-clic copie la somme complète préfixée `=`. Le simple clic attend
  280 ms pour distinguer le double-clic. Aucune feuille Excel n'est nécessaire.
  Feedback « ✓ … copié » : R revient après environ 1,2 s au texte gris
  **« En attente d'un équipement » même avec un focus**, M restaure son ancien
  footer après 1,2 s, C efface son feedback après 0,9 s. Une mise à jour
  intervenue entre-temps peut modifier ces retours.
- **Échec :** E0 + texte brut collé, cible exacte, simple/double-clic, culture et
  chronologie du feedback ; ne pas comparer seulement les valeurs compactées `k/M`.

### A4-UI-05 — Persistance après redémarrage [S1, S2, S4, S6, S9]

- **Préconditions :** dispositions personnalisées par UI-02 ; noter le JSON et
  une capture ; R/M/C visibles ; laisser les sauvegardes d'historique terminer.
- **Actions :** fermer la fenêtre principale ; vérifier la fermeture des
  overlays et de la barre ; relancer ; attendre puis ouvrir R/M/C par la barre.
- **Attendu :** positions et dimensions sont restaurées. La visibilité n'est
  pas persistée : barre visible, R/M/C masqués à nouveau. Focus, dernier
  équipement réseau, diagnostic M et résultat C sont des états mémoire et ne
  sont pas restaurés à partir de l'historique. Le serveur doit être choisi de
  nouveau dans la session normale ; les données persistées restent présentes.
- **Échec :** E0 + JSON avant fermeture/après lancement, PID pour exclure une
  seconde instance encore ouverte et état initial avant nouvelles réceptions.

### A4-UI-06 — Restauration des positions et adaptation d'écran [S9, S8]

- **Préconditions :** disposition personnalisée ; un overlay masqué ; deux
  écrans pour la variante optionnelle. Ne pas modifier le JSON à la main.
- **Actions :** page S → ⚙ Paramètres → **Restaurer la position par défaut des
  overlays**. Observer les fenêtres, puis ouvrir celle masquée. Pour la variante
  multi-écran : déplacer un overlay sur l'écran secondaire, fermer BestCrush,
  retirer cet écran dans Windows, relancer et ouvrir l'overlay.
- **Attendu :** confirmation « Positions et tailles des overlays restaurées. ».
  Positions et dimensions reviennent aux valeurs ci-dessous, contraintes à la
  zone de travail ; la restauration ne force pas l'ouverture d'un overlay masqué.
  À la relance sans écran secondaire, le chargement contraint la disposition
  au moniteur disponible le plus proche et sauvegarde le résultat ajusté.
- **Échec :** E0 + JSON, topologie/DPI avant et après, fenêtres restées masquées.

| Fenêtre | X | Y | Largeur | Hauteur |
|---|---:|---:|---:|---:|
| R | 40 | 70 | 340 | 432 |
| M | 395 | 70 | 330 | 300 |
| C | 750 | 80 | 390 | 520 |
| Barre | 210 | 5 | 205 | 50 |

## Interactions particulières

### A4-INT-01 — Concassage après F7 et nouvel ensemble à restaurer [S1, S6, S7]

- **Préconditions :** R/M visibles, C masqué ; S et UID connus ; aucun F8 en file.
- **Actions :** F7 ; concasser ; attendre C ; F7 puis F7 à nouveau.
- **Attendu :** le concassage rouvre **C seul** ; F7 n'est pas une suspension
  de la capture ni un verrou durable de visibilité. Au F7 suivant, C visible
  remplace l'ancien ensemble R/M mémorisé. Le dernier F7 restaure donc **C seul**.
  Cette particularité diffère du cas sans événement intercalé.
- **Échec :** E0 + chronologie F7/`kci`, triplet visible à chaque étape et tout
  focus explicite concurrent (qui ouvrirait R et effacerait l'ancien ensemble).

### A4-INT-02 — Changement de serveur et dernier équipement [S1, S7, S8]

- **Préconditions :** S sélectionné ; dernier équipement réseau A enregistré
  sous S ; focus A et éventuellement C affiché. Plus aucun trafic DOFUS pendant
  la première partie : fermer le client après l'observation, sans fermer BestCrush.
- **Actions :** par le menu **Serveur**, choisir T et atteindre sa page. Noter
  R/C avant toute nouvelle action. Cliquer Focus B sur T. Revenir ensuite sur S.
  Pour vérifier la molette, recommencer avec DOFUS visible mais sans nouvelle
  observation d'équipement, vérifiée dans les traces : sur T, clic molette ;
  revenir sur S et refaire le clic.
- **Attendu :** `SelectServer` change le serveur courant sans vider le focus ni
  les caches et sans demander à lui seul un recalcul : R/C peuvent encore
  montrer les données précédentes. Le focus explicite B recalcule pour T.
  `GetForServer(T)` refuse le snapshot étiqueté S, donc la molette ne reprend
  pas A sur T. Si aucun autre snapshot n'a été écrit, retour sur S : A peut
  être repris. Il n'existe qu'un snapshot, pas une mémoire par serveur : une
  identification sous T remplace celle de S.
  L'étiquette serveur provient de la sélection BestCrush au traitement, pas
  d'une identification du serveur de la connexion DOFUS. Garder T sélectionné
  pendant du trafic réel de S peut donc attribuer les nouvelles données à T.
- **Échec :** E0 + noms exacts, chronologie de sélection et nouvelles traces.
  Si le client relancé envoie un inventaire, la variante sans nouvelle
  observation n'est plus valide. Ne pas produire volontairement des écritures
  sous le mauvais serveur pour démontrer ce risque.

### A4-INT-03 — Absence de Npcap [S1, S3, S10]

- **Préconditions :** poste/VM Windows sans Npcap, mais BestCrush et son catalogue
  utilisables ; pour F8, client DOFUS et capture graphique fonctionnels.
  Ne pas désinstaller ou renommer des fichiers système du poste habituel.
- **Actions :** lancer BestCrush ; relever le bandeau ; sélectionner S ; ouvrir
  les overlays, tester F7 puis F8 sur A et la molette sans snapshot. Cliquer
  **Télécharger Npcap** ; sans installer, cliquer **Revérifier**. Si le testeur
  installe ensuite Npcap séparément, refaire Revérifier et une observation réseau.
- **Attendu :** bandeau « Npcap requis pour la capture réseau passive », capture
  réseau inactive dans le log. L'UI, F7 et F8 restent disponibles ; la molette
  n'a aucun équipement réseau à reprendre. Télécharger ouvre le site officiel,
  BestCrush n'installe ni ne redistribue Npcap. Revérifier garde l'avertissement
  tant que la détection échoue ; si elle réussit, appelle `Start` et retire le
  bandeau. La présence des DLL et du pilote/registre n'est pas une garantie
  d'ouverture des interfaces : vérifier les logs et la réception effective.
- **Échec :** E0 + statut du bandeau, chemins vérifiés, logs d'ouverture ; sans
  environnement sans Npcap, classer **Bloqué**. Ce test ne valide pas un
  redémarrage de capture après `Stop`.

### A4-INT-04 — Activation et désactivation des artefacts [S3, S7, S8, S11]

- **Préconditions :** S choisi, Npcap fonctionnel, A reconnu ; relever la liste
  et les dates des fichiers existants sans les supprimer.
- **Actions :** désactiver **Conserver les artefacts de debug** ; F8 puis une
  consultation HDV ; attendre leur fin. Activer l'option, refaire ces actions.
  Désactiver, attendre la fin des écritures déjà lancées, refaire. Réactiver
  sans redémarrer, puis produire une nouvelle observation. Restaurer le réglage.
- **Attendu :** option désactivée : les captures F8 temporaires sont encore
  créées pour l'analyse puis leur dossier est supprimé à la fin ; aucune
  nouvelle trace réseau détaillée n'est écrite. Activée : images/crops/texte
  selon la branche, et traces réseau conservés ; `session.txt` est créé à la
  première écriture, `wire.jsonl` reçoit les enveloppes décodées, `events.log`
  les événements sémantiques. Réactivation dans la même instance : reprise dans
  le même dossier réseau, pas une nouvelle session par bascule. Désactiver ne
  purge pas les anciens artefacts. Les logs applicatifs ordinaires continuent.
  Pour F8, le flag est lu au nettoyage : une bascule pendant l'analyse change
  la conservation de cette capture. Le champ Server de `session.txt` décrit
  sa création et ne suit pas les changements de serveur ultérieurs.
- **Échec :** E0 + option, horaires précis, arborescence, tailles/dates avant et
  après, résultat fonctionnel. En cas d'erreur d'accès disque, conserver le log :
  les écritures debug sont attendues dans le chemin réseau et ne sont pas toutes
  isolées du traitement métier ; ne pas provoquer artificiellement cette erreur.

## Particularités à conserver et anomalies à traiter séparément

Les libellés historiques de M, le diagnostic « Dofus non détecté » sur zéro
infobulle, le footer R après copie, la restauration F7 remplacée après un `kci`,
la sélection du dernier équipement plutôt que du survol, et le snapshot unique
multi-serveur sont des attendus explicites ci-dessus. A4 ne les corrige pas.

Les constats suivants sont consignés comme **BUG EXISTANT — CORRECTION
FONCTIONNELLE À VALIDER**, établis par lecture et à confirmer à l'exécution :

- R peut conserver un affichage de l'ancien serveur après sélection du nouveau,
  jusqu'au prochain recalcul ; les observations réseau utilisent le serveur
  sélectionné par l'utilisateur, sans vérification de correspondance au client.
- C n'est pas abonné aux notifications de prix par le parcours réseau, donc sa
  valorisation ne se rafraîchit pas sur les seuls prix ultérieurs.
- Le détail de lot estimé dans History affiche une multiplication de lots là
  où `Count` représente le reliquat en unités, sans la division employée pour
  sa valorisation ; le total sauvegardé n'est pas recalculé par cet affichage.

Le [suivi des lots distincts](codebase-cleanup-follow-ups.md) garde ces constats,
le défaut de chevauchement TCP et **NU1903 — SQLitePCLRaw.lib.e_sqlite3 2.1.10,
GHSA-2m69-gcr7-jv3q**. Aucun correctif ou changement de dépendance dans A4.

## Couverture A3 à compléter impérativement avant la Phase E

Voir le [rapport A3](codebase-cleanup-a3-network-tests.md) pour les obstacles
et adaptations minimales proposées, toujours non appliquées. Les 111 tests A3
ne prouvent pas le routage privé Windows de `DofusNetworkCaptureService`.
Les exemples manuels A4 apportent des observations UI ; ils ne ferment pas
les lacunes suivantes et n'autorisent pas à extraire les traitements sans protection.

| Comportement non protégé par A3 | Caractérisation encore requise avant extraction |
|---|---|
| `jzn` → persistance | Minimum strictement positif par lot x1/x10/x100/x1000, aucune offre, limites numériques, flags et retours anticipés |
| `kei/kef/kbd` | OfferId, remplacement de demande en attente, fenêtre inclusive 5 s / 5 s + un tick, reçu, refresh ressource/rune sans remplacement du prix équipement |
| UID/ItemId → équipement fiable | Remplissage des caches par les vrais handlers, catalogue/type, serveur et appel réel à `RememberLastEquipmentAsync` |
| `kci` | UID connu/inconnu, filtrage runes, conversions `checked`, ordre des coefficients, dernière ligne, flags indépendants de la transmission du résultat |
| Écritures → notifications | Nombre et ordre réels ; état persisté lu depuis le callback ; notification maintenue après déduplication |
| Résultat → snapshot | Agrégation, remplacement, dernier coefficient par ItemId, prix/décote, garde sans rune, constitution du write model et sauvegarde asynchrone |
| Worker / Channel | Lecteur unique, ordre métier, horodatages, retours anticipés, erreurs ; l'ordre des frames testé ne remplace pas cette protection |

Le test manuel de concurrence du focus reste lui aussi une observation, pas
une preuve automatisée de tous les entrelacements. Toute adaptation de
production destinée à rendre ces cas testables nécessite un lot approuvé,
avant la première extraction de méthodes réseau en Phase E.

## Sources relues au HEAD de référence

| Repère | Fichiers et points d'entrée |
|---|---|
| S1 | [OverlayService.cs](../BestCrush/Services/OverlayService.cs) : `Initialize`, `ToggleAllOverlays`, `KeyboardHookCallback`, `MouseHookCallback`, `RequestTooltipRead`, `ProcessTooltipReadQueueAsync`, `ProcessCapturedReadAsync`, `FocusLastNetworkEquipmentAsync`, `FocusEquipmentAsync`, `RefreshFocusedProfitabilityAsync`, `OnMarketDataChanged`, `ActivateMainWindow`, toggles de visibilité |
| S2 | [App.xaml.cs](../BestCrush/App.xaml.cs), [OverlayControlBarService.cs](../BestCrush/Services/OverlayControlBarService.cs), [OverlayControlBarPage.cs](../BestCrush/Overlay/OverlayControlBarPage.cs) : création/destruction, bindings, grip, boutons, rafraîchissement des états |
| S3 | [DofusWindowService.cs](../BestCrush/Services/DofusWindowService.cs), [DofusCaptureService.cs](../BestCrush/Services/DofusCaptureService.cs), [DofusItemTooltipDetectionService.cs](../BestCrush/Services/DofusItemTooltipDetectionService.cs), [DofusItemRecognitionService.cs](../BestCrush/Services/DofusItemRecognitionService.cs) : choix de fenêtre, capture/nettoyage, `DetectAsync`, `RecognizeEquipmentAsync`, `FindBestMatch` |
| S4 | [MarketCaptureOverlayService.cs](../BestCrush/Services/MarketCaptureOverlayService.cs), [MarketCaptureOverlayPage.cs](../BestCrush/Overlay/MarketCaptureOverlayPage.cs) : `Update`, `EnsureWindow`, diagnostics `Show…`, copie |
| S5 | [OverlayPage.cs](../BestCrush/Overlay/OverlayPage.cs), [FocusedEquipmentState.cs](../BestCrush/Services/FocusedEquipmentState.cs) : `ShowProfitability`, coefficient, détails au survol, copie, état de focus |
| S6 | [CrushSessionService.cs](../BestCrush/Services/CrushSessionService.cs), [CrushSessionOverlayPage.cs](../BestCrush/Overlay/CrushSessionOverlayPage.cs) : `ApplyNetworkCrushAsync`, `CreateSnapshot`, `BuildRuneLotBreakdown`, `TryPrepareHistorySaveLocked`, `ScheduleHistorySave`, `PersistHistoryAsync`, `Show/Hide`, `StartNew`, `Update`, copie |
| S7 | [DofusNetworkCaptureService.cs](../BestCrush/Services/DofusNetworkCaptureService.cs), [LastNetworkEquipmentState.cs](../BestCrush/Services/LastNetworkEquipmentState.cs) : `Start`, `ProcessMessageAsync`, `PersistMarketAsync`, `PersistCrushAsync`, `RememberLastEquipmentAsync`, `GetForServer`, debug |
| S8 | [Server.razor](../BestCrush/Components/Pages/Server.razor), [Servers.razor](../BestCrush/Components/Pages/Servers.razor), [MainLayout.razor](../BestCrush/Components/Layout/MainLayout.razor), [History.razor](../BestCrush/Components/Pages/History.razor), [CurrentServerState.cs](../BestCrush/Services/CurrentServerState.cs), [ServersService.cs](../BestCrush.Domain/Services/ServersService.cs) : sélection de session, `FocusInOverlay`, paramètres, `RefreshAsync`, détails de session |
| S9 | [OverlayLayoutSettingsService.cs](../BestCrush/Services/OverlayLayoutSettingsService.cs) et services de fenêtres S1/S2/S4/S6 : `GetDefaultLayout`, `GetValidatedLayout`, `ConstrainToVisibleScreen`, `ResetAll`, `Begin/EndDrag`, `Begin/EndResize`, `RestoreDefaultLayout` |
| S10 | [NpcapPrerequisiteService.cs](../BestCrush/Services/NpcapPrerequisiteService.cs), [NpcapRequirementBanner.razor](../BestCrush/Components/Pure/NpcapRequirementBanner.razor) : `Refresh`, `Recheck`, lien officiel |
| S11 | [BestCrushSettingsService.cs](../BestCrush/Services/BestCrushSettingsService.cs), [MauiProgram.cs](../BestCrush/MauiProgram.cs) : flags/préférences, chemins, logging et DI |
| S12 | [Historique des tests A3](codebase-cleanup-a3-network-tests.md), [suivis séparés](codebase-cleanup-follow-ups.md) |

## Fiche de retour du checkpoint

Copier une ligne par scénario et par variante, avec les pièces jointes E0 :

| Scénario / variante | Statut | Observation réelle | Écart / obstacle | Pièce jointe |
|---|---|---|---|---|
| A4-… | Non exécuté | | | |

Indiquer en tête : HEAD testé, version binaire, environnement Windows/DOFUS,
serveurs et réglages, puis le nombre de cas conformes, partiels, bloqués et en
écart. Signaler particulièrement les faux positifs OCR, les entrelacements non
observés et les variantes de messages impossibles à provoquer normalement.

**Arrêt après ce protocole A4 et sa publication. Aucun début de Phase B ou
d'extraction réseau avant retour et validation du checkpoint.**
