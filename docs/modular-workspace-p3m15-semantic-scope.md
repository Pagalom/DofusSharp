# P3m.15 — Protection des caches métier par connexion TCP

## Point de départ vérifié dans le code

Le réseau de production `DofusNetworkCaptureService` donne à chaque socket TCP logique un `ConnectionId` opaque, dans le périmètre d'un bail serveur manuel (`NetworkCaptureLease`) et d'une époque de capture. Les messages transitent par un worker ordonné, puis par `DofusNetworkMessageProcessor`.

**Déjà en place avant P3m.15 :** les corrélations `ItemUid → ItemDetail` et `kei → kef/kbd` sont distinctes par `ConnectionId`. Les tests antérieurs couvraient deux comptes ayant le même UID ou le même identifiant d'offre, ainsi que le reset serveur/capture. Il ne fallait pas réimplémenter ces caches.

## Changement de défense en profondeur

Le processeur conserve désormais, **en RAM uniquement**, la première fenêtre Windows vérifiée pour un `ConnectionId`, séparément du cache métier (`_correlations`). La même connexion ne peut jamais être réattribuée à une autre fenêtre vérifiée, même lorsque ses données métier ont été évincées ou ont expiré.

- Une association contradictoire met **ce jeton de connexion** en quarantaine jusqu'au changement de bail serveur / d'époque de capture ; ses données métier en cache sont abandonnées.
- Une absence de métadonnées de fenêtre ne constitue **pas** une contradiction : le serveur choisi manuellement reste autoritaire, conformément au comportement antérieur.
- Les associations sont bornées à 8192 jetons vérifiés ; au-delà, le traitement métier échoue fermé pour le reste de cette époque plutôt que d'oublier silencieusement une association ancienne.
- Aucune attribution de compte ou de personnage n'est produite par le seul HWND/PID. Aucun PID, titre, tuple ou identifiant privé n'est écrit dans la documentation ou les traces.
- Les contrôles existants dans `LiveDofusClientNetworkBindings` continuent de réévaluer une association candidate lors du dépilage des messages réseau réels.
- Le mode ETW P3m.14 demeure **expérimental et opt-in**. Il n'est pas intégré au routage métier de BestCrush.

Une extension **strictement interne aux tests** du replay permet d'injecter un contexte de fenêtre synthétique prévalidé. Les messages Npcap réels gardent la réévaluation de fenêtre existante.

## Tests de régression ajoutés (6 cas)

Dans `Tests.BestCrush/Network/DofusNetworkRoutingTest.cs` :

1. Le changement contradictoire de fenêtre sur une connexion bloque le rapprochement `kei/kef`, y compris lors du retour vers la fenêtre initiale.
2. Une contradiction de fenêtre bloque la résolution d'un UID préalablement connu par `kch/kag`.
3. Une quarantaine ne disparaît pas après éviction des corrélations métier.
4. La première fenêtre vérifiée reste mémorisée même après éviction du cache métier ; une contradiction ultérieure est refusée.
5. Un nouveau cycle de capture réinitialise la quarantaine ancienne.
6. Une reconnexion dans **la même fenêtre**, avec un **nouveau `ConnectionId`**, doit reconstruire ses UID et achats : aucune reprise du cache de la connexion précédente.

**À valider sur le Windows de l'utilisateur :** les modifications ont été publiées sur `feature/modular-workspace`, mais aucune compilation .NET ni nouvelle exécution de tests Windows n'a été communiquée depuis P3m.15.

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

## Frontière non encore sécurisée / suite

La base de données des marchés, les coefficients, l'état `LastNetworkEquipmentState` et les overlays fonctionnent encore selon le **serveur choisi manuellement**, pas selon une identité de compte/ personnage réseau vérifiée. Ce comportement n'a **pas** été modifié dans P3m.15.

La prochaine étape doit caractériser, sans activer l'écriture multicomptes, le routage de `NetworkObservationWriter` et `LastNetworkEquipmentState`, puis définir comment les observations propres à un personnage seront bloquées ou isolées lorsque plusieurs clients sont ouverts. Une fenêtre OS seule n'identifie pas de manière certaine le personnage.
