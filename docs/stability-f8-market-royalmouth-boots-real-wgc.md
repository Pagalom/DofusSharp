# F8 : Bottes du Royalmouth devant l'HDV — cas WGC caractérisé

**Date :** 11 octobre 2026. **Branche de travail :** `refactor/codebase-cleanup`.

## Symptôme et observation

L'utilisateur confirme que F8 reconnaît tous les équipements lorsque l'HDV est fermé, mais ne reconnaît que certains objets (Gelano et Hache du Guerrier Zoth) lorsque l'HDV est ouvert.

La capture F8 réellement produite par BestCrush, `dofus(20261011-081246).png`, montre les **Bottes du Royalmouth** au-dessus de l'HDV. Image de session privée, **non ajoutée au dépôt**.

Sur les pixels réels en BGR avec les plages et `FindRectangles` actuels :
- bandeau de titre : `Rect(1079, 79, 387, 112)`, palette `CurrentTooltipBody`, taux de remplissage ~0,621, accepté comme bandeau (`>=0,25`) ;
- corps principal subsistant : `Rect(1076, 283, 390, 501)`, même palette, taux de remplissage ~0,988, accepté comme corps (`>=0,75`) ;
- distance entre bandeau et corps : **92 px** ;
- en parallèle, la plage `CurrentTooltipHeader` englobe le bandeau et une partie de l'HDV, produisant un rectangle fusionné `Rect(526, 79, 940, 112)`, rejeté car la largeur excède 600 px.

Les détecteurs historiques, modernes, mixtes et de récupération verticale ne renvoient aucun bandeau pour ces pixels. Le dernier recours introduit pour Gelano accepte la même palette pour bandeau et corps, **mais uniquement jusqu'à 35 px d'écart**.

## Correction limitée

Dans le **dernier recours seulement** (`CurrentTooltipBody` + `CurrentTooltipBody`), autoriser également le corps éloigné selon les conditions **déjà utilisées** pour les longs corps de l'interface moderne :
- écart vertical jusqu'à **220 px**, seulement lorsque le corps est haut d'au moins **300 px** ;
- écarts horizontaux du bandeau/corps inchangés : X `<=15 px`, largeur `<=20 px` ;
- écart standard `<=35 px` maintenu pour Gelano et les corps courts ;
- les détecteurs historiques, modernes, mixtes et la récupération par corps demeurent prioritaires et inchangés.

Ce réglage ne touche pas à l'OCR, au matching d'objets, à F8, aux overlays, au protocole réseau ni à SQLite.

## Tests

Trois nouveaux tests OpenCV **synthétiques uniquement** :
1. Bandeau/corps à la géométrie observée sur la capture Bottes/HDV, avec le fond HDV fusionnant le contour `CurrentTooltipHeader` sur une largeur >600 px. Tous les anciens parcours échouent, le dernier recours doit produire le **bon** bandeau de titre.
2. Corps éloigné **de 92 px**, mais seulement haut de **250 px** : rejet, le corps ne justifie pas l'écart supplémentaire.
3. Corps haut de **305 px**, mais éloigné de **221 px** : rejet au-delà de la limite de 220 px.

Test hors dépôt des critères OpenCV sur les captures de la conversation : les Bottes sont retrouvées dans le PNG WGC réel, Gelano et Hache restent reconnus, pas de candidat sur une image HDV sans infobulle.

## Checkpoint Windows requis

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Dernier total attendu non encore confirmé sur Windows : 286. Ajout de 3 tests → **289 attendus**, pas encore certifiés.

Dans F5, tester **Bottes du Royalmouth avec HDV ouvert**, Gelano et Hache avec HDV ouvert, puis ces objets sans HDV. Vérifier que la **bonne infobulle** est sélectionnée et que le focus Rentabilité suit l'équipement survolé. Si certains items échouent, analyser le `dofus.png` WGC de chaque cas, pas seulement le screenshot du bureau.

Garder `main`, les releases, la version et le stash local intacts.
