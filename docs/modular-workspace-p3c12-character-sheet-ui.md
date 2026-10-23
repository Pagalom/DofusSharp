# P3c.12 — Refonte Dofus-like des fiches personnages (octobre 2026)

## Portée du lot

L'utilisateur souhaite une fiche joueur reconnaissable, proche des panneaux **Caractéristiques** et **Métiers** de Dofus : portrait, nom, niveau, classe, kamas, kamas en banque, métiers avec niveaux et **vraies barres de progression**, sans inventaire détaillé, sans bouton d'activation ou statut « connecté », sans chevron par métier et sans compteur technique du nombre de candidats `ksc`. Respecter le thème BestCrush (ivoire clair, vert ardoise, or).

## Implémentation

- Nouveau composant réutilisable `Components/Pure/CharacterSheetCard.razor` + CSS isolé. En-tête sombre, illustration fantasy **générique locale** (`wwwroot/img/character-portrait-placeholder.svg`), classe et niveau, deux panneaux kamas, section métiers en lignes à icône/nom/niveau/barre/XP, liste à défilement borné et bouton de ciblage local. Le portrait **n'est pas l'apparence exacte** du personnage ; le dépôt contient déjà un client `DofusDbBreedImagesClient` qui pourra fournir à terme une illustration de classe, mais aucune identité/gender/skin issue du trafic n'est encore certifiée.
- `CharacterProfile.razor` reprend ce composant pour les cartes candidates P3c.11 et les profils explicitement confirmés de `CurrentCharacterState`. Les cartes candidates n'affichent **plus** « Personnages candidats dans la liste : N », ni « Connecté », ni inventaire/objets/bank inventory. Le texte technique est réduit à une note en bas de page.
- Le **niveau candidat de la sélection active** vient du message `ksb` déjà décodé, après confirmation de concordance `ksc/ksb` sur le même flux TCP lié à la même fenêtre OS. Il est conservé uniquement dans l'index RAM P3c.8, exposé à Blazor comme `CandidateWindowRosterCard.SelectedLevelCandidate` (sans ID de personnage, compte ni niveau de tiers de la liste), et détruit à la fermeture de fenêtre, nouvelle connexion, nouveau serveur manuel, nouveau captureEpoch, contradiction.
- Pour le candidat, le **nom et la classe proviennent du titre de fenêtre Windows** (et non d'une identité Ankama prouvée). La carte porte la discrète mention « Données de personnage provisoires ». Aucune promotion en `CurrentCharacterState` ou `VerifiedAccountServerKey`.
- `CurrentCharacterState` conserve ses profils confirmés séparément et les affiche seulement si `IsCharacterConnected` ; le mapping statique public `DofusClassDisplayNames` traduit `ClassId` vérifié en nom de classe. Les cartes ne sont **jamais** jointes par nom.
- Kamas de poche et de banque : seuls `CharacterSessionProfile.PocketKamas` et `BankKamas` déjà attribués à une identité vérifiée peuvent remplir la fiche. Les cartes candidates affichent **Non observés** (jamais zéro). Aucun nouveau décodeur kamas/banque n'a été implémenté.
- Métiers : `CurrentAccountProfessionsState.ForCharacter` exige toujours un `VerifiedAccountServerKey` indépendant, et reste sans writer live. **P3g avait caractérisé 20 ID `f1.f5`, P3e les niveaux/XP et les seuils `f1.f3/f1.f6`**, mais ces éléments ne sont PAS fusionnés à des groupes candidats pour alimenter des profils. La liste métiers est donc **indisponible pour les cartes candidates** tant que la propriété des données n'est pas établie.
- Les vrais remplissages de barres sont préparés dans `ProfessionProgressPresentation.For` : niveau XP = `CumulativeExperience - LevelStartCumulativeExperience`, XP du niveau = `NextLevelCumulativeExperience - LevelStartCumulativeExperience`. Les deux seuils sont facultatifs dans `AccountProfessionProgress`, pour une future ingestion vérifiée (pas de writer ajouté ici). Pas d'estimation linéaire depuis le niveau ni depuis l'XP cumulée : si les seuils manquent, une barre grisée/indéterminée et « Progression non observée » apparaissent. Niveau 200 affiché comme niveau maximal. Aucun fake `0/20`.
- La disparition sans nouveau paquet TCP est conservée via le polling Win32 de `MainLayout` et le service P3c.11. Le mode candidat **reste réservé au Debug opt-in**, `BESTCRUSH_P3C10_ROSTER_STUDY=1`, comme avant.
- L'affichage ne présente un candidat que lorsque le titre de fenêtre indique un **personnage chargé** (`HasCharacterTitle`). La vue se masque à l'écran de sélection, même si un ancien lien TCP candidat existe encore en RAM ; le titre n'est pas une preuve réseau, et ne réattribue aucune donnée. `CandidateCharacterSheetVisibility` effectue exclusivement une jointure sur la clé de fenêtre locale unique.

## Validation ciblée

Nouveaux tests :
- `ProfessionProgressPresentationTest` : progression proportionnelle réelle, bornes exactes, niveau et XP connus mais bornes absentes, bornes incohérentes, très grands cumuls, niveau invalide et aucune écriture de métier dans l'état live.
- `CurrentCandidateWindowCardsStateTest` : deux tests sur la propagation du niveau au bon HWND/TCP et sa suppression sur fermeture.
- `EphemeralCandidateRosterGroupsStateTest` : deux tests sur le niveau cantonné au bon bail/époque et sur une contradiction qui invalide le HWND.
- `CandidateCharacterSheetVisibilityTest` : trois tests pour masquer l'écran de sélection, gérer deux fenêtres avec noms identiques et refuser l'inventaire HWND ambigu.

Ne pas annoncer la compilation .NET ou une suite de tests validée avant retour du poste Windows. Aucun runtime .NET n'est disponible dans l'environnement d'édition.

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Pour le test visuel opt-in, fermer BestCrush et ses deux clients Dofus, puis :

```powershell
$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Ouvrir les deux personnages sur le serveur **choisi manuellement** ; visiter `/profile`. Les fiches doivent présenter des en-têtes **NIVEAU X | CLASSE**, le portrait générique, les deux soldes « Non observés » et un message clair sur les métiers non attribués. Fermer B sans trafic pour vérifier la disparition de sa fiche sans affecter A. Valider que le compteur de candidats `ksc` et les inventaires ne sont plus affichés. Désactiver après test : `Remove-Item Env:BESTCRUSH_P3C10_ROSTER_STUDY`.

**Prochaines tâches distinctes** : image de classe DofusDB (sans feindre l'apparence réelle) ; ingestion des seuils `ipz` et des métiers une fois leur propriété établie ; découverte/décodage sûr des kamas et banque ; activation produit du moteur de cartes seulement après stabilisation du mapping d'identité. Ne pas confondre redesign visuel avec activation de ces décodeurs.
