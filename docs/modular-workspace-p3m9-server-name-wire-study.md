# P3m.9 — Chercher le nom du serveur Dofus dans les messages texte

## Objectif

L'utilisateur propose de chercher directement **le nom du serveur
Dofus**, soit dans un message de sélection/connexion, soit dans
la liste des personnages. Jusqu'à présent, les études P3m.5/P3m.6
comparaient des **IP** ou des **champs numériques protobuf** :
elles ne recherchaient pas les chaînes de serveurs.

**Hypothèses ouvertes :**

- le nom exact du serveur est transmis dans une chaîne protobuf ;
- une liste de serveurs contient plusieurs noms et éventuellement
  des identifiants ; cette liste n'établit pas le serveur sélectionné ;
- la sélection transmet seulement un ID numérique, le nom étant
  résolu depuis un catalogue côté client ;
- l'échange déterminant a lieu avant le jeu, sur un port ou un
  protocole différent de TCP/5555, inaccessible à ce diagnostic.

Le service `ServersService` utilise déjà un **catalogue de noms
de serveurs** depuis Dofocus. Pour ce premier contrôle, le probe
demande directement deux noms à l'opérateur, sans dépendance
réseau supplémentaire ni découverte automatique.

## Périmètre technique

Nouveau mode de recherche isolé :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-name-study
```

- **L'opérateur saisit les noms exacts de deux serveurs A et B.**
- Scénario **A1 → B → A2**, avec **une seule fenêtre Dofus**
  et un seul compte à la fois.
- Le tracker TCP déjà présent examine **uniquement les messages
  Ankama décodés sur TCP/5555**, reçus ou envoyés
  **avant `ksb` dans chaque connexion**, en incluant
  les listes `ksc`, les éventuels préambules et les connexions
  d'amorçage qui précèdent le chargement.
- Détection de chaîne UTF-8 égale au nom (insensible à la casse)
  ou contenant ce nom comme mot séparé par des frontières
  non alphanumériques (par exemple texte JSON).
- Parcours des champs `length-delimited` protobuf, avec un
  maximum de 4 niveaux de profondeur ; les données qui ne
  se décodent pas en protobuf peuvent tout de même être
  comparées comme du texte UTF-8 strict.
- La sortie donne **direction, opcode, chemin protobuf,
  label A/B et mode exact/embedded**, pas les autres chaînes
  observées, ni les données d'authentification.
- Seules la **présence** et la **position** des deux noms
  connus sont enregistrées en RAM pendant l'essai.
- Les IP/ports des sessions de jeu restent affichés par
  P3m.5 pour le diagnostic, indépendamment des noms.
- Limites : 24 connexions, 192 messages décodés/connexion,
  8 Kio/message, 4 Kio/chaîne ou sous-message,
  256 champs/nœud, 96 chemins correspondants/connexion.
  Le dépassement entraîne `coverage=partial` ou,
  pour les connexions, `comparable=no`.

### Protocole Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 6 --server-name-study
```

Le périphérique **6** correspondait à l'Ethernet Realtek
sur le poste de référence ; revérifier `--list` si nécessaire.

1. **Fermer complètement Dofus** avant le lancement.
2. Saisir localement les noms de **serveur A** et **serveur B**.
   Il s'agit de noms publics, pas de secrets.
3. Phase A1 : ouvrir le client, choisir le serveur A, charger
   un personnage, fermer Dofus entièrement, puis **Entrée**
   dans la console.
4. Phase B : répéter avec le serveur B puis **Entrée**.
5. Phase A2 : revenir au serveur A, charger un personnage,
   fermer Dofus et **Entrée**.
6. Transmettre les lignes `p3m5=`, `p3m9=` et les éventuelles
   erreurs de compilation/tests.

### Lecture des résultats

Les lignes `p3m9=stage-...` donnent :

- `expected-name-seen=yes/no` : nom A ou B attendu
  pour cette phase, présent dans un champ étudié ;
- `other-name-seen=yes/no` : nom de l'autre serveur
  également trouvé ; un **catalogue commun** pourrait
  expliquer la présence des deux noms dans chaque phase ;
- `paths=[S-C:xxx:f2{name=A,mode=exact}]` : exemple
  **fictif** de chemin avec une correspondance exacte ;
- `scanned-messages=N` et `truncated=yes/no` :
  bornes et couverture du diagnostic.

Le résumé cherche les **chemins de sélection possibles**
pour lesquels, **au même chemin**, la phase A1 montre
seulement A, la phase B seulement B et la phase A2
seulement A. Cela ignore les chemins affichant les
deux serveurs dans toutes les phases. Le champ peut
tout de même provenir d'un caractère, du compte ou
d'un autre contexte.

`scoped-candidate-count=0` ou
`any-name-match=no` **ne démontre pas** que le jeu
ne transmet jamais le nom du serveur : le diagnostic
ne voit que les corps Ankama décodés sur TCP/5555,
et il coupe chaque connexion après `ksb`.

Un nom détecté en `ksc` ou `ksb` n'est pas
automatiquement le serveur du personnage ; il faudra
vérifier le sens du champ. **Aucune sortie n'a
`server=verified`.**

## Résultat réel P3m.9 — 22 octobre 2026

L'opérateur a déclaré A = **Draconiros** et B = **Tylezia**.
Le diagnostic `--server-name-study`, interface Npcap index 3
(même interface Ethernet dont l'index avait été 6), a suivi
trois phases A1 → B → A2. Une session de jeu complète
`ksc`+`ksb` terminée par phase.

| Phase | Connexions TCP | Messages décodés et étudiés | Correspondances texte | Destination jeu |
| --- | ---: | ---: | ---: | --- |
| A1 Draconiros | 4 | 32 | 0 | `34.252.214.29:5555` |
| B Tylezia | 5 | 32 | 0 | `34.243.47.91:5555` |
| A2 Draconiros | 4 | 32 | 0 | `34.252.214.29:5555` |

L'exécution n'a signalé aucune troncature de messages
(`truncated=no` pour les trois phases) ; l'analyse reste
**bornée** aux corps de messages Ankama décodés sur
**TCP/5555**, au parcours de connexion avant `ksb`,
avec les limites de profondeur, taille et de type UTF-8.

```text
p3m9=summary comparable=yes any-name-match=no scoped-candidate-count=0 scoped-candidate-paths=[none] output-truncated=no coverage=bounded semantic=unverified server=unverified
```

**Conclusion négative circonscrite** : aucun nom `Draconiros`
ou `Tylezia` n'a été rencontré dans le périmètre
effectivement scruté. Cela n'exclut pas un **ServerId**
numérique, un catalogue local, ni un échange distinct dans
le launcher ou sur un autre port/protocole.

**Décision** : clore cette recherche ciblée sur TCP/5555
et ouvrir P3m.10 — observation des destinations TCP,
tous ports, appartenant aux processus Dofus/Ankama,
durant A1 → B → A2. Aucun payload ni contenu TLS
ne sera déchiffré.

Voir `docs/modular-workspace-p3m10-upstream-tcp-endpoints.md`.

## Limites de production

- Pas de modification au `CurrentServerState`, au
  `CharacterIdentityCorrelator`, aux fiches, à la base
  ou aux overlays.
- Le serveur que l'utilisateur choisit dans BestCrush
  reste **autoritaire**, sans obligation de preuve réseau.
- Pas de dump de messages arbitraires, ni de token,
  de mot de passe ou d'authentifiant dans le diagnostic.
- L'exploration du launcher, de HTTPS/TLS ou d'autres
  ports est **hors scope P3m.9** et devra être traitée
  séparément si aucune chaîne n'est trouvée.
- Tests synthétiques : nom exact, texte JSON, correspondance
  Unicode, absence, faux positif partiel, catalogue partagé,
  isolation des phases et intégration au tracker TCP.

## Séquençage

P3m.8 (`--kta-character-compare`) **reste disponible mais
reporté**, conformément à la nouvelle priorité :
chercher d'abord une preuve textuelle du serveur.
