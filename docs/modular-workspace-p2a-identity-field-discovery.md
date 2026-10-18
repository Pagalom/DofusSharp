# P2a — Localisation des champs d'identité de personnage (recherche privée)

## Signal de départ

Deux chargements successifs du client Dofus 3.6.11.15 ont produit
sur deux sessions TCP distinctes des messages de même structure :
`krg` (855 octets), `ksb` (162), `kes` (21),
`jev` (45), `jgi` (48), `ica` (~2821), `irl` (~14080),
`ifg` (~4323). Après déconnexion, `ksc` (171) apparaît sur la
nouvelle session. Une simple ressemblance de structures et de
tailles ne permet pas d'identifier le personnage.

`jev`, `jgi`, `ksb`, `kes`, `ksc`, `krg`,
`knc`, `kod`, `ifg`, `kmz` sont les candidats
retenus pour cette expérience. Aucune sémantique n'est confirmée.

## Fonctionnement

`--identity-name` demande **dans le terminal local** un couple
`NomDuPersonnage:Niveau`. Il inspecte seulement les 10 opcodes
ciblés, les deux directions, sur chaque flux TCP isolé.

Le moteur :
- recherche l'**égalité exacte** avec les octets UTF-8 d'un
  champ protobuf `length-delimited` (pas un fragment de texte) ;
- indique seulement les chemins de champs contenant ce nom ;
- indique les chemins des champs varint de **même noeud protobuf**
  égaux au niveau déclaré, s'il y en a ;
- borne l'analyse aux corps de 8 Kio, 5 niveaux de profondeur
  et 6000 champs maximum, avec une sortie condensée ;
- affiche au plus 3 variantes par opcode et connexion TCP ;
- **ne produit aucun ID, nom, niveau, payload, adresse ni
  empreinte/hash de données personnelles dans les journaux** ;
- n'écrit aucun fichier et n'alimente aucune fiche personnage.

Une absence de correspondance peut provenir d'une autre
structure, d'un autre encodage du nom ou d'un autre opcode.
Un nombre égal au niveau peut être fortuit. Il ne s'agit
pas d'une identification validée.

## Commandes Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-name
```

À l'invite `Référence P2a`, saisir le nom exact du
personnage et son niveau. Exemple **fictif** :
`Explorateur:137`.

Démarrer la capture avant la connexion, entrer sur le personnage,
attendre quelques secondes, revenir à la sélection puis entrer
à nouveau sur ce personnage. Utiliser `Ctrl+C` pour arrêter.

Ne partager **que les lignes `IDENTITY_NAME=[...]`** et
les numéros de sessions, en supprimant la ligne
`Référence P2a` du terminal, car celle-ci affiche la
valeur saisie par l'utilisateur.

## Étapes suivantes et exigences de sécurité

Une fois les champs de nom localisés :
1. caractériser les IDs, la classe et le niveau depuis des
   messages réseau de deux familles indépendantes ;
2. comparer ces familles sur une même connexion et vérifier
   une identité stable ; une répétition d'un même opcode
   n'est PAS une double preuve ;
3. tester les invalidations de connexions sur FIN/RST,
   changement de personnage et reconnexion ;
4. relier `ipz` seulement à une session avec identité confirmée.
   Maintenir les personnages uniquement en RAM et éviter toute
   écriture dans la base ou les logs persistants.

La réalisation de ces points n'est **pas** impliquée par la
réussite de la présente expérience.

Les changements restent confinés au projet NetworkProbe et
aux diagnostics de BestCrush.Network ; aucun changement
BestCrush UI, stockage, routage métier ni release.
