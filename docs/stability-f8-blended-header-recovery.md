# F8 — récupération d'un bandeau fusionné avec l'interface HDV

## Constat utilisateur — 11 octobre 2026

Après `08ae64f`, la **Hache du Guerrier Zoth** est reconnue par F8, mais l'infobulle de la **Coiffe du Royalmouth** (visible sur un nouveau screenshot 1920 × 1080) laisse « Infobulle non détectée ». L'overlay Rentabilité affiche encore la hache reconnue précédemment.

Analyse des pixels du screenshot reçu **sans ajouter l'image dans Git** :

- `modern_header` : le contour du bandeau se confond avec des surfaces de l'HDV, largeur > 600 px et rejetée par le détecteur ;
- `modern_body` : rectangle régulier de **(1074,195,395,654)**, rapport de remplissage ~0,91 ;
- un rectangle de palette `legacy_header` autour de **(1073,145,373,75)** correspond uniquement à une partie **basse** de l'entête. L'utiliser comme bandeau ferait lire « STATUT » plutôt que le titre OCR ;
- en recherchant les lignes de couleur du bandeau moderne à partir du corps, la vraie zone de titre retrouvée est **(1074,79,395,112)**, correspondant à « Coiffe du Royalmouth ».

Le screenshot de l'écran n'est pas une copie garantie du `dofus.png` WGC produit exactement au moment de F8 ; le succès final doit donc être confirmé en exécution réelle.

## Correctif isolé

- Conserver l'ordre et le comportement des détecteurs `legacy` et `modern` existants ; la nouvelle approche n'est tentée que s'ils ne renvoient **aucun bandeau**.
- Sur la palette `modern_body`, détecter les rectangles remplis (>=75%) d'au moins 100 px de hauteur.
- Remonter dans les 200 px précédant le corps, en mesurant la densité de `modern_header` sur la moitié droite, moins perturbée par l'illustration d'objet et le texte.
- Accepter uniquement une véritable bande de titre : hauteur 60–180 px, séparateur <=12 px, largeur/hauteur >=1,8. Tolérer jusqu'à quatre lignes isolées de rupture.
- Retourner un bandeau calé sur **le début de la zone de titre**, afin de conserver `ExtractTitleRegion` et l'OCR existants.
- Refuser le cas d'un corps sombre sans bandeau ; maintenir la gestion existante des bandeaux multiples.

Aucune modification d'OCR, de serveur, de focus molette, de capture réseau, de `protocol-map.json` ou de SQLite.

## Régression

Trois nouveaux tests synthétiques :
1. bandeau « fusionné » horizontalement avec l'HDV, corps long → récupérer un titre correctement positionné ;
2. corps sans bandeau → aucun candidat ;
3. corps de taille moyenne avec bandeau fusionné → titre correctement positionné.

La base précédemment validée était de **275 tests**. Trois tests ajoutés par `08ae64f` n'ont pas été encore confirmés sous Windows ; trois nouveaux tests portent le total **attendu** à **281**.

## Checkpoint Windows

```powershell
git pull --ff-only origin refactor/codebase-cleanup
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

F5 : serveur sélectionné, F8 sur **Coiffe du Royalmouth**, F8 sur **Hache du Guerrier Zoth**, puis sur une troisième infobulle plus petite. Contrôler que l'overlay Rentabilité reçoit les noms réellement survolés (ne pas confondre focus réseau et lecture F8). Si un item échoue encore, activer « Conserver les artefacts de debug » et comparer `DebugCaptures/Tooltip/f8-tooltip-*/dofus.png` avec le screenshot de bureau.

La source du problème pourrait aussi se situer dans une différence de capture WGC, ou dans l'OCR, si le message d'erreur change. Aucune de ces causes n'est présumée résolue par le test de géométrie synthétique.
