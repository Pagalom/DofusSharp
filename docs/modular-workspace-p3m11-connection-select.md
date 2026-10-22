# P3m.11 — Observer `SelectServer` dans les trames du Connection Server

## Découvertes et problème concret

P3m.9 a traité **96 messages Ankama décodés** (32 par
phase A1/B/A2) sur TCP/5555, sans retrouver les noms
Draconiros et Tylezia. La recherche est bornée aux messages
`type.ankama.com/...` et aux chaînes de texte.

P3m.10 a inventorié les sockets Windows Dofus/Ankama
**sur tous les ports**, avec :

- **A1 Draconiros** : `52.30.61.61:5555` et
  `34.252.214.29:5555` ;
- **B Tylezia** : `108.128.247.72:5555` et
  `54.77.100.255:5555` ;
- **A2 Draconiros** : `108.128.247.72:5555`,
  `52.30.61.61:5555`, `54.77.100.255:5555` et
  `18.202.11.172:5555`.

D'autres canaux `443`, `6337` et loopback ont été
observés ; leur rôle dans la sélection reste **inconnu**.
Les IP de jeu changent : une adresse seule ne peut pas
servir de clé permanente du serveur logique.

### Protocole de connexion trouvé publiquement

Deux sources de recherche indépendantes décrivent un
protocole Unity **Connection**, distinct du protocole **Game** :

- [LuaxY — connection/message.proto](https://github.com/LuaxY/dofus-unity-protocol-builder/blob/main/proto/connection/message.proto)
  : `Message.request=1`, `Request.selectServer=4`,
  `SelectServerRequest.server=1` (`int32`) ;
  la réponse contient `host`, `ports`, `token`.
- [Jondo — GameProtocol.proto](https://github.com/D3SignedSkin/3.x/blob/main/Jondo.Unity.Launcher/Protocol/GameProtocol.proto)
  : `GameMessage.auth=1`,
  `AuthenticationTicketMessage.selectedServer=4`,
  `SelectedServerSelection.serverId=1` (`int32`).
  Cet émulateur cible **Dofus Unity 3.6.4.3** et
  décrit Connection Server et Game Node en TCP/5555.

**Important :** dans les deux sources, la structure minimale
de sélection est par hasard le même chemin numérique
`root.f1.f4.f1`, mais l'enveloppe, les préambules et le
sens métier peuvent diverger. Ces sources ne prouvent pas
que le schéma est inchangé dans le **client actuellement
installé** ni que notre capture inclut réellement
le Connection Server.

## Défaut identifié dans le décodeur précédent

`CharacterCaptureFlowTracker` appelait :

```csharp
AnkamaAny? any = AnkamaFrameDecoder.FindAny(frame);
if (any is null)
    continue;
```

Il ignorait **toute trame protobuf ne contenant pas**
une enveloppe `type.ankama.com/...`. Les messages
de sélection décrits dans les deux schémas Connection
n'ont pas cette enveloppe. Notre recherche historique
n'était donc **pas adaptée à ce protocole**.

## Implémentation opt-in

Le mode `--connection-selection-study` :

- reçoit les **trames reconstituées TCP/5555** juste
  avant le filtre de messages du protocole de jeu ;
- ignore les trames possédant une enveloppe `AnkamaAny`
  (elles sont traitées par le pipeline Game habituel) ;
- examine uniquement les trames **client→serveur**,
  au maximum **192 trames par session, 8 Kio/trame**,
  24 sessions dans l'étude ;
- vérifie une structure stricte : racine protobuf
  avec un seul `f1` (message), sous-message ayant
  un `f1` texte non vide (langue ou UUID) et
  `f4` message, puis `f4.f1` varint positif ;
- conserve en RAM les **entiers candidats** à
  `serverId`, sans afficher leur valeur, sans
  jeton, UUID, login ni contenu d'authentification ;
- garde la séparation **par flux TCP, session et
  phase manuelle** A1/B/A2 ;
- exige une unique session **`ksc`+`ksb` clôturée**
  par phase pour valider le parcours du jeu ;
- compare les valeurs candidates par phase :
  motif A1=A2≠B, sans l'interpréter comme
  une preuve d'identification du serveur ;
- affiche l'IP:port de la connexion ayant porté
  un candidat, ce qui aide à distinguer
  Connection Server et Game Node.

La simple forme `f1.f4.f1` **n'est pas unique** au
protocole de connexion. Même si elle suit A-B-A,
la sémantique devra encore être vérifiée par
la liste des serveurs et l'association
`server_id` ↔ nom.

Aucun service métier, compte, serveur choisi
manuellement, overlay, profil, base de données
ou release n'est modifié.

## Procédure Windows

Depuis la racine du dépôt, avec Dofus fermé :

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --connection-selection-study
```

L'index Npcap constaté au dernier essai était **3** ;
relancer `--list` si la carte n'est plus la même.

Suivre les phases :

1. A1 : sur Draconiros, attendre le chargement
   en jeu, fermer complètement Dofus, puis Entrée ;
2. B : sur Tylezia, idem ;
3. A2 : revenir sur Draconiros, idem.

Une fenêtre de jeu à la fois. La capture
est lancée **avant chaque démarrage de Dofus**.

## Lecture de la sortie

Les lignes `p3m11=stage-...` comportent :

- `frames` : nombre de trames reconstituées ;
- `non-game-frames` : trames sans
  `type.ankama.com` (pas nécessairement
  toutes des messages Connection) ;
- `select-candidates` : occurrences respectant
  **la structure de sélection candidate** ;
- `distinct-values` : diversité de valeurs
  candidates dans cette phase ;
- `selector-session`, `selector-destination` :
  session TCP du candidat, quand univoque ;
- `truncated` : une limite du collecteur
  a été atteinte.

La ligne `p3m11=summary` est
`comparable=yes` **uniquement** quand chaque
phase a une connexion de jeu complète et
exactement **une** valeur candidate. Elle donne
les comparaisons A1/A2 et A1/B sans afficher
les valeurs numériques.

Si `select-candidates=0`, vérifier d'abord
`non-game-frames`. Il peut s'agir d'un
autre enmarquage de trames, d'une nouvelle
structure Protobuf, d'un canal TLS ou du
fait que le client choisit le serveur sans
réémettre ce message à chaque connexion.

## Suite

Si A1=A2≠B est observé : déchiffrer
**le sens structurel** grâce à la réponse
Connection/ServerList et aux identifiants
de serveur présents dans les données
publiques. Ne pas encore attribuer
automatiquement un serveur à un personnage.

Si aucun candidat : étude de l'enveloppe/framing
du Connection Server sur une session choisie,
sans collecte de tokens ou payloads persistés.

**Le serveur choisi par l'utilisateur reste
autoritaire dans BestCrush.**
