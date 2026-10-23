# P3m.17 — Routage économique automatique et mutualisation par serveur

## Décision du 23 octobre 2026

- Chaque fenêtre Dofus est reliée à un chargement Game précis (socket TCP + propriétaire Windows PID/HWND).
- Le serveur du personnage est déterminé automatiquement à partir du candidat `ServerId` de Connection, puis d'un Game chargé `ksc + ksb` cohérent sur cette même fenêtre.
- **Deux fenêtres reliées au même serveur partagent exactement les mêmes données économiques** dans SQLite. Les observations restent séparées entre serveurs.
- Plus de sélection globale ou manuelle de serveur pour l'utilisateur.
- Les kamas, inventaires et informations propres au personnage restent individuels. Les métiers ne seront mutualisés par compte+serveur que lorsque le compte aura lui-même une identité prouvée.

## Implémentation publiée sur `feature/modular-workspace`

### Autorisation réseau

`NetworkCaptureLease` transporte désormais, en mode automatique :
`ServerName`, génération monotone, `Window` (HWND/PID), `CaptureEpoch` et `ConnectionId`.

`WindowServerAttributionState` ne délivre un tel bail qu'après :

1. Une sélection Connection `root.f1.f4.f1` connue du catalogue (295 Draconiros, 293 Tylezia, 355 Kourial, etc.), associée à un propriétaire TCP et une seule fenêtre.
2. Un Game `ksc` puis `ksb` cohérents **sur le même flux TCP Game**, avec correspondance de la sélection de personnage et une unique sélection Connection récente de la même fenêtre.
3. L'absence d'ambiguïtés (sélections multiples, propriétaires absents/conflictuels, mauvais ordre, reconnexion ou fenêtre réutilisée).

Un bail A et un bail B peuvent concerner tous deux Draconiros : leurs autorisations restent indépendantes et révocables. Aucune IP distante, aucun titre de fenêtre et aucun nom de personnage ne suffisent seuls à fournir un `ServerId`.

`CurrentServerState` est configuré en **mode automatique par l'injection de dépendances de l'application**. Dans ce mode, la sélection UI n'émet aucun bail de capture ; `GetCaptureLease()` retourne `null`. Les tests de replay historiques restent en mode manuel pour préserver leurs contrats.

Le worker réseau remplace le périmètre d'observation/découverte par le bail Game prouvé avant de transmettre un message à `DofusNetworkMessageProcessor`. Sans bail Game valide, l'écriture économique est refusée. `NetworkObservationWriter` revérifie le bail aux points d'écriture et utilise **le `ServerName` figé dans ce bail**, jamais celui affiché dans l'UI.

### SQLite : séparation par serveur, partage entre fenêtres

Aucune table ni colonne spécifique aux fenêtres n'est ajoutée. Les services `MarketPriceService` et `CoefficientService` continuent d'enregistrer sous le `ServerName` existant, obtenu par la conversion exacte `ServerId → nom public`.

Exemple : les fenêtres A et B de Draconiros (ID 295) écrivent toutes deux dans les historiques `ServerName = "Draconiros"`. Une fenêtre C de Tylezia (ID 293) écrit sous `ServerName = "Tylezia"`. Elles utilisent **une seule base SQLite**, sans migration.

L'item UID et les requêtes d'achat restent corrélés par `ConnectionId`. Le processeur ne vide plus le cache d'un client simplement parce qu'un paquet d'un autre serveur arrive entre deux messages du premier client.

### Fenêtres, interface et overlays

- Le bandeau affiche le serveur **du client ciblé**, pas un menu de sélection.
- Le vieil écran de choix global redirige vers les personnages ; le sélecteur historique n'autorise jamais une écriture en mode automatique.
- La fiche de chaque fenêtre indique son serveur ou « identification en cours ». Le niveau candidat peut provenir du `ksb` lié.
- Les anciens composants de marché par URL utilisent le serveur du client ciblé. En absence de preuve, les modules nécessitant un serveur ne disposent pas de contexte.
- Les équipements réseau mémorisés pour la molette sont conservés **par fenêtre et bail**, pour éviter qu'une fenêtre Tylezia écrase le dernier équipement d'une fenêtre Draconiros.
- Un clic sur la molette n'utilise un bail que pour la fenêtre réellement pointée ; le focus économique est refusé si le serveur UI actif est différent.
- Une fermeture Windows révoque le bail. Le retour d'une fenêtre précédemment reconnue comme personnage à un titre générique (écran des personnages) révoque également le bail.
- Le point ambré expérimental (`ksc/ksb`) reste disponible en mode Debug P3c.10 : son état de recherche est isolé du bail économique et n'a plus besoin d'une sélection globale.

### Limites et sécurité

**Ce code doit encore être compilé et testé sur le poste Windows.** Aucune validation réelle de la nouvelle version ne peut être affirmée avant les sorties `dotnet build` et `dotnet test`, puis l'essai Dofus.

Les connexions Connection peuvent être trop brèves pour qu'un propriétaire de socket soit identifiable : dans ce cas, la fenêtre reste « identification en cours » et **aucune donnée économique n'est écrite**, même si le client Game émet des prix. Il n'y a aucun choix manuel de secours.

Le catalogue ServerId est de provenance communautaire, non officielle ; des serveurs nouveaux ou non référencés restent non attribués. Vérifier sa compatibilité avec la version effective du client.

L'absence de titre de personnage n'est jamais utilisée comme preuve positive d'appartenance : les titres ne servent qu'à l'affichage et à révoquer une association après changement d'état local constaté.

Le contrôle pré-écriture garantit l'absence d'attribution au **nouveau mauvais serveur**. Comme auparavant, une opération SQLite déjà engagée juste avant la révocation peut terminer sous l'ancien nom de serveur figé dans son bail, sans jamais être re-étiquetée.

### Tests automatisés ajoutés

- Identifiants catalogue, sélection ambiguë, deux fenêtres sur serveurs différents, deux fenêtres sur le même serveur, déconnexion, révocation du bail d'une seule fenêtre, redémarrage et rejeu périmé.
- Régression de la molette avec deux baux de fenêtres sur deux serveurs indépendants.
- Tests intégrés du worker replay/SQLite : écritures A et B sur deux serveurs distincts, écritures A et B dans **le même historique** de Draconiros, refus d'enregistrement lorsque B n'a aucun ServerId, révocation d'anciennes connexions.
- Les tests antérieurs restent présents.

**Total attendu à ce jalon : 1144 tests** (1117 confirmés sur Windows avant P3m.16, puis 27 nouveaux cas). À confirmer.

## Commandes Windows

Depuis la racine DofusSharp :

```powershell
git status --short --branch
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0 -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Démarrer BestCrush **avant** les clients Dofus pour ne pas manquer les connexions courtes du protocole Connection. Aucun drapeau `BESTCRUSH_P3C10_ROSTER_STUDY` n'est nécessaire pour les fiches serveur automatiques.

### Recette fonctionnelle demandée

1. Une fenêtre Draconiros, une autre Kourial : vérifier les deux noms et ID, sans sélectionner de serveur dans BestCrush.
2. Deux fenêtres Draconiros : vérifier que les prix observés par les deux alimentent un seul historique de marché Draconiros.
3. Fermer/déconnecter un client : le serveur et la fiche de celui-ci disparaissent, l'autre reste intact.
4. Reconnecter un client : si la sélection Connection n'est pas fiable, vérifier « identification en cours » et **absence d'enregistrement**. Ne jamais hériter implicitement de l'ancien ServerId.
5. Vérifier la molette sur deux fenêtres avec équipements différents.
6. Ne réaliser les essais de capture économique qu'avec une base de test ou une sauvegarde préalable : toute régression non détectée pourrait altérer des observations existantes.

**Ne pas publier de release avant validation Windows et essais réels.**
