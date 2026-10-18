# P2f — Décodeur expérimental d'identité (3.6.11.15)

## Point de départ — checkpoint Windows

Suite globale vérifiée par l'opérateur avant ce lot :
**579 tests réussis, 0 échec, 0 ignoré**, durée 6,4 s ;
compilation réussie avec deux avertissements non caractérisés.
Ce résultat est le **checkpoint antérieur à P2f**, il
n'atteste PAS des tests créés par P2f.

P2e a corroboré sur un vrai compte possédant deux personnages
du même serveur :
- `ksc` contient deux entrées avec noms et `f3` distincts ;
- chaque entrée sélectionnée dans `ksc` a concordé avec
  `ksb` lors du chargement de ce personnage ;
- `ksb.f1.f1.f3` est stable sur deux connexions successives
  du même personnage.

Ces observations rendent l'hypothèse d'un ID de personnage
individuel fortement probable, **mais non formellement
confirmée au sens de la politique d'attribution BestCrush**.

## Implémentation du lot

Nouveau `CharacterWireCandidateDecoder` dans
`BestCrush.Network/Protocol/` :

- `ksb` : un seul candidat, chemin
  `f1.f1.f3` (ID candidat), `f1.f1.f4.f6` (nom),
  `f1.f1.f4.f3` (niveau) ;
- `ksc` : une liste de 1 à 16 candidats, champs
  `f2.f3` (ID candidat), `f2.f4.f6` (nom),
  `f2.f4.f3` (niveau) ;
- contrôle de structure strict (corps ≤ 8192 octets,
  nombre de champs borné, valeurs absentes ou répétées
  refusées, UTF-8 strict, IDs positifs, niveau 1–200) ;
- refus des noms ou IDs en doublon dans la liste `ksc` ;
- `ClassId=null` : aucun champ de classe n'est encore validé ;
- `IdentityConfirmed=false` dans tous les snapshots ;
- `ToString()` expurgé des objets sensibles ;
- aucune écriture, collecte d'XP ou intégration en production.

Le schéma expérimental est :
`BestCrush.Network/Protocol/character-identity-candidate.3.6.11.15.json`.
Il ne contient aucune valeur personnelle, et ne configure pas
les décodeurs en production.

## Diagnostic réel sans données privées

Le mode existant `--identity-fields` est prolongé pour
tester **le nouveau décodeur** sur les vrais messages,
sans nouvelle option CLI. À côté des résultats P2b/P2c,
le terminal affiche :

- `p2f=decoded candidates=N class=unknown
  candidate-link=pending identity=unverified` ;
- lorsque `ksb` et `ksc` de la **même connexion TCP**
  contiennent le même ID candidat, nom et niveau :
  `candidate-link=yes` ;
- si les candidats ne concordent pas : `candidate-link=no` ;
- si la structure réelle est différente :
  `p2f=layout-rejected identity=unverified`.

Jamais de nom, d'ID, de niveau, de payload ou de hash
dans la sortie, ni dans les documents du dépôt.
Les snapshots P2f sont conservés seulement en RAM dans la
session TCP et supprimés au FIN/RST, à la réinitialisation
de connexion ou en cas de contradiction.

**Important :** `candidate-link=yes` n'est qu'une
**concordance de fils protobuf**, pas deux preuves
sémantiquement indépendantes du personnage actif.
`ksc` peut être une liste de sélection et `ksb`
un état résultant du choix ; on ne doit pas les
compter automatiquement comme deux preuves indépendantes.

## Checkpoint Windows demandé

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.Network\BestCrush.Network.csproj
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
```

Saisir localement le nom exact/niveau du personnage, puis
se connecter, revenir à la sélection, et reconnecter ce
même personnage. Ne communiquer que les lignes
`IDENTITY_FIELDS`, **pas la ligne de saisie**.

## Ce qui reste bloqué

- Valider la source indépendante d'identité, la classe et
  le rôle de chaque message ;
- propager clé TCP/session et invalidations au routage
  métier de production ;
- préserver l'isolation multi-clients et la vérification du
  serveur réellement connecté ;
- brancher `CharacterIdentityCorrelator` seulement
  après ces preuves et le checkpoint Windows de ce lot ;
- ne pas attribuer `ipz` tant que l'identité n'est pas
  confirmée, ne pas persister les personnages.
