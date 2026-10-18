# P3d — Métiers dans un modèle compte + serveur (sans attribution réseau)

## Objectif

Les métiers sont partagés entre personnages d'un même
**compte sur un même serveur**. Ils ne doivent donc
jamais être rattachés à une fiche personnage.
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
- Préparer dans `CharacterProfile.razor` un
  encart **« Métiers — compte et serveur »**
  distinct des cartes personnages. Il affiche
  « Non disponibles » tant qu'aucune
  vérification complète n'a eu lieu ; la
  présentation du tableau métier/niveau/XP
  est préparée pour un futur snapshot confirmé.
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
l'encart « Métiers — compte et serveur »
doit être visible **même sans personnage identifié**,
et afficher le statut « Non disponibles ».
Chaque fiche personnage ne doit plus présenter
de ligne métier.

P3d sera techniquement validé après cette
compilation et ce contrôle visuel Windows.
L'identification du compte et du serveur reste
à étudier ultérieurement, sans activation
d'attribution automatique à ce stade.
