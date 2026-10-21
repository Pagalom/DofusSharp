# P3m.3c — Observabilité du diagnostic --identity-pair

## Pourquoi ce correctif

Sur un essai réel, le mode --identity-pair n'a émis **aucune** ligne
OPENED, ksb ou ksc après la saisie des deux références locales.
Aucun résultat f3 n'a donc été produit. Le terminal est revenu à
PowerShell après la fin du probe.

L'ancienne sortie ne permettait pas de différencier les hypothèses :
- aucune trame arrivée depuis l'interface Npcap choisie ;
- une connexion ouverte sur un autre port ou une autre interface ;
- des paquets TCP reçus mais sans payload ;
- des trames TCP avec payload sans frame Ankama décodable ;
- des messages décodés, mais sans ksc ;
- des messages ksc/ksb présents, sans correspondance nom+niveau.

**Le résultat ne signifie pas que f3 est invalide.**

## Ce qui change

Uniquement pour --identity-pair :
- une confirmation explicite de démarrage de capture ;
- un état périodique toutes les 10 secondes, pendant que le diagnostic tourne ;
- un état final sur Ctrl+C ;
- compteurs anonymes :
  filtered-packets, tcp, payload-packets, opened, decoded, ksc, ksb, errors ;
- nombre **agrégé et instantané** de connexions OS IPv4 établies dont le
  **port distant** correspond au port configuré (par défaut 5555),
  sans détails d'IP, port local, PID ou titre de fenêtre.

Cette statistique OS est un indice, pas une vérification de propriétaire
de socket Dofus. Elle n'inclut pas les connexions IPv6. Si l'inventaire Windows
est indisponible, la valeur agrégée -1 indique cet état (et non
une absence de sockets).

Tous les autres modes de probe, les captures de production et la validation
des identités de personnage restent **inchangés**.

## Comment lire les lignes de diagnostic

Sortie synthétique (valeurs fictives) :

```text
p3m3=capture-status phase=live filtered-packets=0 tcp=0 payload-packets=0 opened=0 decoded=0 ksc=0 ksb=0 errors=0 os-ipv4-remote-port-sockets=1 diagnostic=capture-interface-or-filter-check identity=unverified
```


- `filtered-packets=0` + sockets OS > 0 :
  contrôler l'interface Npcap et le filtre capture, pas les références
  d'identité.
- `filtered-packets=0` + sockets OS = 0 :
  la connexion TCP attendue n'est pas visible à cet instant sur ce
  port IPv4. Cela peut venir du démarrage, du port, du jeu ou d'IPv6.
- `tcp>0 payload-packets=0` : seules des trames de contrôle
  TCP ont été observées.
- `payload-packets>0 decoded=0` : données TCP reçues mais
  aucun message Ankama extrait ; examiner le démarrage complet et la
  reconstruction du flux.
- `decoded>0 ksb>0 ksc=0` : l'étape ksc n'a pas été
  observée, ne pas attribuer de valeur f3.
- `decoded>0 ksc>0 ksb>0` : le problème se situe au niveau
  de la concordance nom+niveau / session TCP / parent f3, à étudier
  à partir des seuls statuts anonymisés.

## Procédure sur Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-pair
```


Après saisie locale des deux références, observer au moins un bilan
`phase=live` **pendant que Dofus est réellement connecté**.
Pour ce test, ouvrir puis quitter Dofus après l'émission d'un état
intermédiaire, et transmettre uniquement les lignes
`p3m3=capture-status` anonymisées et les statuts p3m3.
Si aucune trame n'arrive, ne pas répéter l'étude des champs d'identité :
commencer par vérifier l'interface Npcap et le port.

Neuf tests synthétiques vérifient les différentes branches du diagnostic,
sa rétention uniquement sous forme de compteurs et l'absence d'inférence
d'identité à partir d'une paire ksc/ksb.

Aucune modification de main, aucune release, aucun profil persistant.
