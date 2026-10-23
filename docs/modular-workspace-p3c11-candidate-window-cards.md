# P3c.11 — Cartes candidates éphémères (23 octobre 2026)

## Point de départ

P3c.10 validé avec **1086/1086 tests Windows** (0 échec, 7,5 s) et deux clients Dofus réels : `linked-windows=2 equal-roster-pairs=0 disjoint-roster-pairs=1`, puis reconnexion de B confirmée. Ni compte Ankama ni serveur réseau authentifié par ces observations.

## Comportement introduit

Le singleton `CurrentCandidateWindowCardsState` reste **inactif par défaut**. Il est activé uniquement en compilation **Debug**, avec `BESTCRUSH_P3C10_ROSTER_STUDY=1`, dans le service qui reçoit déjà `ksc/ksb` et valide le lien local TCP/PID/HWND.

Les cartes publiées à Blazor ne comportent que **la fenêtre locale (HWND/PID)** et **le nombre de personnages candidats dans le roster**. Aucun ID de personnage, nom issu du réseau, token, classe candidate ou groupe de compte n'est exposé. Les étiquettes sont lues dans le **titre de fenêtre Windows**, sans les considérer comme preuve d'identité réseau.

La page **Personnages** présente une section séparée et explicitement « expérimentale / compte non vérifié » avec une carte par fenêtre liée et un bouton pour cibler la **fenêtre**, pas le compte. Les portraits existants affichent un **point ambre en bas à gauche** pour le roster candidat ; le **point vert en haut à droite** conserve sa signification de lien TCP. Ces éléments ne sont visibles en lien avec les groupes candidats qu'en opt-in Debug.

La boucle `MainLayout.PollDofusClientsAsync` **toutes les secondes**, indépendante du trafic réseau, évince la carte d'une fenêtre fermée, y compris si aucun paquet supplémentaire n'arrive. Les flux candidats en attente sont supprimés eux aussi. Une clé HWND/PID disparue est mise en quarantaine pour l'époque de capture ; après reconnexion de B sur une nouvelle fenêtre, un nouveau `ksc/ksb` est obligatoire. La carte A reste inchangée.

Changement de serveur **manuellement choisi**, redémarrage Npcap ou fermeture de BestCrush : effacement des candidats en RAM. Aucun profil, métier, donnée d'inventaire, valeur économique, SQLite, préférence, historique, `CurrentCharacterState`, `CurrentAccountProfessionsState` ou `VerifiedAccountServerKey` n'est modifié. Pas de validation de serveur forcée.

## Tests nouveaux et validation

**10 nouveaux tests ciblés** dans `CurrentCandidateWindowCardsStateTest.cs` : disabled par défaut, liaison seulement après `ksc/ksb`, deux fenêtres isolées, fermeture B **sans nouveau paquet**, reconnexion et réemploi de HWND refusé, fermeture d'un `ksc` en attente, nouveau serveur, redémarrage de capture, non-divulgation de valeurs sensibles et absence de sélection implicite du personnage.

**1096 tests attendus**, non encore validés par le poste Windows (1086 confirmés précédemment + 10).

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Après validation, fermer BestCrush et les clients Dofus, puis :

```powershell
$env:BESTCRUSH_P3C10_ROSTER_STUDY = "1"
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

1. Sélectionner manuellement le serveur utilisé précédemment.
2. Ouvrir A-X, puis B-Z et conserver A ouvert. Sur « Personnages », deux cartes doivent apparaître, avec **2** et **1** membres candidats dans le scénario observé.
3. **Fermer B sans provoquer de nouveau trafic réseau** : sa carte doit disparaître au prochain relevé Windows (~1 seconde), tandis que A reste.
4. Rouvrir B-Z : la carte de B doit réapparaître après nouveau `ksc/ksb`.

Serilog peut écrire dans `%LOCALAPPDATA%\BestCrush\Logs\bestcrushYYYYMMDD.log` plutôt qu'afficher dans PowerShell. Pour analyse, ne transmettre que les lignes `p3c10=`, les résultats build/test et une description de l'interface, jamais les journaux complets, PCAP, noms, IP/PID/HWND ou jetons.

Après l'essai : `Remove-Item Env:BESTCRUSH_P3C10_ROSTER_STUDY`.

**Les cartes sont une vue expérimentale de fenêtres et de candidats, pas des comptes Ankama authentifiés ; aucun métier ne doit y être rattaché.**
