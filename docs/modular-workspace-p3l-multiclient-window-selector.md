
# P3l — Sélecteur multiclient de l'en-tête BestCrush

## Objectif

Chaque **fenêtre Dofus visible** reçoit un bouton compact en haut à
droite de `MainLayout.razor`, avec une silhouette et l'initiale
du nom de personnage (lorsque le titre Windows suit la forme connue
`Nom - Classe - Build - Release`). Le nom et la classe sont
également indiqués dans l'infobulle. Une bordure distingue la fenêtre
ciblée. Les véritables portraits sont reportés : aucun visuel vérifié
de personnage n'est disponible dans le projet.

**Fenêtre Dofus ≠ compte Ankama.** Le terme "client" est utilisé
dans l'UI tant que l'identité réseau du compte n'est pas établie.

## Comportement

- `DofusWindowService.ListVisibleDofusWindows()` énumère les
  fenêtres visibles (PID + HWND, titre local), sans persistance.
- `GetForegroundDofusWindow()` donne la fenêtre réellement
  au premier plan, ou `null` si BestCrush/une autre application
  est active. **Pas de repli sur la première fenêtre** dans ce mode.
- `CurrentDofusClientsState` contient seulement les clients
  détectés sur ce démarrage, avec leurs libellés extraits du titre.
- La barre d'en-tête réinterroge les fenêtres chaque seconde.
  Les rafraîchissements sans changement ne déclenchent pas de rendu.
- Cliquer un bouton choisit explicitement sa fenêtre comme cible UI.
  Tant que l'utilisateur reste sur BestCrush, la sélection est stable.
  L'activation ultérieure d'une fenêtre Dofus actualise la cible.
- Si une fenêtre disparaît, elle n'est plus sélectionnable et sa
  sélection est révoquée. Une autre instance de BestCrush part de zéro.
- Sur un build/titre inconnu, afficher un client anonyme générique au
  lieu de deviner le nom ou la classe.
- Conserver les boutons historiques de personnages vérifiés en
  secours uniquement lorsqu'aucune fenêtre Dofus n'est visible.

## Limites volontairement conservées

Cette phase **ne lie PAS** le sélecteur au traitement réseau de
production. P3k valide expérimentalement le lien IPv4 quatre-tuples
TCP → PID → HWND sur **deux clients**, mais il reste à transférer ce
mécanisme dans les services actifs avec une gestion de cycle de vie
strictement par flux, de reconnexion et de génération.

Le clic ne modifie ni `CurrentCharacterState.ActiveProfile`, ni
`CurrentAccountProfessionsState`, ni l'association d'un message
à un personnage/compte/serveur. Il n'affecte pas le serveur global
sélectionné et n'autorise aucune capture intercompte.
Aucune donnée de compte, titre, PID/HWND, métier, inventaire ou kamas
n'est persistée. Le choix de fenêtre est un ciblage **UI seulement**.

La conception ultérieure devra lier **un flux réseau vérifié** à
la fenêtre correspondante avant de changer le contexte métier.
Ne jamais assimiler un titre ou un PID à une identité de compte.

## Validation

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```


Sur le poste Windows :
1. Ouvrir BestCrush et deux clients Dofus.
2. Observer deux boutons en haut à droite.
3. Basculer le focus Dofus A/B ; vérifier la bordure active.
4. Revenir dans BestCrush et sélectionner manuellement l'autre bouton ;
   la sélection ne doit pas être écrasée par le focus de BestCrush.
5. Fermer un client et vérifier qu'il disparaît.
6. Redémarrer BestCrush : tous les clients sont découverts à nouveau,
   pas restaurés depuis un fichier.

Les tests unitaires utilisent des HWND, PID et noms fictifs.
L'environnement assistant n'inclut pas dotnet : le build Windows reste
à attester par le développeur.

Pas de merge vers main, changement de version, publication ni migration.
