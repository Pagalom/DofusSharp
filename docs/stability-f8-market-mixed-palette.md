# F8 — fond HDV et palettes mixtes (11 octobre 2026)

## Symptôme validé par l'utilisateur

F8 fonctionne avec les objets survolés quand l'hôtel de vente (HDV) est fermé. Avec l'HDV ouvert derrière l'infobulle, la même capture F8 affiche « Infobulle non détectée », tandis que le focus Rentabilité conserve un objet reconnu précédemment.

L'utilisateur a fourni un screenshot 1920 × 1080 de la Hache du Guerrier Zoth avec l'HDV ouvert. Analyse du screenshot **sans l'ajouter au dépôt** :

| Zone | Rectangle OpenCV | Palette qui l'identifie |
| --- | --- | --- |
| Bandeau de titre | (1079, 26, 387, 112) | moderne |
| Corps directement adjacent | (1079, 139, 387, 116) | historique |
| Section inférieure | (1079, 346, 387, 102) | moderne |

Le bandeau et le corps adjacents sont alignés au pixel près, avec un intervalle vertical de **1 pixel**. La sélection du bon corps est donc possible, mais le détecteur exigeait la même palette pour les deux rectangles. Avec l'HDV, les sections modernes du corps sont fragmentées (la zone inférieure de 102 px est trop petite pour le cas « corps long distant »), et la récupération par recherche verticale ne trouve pas l'entête.

**Interprétation :** l'échec sur le screenshot de l'utilisateur est reproduit par la séparation des palettes, pas par un problème OCR, réseau ou SQLite. La capture WGC réelle prise au moment de F8 reste à valider dans l'application.

## Correction limitée

Dans `DofusItemTooltipDetectionService.DetectTooltipHeaders` :
1. La détection historique et la détection moderne restent prioritaires et inchangées.
2. Si elles échouent, tenter le couple **bandeau moderne + corps historique**, sans augmenter les tolérances géométriques : largeur, abscisse, hauteur, intervalle vertical (-5 à +15 px).
3. Conserver la recherche par corps moderne du commit précédent comme **dernier recours**.

Aucun assouplissement général des contours de l'HDV, aucun changement d'OCR, de focus, de protocole, de SQLite ni de version.

## Tests

Deux nouvelles régressions OpenCV synthétiques :
- HDV à gauche, bandeau moderne et corps historique en contact, plus une section moderne distante → un seul bandeau reconnu et bien positionné.
- Corps de palette historique distant de 60 px d'un bandeau moderne → zéro faux positif.

Le dernier total certifié sous Windows était **275/275** avant les précédents correctifs F8. Avec les deux derniers lots (+3, +3) et ce lot (+2), le total **attendu** est **283 tests**, à confirmer sous Windows.

## Checkpoint Windows

```powershell
git pull --ff-only origin refactor/codebase-cleanup
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

F5, serveur sélectionné :
- HDV **fermé**, F8 sur l'objet : nom de focus correct ;
- HDV **ouvert** derrière la même infobulle, F8 : le nom de focus doit suivre, et « Mise à jour marché » ne doit plus signaler d'infobulle absente ;
- tester la Hache du Guerrier Zoth, la Coiffe du Royalmouth, et un objet court.
- Le clic molette (réseau) est un chemin distinct et doit rester opérationnel.

Si un échec persiste, conserver les artefacts en mode Développement et analyser **le `dofus.png` WGC exact de ce F8**, pas seulement le screenshot du bureau, pour ne pas continuer à élargir des seuils à l'aveugle.

**Contexte :** ceci est un correctif d'interface F8. Le nouveau scan passif des prix et du concassage sur le protocole Dofus actuel n'est pas traité ici et ne doit pas être marqué compatible du fait du focus.
