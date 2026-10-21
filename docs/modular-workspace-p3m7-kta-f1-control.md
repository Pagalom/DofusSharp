# P3m.7 — Contrôle ciblé de C→S:kta:f1 (personnage vs serveur)

## Résultat P3m.6 ayant déclenché l'étude

L'utilisateur a exécuté les trois phases A1 → B → A2
avec une capture réseau TCP/5555 :

- A1 : session jeu #2, `34.252.214.29:5555` ;
- B : session jeu #6, `54.77.100.255:5555` ;
- A2 : session jeu #10, `18.202.11.172:5555`.
- Chaque phase : une session `ksc` + `ksb` et clôture.
- Messages préchargement étudiés : 16, 29, 29 ;
  chemins numériques distincts : 23, 24, 24.
- `truncated=yes` aux trois phases :
  `coverage=partial`, donc une partie des messages/champs
  a été ignorée par les limites de l'étude générale.
- **Seul motif candidat :**
  `candidate-count=1 candidate-paths=[C-S:kta:f1]`.

La valeur du champ racine `f1` du message **client→serveur**
`kta` est **égale entre A1/A2 et différente en B** dans cet
essai. On ne connaît PAS sa sémantique. Les deux personnages
A et B étant distincts, cet essai ne permet pas de distinguer
un identifiant de personnage, de serveur, de sélection,
d'une association compte+serveur ou un autre entier stable.

La variation des IP pour un même serveur logique entre les
essais P3m.5 et P3m.6 confirme qu'**IP != ServerId**.

## Pourquoi P3m.7 ?

L'exploration non ciblée de P3m.6 couvre beaucoup de messages
avec un budget limité, donc le diagnostic signalait
`coverage=partial`. P3m.7 lit **exclusivement le champ racine
varint f1 des messages C→S `kta`**, ce qui évite cette source
de troncature globale et permet de tester deux dimensions.

Quatre phases sur **un seul compte**, **une seule instance Dofus
à la fois** :

1. **A-X1** : serveur A, personnage X.
2. **A-Y** : **même serveur A**, personnage Y différent de X
   sur le **même compte**.
3. **B-Z** : serveur B, personnage Z sur ce **même compte**.
4. **A-X2** : serveur A, **retour au personnage X initial**.

Attendre le chargement complet du personnage et fermer
entièrement Dofus **avant Entrée** pour passer à la phase suivante.

Pour la capture, `ksc` et `ksb` dans une session TCP clôturée
indiquent le chargement du personnage. Le message `kta`
peut appartenir à la même session ou à une connexion TCP
d'amorçage au cours de la **même phase**, d'où la conservation
de leur phase à l'ouverture du flux.

## Commandes Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --kta-f1-control
```

L'interface **6** est l'interface Realtek Ethernet utilisée
dans les derniers essais ; revérifier `--list` au besoin.

## Résultats interprétables

La sortie `p3m7=phase-...` affiche pour chaque étape :

- nombre de sessions TCP ;
- nombre de sessions de jeu complètes (`ksc`, `ksb`, CLOSED) ;
- quantité de messages `kta` C→S ;
- nombre de valeurs distinctes du candidat `f1` dans la phase ;
- destination IP:port de la session TCP de jeu reconnue.

La sortie `p3m7=summary` compare :

- `reconnect-X` : même personnage X après reconnexion ;
- `same-server-X-vs-Y` : deux personnages différents
  sur le même serveur A et le même compte ;
- `cross-server-X-vs-Z` : deux personnages différents
  sur des serveurs A et B du même compte.

Valeurs `equal` ou `different`. Hypothèses :

- `server-or-account-server-candidate` : X=X2, X=Y,
  X≠Z. La valeur ne semble pas être simplement une
  constante propre au personnage. **Impossible à ce stade
  de séparer serveur logique d'association compte+serveur**.
- `character-or-selection-candidate` : X=X2, X≠Y.
  Le champ n'est pas une valeur universelle propre au
  serveur A pour les deux personnages.
- `account-wide-or-constant-candidate` : X=X2, X=Y, X=Z.
- `unstable-candidate` : X≠X2.
- `unavailable` : trop de sessions, absence de `kta`,
  valeur ambiguë, `ksc`/`ksb` absent ou connexion non
  terminée. **Ne jamais attribuer arbitrairement**.

**Toutes** les catégories restent `semantic=unverified
server=unverified` et ne constituent pas la découverte
officielle de `ServerId`.

## Limites de sécurité et d'architecture

- Une seule connexion complète `ksc` + `ksb` clôturée
  par phase. Les autres connexions du chargement sont
  comptées sans alimenter la fiche d'un personnage.
- `kta` ne doit provenir que du client et avant `ksb`
  de la session TCP correspondante. Les messages contenant
  un champ `f1` dupliqué ou d'un mauvais wire type sont
  invalidés pour cette phase.
- Analyse **uniquement** de `kta.f1` en varint, racine,
  jusqu'à 1 Kio/corps, 64 champs, 24 messages kta/session
  et 32 sessions TCP de cette exécution.
- Les valeurs `f1` sont conservées en RAM pour les
  comparaisons ; elles ne sont pas affichées : tant que
  la sémantique n'est pas reconnue, il pourrait s'agir
  d'un ID utilisateur, d'un handle ou d'une autre donnée
  personnelle et non du serveur.
- Les IP et ports des connexions de jeu sont affichés
  pour le diagnostic technique comme demandé.
- Aucune capture brute persistée, aucun enregistrement
  de profil, aucune modification aux overlays, à
  `CurrentCharacterState` ou aux services métiers.
- Tests synthétiques ajoutés pour motifs de comparaisons,
  ambiguïtés, directions TCP, invalidations et intégration
  avec le tracker. Ils doivent être exécutés sur Windows.

## Étape après contrôle

Si `same-server-X-vs-Y=equal` et
`cross-server-X-vs-Z=different`, faire plus tard
un contrôle **autre compte, même serveur** pour départager
serveur universel et contexte compte+serveur.

Ne pas activer d'association production avant
validation sémantique indépendante et cas multicomptes.
