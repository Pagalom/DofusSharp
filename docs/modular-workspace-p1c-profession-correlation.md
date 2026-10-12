# P1c : corrélation expérimentale des métiers

## Objectif

Rechercher deux couples distincts (niveau, XP de progression) au sein
de mêmes sous-structures protobuf dans les messages envoyés lors
de la connexion du personnage. Cette étape n'est pas un décodeur
de métiers et ne branche aucune donnée aux interfaces BestCrush.

## Exécution

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-match
```

Le programme demande les références directement dans sa console :
2 à 20 couples distincts `niveau:XP`, séparés par une virgule.
Exemple totalement fictif : `82:150,63:245,47:20`.
Privilégier des métiers de niveaux différents, supérieurs à 1.

Les références ne sont ni enregistrées dans un fichier ni passées
sur la ligne de commande PowerShell. Elles sont seulement retenues
pour la durée du programme. Les trames analysées sont traitées en RAM.

Une fois la capture lancée, sélectionner un personnage dans Dofus,
entrer dans le jeu, patienter, puis se déconnecter et presser Ctrl+C.

## Sortie

Seuls les candidats d'au moins deux références numériques différentes
apparues dans les mêmes numéros de champs et au même niveau
d'imbrication sont affichés :

```text
Session #1 S→C abc 208 octets CANDIDAT=[node=f2 level=f1 xp=f3 pairs=3]
```

Ce résultat fictif est un simple indice de corrélation, pas la preuve
que f1 signifie « niveau » ou f3 signifie « expérience ».

Il est possible de n'avoir **aucun candidat**, par exemple si Dofus
transmet une expérience cumulée ou un autre encodage que la barre d'XP
visible. Les valeurs des références ne sont jamais reprises dans la
sortie. Pour préserver les frontières de personnages, une paire ne
peut être assemblée à partir de deux connexions distinctes.

## Limites techniques

- Trafic analysé : serveur vers client uniquement.
- Taille du corps limitée à 256 Kio.
- Profondeur récursive limitée à 6.
- Budget de lecture de 50 000 champs protobuf par message.
- Maximum 64 champs numériques et 12 000 sous-messages par noeud.
- Au plus six chemins candidats rapportés par message.
- Sous-messages de taille délimitée analysés comme hypothèses ;
  un champ peut réellement contenir du texte ou une liste compacte.
- Aucune activation d'un décodage automatique en production.
- Modes P1b et P1c séparés. Le mode historique --all est évité.

## Validation

Les tests synthétiques couvrent les concordances par sous-message,
les mauvaises associations de champs, un seul couple insuffisant,
la séparation des connexions TCP, le filtrage des directions
et la non-exposition de valeurs dans les résultats.

La compilation et les tests doivent être exécutés sur Windows.
