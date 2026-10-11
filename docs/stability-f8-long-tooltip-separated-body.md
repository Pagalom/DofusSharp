# F8 — infobulles longues et corps interrompu (2026-10-11)

## Symptôme

Sous Windows, BestCrush affiche « Dofus détecté — 1920×1080 » mais « Infobulle non détectée » en présence d'une infobulle visible « Hache du Guerrier Zoth ». Rentabilité garde en mémoire « Pelle à Thart' » et ne prend pas la hache en focus via F8.

L'image communiquée par l'utilisateur a été examinée **localement** sans être ajoutée au dépôt. Avec les plages OpenCV actuellement utilisées par `DofusItemTooltipDetectionService` :

- bandeau de la hache : `Rect(1079,26,387,112)`, palette courante ;
- bloc inférieur du corps : `Rect(1074,330,395,660)`, palette courante ;
- écart entre les deux rectangles détectables : `330 - (26 + 112) = 192 px`.

La géométrie précédente exigeait `-5 <= verticalGap <= 15`, donc aucune paire valide, même si les couleurs et alignements correspondaient.

Ces mesures concernent la **capture illustrant le symptôme**, pas nécessairement l'image WGC `dofus.png` prise par BestCrush à l'instant F8. Le correctif doit être confirmé sur F5 et, en cas de doute, sur les artefacts F8 réels.

## Correctif limité

- Profil historique : géométrie inchangée.
- Profil moderne de secours : toujours accepter le corps contigu (`<= 15 px`), et également **un corps long de 300 px minimum**, rigoureusement aligné sur le bandeau, distant de **220 px maximum**.
- Seuils BGR, ratio largeur/hauteur, OCR, détection de plusieurs infobulles, reconnaissance équipement, logique serveur, overlay et réseau : inchangés.

Ce garde-fou évite d'interpréter un petit panneau détaché comme une infobulle longue.

## Tests ajoutés

Trois cas synthétiques OpenCV :
1. Long corps moderne aligné avec écart 190 px → un bandeau.
2. Panneau court éloigné de 190 px → aucun bandeau.
3. Corps long mais éloigné de 280 px → aucun bandeau.

Images de l'utilisateur **non stockées dans Git**.

## Validation Windows

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

La suite complète précédente compte **275/275 tests réussis**, confirmés par l'utilisateur. Le correctif ajoute 3 tests : **278 attendus**.

F5 : choisir le serveur dans BestCrush, ouvrir une infobulle longue « Hache du Guerrier Zoth », déclencher F8 **pendant qu'elle est visible**, contrôler « Mise à jour marché » puis le focus Rentabilité. Tester une infobulle courte et, séparément, le clic molette réseau.

Si l'échec persiste, conserver les artefacts dans Paramètres → Développement et transmettre le `dofus.png` du dernier dossier `%LOCALAPPDATA%\BestCrush\DebugCaptures\Tooltip\f8-tooltip-*` pour vérifier le rendu exact que l'algorithme a reçu. Ne pas déduire du seul screenshot de bureau que la capture WGC contient les mêmes pixels.
