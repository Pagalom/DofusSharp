# P2b — Comparaison des champs voisins de l'identité (ksb et ksc)

## Observation P2a du client 3.6.11.15

Sur les sessions du personnage étudié, le nom UTF-8 exact
et son niveau apparaissent dans deux familles distinctes :

| Opcode | Chemin du nom | Chemin du niveau |
|---|---|---|
| `ksb` | `f1.f1.f4.f6` | `f1.f1.f4.f3` |
| `ksc` | `f2.f4.f6` | `f2.f4.f3` |

`ksb` apparaît au chargement du personnage, `ksc` au début de
la seconde session observée. Les deux sont vus sur une même
connexion TCP dans une capture réelle. **Deux opcodes différents
ne sont pas encore deux preuves sémantiques indépendantes
d'identité** : leurs rôles doivent être caractérisés.

## Objectif du diagnostic

Le mode `--identity-fields` utilise une référence locale
`NomDuPersonnage:Niveau`, comme en P2a, puis :

1. Décode strictement les chemins protobuf propres à chaque
   opcode ; pour `ksc`, il peut parcourir plusieurs lignes
   de personnages.
2. Accepte seulement **une et une seule** ligne contenant
   exactement le nom et le niveau, au sein du même nœud.
3. Retient uniquement les varints directement voisins,
   sauf le champ de niveau f3. Les champs dupliqués sont exclus.
4. Compare les valeurs numériques par **numéro de champ**,
   uniquement si `ksb` et `ksc` ont été observés sur la
   **même session TCP** ; ne révèle ni les valeurs ni le nom.
5. Bloque la comparaison si une même famille d'opcodes renvoie
   des valeurs contradictoires sur la connexion.
6. Réinitialise automatiquement le contexte avec le cycle
   de vie du traqueur TCP, notamment FIN/RST et reconnexion.

Un résultat `shared-equal-numeric=[f1,...]` représente seulement
un champ numérique à étudier comme candidat, **pas une preuve que
f1 est un ID stable**, et certainement pas une identité
confirmée pour BestCrush. D'autres scalaires comme la classe
peuvent être égaux par hasard.

Aucune fiche personnage n'est alimentée, aucune écriture disque
ou base n'est ajoutée, aucun payload ou ID brut n'est affiché.

## Lancement Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

À l'invite `Référence P2b`, saisir localement le nom **exact**
et le niveau du personnage (exemple purement fictif :
`Exemple-Ciel:137`).

Démarrer la capture avant la connexion Dofus, puis :
1. Se connecter au personnage et attendre quelques secondes.
2. Retourner à la sélection et se reconnecter une fois.
3. Arrêter le diagnostic avec Ctrl+C.

Ne partager que les lignes `IDENTITY_FIELDS=[...]` des logs,
pas la ligne de saisie portant le nom du personnage.

## Prochaine étape

Si un champ numérique distinct du niveau est identique
dans les deux familles, étudier sa **stabilité sur plusieurs
connexions** et sa **différence entre personnages fictifs**
dans des tests synthétiques. Rechercher ensuite la classe,
l'identifiant persistant et une vraie preuve d'indépendance
sémantique des sources avant d'alimenter
`CharacterIdentityCorrelator`.

Reste expressément interdit à cette étape :
- déduire l'identité d'un personnage à partir du seul nom saisi ;
- faire une sélection globale automatique entre plusieurs TCP ;
- attribuer les métiers `ipz` à un personnage non confirmé ;
- sauvegarder identités, niveaux ou données de métiers en dur ;
- modifier les fonctionnalités de production de BestCrush.
