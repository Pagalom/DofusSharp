# P3a — Étude des métiers par compte + serveur (diagnostic TCP)

## Portée métier corrigée

Depuis la mutualisation des métiers de DOFUS 3.3,
les métiers sont partagés entre **tous les personnages
d'un même compte sur un même serveur**. Ils ne sont
pas une propriété individuelle d'un personnage et
ne sont pas transférés avec un personnage sur un
autre serveur.

Sources publiques :
- https://dofuspourlespros.com/mise-a-jour-303.html
- https://support.ankama.com/hc/fr/articles/201165017--DOFUS-Transf%C3%A9rer-un-personnage-sur-un-autre-serveur
- https://support.ankama.com/hc/fr/articles/214542118--DOFUS-WAKFU-Transf%C3%A9rer-un-personnage-sur-un-autre-compte

Les messages `ksc` et `ksb` sont des événements de
**chargement de personnage**, utiles pour retracer
l'origine d'un `ipz`, mais ils n'identifient
**ni le compte, ni le serveur**. Leur concordance
ne doit donc jamais confirmer la propriété des
métiers. Une connexion TCP est un périmètre
transitoire de capture, pas un compte DOFUS.

## Situation

Sur le client Dofus `3.6.11.15`, la validation
expérimentale P2j a confirmé sur une même connexion
`ksc` suivi de `ksb`, ainsi qu'un candidat de classe
cohérent. Une capture metadata-only a aussi observé
`ksc → kta → ksb → ipg → ipz`.

Le mapping versionné `ipz-profession-mapping.3.6.11.15.json`
contient déjà **20 noms et IDs f5 candidats** issus
d'études précédentes. Les `professionId` confirmés
restent tous à `null`. L'association entre `ipz`
et une identité réelle reste non vérifiée.

## Objectif et fonctionnement

Ajouter un diagnostic **opt-in**, isolé de l'application
et de toutes les autres études :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-flow
```

Aucun nom, niveau, compte, serveur ou référence
locale n'est demandé. N'envoyer que les lignes
`PROFESSION_FLOW` et les numéros de session anonymisés.

- `ksc` : charge une liste expérimentale de candidats,
  annule tout personnage précédemment chargé sur cette
  connexion, même si le nouveau `ksc` est malformé ;
- `ksb` : remplace systématiquement le candidat
  précédemment chargé, même en cas d'échec de décodage ;
- `ipz` : vérifie la structure stricte des **20 lignes**
  et signale uniquement si les 20 `f5` sont présents,
  non nuls et uniques ;
- `selection-link=yes` : `ksb` apparaît une fois
  dans le `ksc` courant, égalité de l'ID candidat,
  du nom et du niveau ; `class-link=yes` ajoute
  une égalité du candidat de classe ;
- `selection-link=no` : présence de deux candidats
  non concordants sur la **même** connexion ;
- `selection-link=unavailable` : fiche chargée sans
  `ksc` préalable ;
- `selection-link=pending` : pas de `ksb` courant ;
- `scope=account-server account=unverified server=unverified
  mapping=unverified` **toujours** : le périmètre
  logique des métiers est bien compte + serveur,
  mais ni le compte ni le serveur ne sont établis
  par cette seule capture. L'ordre des paquets et
  l'égalité des fiches ne constituent pas une
  preuve d'identité de compte.

Le diagnostic ne révèle jamais les valeurs `f5`,
les XP ou identifiants candidats. Pas d'écriture
dans la base, les fichiers de mapping, les profils
ou `CharacterIdentityCorrelator`.

### Cycle de vie et confidentialité

L'état P3a est limité à **une session TCP**.
Tout nouveau `ksc` révoque l'ancien chargement.
Tout `ksb`, même invalide, remplace l'ancien.
FIN/RST, limites de réassemblage, réutilisation
du tuple TCP ou éviction effacent cet état.
Une connexion indépendante n'hérite jamais d'une
fiche ou d'une association `ipz` précédente.

Les données de candidat ne sont détenues que
transitoirement en RAM : aucune sortie d'identité,
hash, payload ni session brute sur disque.

## Tests synthétiques

Le fichier `ProfessionFlowStudyTest.cs` couvre :

- parcours complet `ksc → ksb → ipz` ;
- `ipz` avant le chargement, ou `ksb` seul ;
- alternance A → B → A, sans création de propriétés
  métiers par personnage ;
- `ksb` erroné, classe contradictoire,
  identité absente de la sélection ;
- `ipz` malformé, `f5` absent ou dupliqué ;
- confidentialité des indicateurs ;
- isolement multi-TCP, fin de session et reprise SYN ;
- absence d'analyse des faux messages venant du client ;
- exclusivité du mode diagnostic.

**Aucune donnée réelle de joueur** n'est enregistrée
dans les tests ou la documentation.

## Checkpoint Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

La dernière suite **explicitement attestée avant P3a**
est `635 réussis, 0 échec, 0 ignoré` avec
17 avertissements. Le build P3a et ses nouveaux tests
doivent encore être validés sur Windows.

## Expérience réelle à conduire

1. Démarrer le probe en étant déjà connecté en jeu.
2. Retourner à la sélection des personnages et
   attendre `PROFESSION_FLOW=[p3a=ksc ...]`.
3. Charger le personnage A et attendre `ksb`,
   puis `ipz` **dans cette même session TCP**.
4. Quitter et charger un autre personnage B **du
   même compte et du même serveur**, puis répéter
   pour A.
5. Ne transmettre que `PROFESSION_FLOW`,
   sans aucune saisie ou trace contenant
   une donnée personnelle.

Objectif : vérifier la cohérence temporelle du
flux métiers partagé au sein du compte + serveur,
la remise à zéro de l'**observation de personnage**
sur changement de session, et l'absence de
confusion entre comptes simultanés.
**Ce n'est pas une association automatique
à un compte ni à un personnage dans BestCrush.**
L'égalité de deux `ipz` entre personnages
n'est pas, à elle seule, une preuve qu'ils
appartiennent au même compte.

## Décision à ne pas anticiper

La confirmation des identifiants globaux `f5`,
une **identité de compte** fiable, un **serveur**
identifié, et une politique de séparation
multi-compte/multi-serveur sont nécessaires avant
toute attribution ou persistance des métiers.

### Dette d'architecture BestCrush

`BestCrush/Services/CurrentCharacterState.cs`
contient aujourd'hui `CharacterSessionProfile.ProfessionLevels`
et `CharacterDataObservation.ProfessionLevels`,
deux propriétés définies **par personnage**.
Elles reflètent une hypothèse fonctionnelle
désormais obsolète. À terme, déplacer ces données
vers un état métier distinct, indexé sur la paire
`(AccountKey, ServerKey)`, avec une identité de compte
authentifiée et sans emprunt automatique au personnage
courant.

Le compte et le serveur ne doivent **jamais**
être inférés à partir du seul numéro de session TCP,
du nom de personnage ou du fait que `ksb` et
`ipz` sont proches dans le temps. En attendant cette
refonte, aucune modification du stockage ou des profils
de production : ce mode P3a reste un diagnostic passif.
