# P3m.6 — Comparaison des champs numériques de connexion A → B → A

## Situation au départ

- P3m.3 : le candidat `parent.f3` de `ksc/ksb` est
  stable après reconnexion d'un même personnage et différent
  entre deux personnages du même compte et du même serveur
  selon le contrôle manuel de l'opérateur. `semantic=unverified`.
- P3m.4 : deux connexions du même personnage ont conservé `f3`,
  mais les **8 messages ciblés par session** n'ont révélé
  aucun autre chemin numérique égal à `f3`.
- P3m.5 : lors d'un essai A → B → A,
  le couple A1/A2 a emprunté `34.252.214.29:5555`
  et B a emprunté `54.77.100.255:5555`.
  Un autre essai avait produit des destinations différentes
  pour A1 et A2. **L'adresse réseau n'est pas une preuve
  du serveur logique.**

## Résultat réel P3m.6 (capture A → B → A)

- TCP jeu A1 : session **#2**, `34.252.214.29:5555`.
- TCP jeu B : session **#6**, `54.77.100.255:5555`.
- TCP jeu A2 : session **#10**, `18.202.11.172:5555`.
- `completed-game=1` pour chaque phase ; observation complète
  des connexions de jeu, mais `truncated=yes` pour chaque
  phase dans la **recherche numérique générale**.
- `early-messages=16/29/29` et
  `numeric-paths=23/24/24` pour A1/B/A2.
- **Seul chemin candidat : `C-S:kta:f1`**,
  `candidate-count=1 coverage=partial
  semantic=unverified server=unverified`.

Il s'agit du champ numérique racine `f1` du message
**client → serveur** de type `kta`, pas d'une preuve
de la signification de cette valeur. La condition
A1 = A2 ≠ B peut découler du choix de personnage,
du serveur, du compte ou d'autres éléments.

**Décision** : ne pas relancer l'exploration générale
pour l'instant. Nouvelle étude **P3m.7** uniquement
sur `kta:f1`, avec changement de personnage **à serveur
constant**, puis changement de serveur. Voir
`docs/modular-workspace-p3m7-kta-f1-control.md`.

## Hypothèse expérimentale et portée

Chercher, parmi les premiers messages sur **TCP/5555**,
un chemin numérique (`direction:opcode:fN.fM...`) qui :

1. est présent dans les phases A1, B et A2 ;
2. n'a qu'une seule valeur distincte par phase ;
3. possède la **même valeur** en A1 et A2 ;
4. possède une **valeur différente** en B.

Cette condition est **nécessaire mais très insuffisante**
pour identifier un champ serveur. Un identifiant de personnage,
de compte, de sélection, de serveur ou une simple constante
conditionnelle peuvent imiter exactement le motif recherché reste **A1 = A2 et A1 ≠ B**.

**Contrôle de confusion essentiel :** lorsque le personnage
sur A1 et A2 est le même et que celui de B est différent,
tout identifiant de personnage suit aussi ce motif. Ce mode
ne valide donc **jamais** la sémantique `ServerId`.
Il établit seulement une liste limitée de **chemins candidats**
à tester ensuite sur plusieurs personnages et comptes.

Le filtre est toujours `tcp port 5555`. Le choix du serveur
peut être négocié via un autre service, une connexion TLS
ou un autre port, hors du périmètre de cette capture. Si
aucun chemin ne correspond, cela ne réfute pas l'existence
d'un identifiant de serveur dans Dofus.

## Implémentation

Le mode `--server-wire-study` réutilise le suivi TCP
et les phases manuelles de P3m.5 :

- une instance Dofus à la fois ;
- comparaison sur les trois phases A1 → B → A2 ;
- **exactement une connexion TCP par phase** avec
  `ksc` puis `ksb` et une clôture effective
  (plusieurs autres connexions d'amorçage sont tolérées) ;
- examine les messages décodés **dans les deux directions**,
  depuis leur apparition et **avant `ksb`** de leur connexion ;
- n'interprète **jamais** `ksc`, `ksb`, `ipz` comme des
  champs serveur, puisqu'ils portent notamment des éléments
  de personnages/métiers ;
- recueille temporairement les champs numériques protobuf
  `varint` à profondeur maximale 3 ;
- conserve par session 256 messages au maximum,
  64 champs numériques par message, avec corps de 8192 octets
  maximum et champs imbriqués de 2048 octets maximum ;
- borne à **24 sessions TCP**. En cas de dépassement
  de ce nombre, pas de comparaison ;
- les autres données ignorées par les limites sont indiquées
  par `coverage=partial` et `truncated=yes` ;
- n'imprime **aucune valeur numérique** des messages,
  aucun identifiant de compte, ni aucun payload ou jeton ;
- garde les valeurs comparées uniquement **en RAM** ;
  affiche la destination IP:port de la connexion de jeu
  dans le bilan P3m.5, comme convenu avec l'opérateur ;
- ne modifie aucun service métier, la sélection de serveur,
  les fiches, la base, la capture de production ni les overlays.

La recherche n'est pas exhaustive : un message peut être
ignoré si sa structure protobuf n'est pas reconnue
ou si son corps dépasse la borne.

## Manipulation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-wire-study
```

L'index Npcap **6** est l'interface Realtek de référence
sur ce poste au dernier essai. Vérifier `--list` si besoin.

1. Fermer toutes les fenêtres Dofus **avant** de lancer
   le diagnostic.
2. **A1 :** sur le même compte, connecter un personnage
   du serveur A, attendre l'arrivée en jeu,
   fermer le client, puis appuyer sur Entrée.
3. **B :** sur ce compte, connecter un personnage
   du serveur B, fermer le client, puis Entrée.
4. **A2 :** se reconnecter au **même personnage**
   que A1 sur le serveur A, fermer le client, puis Entrée.
5. Transmettre la sortie de `p3m5=` et `p3m6=`
   ainsi que les erreurs éventuelles de build/tests.
   Aucune valeur `Nom:Niveau` n'est demandée.

Résultat fictif (sans valeur serveur exposée) :

```text
p3m6=stage-A1 tcp-sessions=2 completed-game=1 early-messages=21 numeric-paths=17 truncated=no server=unverified
p3m6=stage-B tcp-sessions=4 completed-game=1 early-messages=24 numeric-paths=19 truncated=no server=unverified
p3m6=stage-A2 tcp-sessions=4 completed-game=1 early-messages=20 numeric-paths=17 truncated=no server=unverified
p3m6=summary comparable=yes candidate-count=1 candidate-paths=[C-S:abc:f3] output-truncated=no coverage=bounded semantic=unverified server=unverified
```

`candidate-paths=[none]` est une observation valide.
`coverage=partial` signale un échantillonnage borné :
ne pas déduire qu'il n'existe aucun autre chemin.

## Décision après résultat

1. Si un ou plusieurs chemins apparaissent, les soumettre
   à un contrôle **même serveur, personnages différents**
   afin d'écarter une valeur propre au personnage.
2. Répéter sur le serveur B ou un troisième serveur
   afin d'écarter une constante de connexion.
3. Rechercher si une famille de messages **déclare
   explicitement** la sélection/confirmation du serveur.
4. Sans ces contrôles, maintenir `server=unverified`
   et ne brancher aucune attribution automatique.
