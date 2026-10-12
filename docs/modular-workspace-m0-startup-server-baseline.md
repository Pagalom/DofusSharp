# BestCrush — M0 : caractérisation du démarrage et du contexte serveur

**Branche de travail** : `feature/modular-workspace`  
**Base figée** : `main` au commit `997a46c66e0f64bcb4b0e67dcbf7377166542718`.  
**Statut du lot** : ajout de tests de caractérisation et d'un seam de routage sans changement attendu de comportement ; **validation Windows en attente**.  
**Dernière suite Windows de référence sur la base** : **394 tests réussis / 0 échec** et contrôle F5 validé.

## Besoin utilisateur à réaliser dans les lots suivants

- Ouvrir directement l'interface modulaire, avec **Concassage actif** à chaque lancement.
- Afficher **neuf onglets horizontaux** : Concassage, Achat/Vente, Pierres d'âme, Craft, Familiers/XP, Élevage, Farming, Forgemagie, Opportunités.
- Barre **au-dessus des onglets, alignée à droite** : **Serveur, Personnage, Paramètres**.
- Restaurer automatiquement le **dernier serveur utilisé**, conservé entre deux sessions de l'application.
- Au premier lancement sans serveur mémorisé : afficher l'interface modulaire **sans capturer** de trafic avant sélection.
- Le changement de serveur reste autoritaire : **aucune observation, UID, prix ni session** ne doit être attribué au mauvais serveur après changement ; les anciens baux de capture deviennent obsolètes.
- Laisser les pages du Concassage et les overlays F7/F8/molette fonctionner pendant la migration.

## Comportement réel observé en lecture de code (M0)

1. `Splash.razor` fait les migrations/mises à jour puis obtient le serveur courant. Il redirige actuellement vers `/servers/{name}` si présent ou vers `/servers` sinon. Cette **ancienne règle**, appelée maintenant via `KnownRoutes.AfterInitialization`, est encore active : M0 **ne modifie pas la navigation utilisateur**.
2. `ServersService` (singleton) conserve le serveur dans un champ `_currentServer` **non persisté**. `SetCurrentServerAsync` ne sauvegarde pas actuellement de préférence locale. M2 devra fournir un mécanisme durable de préférence/restauration.
3. `MainLayout.razor` affiche déjà un sélecteur de serveur et appelle `CurrentServerState.SelectServer` à l'initialisation ; il redirige vers le picker en absence de serveur. Il faudra conserver le chargement asynchrone et rendre le sélecteur global aux neuf modules.
4. `Server.razor` sélectionne encore le serveur à partir du paramètre URL dans `OnParametersSet`. **Ne pas laisser cette ancienne page rétablir un autre serveur** lors du basculement vers la navigation modulaire.
5. `CurrentServerState` est déjà partagé et protégé contre les accès concurrents. Sa génération de bail invalide les observations prises sur une sélection précédente. Re-sélectionner le même serveur ne modifie pas la génération.
6. `DofusNetworkCaptureService` consomme le bail actif, efface les buffers de flux lors du changement et invalide les anciens messages ; ne pas réintroduire une capture automatique sans contexte utilisateur fiable.
7. `StartupInitializationService` possède déjà une protection contre le double démarrage et des tests de cycle de vie des scopes de base de données.

## Changements inclus dans M0 uniquement

- `BestCrush/Components/KnownRoutes.cs` : petite fonction statique `AfterInitialization(DofocusServer?)`, réutilisant exactement les routes antérieures.
- `BestCrush/Components/Pages/Splash.razor` : appelle cette fonction, sans changer les conditions de navigation.
- `Tests.BestCrush/StartupNavigationCharacterizationTest.cs` : quatre tests couvrant absence/présence du serveur au lancement, URLs existantes et sélection actuelle en mémoire sans persistance.
- `Tests.BestCrush/Network/NetworkServerSelectionTest.cs` : cinq cas supplémentaires : retour A→B→A, re-sélection idempotente et trois choix invalides ne modifiant pas le bail actif.

**Nombre attendu** : **403 tests** (394 + 9). **Non exécutés sur Windows à la rédaction**.

## Séparation des prochains lots

- **M1** : construire la coque d'interface (barre supérieure et neuf onglets) en conservant les composants existants et les captures, sans imposer un écran de serveur.
- **M2** : implémenter le choix mémorisé du serveur, l'absence de serveur au premier lancement, le profil personnage global et l'entrée directe dans Concassage ; remplacer explicitement les tests de caractérisation des anciennes routes par des tests correspondant à la nouvelle règle validée.
- **M3** : stabiliser les sous-pages du Concassage dans la nouvelle navigation ; protéger toute modification des URLs, overlays et événements réseau par des tests et des scénarios F5.
- **M4+** : services économiques communs, modules métier, module transversal Opportunités.

> M0 ne valide **pas encore** la persistance du serveur, la navigation en onglets ou la sélection du personnage. Ces fonctionnalités sont **à construire**, pas à supposer présentes.

## Checkpoint Windows M0

Préserver les modifications locales/stashes, ne pas utiliser `git clean`, `reset` ni `stash` :

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git switch -c feature/modular-workspace --track origin/feature/modular-workspace
# Si la branche locale existe déjà : git switch feature/modular-workspace
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

**Attendu** : build Windows réussi, **403/403 tests**, aucun nouveau warning. M0 n'affectant pas intentionnellement le comportement en jeu, F5 n'est pas nécessaire pour ce lot de caractérisation seul. Conserver la branche `main` inchangée, aucune release ni changement de version.
