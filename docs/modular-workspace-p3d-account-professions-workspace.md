# P3d — Métiers dans un modèle compte + serveur (sans attribution réseau)

## Objectif

Les métiers sont partagés entre personnages d'un même
**compte sur un même serveur**.

**Stockage : compte + serveur. Affichage : à l'intérieur de chaque
fiche personnage concernée.** Il ne faut pas copier les
métiers dans chaque profil, seulement consulter un snapshot commun
après vérification explicite du rattachement du personnage à ce compte.
L'étude réelle P3b a montré, dans le parcours
A → B → A, les mêmes vingt candidats `f5`,
niveaux et compteurs XP. La preuve d'identité du
compte/serveur est **toujours absente** (P3c
reporté jusqu'à disponibilité d'un second compte).

## Modifications de P3d

- Supprimer `ProfessionLevels` de
  `CharacterSessionProfile` et
  `CharacterDataObservation`.
- Supprimer la conservation et la mise à jour de
  métiers dans `CharacterIdentityCorrelator` :
  la confirmation d'un personnage n'est pas
  la confirmation du compte.
- Introduire `CurrentAccountProfessionsState`,
  `AccountServerKey`, `AccountProfessionProgress`
  et `AccountProfessionsSnapshot` :
  modèles en RAM, indépendants des personnages.
- Enregistrer le nouveau service dans
  l'injection de dépendances MAUI.
- Conserver les métiers **dans la fiche de chaque personnage**
  dans `CharacterProfile.razor`, sous la forme d'un
  tableau métier / niveau / XP cumulée, sans propriété
  `ProfessionLevels` propre au personnage.
- Prévoir sur `CharacterSessionProfile` uniquement
  une référence facultative `VerifiedAccountServerKey`,
  initialement `null`, vers le couple compte + serveur
  établi indépendamment : elle ne contient aucun métier.
- `CurrentAccountProfessionsState.ForCharacter(character)`
  n'affiche le snapshot partagé que si **compte et serveur
  correspondent à cette référence vérifiée**, jamais sur
  la base du seul serveur choisi dans BestCrush ou du
  personnage actif.
- Lors de la reconnexion ou de l'invalidation d'une session,
  révoquer ce lien tant qu'il n'est pas à nouveau prouvé.
- Afficher dans chaque carte « Non disponibles » tant
  que P3c et le mapping des métiers ne sont pas validés.
  Aucun état métier non vérifié n'est promu automatiquement.
- Adapter les tests existants affectés par
  le retrait des champs personnages. Ajouter
  uniquement un contrôle simple de l'état
  initial : aucune fiche métier disponible.

## Hors périmètre explicite

À la demande de l'utilisateur, **aucun nouveau
test d'isolation multicomptes/multiserveurs** n'est
ajouté dans cette phase. Les protections et
tests historiques non concernés sont conservés ;
la campagne d'isolation est reportée avec P3c.

**Aucun décodeur réseau n'alimente le nouveau
modèle.** Il ne possède intentionnellement
aucune méthode d'import ou de mise à jour tant
que les identités compte + serveur et le mapping
global des métiers restent non vérifiés.
Le `CurrentServerState` sélectionné manuellement
ne peut pas fabriquer une identité de compte.
Ne pas exposer ni persister d'identifiant
personnel, token, empreinte ou payload.

Aucune base de données, migration, publication,
branche `main` ou version produit modifiée.

## Vérification Windows requise

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Ouvrir la page Profil de BestCrush :
chaque **carte de personnage déjà identifié** doit contenir
une section « Métiers (compte + serveur) » et afficher
« Non disponibles » tant que l'identité de compte
n'est pas confirmée. Le tableau détaillé devra apparaître
dans cette même carte lorsqu'un snapshot partagé et
un lien compte–personnage indépendant seront disponibles.
Il ne doit plus y avoir de panneau métiers global
séparé des fiches personnages.

Aucun test d'isolation supplémentaire n'est demandé
dans cette étape de correction de l'affichage.

P3d sera techniquement validé après cette
compilation et ce contrôle visuel Windows.
L'identification du compte et du serveur reste
à étudier ultérieurement, sans activation
d'attribution automatique à ce stade.

## Correction de conception P3d — stockage commun, affichage par personnage

Le premier commit P3d avait déplacé **à tort** la section
métiers hors des cartes personnages. Cette correction distingue
les responsabilités sans retirer l'information du profil :

- **source unique** : `AccountProfessionsSnapshot`
  identifié par `AccountServerKey` ;
- **association** : référence `VerifiedAccountServerKey`
  depuis le personnage vers son compte/serveur, sans duplication
  des niveaux/XP ;
- **présentation** : liste des métiers dans **chaque fiche personnage**
  qui dispose de cette association vérifiée ;
- **sécurité** : pas de correspondance automatique par serveur,
  nom, ID de personnage ou session TCP. Le lien reste absent à ce stade.

Les tests d'isolation multicomptes demeurent reportés
à la checklist P3c, conformément à la demande de l'utilisateur.
