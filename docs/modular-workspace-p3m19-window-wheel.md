# P3m.19 — Molette réseau par fenêtre Dofus

## Départ

P3m.18 : **949 tests réussis / aucun échec / aucun ignoré** sous Windows, confirmé par l'utilisateur.

P3m.19 : modifications de production et **10 nouveaux tests** publiés. La compilation Windows et les tests ne sont **pas encore validés** pour cette étape.

## Règles produit conservées

- Le serveur choisi manuellement est le contexte de capture et le seul serveur de référence pour les prix et coefficients. **Aucune validation forcée** du serveur réseau.
- **F8** reste réservé à la lecture de l'infobulle OCR ; **F7** conserve son comportement.
- La molette ne fait **aucune** reconnaissance visuelle : elle utilise exclusivement le dernier équipement reconnu du flux réseau.
- Les fenêtres, identités de personnages et équipements associés à chaque client restent **en RAM**, jamais sauvegardés ni restaurés au redémarrage de BestCrush.
- L'historique des prix et les coefficients en SQLite restent des données **économiques durables**, pas des fiches personnages.
- L'ETW expérimental P3m.14 n'est pas branché dans la capture de production.

## Résolution stricte de la molette

Lors d'un clic molette, `OverlayService` :

1. Rejette la souris sur un overlay BestCrush.
2. Demande à `DofusWindowService.GetDofusWindowAtScreenPoint` le **top-level HWND réellement sous le pointeur**, sans déduction du focus ni de l'ordre des fenêtres.
3. Exige la paire `DofusClientWindowKey(HWND, PID)` et un bail serveur manuel `NetworkCaptureLease` actif.
4. Interroge **exclusivement** `WindowScopedNetworkEquipmentState.GetFocusToken`. Ce token contient la fenêtre, le bail manuel, l'époque Npcap, l'équipement et une version d'observation **propre à la fenêtre**.
5. Relit après le chargement asynchrone de l'équipement : version du clic, version de la mise au focus (pour ne pas supplanter une action F8 ou UI plus récente), serveur toujours sélectionné, token de capture toujours actuel, fenêtre HWND+PID toujours visible et unique.
6. Met au focus l'équipement seulement si tous les contrôles sont satisfaits.

**Aucun repli sur `LastNetworkEquipmentState` global.** Une fenêtre sans équipement identifié et lié avec certitude ne déclenche **aucun changement de focus**. Cette règle s'applique aussi à un seul client : un résultat global sans attribution OS certaine ne prouve pas quel compte a produit cet équipement. C'est une restriction délibérée pour éviter les contaminations multicomptes. Le global reste alimenté pour compatibilité interne, mais la molette ne le consulte plus.

Une nouvelle pression molette annule le résultat d'une demande antérieure (même si la seconde fenêtre n'a aucun équipement). Un F8 ou changement de focus manuel intervenant pendant le chargement annule l'ancien clic molette.

## Durée de vie et isolation

- Une autre fenêtre peut observer de nouveaux équipements sans invalider le token du premier client.
- Une nouvelle observation **sur la même fenêtre** change sa version, même si les valeurs d'équipement et d'horodatage sont identiques.
- Changement de serveur, redémarrage/arrêt de capture, fermeture de fenêtre, nouveau token d'époque, nouvelle génération, ou suppression de fenêtre invalident les lectures en attente.
- Une fenêtre recréée avec le même HWND et PID ne peut pas réactiver un ancien token dès lors que sa fermeture a été observée.
- Le relevé UI des fenêtres continue à supprimer les données RAM des clients fermés, sans attendre un nouveau message réseau.

## Tests ajoutés (10)

Dans `WindowScopedNetworkEquipmentStateTest` :
- isolation A/B et indépendance des tokens ;
- absence de repli global quand le client ciblé n'a pas de donnée réseau attribuée ;
- mauvais nom de serveur/génération du bail ;
- nouvelle observation sur A invalide A sans toucher au clic B ;
- fermeture de fenêtre ;
- nouvelle époque Npcap avec même équipement et horodatage ;
- vidage RAM / nouvelle instance de BestCrush ;
- réutilisation de handle avec PID différent ou identité invalide ;
- nouvelle observation identique dans la même époque ;
- fermeture puis recréation du même couple HWND+PID.

Les tests Win32 synthétiques du hit-test exact, précédemment ajoutés en P3m.18, restent présents.

## Validation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**959 tests attendus**, si aucun test préexistant n'est supprimé et si la découverte reste identique. Ne pas déclarer la validation acquise avant retour utilisateur.

## Après tests unitaires : vérification manuelle

1. Démarrer BestCrush et deux fenêtres Dofus sur deux comptes, en choisissant manuellement le serveur de capture voulu.
2. Sur A, consulter un équipement identifiable puis cliquer molette sur A : vérifier l'équipement affiché.
3. Sur B, consulter un **autre** équipement et cliquer molette sur B ; revenir sur A, cliquer molette et vérifier que A retrouve **son** équipement.
4. Cliquer molette sur une fenêtre dont le flux n'est pas relié / qui n'a pas encore fourni d'équipement attribué : l'ancien équipement affiché ne doit **pas** changer.
5. Fermer B sans fermer BestCrush : son état RAM doit être supprimé, et A doit continuer à fonctionner.
6. Vérifier séparément la lecture d'infobulle F8 et l'affichage F7.

Ce test manuel ne vaut pas validation de l'identité personnage ni des métiers, qui restent soumis à des preuves de protocole indépendantes.
