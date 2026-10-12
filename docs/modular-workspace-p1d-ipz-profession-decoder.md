# P1d — Décodeur expérimental ipz et validation des 20 métiers

## Périmètre et preuves actuelles

Sur le client Dofus 3.6.11.15, le diagnostic P1b a trouvé
`ipz` (serveur vers client), de 268 octets, avec 20 champs
racine `f1` de longueur délimitée. Le joueur a fourni une liste
visuelle de 20 métiers et six couples niveau/XP totale.
Le comparateur P1c a trouvé six concordances sur six au
chemin `f1.f1` (niveau) / `f1.f2` (compteur XP).

Cela étaye fortement la présence d'une liste de métiers, mais
**n'établit pas** l'identité métier dans les champs restants,
la relation au personnage actif ni le sens exact du compteur XP.

## Implémentation

`ProfessionIpzDecoder.TryDecode` est un décodeur de recherche isolé.
Il vérifie le format du message entier avant de fournir des valeurs
en mémoire : exactement 20 lignes de type `f1:len` ; dans chaque
ligne, un et un seul `f1:varint` de niveau (1–200), et au plus un
`f2:varint` de compteur XP. Un `f2` absent est décodé en zéro
(valeur implicite autorisée par protobuf). Les doublons et les
structures incohérentes sont rejetés en bloc. Aucun nom de métier n'est inféré.

Les autres champs numériques sont conservés temporairement
en mémoire comme *champs non identifiés* ; leur rôle demeure
indéterminé. Le rapport ne montre pour chaque numéro de champ
que le nombre de lignes où il est présent et le nombre de valeurs
distinctes (jusqu'aux douze premiers numéros) : par exemple
`f3:present=20/20,distinct=20`.

Le diagnostic `--profession-decode` sélectionne les messages
`ipz` provenant du serveur, par connexion TCP isolée. Le rapport
contient le nombre de lignes, le nombre de couples de référence
retrouvés et les statistiques des champs inconnus, sans leurs
valeurs. Les autres opcodes sont supprimés de la sortie.

Une trame `ipz` malformée est signalée
`IPZ=[rejected-layout:<categorie>]`, sans aucun détail privé.
La catégorie fixe (par exemple `xp-duplicate`, `level-cardinality`,
`row-envelope` ou `row-count`) aide à corriger le décodeur sans
imprimer aucune valeur issue de la trame.

## Mode d'emploi (Windows)

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-decode
```

Saisir dans la console du diagnostic 2 à 20 couples
`niveau:XP_totale` séparés par des virgules, en utilisant
les valeurs déjà validées localement en P1c. Ne pas fournir
l'XP partielle affichée dans la barre des métiers.

Lancer la capture **avant** de connecter le personnage,
se connecter, patienter environ 10 secondes puis quitter le
personnage et arrêter avec Ctrl+C.

Résultat fictif (aucune donnée personnelle) :

```text
[14:05:01.123 UTC] Session #2 S→C ipz 268 octets IPZ=[rows=20 matched=3/3 unknown=[f3:present=20/20,distinct=20 f5:present=20/20,distinct=1] identity=unverified]
```

## Critères d'acceptation

- La compilation du probe et la suite de tests doivent réussir.
- `rows=20` sur un message `ipz` de connexion complet et
  `xp-implicit-zero=N` pour les lignes sans champ `f2`.
- `matched=6/6` attendu si les six valeurs de référence
  sont toujours à jour depuis la capture précédente.
- Vérifier les statistiques des champs `f3`, `f5`, `f6`
  plutôt que d'affirmer arbitrairement lequel est l'ID métier.
- Le compteur XP de chaque ligne reste une valeur opaque
  jusqu'à preuve que Dofus transmet l'XP cumulée.
- Pas de transformation d'une simple corrélation en
  identité de personnage vérifiée.

## Retour P1d.1 sur la capture réelle

La capture du 12 octobre vers 18 h 21 (heure française) montre
`ipz 268 octets IPZ=[rejected-layout]`. Le message existe,
mais le décodeur P1d était trop strict sur `f2` : plusieurs
métiers visibles ont un compteur d'expérience nul.
En proto3, un scalaire nul peut ne pas être sérialisé.

P1d.1 teste donc l'absence de `f2` comme zéro implicite,
sans inférer de métier. Pour distinguer cette hypothèse
d'une autre incompatibilité de structure, le rapport produit
désormais uniquement des catégories de rejet prédéfinies,
sans afficher ni identifiant, ni niveau, ni expérience.

La validation sur Windows reste nécessaire. Si le décodeur
retourne encore un rejet, relever uniquement la catégorie
et garder les trames brutes privées.

## Confidentialité et isolation

Aucune donnée du personnage n'est écrite dans une base ou un
fichier. Les niveaux, compteurs d'XP et champs inconnus sont
maintenus seulement pendant le traitement de la capture.
La sortie n'expose que des nombres de correspondances et des
statistiques structurelles ; l'invite Windows utilisée pour
entrer les références peut, elle, être visible dans le
terminal local — ne la partager que si souhaité.

Cette phase ne modifie ni `main`, ni les overlays, ni
les services d'identité, ni les prix, ni le routage métier
de BestCrush. Elle n'établit pas encore le mapping
`ID métier ↔ nom` et ne traite pas la sélection réelle
de personnage multiclient.
