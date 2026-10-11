# F8 — Gelano devant HDV : diagnostic sur capture WGC réelle (11 octobre 2026)

## Symptômes et preuve

L'utilisateur confirme que F8 peut reconnaître certains équipements (Hache du Guerrier Zoth) mais échoue sur des infobulles plus courtes, notamment **Gelano**, même sans l'HDV ; il échoue aussi avec HDV en arrière-plan. Les précédentes modifications ciblant les palettes ou les grands corps n'ont pas suffi.

Fichier fourni **exactement depuis la capture Windows F8** : `dofus(20261011-080242).png`, résolution **1920×1080**. L'image de session **reste hors dépôt**.

Analyse OpenCV locale avec les quatre plages BGR de `DofusItemTooltipDetectionService` (contours `RETR_EXTERNAL`, contraintes dimension/aire du code), sur la capture :
- bandeau réel Gelano : `Rect(1079,79,387,112)`, contour `CurrentTooltipBody`, remplissage ~0,637 ; contient « Gelano » et sert de titre OCR ;
- corps : `Rect(1079,220,387,213)`, contour `CurrentTooltipBody`, remplissage ~0,993 ;
- écart réel **29 px**, alors que le couplage de proximité exigeait **15 px** ;
- la palette d'en-tête `CurrentTooltipHeader` ne donne pas de paire valide sur cette capture ; le parcours de récupération verticale abandonne avant le bandeau lorsque la zone séparatrice dépasse 12 px.

En simulant le couplage **corps moderne / corps moderne** avec un écart de 35 px et les garde-fous existants (abscisse ±15, largeur ±20, en-tête largeur/hauteur ≥1,8), un seul candidat ressort sur cette capture : précisément le bandeau `(1079,79,387,112)`. Le même essai sur le screenshot HDV sans infobulle ne donne aucun candidat ; sur le screenshot Gelano sans HDV, il retrouve le titre en `(1354,74,387,112)`.

## Correction

Après les détecteurs historiques, moderne, palettes mixtes et récupération par corps (tous **inchangés**), ajouter un **dernier recours** qui :
1. cherche un bandeau et un corps tous les deux dans la palette `CurrentTooltipBody` ;
2. autorise un écart vertical de **35 px** maximum (au lieu de 15), en conservant l'alignement horizontal strict et l'écart négatif de -5 px ;
3. exige un corps de **100 px de haut au minimum**, pour éviter de réinterpréter les petites barres UI ;
4. retourne uniquement le rectangle du bandeau ; OCR, matching DofusDB, focus et logique de mise à jour restent inchangés.

Cette détection est appelée uniquement lorsque les approches précédentes n'ont trouvé **aucun** candidat. Elle ne modifie pas la capture réseau passive, `protocol-map.json`, SQLite, les overlays ni la version.

## Régression ajoutée (synthétique uniquement)

1. Géométrie réelle Gelano 1920×1080 devant un panneau HDV, bandes BGR modernes séparées de 29 px → un bandeau `Rect(1079,79,387,112)`.
2. Même mécanisme sur un petit Gelano sans HDV, séparation 29 px → bandeau attendu.
3. Deux panneaux sombres alignés mais éloignés de 45 px → aucune infobulle.

Les **283 tests** étaient précédemment attendus mais pas certifiés sous Windows ; ce lot ajoute **3 tests**. Total attendu : **286**, à confirmer en exécution Windows.

## Checkpoint utilisateur

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Puis F5, serveur sélectionné :
- Gelano sous inventaire avec HDV **ouvert**, F8 : doit apparaître en focus dans Rentabilité ;
- Gelano avec HDV **fermé**, F8 : même résultat ;
- Hache du Guerrier Zoth et Coiffe du Royalmouth : aucune régression ;
- comparer bien le focus issu de F8, **distinct** du clic molette réseau.

Si F8 échoue à nouveau, inspecter le `dofus.png` WGC pris exactement sur la tentative, ainsi que les éventuels `tooltip-title-ocr.txt`. Les tests de géométrie et l'analyse locale ne prouvent pas encore que l'application complète fonctionne sous Windows.

Ne pas restaurer le stash NetworkProbe tant qu'il pourrait créer des conflits. Aucun changement de `main`, merge, publication ou version.
