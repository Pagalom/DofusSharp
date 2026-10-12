# P1b — Observation structurale des messages de chargement Dofus

## Statut

Ce lot s'ajoute à P1a sur `feature/modular-workspace`. Il ne branche
**aucun décodeur de personnage** à l'application BestCrush et ne modifie
ni les overlays, ni les prix, ni les bases SQLite, ni la sélection de serveur.

Les captures précédentes (build Dofus 3.6.11.15) ont montré la séquence
récurrente `kta` (client vers serveur), puis une rafale côté serveur contenant
notamment `krg`, `mdp`, `ica`, `irl`, `ico`, `jei`, `isr`.
`kmz` a été observé juste avant plusieurs fermetures TCP. **Aucun de ces
opcodes n'est encore démontré porteur de classe, niveau, inventaire, kamas,
métiers, banque, connexion ou déconnexion logique.**

## Lecture sûre

`--character-shape` utilise le même suivi TCP multi-connexions que P1a
et ne donne pour les opcodes sélectionnés que :

- Horodatage UTC, numéro de session anonyme et direction ;
- opcode, taille de corps déjà disponible en P1a ;
- nombres de champs, types protobuf (varint `v`, fixed32 `32`,
  fixed64 `64`, length-delimited `len`) ;
- sous-champs *présumés* identifiés par un préfixe `?` ;
- indicateur `[partial]` ou `unrecognized` en cas de limites.

**Jamais de valeurs scalaires, textes, octets bruts, base64, IP,
UID, noms, niveaux, soldes bancaires ou jetons.** Le filtrage utilise
une liste de clés explicitement observées ; les noms métier ne sont
pas déduits des opcodes.

Les limites sont : corps maximum 256 Kio, 512 champs, profondeur deux,
48 chemins affichés, 16 Kio de tentative d'analyse imbriquée,
3 variantes distinctes par opcode/connexion. Les variantes identiques
sont supprimées pour éviter les centaines de lignes répétitives.
Tout est traité en RAM uniquement, aucun fichier n'est produit.

Le mode `--all` reste un ancien outil séparé et **ne doit pas** être
utilisé pour les investigations de personnage car il peut afficher
des contenus sensibles.

## Commandes Windows

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape
```

Remplacer `3` par l'index Npcap réel, si nécessaire.

Pour viser seulement les messages les plus volumineux de la phase
de connexion :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys irl,ico,jei,isr,mdp
```

`--shape-keys` prend une liste de 1 à 32 opcodes simples séparés
par des virgules et implique le mode `--character-shape`.

## Protocole d'observation (un seul personnage)

1. Lancer le probe avant de reconnecter le personnage (Dofus doit pouvoir
   capturer sa transition écran de sélection → personnage).
2. Entrer dans le jeu, attendre une quinzaine de secondes sans action,
   puis ouvrir les caractéristiques et les métiers.
3. Ouvrir l'inventaire et, si pratique, la banque. Ne pas interpréter
   l'absence de nouvelles trames comme des données manquantes.
4. Quitter le personnage, arrêter par `Ctrl+C`.
5. Conserver localement les résultats, ne partager que les lignes `shape=`
   produites par le nouveau mode et les heures approximatives des actions.

**Interprétation prudente** : deux structures identiques ne prouvent pas
qu'elles décrivent le même événement. Une valeur scalarisée inconnue
pourrait être l'ID du personnage comme n'importe quelle autre donnée.
Le préfixe `?` ne distingue pas un sous-message d'un tableau compacté
de nombres. Avant d'écrire des décodeurs, il faudra vérifier sur captures
répétées et fixtures fabriquées, puis relier les messages à une connexion
et au personnage cible sans incertitude.

## Checkpoint

Tests C# synthétiques ajoutés : types protobuf, confidentialité des
valeurs, imbrications hypothétiques, données malformées et trop
volumineuses, limitation des sorties, filtrage d'opcodes, session TCP
par personnage.

Les tests Windows restent **à exécuter** par l'utilisateur.
Pas d'activation de lecture d'identité en production avant P2 et
validation des trames réelles.
