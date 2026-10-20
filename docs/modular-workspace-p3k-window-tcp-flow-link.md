# P3k — Liaison exacte fenêtres Windows et flux Npcap

## Contexte

P3i/P3j ont montré, avec deux clients Dofus connectés, deux
fenêtres HWND/PID distinctes, des titres évolutifs, un focus correct et
un socket TCP établi vers le port 5555 pour chaque processus.
Ces éléments ne prouvent pas encore quel flux Npcap appartient à
chaque fenêtre.

## Diagnostic (opt-in, indépendant du protocole du jeu)

~~~powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --window-flow-link
~~~

Lancer avec **deux clients connectés**. Ouvrir l'inventaire ou effectuer
une action réseau inoffensive sur chacun, puis arrêter avec Ctrl+C.

L'outil compare exclusivement en mémoire :

1. Les quatre champs d'une connexion TCP vus par Npcap, en
   normalisant le sens des paquets S→C et C→S.
2. Le quadruplet IPv4 dans la table TCP Windows,
   lu par GetExtendedTcpTable, avec le PID propriétaire.
3. Le PID du processus propriétaire et les fenêtres Dofus visibles,
   identifiées par EnumWindows/GetWindowThreadProcessId.

Le lien est accepté seulement si le quadruplet TCP correspond à
**un seul socket Windows** et le PID à **une seule fenêtre Dofus**.
Un couple de clients est candidate lorsque deux flux distincts se
rattachent à deux fenêtres différentes. Une correspondance de simple
port, de nom, de processus ou de fenêtre active est insuffisante.

### Exemple fictif de sortie, sans données personnelles

~~~text
p3k=flow flow-index=1 status=matched window-index=1 account=unverified server=unverified
p3k=flow flow-index=2 status=matched window-index=2 account=unverified server=unverified
p3k=summary windows=2 sockets-5555=2 flows-seen=2 flow-matched=2 unique-windows-linked=2 status=two-client-links-candidate account=unverified server=unverified
~~~

Les lignes peuvent aussi signaler no-unique-socket,
no-unique-window ou not-confirmed : il ne faut pas forcer le lien
dans ces cas.

### Confidentialité et limites

- Aucun paquet, titre de fenêtre, adresse IP, port local, PID, HWND,
  nom de personnage ou identifiant de compte n'est imprimé, hashé,
  persisté ou recopié dans les tests.
- Les données de socket restent uniquement dans le processus
  du diagnostic. Le programme ne modifie aucun profil ou base.
- L'implémentation interroge la table IPv4 **ESTABLISHED** :
  un trafic IPv6, un socket déconnecté ou une capture sans activité
  peut laisser le statut indéterminé.
- Une session TCP liée à une fenêtre n'est PAS une identification
  du compte Ankama ou du serveur du personnage.
- Le client observé est désormais Dofus 3.7.12.12 ; les validations
  antérieures de champs métiers sur 3.6.11.15 ne s'appliquent pas
  automatiquement à la nouvelle version.

## Validation Windows

~~~powershell
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
~~~

Six tests synthétiques couvrent les deux sens d'un même flux, la
recherche du quadruplet exact et le refus en cas d'absence,
collision ou PID présent dans plusieurs fenêtres. Les API natives
seront vérifiées par la campagne Windows.

Aucune modification de main, release, migration, mapping de métier,
carte de personnage, ou attribution réseau de production. Le
sélecteur multiclient reste une prochaine étape conditionnelle
à la validation réelle du diagnostic.


## Observation réelle P3k — deux clients, 20 octobre 2026

Le mode `--window-flow-link` a été exécuté avec deux clients
Dofus visibles et chacun une connexion TCP/5555.

La sortie anonymisée montre :

```text
p3k=flow flow-index=1 status=matched window-index=2 account=unverified server=unverified
p3k=flow flow-index=2 status=matched window-index=1 account=unverified server=unverified
p3k=summary windows=2 sockets-5555=2 flows-seen=2 flow-matched=2 unique-windows-linked=2 status=two-client-links-candidate account=unverified server=unverified
```


**P3k validé pour ce scénario réel à deux clients :** chaque flux
observé est lié à un socket Windows unique puis à une fenêtre
différente. Cette validation concerne le lien réseau local
et ne valide ni un identifiant de compte, ni le serveur, ni l'usage
des professions sur le client Dofus 3.7.12.12.
La compilation du probe est attestée par son exécution;
le résultat de la suite de tests complète n'a pas été fourni ici.
