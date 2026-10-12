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

### Exploration des personnages (P1 — à privilégier)

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-scan
```

Ce mode est **indépendant de l'analyse de marché** et affiche uniquement :

```text
[15:42:18.100 UTC] Session #1 S→C abc 42 octets
[15:42:19.020 UTC] Session #1 C→S def 7 octets
[15:42:20.450 UTC] Session #1 S→C CLOSED
```

Chaque numéro de session identifie une **connexion TCP spécifique**. Aucun endpoint, nom de personnage, contenu protobuf, token ou payload n'est imprimé ou sauvegardé par ce mode. Les flux sont plafonnés à 128 connexions et purgés après 30 minutes d'inactivité. Un FIN/RST est journalisé même sans payload. `--character-scan` prend la priorité sur `--all` lorsqu'ils sont combinés.

**Attention :** les traces terminal peuvent toujours révéler un timing d'activité. Ne partage que les lignes nécessaires et relève manuellement l'heure des actions en jeu. Aucun décodage d'identité, de métier, de banque ou de kamas n'est encore revendiqué.

`--all` affiche chaque message Ankama détecté dans le **mode historique**. Sans `--all`, seuls les événements sémantiques reconnus sont affichés. Ce mode historique peut afficher des contenus réseau : ne pas le choisir pour l'étude des personnages.

## Mapping protocole

`../BestCrush.Network/protocol-map.json` est la source canonique, copiée en sortie du Probe sous `protocol-map.json`. Ce fichier contient les opcodes observés pour le build Dofus testé :

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
