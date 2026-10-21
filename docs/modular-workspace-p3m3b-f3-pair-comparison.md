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

## Résultat du comparatif réel — 21/10/2026

**P3m.3b validé sur le poste Windows, capture Dofus 3.7.12.12.**

Après correction de l'index Npcap (l'ancienne interface réseau
Ethernet passe de l'index 3 à **6**, mais conserve le même GUID), le
probe `--identity-pair` a observé **deux références locales ayant le
même nom et des niveaux différents** :

- Première référence : `ksc` puis `ksb`, session TCP **#2**,
  statut `confirmed=1/2`.
- Seconde référence : `ksc` puis `ksb`, session TCP **#4**,
  statut final `p3m3=pair-complete f3=distinct semantic=unverified`.
- Total : **399 paquets filtrés**, **338 messages décodés**,
  **2 ksc**, **2 ksb**, **0 erreur de capture**.
- La capture antérieure P2b du **même personnage** a déjà obtenu
  `cross-session-f3=equal reference-sessions=2` après fermeture puis
  reconnexion.

**Portée confirmée :** le candidat `parent.f3` est stable au cours
de deux connexions du même personnage (test antérieur), identique
entre ses messages `ksc` et `ksb` dans chacune des captures,
et différent entre deux personnages homonymes de niveaux distincts
dans l'essai P3m.3b.

**Portée non confirmée :** ces observations ne prouvent pas encore
que f3 est le **CharacterId** sémantique : la relation entre les
deux personnages et les **comptes/serveurs** n'a pas été établie dans
le diagnostic ; la classe, l'account ID et le serveur officiel restent
non vérifiés. Le statut `semantic=unverified` doit être conservé.
Aucune attribution de profil en production n'est activée.

## Second essai — deux personnages du même compte et du même serveur

L'opérateur indique avoir suivi le protocole de comparaison de **deux
personnages distincts d'un même compte et d'un même serveur** ; le probe
ne dispose pas encore d'une preuve réseau indépendante du compte ou du
serveur. Les noms, niveaux et valeurs candidates ne sont pas repris ici.

Observations transmises (21/10/2026, interface Npcap Ethernet GUID
`545E92A4-567E-4539-88A6-39BE852836A7`, index courant 6) :

- A : connexion TCP **#2**, `ksc` (173 octets) puis
  `ksb` (162 octets), `confirmed=1/2` ;
- B : connexion TCP **#4**, `ksc` (173 octets) puis
  `ksb` (137 octets),
  `p3m3=pair-complete f3=distinct semantic=unverified` ;
- résumé final : `filtered-packets=350`,
  `payload-packets=225`, `decoded=313`, `ksc=2`, `ksb=2`,
  `errors=0`.

**Conclusion expérimentale :** sous la condition déclarée et
contrôlée manuellement « même compte / même serveur », f3 diffère
entre deux personnages. Cela contredit l'hypothèse d'une simple
valeur f3 constante par compte ou par serveur **dans cette observation**,
et renforce l'interprétation d'un identifiant propre au personnage.
Ce n'est **pas** une preuve du nom sémantique officiel du champ et
aucune identité compte/serveur n'a été vérifiée par le protocole.

### Étape suivante

Avant de connecter `parent.f3` à `CurrentCharacterState` en
production, prouver le **champ serveur** (en particulier en
multicompte/multiserveur) et corroborer f3 avec une autre famille
de messages sémantiquement identifiée, ou une autre source de
preuve indépendante. Conserver le verrou `semantic=unverified`,
ne jamais créer/mettre à jour de fiche personnage sur une seule
référence `Nom:Niveau`, préserver les isolations par connexion,
et ne pas persister les profils au-delà de la session.

## Pourquoi un nouveau mode opt-in ?

Deux exécutions distinctes de `--identity-fields` ne peuvent
pas comparer les champs bruts entre personnages : les valeurs privées
sont volontairement supprimées des sorties. Un mode comparatif sûr
doit conserver les candidats **seulement en mémoire, pendant UNE même
exécution du probe**.

## Mode P3m.3b : `--identity-pair`

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --identity-pair
```


Saisir **deux références nom+niveau différentes**, avec leur niveau
actuel, uniquement dans les deux invites de la console locale.
Les personnages peuvent porter le **même nom** : dans ce cas,
leurs **niveaux doivent différer** pour être discriminables par
ce diagnostic. Si nom et niveau sont identiques, le mode refuse
la comparaison, même si les personnages jouent sur deux serveurs
différents. Il faudra alors une preuve de serveur et d'identifiant
de personnage validée indépendamment du nom :

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

- Le nom n'est **jamais un identifiant global** ; il peut être partagé
  par plusieurs personnages, particulièrement entre serveurs.
- Chaque référence doit être trouvée **exactement une fois**, par
  correspondance locale du nom ET du niveau. Une paire nom+niveau
  ne suffit pas non plus à prouver l'identité en production.
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
Onze nouveaux tests synthétiques examinent notamment : f3 différent
ou égal, concordance ksc/ksb, famille manquante, contradiction,
changement de valeur pour un même personnage, deux références
partageant une session, confidentialité et intégration avec le
tracker TCP.
