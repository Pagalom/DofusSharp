# P3m.3b — Vérifier la distinction du candidat f3 entre deux personnages

## Observation déjà validée sur le poste Windows

Sur le client Dofus courant, la même référence locale nom+niveau
a été capturée deux fois dans **une seule exécution P2b** :

- 1re connexion : TCP #2, `ksc` + `ksb` ;
- 2e connexion : TCP #4, `ksc` + `ksb` ;
- 1er `ksb` : `cross-session-f3=baseline reference-sessions=1` ;
- 2e `ksb` : `cross-session-f3=equal reference-sessions=2` ;
- chaque connexion : `same-session=yes shared-equal-parent=[f3]`,
  `candidate-link=yes` ;
- classe et sémantique officielle de f3 : toujours **non validées**.

Il s'agit d'une observation de **stabilité du champ candidat pour une
référence locale**, non d'une preuve définitive de CharacterId.

## Pourquoi un nouveau mode opt-in ?

Deux exécutions distinctes de `--identity-fields` ne peuvent
pas comparer les champs bruts entre personnages : les valeurs privées
sont volontairement supprimées des sorties. Un mode comparatif sûr
doit conserver les candidats **seulement en mémoire, pendant UNE même
exécution du probe**.

## Mode P3m.3b : `--identity-pair`

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-pair
```


Saisir **deux noms de personnages différents**, avec leur niveau
actuel, uniquement dans les deux invites de la console locale :

```text
Référence personnage A : NomA:NiveauA
Référence personnage B : NomB:NiveauB
```


Ne pas copier les entrées elles-mêmes dans un compte rendu.

1. Lancer le probe avant de connecter le personnage A.
2. Connecter A dans un client Dofus, attendre les deux familles
   `ksc` et `ksb` (la console affichera un statut
   anonyme `confirmed=1/2`).
3. **Garder le probe ouvert.** Fermer totalement la fenêtre/instance
   Dofus de A pour terminer la connexion TCP.
4. Ouvrir le client du personnage B, puis connecter B. Attendre
   l'apparition de `ksc` et `ksb` dans un **autre numéro
   de session TCP**.
5. Terminer avec Ctrl+C seulement après l'apparition d'un résultat.

Résultat recherché :

```text
p3m3=pair-complete f3=distinct semantic=unverified
```


La sortie peut également indiquer
`f3=same`, `p3m3=conflicted`,
`p3m3=reference-pending` ou `p3m3=same-tcp-session`.
Ces cas **ne valident pas** la distinction. Ne pas forcer de
conclusion en leur présence.

### Conditions exactes d'une comparaison

- Chaque référence doit être trouvée **exactement une fois**, par
  correspondance locale du nom ET du niveau.
- Sur une même session TCP, la référence doit être retrouvée
  dans **deux familles différentes : ksc et ksb**.
- Dans les deux messages, le parent numérique f3 doit être
  non nul et **identique** ; sinon conflit.
- Le candidat doit rester stable pour une même référence.
- Les deux références doivent correspondre à **deux sessions TCP
  distinctes**.
- Les observations sont bornées à 16 sessions ; des cas incomplets
  sont simplement déclarés indéterminés.
- Aucune valeur f3, nom, niveau, adresse IP, port, HWND, PID ou
  capture n'est affiché, hashé, persisté ou envoyé au moteur métier.

### Limites

Ce test ne confirme **ni la sémantique officielle de f3 comme
CharacterId, ni l'identité du compte, ni le serveur, ni la classe**.
Il teste seulement la distinction d'un champ candidat sur deux
personnages reconnus localement.

Le mapping historique est marqué **3.6.11.15** ; les essais actuels
portent sur l'instance Dofus **3.7.12.12**. Ne pas confondre
le build du mapping de référence avec la version réellement lancée.

Aucune écriture de profil, inventaire, kamas ou profession et aucune
modification de la capture de production. Pas de merge dans main,
pas de release, pas de sauvegarde de profils sur disque.

## Checkpoint Windows préalable

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```


Ne lancer le nouvel essai que si le build et les tests réussissent.
Neuf nouveaux tests synthétiques examinent notamment : f3 différent
ou égal, concordance ksc/ksb, famille manquante, contradiction,
changement de valeur pour un même personnage, deux références
partageant une session, confidentialité et intégration avec le
tracker TCP.
