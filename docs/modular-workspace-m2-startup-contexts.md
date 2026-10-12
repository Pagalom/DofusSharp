# BestCrush — M2 : démarrage modulaire et contextes globaux

Branche : `feature/modular-workspace`.
Base M1 validée sur Windows : 433/433 tests ; rendu F5 validé par l'utilisateur.
M2 : code préparé, **checkpoint Windows et F5 requis avant M3**.

## Fonctionnement

- À chaque lancement, après les migrations habituelles, le serveur précédemment sélectionné est restauré depuis les préférences locales s'il existe toujours dans le catalogue Dofocus.
- Sans préférence valide : aucun serveur sélectionné, ouverture directe de `/modules/concassage` (onglets toujours accessibles). Les captures sont désactivées et l'utilisateur est invité à sélectionner un serveur via la barre globale.
- Avec serveur valide : ouverture directe de `/servers/{ServerName}`, donc Concassage sélectionné. Aucun écran de choix de serveur préalable.
- Sélecteur Serveur visible en permanence, en haut à droite. Le changement de serveur persiste le nouveau nom et met à jour l'état global.
- Sélection pendant Concassage : navigation vers `/servers/{nouveauServeur}`; depuis un autre module, l'URL et l'onglet restent identiques, mais les contextes globaux changent.
- Avant chaque changement effectif de serveur, `CurrentServerState.Clear()` invalide les baux de capture en cours. Le bail du nouveau serveur est créé seulement après réussite de la persistance. Resélection du même serveur : génération inchangée.
- Les anciennes routes `/servers/{nom}/...` sont bloquées dans le layout et redirigées si le serveur de l'URL diffère du serveur global actif. La page métier n'est pas instanciée pendant ce contrôle : une URL historique ne peut donc pas sélectionner implicitement un serveur périmé ni attribuer silencieusement des données à celui-ci.
- Les onglets sont toujours présents, même sans serveur. Concassage sans serveur présente une explication plutôt qu'un faux moteur métier.

## Personnage

- Un nom de personnage facultatif, configurable dans `/profile`.
- Il est conservé **par serveur** dans les préférences locales pour éviter de mélanger les identités entre serveurs.
- L'état `CurrentCharacterState` est singleton et partagé par tous les modules ; l'en-tête affiche le personnage actuel ou le libellé `Personnage`.
- Les métiers/niveaux ne sont **pas inventés** dans M2 ; leur modèle sera défini lors de l'intégration des modules métier.

## Modifications techniques

- Abstraction `IWorkspacePreferences` dans Domain, adaptateur MAUI basé sur `Preferences.Default`, sans migration SQLite.
- `ServersService` conserve son fonctionnement historique en l'absence d'adaptateur (tests M0), et mémorise/restaure le serveur avec l'adaptateur injecté. Une préférence obsolète est ignorée et supprimée.
- `KnownRoutes.AfterInitialization` ouvre désormais le Concassage modulaire sans serveur.
- `WorkspaceModules.IsRouteCompatible` protège le rendu des anciennes sous-pages serveur.
- `MainLayout` centralise le changement de contexte Serveur/Personnage, la navigation et le garde-fou de route.
- `CharacterProfile.razor` permet la saisie réelle du nom de personnage.
- Test du démarrage M0 adapté explicitement au comportement voulu M2 ; nouveaux tests de persistance, anciens baux et navigation.

Aucun changement des moteurs OCR/Npcap/économie, aucun changement de version, aucune release, aucune fusion vers `main`.

## Validation Windows demandée

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Résultat théorique après ajout de 19 cas de tests : **452 tests** (433 M1 + 19 M2).

F5 :
1. Première ouverture sans serveur mémorisé : 9 onglets, Concassage actif, sélecteur global disponible et captures interdites.
2. Sélection d'un serveur : accès à Concassage ; fermer et relancer l'application : retour direct à Concassage avec le même serveur restauré.
3. Sélectionner un autre serveur depuis Craft : rester sur Craft, serveur global mis à jour. Revenir à Concassage : nouvelle portée de serveur.
4. Ouvrir une ancienne URL `/servers/ancienServeur/history` après changement : redirection vers la sélection active, sans réactivation de l'ancien bail.
5. Modifier le nom du personnage sur deux serveurs différents, relancer : vérifier la conservation distincte et la mise à jour de l'en-tête.
6. Vérifier overlays, F7/F8/molette, capture HDV/concassage et prix isolés par serveur.

**Arrêt au checkpoint M2, pas de M3 avant validation explicite de l'utilisateur.**
