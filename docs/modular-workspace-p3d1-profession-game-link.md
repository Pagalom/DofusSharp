# P3d.1 — Lien métier `ipz` vers Game/fenêtre vérifiée

## Objectif

Vérifier en conditions réelles si les messages `ipz` arrivent sur le
**même flux TCP Game** que le chargement `ksc/ksb` déjà associé à une
fenêtre Dofus et à un serveur connu.

Le résultat **ne prouve pas** l'identité du compte Ankama ni les IDs
globaux des métiers : les métiers ne sont pas encore attribués aux fiches.

## Implémentation

- `ProfessionGameLinkStudy` : observateur en RAM, sans base de données.
- Diagnostic activable **uniquement en Debug**, avec
  `BESTCRUSH_P3D_PROFESSION_LINK_STUDY=1` au lancement.
- Exploite la validation **déjà existante** de
  `DofusNetworkCaptureService.ProcessMessagesAsync` :
  `CaptureEpoch`, `ConnectionId`, fenêtre propriétaire et bail Game actif.
- Annonce si `ipz` est relié à un bail Game reconnu et si la charge
  ressemble à un roster de 20 lignes, décodé structurellement.
- Dans les traces : serveur publiquement identifié, numéro anonyme de
  fenêtre (`slot=1`, `slot=2`…), classe de forme du message.
  **Aucun** PID, HWND, identifiant de connexion, nom, XP, numéro de
  métier ni corps de message.
- Limite : 128 lignes de diagnostic par époque de capture.
- Rien n'est ajouté aux profils, prix, coefficients ou historiques.

## Validation Windows

À la racine du dépôt :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false

$env:BESTCRUSH_P3D_PROFESSION_LINK_STUDY = '1'
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Démarrer BestCrush **avant** de charger les clients Dofus, faute de quoi
les preuves TCP de sélection initiales peuvent manquer.

Dans Dofus : charger le personnage Tylezia, ouvrir métiers (J), puis
faire une fabrication simple. Refaire avec Kourial. Pour la première
recette, de préférence **un seul client à la fois** afin d'associer
sans ambiguïté les lignes émises à l'action de jeu. Le code est
compatible avec plusieurs fenêtres ; aucun nom ni compte n'est déduit.

Dans un second terminal, après les actions :

```powershell
Get-ChildItem "$env:LOCALAPPDATA\BestCrush\Logs" -Filter 'bestcrush*.log' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    Get-Content -Tail 500 |
    Select-String 'p3d='
```

Exemples fictifs de traces :

```text
p3d=enabled mode=debug-opt-in account=unverified mapping=unverified
p3d=ipz link=verified-game slot=1 server=Tylezia shape=roster20 account=unverified mapping=unverified
p3d=ipz link=unverified slot=none server=unverified shape=other account=unverified mapping=unverified
```

## Décisions selon le résultat

- `link=verified-game` pour les `ipz` : le chemin *flux TCP Game →
  fenêtre → serveur* est exploitable. Il faut encore identifier les
  métiers et, si le profil est lié de façon confirmée, distinguer
  données de personnage et données partagées par compte.
- `link=unverified` : **ne pas** attribuer l'observation par nom,
  focus, sélection manuelle ou dernière fenêtre active ; investiguer
  les moments d'émission et la provenance TCP.
- `shape=other` : la forme ne correspond pas à un roster 20 lignes ;
  cela peut être un message mono-entrée lié à une action de métier.
  Le diagnostic ne déduit pas un ID de métier.

Ne jamais activer les captures détaillées `wire.jsonl` pour ce test :
le diagnostic anonyme est suffisant et n'écrit pas les corps réseau.

### État

Code et tests ajoutés sur `feature/modular-workspace`. Compilation,
suite de tests et observation réseau **à valider sur Windows**.
