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

## Personnages (exigence rectifiée)

- **Aucune persistance de personnage** : pas de personnage mémorisé, de dernier personnage restauré, de liste enregistrée, de profils dans SQLite ou les préférences. Le serveur global reste mémorisé indépendamment.
- `CurrentCharacterState` est un singleton **uniquement en RAM pendant l'exécution**. Il démarre vide, même si le serveur est restauré. Toutes les fiches disparaissent quand le processus ferme.
- Une fiche est créée **uniquement** pour un identifiant de personnage confirmé par la corrélation de messages réseau indépendants. Les formulaires d'ajout de personnage par simple nom ont été retirés.
- Plusieurs fiches peuvent coexister pendant la même session. Elles sont séparées par `(serveur, CharacterId)`. L'avatar en haut à droite permet de sélectionner manuellement une fiche déjà découverte ; par défaut la sélection suivra la **fenêtre Dofus ciblée et vérifiée** quand la détection réelle sera branchée.
- Champs visés : inventaire du personnage, kamas en poche, inventaire de la banque, kamas en banque, niveau, classe, métiers et niveaux des métiers. Le modèle distingue `non observé` de `vide/0`. Les champs de banque ne sont pas supposés disponibles sans consultation de la banque.
- Déconnexion : désélectionner le personnage et interdire l'attribution de nouvelles observations sur l'ancienne connexion ; les fiches restent seulement jusqu'à la fermeture de l'application.
- Une reconnexion invalide les valeurs de portefeuille, inventaires et métiers potentiellement périmées jusqu'à nouvelle observation.
- Le prototype de corrélation P0 et les structures de données ne sont **pas encore branchés au trafic réel** ; il reste à identifier les vraies trames du protocole. Les portraits actuels sont des silhouettes temporaires en attendant de disposer d'illustrations fiables des classes.
- Compatibilité : les anciennes préférences `Workspace.CharacterName.{serveur}` de M2 sont supprimées pour les serveurs du catalogue lors du chargement du workspace.

## Modifications techniques

- Abstraction `IWorkspacePreferences` dans Domain, adaptateur MAUI basé sur `Preferences.Default`, sans migration SQLite.
- `ServersService` conserve son fonctionnement historique en l'absence d'adaptateur (tests M0), et mémorise/restaure le serveur avec l'adaptateur injecté. Une préférence obsolète est ignorée et supprimée.
- `KnownRoutes.AfterInitialization` ouvre désormais le Concassage modulaire sans serveur.
- `WorkspaceModules.IsRouteCompatible` protège le rendu des anciennes sous-pages serveur.
- `MainLayout` centralise le changement de contexte Serveur/Personnage, la navigation et le garde-fou de route.
- `CharacterProfile.razor` présente uniquement les fiches temporaires découvertes par observation confirmée ; aucune saisie de nom persistante.
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

Le nombre de tests a évolué depuis la première version de M2 (ajout des tests P0 et des fiches temporaires) : contrôler les réussites et les échecs sous Windows, sans présumer du total avant compilation.

F5 :
1. Première ouverture sans serveur mémorisé : 9 onglets, Concassage actif, sélecteur global disponible et captures interdites.
2. Sélection d'un serveur : accès à Concassage ; fermer et relancer l'application : retour direct à Concassage avec le même serveur restauré.
3. Sélectionner un autre serveur depuis Craft : rester sur Craft, serveur global mis à jour. Revenir à Concassage : nouvelle portée de serveur.
4. Ouvrir une ancienne URL `/servers/ancienServeur/history` après changement : redirection vers la sélection active, sans réactivation de l'ancien bail.
5. Au démarrage, vérifier qu'aucun personnage n'est présélectionné, même si le serveur est restauré. Les portraits et informations n'apparaîtront que lorsque l'identification réseau sera fonctionnelle. Testez le changement de serveur, puis le retour : aucun personnage ne doit être réactivé.
6. Vérifier overlays, F7/F8/molette, capture HDV/concassage et prix isolés par serveur.

**Arrêt au checkpoint M2, pas de M3 avant validation explicite de l'utilisateur.**
