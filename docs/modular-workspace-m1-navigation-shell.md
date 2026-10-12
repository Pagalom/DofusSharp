# BestCrush — M1 : coque des neuf modules (navigation)

**Branche** : `feature/modular-workspace`, base **M0** `0332fb3`.  
**Référence Windows M0 fournie par l'utilisateur** : build BestCrush réussi, **403/403 tests**.  
**M1 : code ajouté, checkpoint Windows et F5 à recevoir avant M2**.

## Périmètre fonctionnel

- En-tête général avec, **à droite et au-dessus des onglets**, sélecteur de **Serveur**, accès **Personnage** et **Paramètres**.
- **Neuf onglets** dans l'ordre validé : Concassage, Achat / Vente, Pierres d'âme, Craft, Familiers / XP, Élevage, Farming, Forgemagie, Opportunités.
- Onglets côte à côte, style navigateur ; défilement horizontal si fenêtre trop étroite, sans masquer silencieusement des modules.
- Le contenu Concassage existant reste sur `/servers/{ServerName}`, avec ses écrans historiques (`equipment-prices`, `rune-prices`, `resource-prices`, `coefficients`, `history`), services et overlays intacts.
- Les **huit autres onglets** ouvrent chacun une page de préparation clairement étiquetée sous `/modules/{slug}`. Ils ne réalisent ni faux calcul ni collecte.
- Le **profil personnage** utilise temporairement la page `/profile`, indiquant que la gestion des métiers et personnages est prévue en M2.
- L'engrenage global dirige vers **la vraie fenêtre de paramètres déjà présente dans Concassage**, en ajoutant `?settings=1` à l'URL legacy ; la fermeture efface ce paramètre.
- La navigation dans les huit nouvelles pages **ne doit pas changer le bail du serveur**. Elle n'appelle pas `CurrentServerState.SelectServer`.

## Contraintes maintenues explicitement en M1

**Ne pas anticiper M2** : le Splash ouvre toujours l'ancien sélecteur en l'absence de serveur en mémoire. La sélection du serveur n'est pas encore persistée entre deux lancements. Le clic sur le sélecteur conserve provisoirement sa navigation initiale vers Concassage après le changement. M2 mettra en place la mémorisation et l'ouverture directe de l'espace de travail avec Concassage actif.

Le rôle du serveur dans l'adresse de l'ancienne page ne change pas, pour éviter des changements intempestifs dans les captures. Les **nouvelles** URL de modules sont volontairement indépendantes du serveur, le contexte étant global.

Aucun changement de version, migration SQLite, nouveau package, moteur réseau/OCR, logique de prix, historisation ou publication.

## Fichiers concernés

- `BestCrush/Components/Layout/MainLayout.razor` et `.razor.css` : nouvelle barre, navigation et mise à jour de l'onglet actif lors des changements de route.
- `BestCrush/Components/WorkspaceModules.cs` : registre central de neuf modules, liens et détermination de l'onglet actif ; routes historiques sous Concassage.
- `BestCrush/Components/KnownRoutes.cs` : routes de modules et du profil ; accès à la fenêtre de paramètres existante.
- `BestCrush/Components/Pages/ModuleWorkspace.razor` : page d'attente des nouveaux modules.
- `BestCrush/Components/Pages/CharacterProfile.razor` : page d'attente du profil.
- `BestCrush/Components/Pages/Server.razor` : accepte la commande `settings=1` et restaure l'URL habituelle à la fermeture, sans déplacer le dialogue existant.
- `Tests.BestCrush/ModularWorkspaceNavigationTest.cs` : ordre, URL, appartenance de routes et séparation des modules.

## Tests ajoutés

**30 nouveaux cas automatisés** couvrent le registre et les routes ; attendu sous Windows : **433/433 tests**, 0 échec (**403 M0 + 30 M1**). Le rendu Blazor de la barre et le clic sur l'engrenage exigent en outre une **vérification F5**.

### Checkpoint Windows

Conserver les modifications non suivies locales `DofusSharp.sln`, `.vscode/`, `BestCrush/BestCrush.csproj.backup`, `q`. Après `git status`, faire uniquement :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Puis lancer BestCrush via F5 et confirmer :

1. Le serveur apparaît **en haut à droite**, suivi de l'icône de personnage et de l'engrenage ; la barre des **neuf onglets** est visible.
2. **Concassage** est actif au chargement, ses sous-pages et leur retour restent accessibles.
3. Les **huit modules de préparation** s'ouvrent et reviennent vers Concassage, sans changer le serveur affiché.
4. Le **profil** s'ouvre avec une page d'attente honnête ; l'**engrenage ouvre bien le vrai panneau de paramètres**, qui se ferme et ne se rouvre pas involontairement.
5. Changement de serveur : retour à Concassage, contexte et prix isolés ; F7/F8/molette et capture HDV/concassage sans régression.
6. M1 ne promet pas encore l'ouverture directe sans serveur mémorisé : l'ancienne sélection initiale reste en place jusqu'à M2.

**Ne pas passer à M2 avant validation.** Aucune modification sur `main`, aucun merge, aucune release ni changement de version.
