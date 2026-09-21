# BestCrush.NetworkProbe

Prototype isolé de collecte réseau passive pour BestCrush.

## Objectif

Valider, sans toucher à la base BestCrush ni aux overlays, que le trafic Dofus TCP/5555 permet de reconstruire :

- les observations HDV (runes, ressources, équipements) ;
- les résultats de concassage ;
- le coefficient réel de concassage ;
- les runes obtenues et leurs quantités.

Le clic molette / OCR de focus n'est pas concerné par ce prototype.

## Prérequis Windows

1. Installer **Npcap**.
2. Activer le mode de compatibilité WinPcap pendant l'installation.
3. Vérifier que Dofus possède une connexion TCP établie vers le port 5555.

Le probe utilise SharpPcap + PacketDotNet et n'injecte aucun paquet : il écoute uniquement le trafic reçu/envoyé par la machine.

## Lancement

Lister les interfaces :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
```

Puis lancer la capture sur l'interface choisie :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --all
```

`--all` affiche chaque message Ankama détecté. Sans `--all`, seuls les événements sémantiques reconnus sont affichés.

## Mapping protocole

`protocol-map.json` contient les opcodes observés pour le build Dofus testé :

- `price_list = jzn`
- `crush_result = kci`

Les opcodes étant obfusqués et susceptibles de tourner entre builds, le reste du code n'utilise jamais ces chaînes directement.

## Test de validation

1. Ouvrir une rune ou une ressource en HDV et comparer les lots/prix affichés avec `[MARKET]`.
2. Ouvrir un équipement et comparer les offres individuelles.
3. Effectuer un concassage et comparer `[CRUSH]` avec l'écran :
   - UID d'instance ;
   - coefficient ;
   - IDs des runes ;
   - quantités.
4. Répéter sur plusieurs objets avant toute intégration à la base BestCrush.

## Limites V0

- sélection manuelle de l'interface réseau ;
- mapping limité aux messages déjà identifiés ;
- pas encore de résolution UID -> DofusDbId pour le concassage ;
- aucune écriture dans `bestcrush.db` ;
- aucun remplacement de F8/F9/OCR à ce stade.
