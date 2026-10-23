# P3m.16 — Audit des écritures métier et état équipement par fenêtre

## Validation du checkpoint précédent

L'utilisateur a validé **924 tests réussis / 0 échec / 0 ignoré** après P3m.15. Aucun autre test Windows n'a encore été communiqué pour P3m.16.

## Audit du code de production existant

### Données durables par serveur choisi manuellement

`NetworkObservationWriter.PersistMarketAsync` :
- contrôle un `NetworkCaptureLease` actif ;
- classe l'objet à partir des tables du catalogue (équipement, rune, ressource) ;
- persiste les prix et notifie par `serverName = lease.ServerName`, après contrôles du catalogue et des options de capture ;
- ne dispose pas encore d'une clé de personnage ou de fenêtre dans sa signature.

`NetworkObservationWriter.PersistCrushAsync` :
- n'associe les résultats aux équipements que via les `ItemUid` issus du cache **de la même connexion** (`DofusNetworkMessageProcessor`) ;
- persiste les coefficients par serveur et émet les notifications existantes ;
- passe les lignes de concassage au service de session, sans contexte de fenêtre dans cette interface.

Les données de marché et coefficients **peuvent rester durables**, contrairement aux profils personnages qui doivent rester en RAM. Une fenêtre ou un nom de personnage n'est jamais une preuve de serveur, compte ou personnage.

### Limite actuelle : dernier équipement réseau

`LastNetworkEquipmentState` n'a qu'un seul `LastNetworkEquipmentSnapshot` global. `OverlayService.FocusLastNetworkEquipmentAsync` appelle `GetForServer(currentServerState.ServerName)` pour l'action molette.

En présence de deux fenêtres Dofus, l'observation d'un équipement sur B peut donc remplacer la dernière observation de A, **y compris si les deux fenêtres utilisent le même serveur sélectionné manuellement**. Le serveur seul ne fournit pas l'isolement par fenêtre nécessaire pour cibler le bon client.

La sélection manuelle du serveur **reste autoritaire**. L'objectif n'est pas de valider automatiquement un serveur par analyse réseau.

## P3m.16 — Préparation isolée, sans activation métier

Ajout de `WindowScopedNetworkEquipmentState` :
- un dictionnaire **en RAM seulement**, indexé par `DofusClientWindowKey` (HWND + PID) ;
- les observations sont également bornées à `NetworkCaptureLease` (nom et génération du serveur choisi) et `captureEpoch` ;
- aucune récupération de l'équipement d'une autre fenêtre en repli ;
- rejet des messages d'ancienne époque ou ancienne génération ;
- retrait du contexte lorsqu'une fenêtre n'est plus présente, via `Reconcile` ;
- capacité bornée à 16 clients ; aucun stockage SQLite, fichier ou synchronisation externe ;
- **aucun câblage** dans `NetworkObservationWriter`, `OverlayService` ou le flux Npcap tant que le propriétaire de la connexion n'est pas suffisamment confirmé.

Les 7 tests synthétiques ajoutés couvrent indépendance entre deux fenêtres, fermeture, changement de bail serveur/capture, retard d'un ancien message, validation des identifiants, `Clear()` et limite de capacité.

**Attention :** `Remember` exige que son futur appelant vérifie le bail et le propriétaire de la connexion. Ce type de staging n'est **pas** un moyen de déduire une identité de compte/personnage à partir d'une fenêtre Windows.

## Profils temporaires : statut de l'audit

`CurrentCharacterState` et `CurrentAccountProfessionsState` sont des structures en RAM. Aucun système de restauration de fiches de personnages à partir d'un stockage durable n'a été identifié dans ces classes.

Néanmoins, `CurrentCharacterState.InvalidateConnection` **conserve explicitement une carte temporaire** lors d'une déconnexion ; seul son contexte de connexion est révoqué. La règle produit « supprimer la fiche lorsque la fenêtre Dofus ferme » nécessitera donc une validation et une intégration dédiées, distinctes du seul arrêt de connexion TCP. La fermeture de l'application efface ces structures mémoire.

**Précaution distincte** : `NetworkDebugWriter` peut écrire en option `wire.jsonl` contenant les corps décodés Base64 quand `DevTool_KeepDebugArtifacts` est activé. Ce ne sont pas des fiches personnages persistées, mais ces captures peuvent contenir des données privées. Ce comportement de diagnostic préexistant n'est pas modifié par P3m.16.

## Prochaine intégration envisagée (NON réalisée)

1. Établir `ConnectionId → fenêtre Dofus` avec preuve robuste pendant la capture réelle, et révoquer le lien dès la fermeture de la fenêtre/connexion ;
2. n'écrire dans l'état par fenêtre que les observations d'équipement ayant ce contexte vérifié ;
3. lire l'équipement de la fenêtre sélectionnée/au focus pour la molette, sans emprunter celui de l'autre ;
4. garantir qu'aucune fiche personnage ne survit à la fermeture de la fenêtre ou de BestCrush ;
5. conserver l'historique commun par serveur seulement sous la sélection manuelle, sans inférer des comptes.

## Validation Windows de P3m.16

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Ne pas affirmer que ces nouveaux tests ont réussi avant retour Windows.
