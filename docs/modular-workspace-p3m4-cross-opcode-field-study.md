# P3m.4 — Recherche exploratoire d'une troisième famille d'indices f3

## Point de départ confirmé sur le poste Windows

La capture `--identity-pair` du 21/10/2026 a produit
`p3m3=pair-complete f3=distinct semantic=unverified`
pour **deux personnages distincts du même compte et du même serveur,
selon l'observation manuelle de l'opérateur**.

Le candidat `parent.f3` est égal entre `ksc` et `ksb`,
stable pour le même personnage après reconnexion, et distinct
entre deux personnages testés. Son sens officiel demeure non prouvé.
`ksc` et `ksb` exploitent des structures de personnage proches :
leur égalité ne constitue PAS nécessairement deux preuves sémantiques
indépendantes.

## Objectif limité du diagnostic `--identity-cross-opcodes`

Chercher si la **valeur numérique** du candidat f3 lié à la
référence locale `Nom:Niveau` apparaît à un **autre chemin de champ**
dans une **autre famille de message du serveur**.

Les opcodes examinés sont délibérément bornés :
`krg,kes,jev,jgi,knc,kod,ifg,kmz,ica,irl`.
Les messages `ksc` et `ksb` ne sont utilisés que pour lier
la référence locale au candidat de la session TCP ; ils ne sont
**pas** traités comme troisième famille.

Un simple nombre égal **ne prouve ni que ce champ désigne le
personnage, ni l'identité du serveur**. Il s'agit d'un indice de
structure à tester ensuite par variation contrôlée entre personnages
et entre connexions.

## Règles de confidentialité et d'isolation

- Référence `Nom:Niveau` saisie localement, sans affichage des
  noms/valeurs réseau dans les résultats.
- RAM seulement : jusqu'à **8 sessions TCP**, **96 messages**
  ciblés/session, **48 varints** par message, corps **<=8192 octets**,
  imbrication de **3 niveaux** ; aucun corps conservé après analyse.
- Résultats anonymes : opcode, chemins numériques protobuf,
  numéros de sessions locales, nombre d'observations, statut.
  Aucun ID, hash, adresse IP, port, nom, niveau ni payload.
- `ksc` et `ksb` doivent être observés **dans la même session**,
  tous deux correspondant uniquement à la référence
  `Nom:Niveau`, avec `parent.f3` non nul et identique.
- Si une contradiction est observée, la session est
  `linked=no` et ne fournit aucun chemin candidat.
- Les paquets client→serveur sont exclus. Les valeurs de messages
  d'autres sessions ne sont jamais rattachées à la session liée.
- Aucun changement à `CurrentCharacterState`, au serveur choisi
  dans l'UI, aux métiers/inventaire, au stockage ou à la capture métier.

## Manipulation Windows

Sur le poste testé, **l'interface Ethernet porte maintenant
l'index 6** ; le numéro peut changer. Vérifier `--list`
si nécessaire.

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj

dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --identity-cross-opcodes
```

1. Fermer complètement tous les clients Dofus.
2. Lancer le probe et saisir la référence locale `Nom:Niveau`
   d'un seul personnage.
3. Se connecter au personnage **depuis le début de la connexion**,
   attendre le message `ksc`, puis `ksb` avec `linked=yes`.
4. Fermer totalement le client, le rouvrir, reconnecter
   **le même personnage**, sans fermer le probe.
5. Arrêter avec `Ctrl+C`; recopier uniquement les lignes
   `p3m4=` (sans la référence locale saisie).

L'essai a besoin de plusieurs sessions TCP car un chemin présent
dans deux reconnexions serait plus intéressant qu'une coïncidence
isolée, **sans suffire à prouver sa sémantique**.

Exemple **fictif** :

```text
p3m4=session=2 linked=yes observed=3 candidate-paths=[krg:f5.f3] meaning=unverified identity=unverified server=unverified
p3m4=session=4 linked=yes observed=4 candidate-paths=[krg:f5.f3] meaning=unverified identity=unverified server=unverified
p3m4=summary linked-sessions=2 cross-session-f3=equal meaning=unverified server=unverified
```

`candidate-paths=[none]` est un résultat valide :
cela ne prouve pas que f3 n'apparaît nulle part dans le protocole
(les opcodes, tailles et profondeurs examinés sont limités).
Les champs numériques trouvés pourraient être des constantes,
des métadonnées sans lien avec le personnage ou des coïncidences.

## Preuve de serveur — étape distincte non réalisée

Le nom de serveur sélectionné dans BestCrush est déclaré par
l'utilisateur et ne constitue pas une preuve réseau. Une IP de
destination, un PID de client, un HWND ou l'association d'un flux TCP
à sa fenêtre **ne prouvent pas à eux seuls** le serveur Dofus logique.
Aucun opcode/chemin de sélection de serveur n'a encore été validé.

Après l'étude des autres familles, caractériser séparément les
messages de sélection/confirmation de serveur au moyen de
transitions contrôlées serveur A → B → A, de préférence sur le
même compte, **sans afficher de jetons, d'ID de compte ni de noms**.
Aucune association personnage→serveur n'est activée en production
avant validation indépendante du serveur et de la sémantique f3.

Ne pas faire de merge dans main, ne pas publier de release ni
écrire de profils persistants à cette étape.
