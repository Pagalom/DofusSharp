# Correctif F8 — Détection des infobulles sur la palette Dofus courante

Base de travail : `1f98fbb` (S4a, encore en attente du checkpoint Windows).

## Symptôme constaté

Une capture F8 `dofus.png` fournie à titre de diagnostic montre l'infobulle **Hache du Guerrier Zoth**, visible au-dessus de l'inventaire ; pourtant l'overlay Rentabilité ne met aucun équipement en focus et « Mise à jour marché » affiche « Dofus non détecté ».

Inspection du chemin F8 :
- `OverlayService.RequestTooltipRead` trouve la fenêtre Dofus et lance `DofusCaptureService.CaptureAsync` ;
- `DofusItemTooltipDetectionService.DetectAsync` retourne zéro candidat si les couleurs/formes de l'infobulle n'entrent pas dans les seuils ;
- `OverlayService.ProcessCapturedReadAsync` appelait à tort `ShowReadCancelled`, qui suggérait que la fenêtre Dofus n'était pas détectée.

## Reproduction locale sans publication de la capture

- Capture originale : **1920×1080**.
- L'algorithme OpenCV original (mêmes plages BGR, contours, bornes de dimensions/ratios) ne retrouve **aucun** en-tête valide dans l'image.
- Une détection de secours avec plages BGR approximant le style visible :
  - bandeau : `(45,25,23)` à `(65,41,42)` ;
  - corps : `(49,29,27)` à `(59,40,41)`
  produit **un seul** couple bandeau/corps, au niveau de l'infobulle visible : bandeau `(1347,36,394,124)`, corps `(1354,161,387,116)`.
- Les coordonnées se réfèrent à l'image brute, pas à l'affichage réduit dans la conversation.

L'image de l'utilisateur **n'est pas** ajoutée au dépôt.

## Correctif

1. Conserver la détection historique prioritaire et n'essayer la palette supplémentaire que si aucun en-tête valide n'est détecté.
2. Préserver les contraintes géométriques en vigueur, l'ambiguïté sur plusieurs infobulles et les règles de reconnaissance des objets.
3. Distinguer « Dofus non détecté » (absence de fenêtre) de « Infobulle non détectée » (capture existante, pas de candidat).
4. Actualiser le texte de la capture pour mentionner F8 plutôt que le clic molette.

## Tests automatiques

Quatre scénarios avec images **synthétiques** OpenCV créées en mémoire :
- style courant : 1 bandeau ;
- style historique : 1 bandeau ;
- fond vide : 0 bandeau ;
- 2 infobulles : 2 bandeaux conservés comme ambigus.

Nombre de tests attendu après S4a et ce correctif : **245** (241 + 4), si le checkpoint de S4a est conforme.

## Limites et validation Windows

Les tests ne simulent ni Windows Graphics Capture, ni l'OCR, ni le matching DofusDB. La capture brute permet de reproduire *la détection géométrique*, pas de garantir que l'OCR reconnaîtra le nom de l'objet. Le résultat final doit être confirmé avec F8 sous Windows.

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Puis : F5 ; sauvegarde préalable de la base conseillée ; survol de la **Hache du Guerrier Zoth** avec une seule infobulle ; F8 ; vérifier l'état du panneau « Mise à jour marché » et la mise au focus Rentabilité. Si l'équipement n'est toujours pas reconnu, collecter le détail OCR `tooltip-title-ocr.txt` (option de debug activée) et inspecter la région du titre, sans abaisser les critères de correspondance au hasard.

Ce correctif ne touche pas au décodage réseau Npcap, à la persistance, aux calculs métier ni au schéma SQLite. Aucun merge `main` ni changement de version.
