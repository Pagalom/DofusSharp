# S2 — Attribution prudente de la capture réseau au serveur

Base : `18a1c08` ; S1 validé (227 tests, disparition NU1903 et intégrité de la copie SQLite : ok, 15 tables).

## Limite fondamentale

Le protocole actuellement décodé (`jzn`, `kci`, `kei`, etc.) ne fournit **aucune identité vérifiée du serveur Dofus**. Le serveur sélectionné dans BestCrush n'établit pas celui de la connexion observée.

La correction utilise donc une **confirmation manuelle** pour un serveur et **une seule connexion TCP Dofus par confirmation**. Ce n'est pas une détection automatique : une confirmation erronée reste possible.

## Mesures

- Aucune observation réseau enregistrée avant confirmation ; navigation et sélection n'autorisent pas la capture.
- Bannière de statut et boutons de confirmation/suspension visibles dans le layout.
- Chaque confirmation délivre un bail (serveur + génération) attaché aux messages à la réception. Tout changement de serveur, suspension ou nouvelle confirmation invalide les messages de l'ancienne génération, y compris ceux encore en queue.
- Une seule connexion TCP bidirectionnelle sur le port 5555 et la même interface réseau est acceptée. Une seconde connexion distincte observée suspend la capture (fail-closed).
- Le réassemblage TCP et le dernier équipement sont vidés au changement d'autorisation ; associations UID et corrélations d'achat sont purgées avant le premier message valide d'une nouvelle génération.
- Avant persistance des prix/coefficients, publication de notifications ou résultat de concassage, la génération est revérifiée. Le résultat utilise le nom figé par le bail, non le serveur courant de l'UI.
- Une opération de base **déjà démarrée** juste avant une révocation peut se terminer sous son ancien nom de serveur ; elle ne sera jamais relabelisée avec le nouveau.

## Limites

- La confirmation manuelle ne prouve pas le serveur réel. L'utilisateur doit garder un seul client Dofus connecté et vérifier la concordance.
- Les connexions TCP silencieuses ou sans payload ne sont pas détectées comme ambiguës.
- La logique Stop/Start Npcap préexistante n'est pas corrigée ici.
- Aucune migration, aucun changement de formule ni de version produit.

## Checkpoint Windows

```powershell
git switch refactor/codebase-cleanup
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Contrôle manuel sur une base **de test**, jamais provoquer de fausses associations sur la base utilisateur : bannière initialement suspendue ; capture après confirmation A ; changement vers B suspend immédiatement ; nouvelle confirmation requise ; historique A inchangé ; F7/F8 et résultat concassage inchangés ; deuxième connexion => suspension, même après arrivée de messages en attente.
