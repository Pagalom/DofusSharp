# S4a — Cycle Stop/Start de la capture Npcap

Point de départ : `b9e9736` (S3 validé Windows, 238/238 tests, vérification manuelle satisfaisante).

## Défaut préexistant

`DofusNetworkCaptureService.Stop()` annulait le `CancellationTokenSource` et complétait le `Channel<DofusWireMessage>` à chaque arrêt. `Start()` conservait pourtant les mêmes instances et un `_worker` non recréable (`??=`). En conséquence un redémarrage de la capture dans le même processus ne relançait pas la consommation des messages.

## Correction limitée

- Le worker, son `CancellationTokenSource` et son canal vivent jusqu'à `Dispose()`, qui seul les termine définitivement.
- `Stop()` désactive la capture, ferme les interfaces, vide les assembleurs TCP et **invalide** la génération des messages de l'exécution précédente. `Start()` réactive les interfaces et incrémente la génération.
- Une reprise ne réutilise pas les anciennes associations UID ni la corrélation d'achats `kei/kef/kbd` : le processeur métier les purge lors du premier message de la nouvelle génération.
- Les messages encore en queue après Stop sont ignorés si leur génération ne correspond pas à la nouvelle ; les replays tentés pendant l'arrêt échouent explicitement sans attendre indéfiniment.
- Les routes, les identifiants de serveur utilisateur, les calculs de prix, les coefficients et les overlays restent inchangés.
- Aucun écrasement ou migration de base SQLite.

## Tests

Trois tests de routage métier sans Npcap :
1. Stop interdit le replay ; reprise autorise les nouveaux messages et conserve les données déjà persistées.
2. Message provenant de la session précédente : rejet après reprise.
3. La reprise ne réutilise pas l'ancien cache UID ni l'ancienne demande d'achat.

Attendu : **241 tests réussis**, contre 238 à la clôture S3.

## Limites

- Les tests simulent le cycle sans ouvrir de carte Npcap : un contrôle fonctionnel sur Windows reste indispensable.
- Une opération de persistance déjà engagée juste avant Stop peut se terminer ; ce correctif empêche surtout le traitement de messages restés en queue.
- L'absence de bornes sur la taille des caches, la file de messages, la répétition des paquets sur plusieurs interfaces et la gestion des métadonnées/debug sont hors S4a.
- Ne pas modifier `main`, la version ou le schéma SQLite.

## Checkpoint Windows

```powershell
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Puis F5 et tests usuels de capture marché, concassage et F7/F8. Le Stop/Start intra-processus via une fenêtre est à vérifier si l'application le permet. Aucun succès Windows présumé avant retour utilisateur.
