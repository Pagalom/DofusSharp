# P3c — Caractérisation indépendante du compte et du serveur

## Checklist P3c — validations reportées

**Statut : EN ATTENTE — l'opérateur ne dispose pas encore d'un second compte Dofus.**
Ces contrôles ne sont pas requis pour poursuivre les autres phases
de recherche, mais **aucune identité de compte vérifiée, attribution
automatique des métiers ou persistance compte + serveur** ne doit être
activée tant qu'ils restent non validés.

- [ ] Disposer d'un **second compte Dofus distinct**, idéalement avec
  un personnage **sur le même serveur** que le premier.
- [ ] Contrôle positif : personnage A → B sur un seul compte / serveur ;
  le candidat de compte reste stable.
- [ ] **Contrôle négatif indispensable : compte A → compte B sur le même serveur** ;
  le candidat de compte doit différer, sans déduction à partir de
  `ipz`, `ksc`/`ksb`, du nom de personnage ou des connexions TCP.
- [ ] Contrôle serveur : utiliser le même compte sur deux serveurs
  distincts, si disponible ; le candidat de serveur doit différer.
- [ ] Contrôle multiclient : deux comptes simultanés, puis changement
  de fenêtre, déconnexion/reconnexion et messages tardifs ; aucune
  contamination de compte, serveur ou métiers entre sessions.
- [ ] Vérifier que les preuves reposent sur des messages
  **sémantiquement indépendants et caractérisés**, sans consulter,
  imprimer, hacher ou sauvegarder d'authentifiants, tokens, payloads
  ni identifiants personnels ; seuls des indicateurs anonymes sont permis.
- [ ] Seulement après validation : définir un modèle de métiers
  **compte + serveur**, distinct des profils personnages,
  avec isolation stricte et tests synthétiques.

**Décision actuelle :** laisser `account=unverified server=unverified`
et ne pas interpréter l'égalité des vingt métiers P3b comme une
preuve d'identité. Reprendre cette checklist lorsque le second
compte sera disponible.

## Avancement parallèle P3d

P3d prépare la séparation des métiers hors des profils
personnages et leur affichage compte + serveur, sans
ingestion réseau ni association automatique.
Voir `docs/modular-workspace-p3d-account-professions-workspace.md`.

**Les contrôles d'identité multi-compte et les tests
d'isolation restent reportés dans cette checklist** :
aucun second compte de test n'est encore disponible.
L'intégration des métiers à un compte réel est
toujours bloquée jusqu'à leur validation.

## État confirmé

- P2j : `ksc` et `ksb` concordent sur une même session
  pour les champs candidats du personnage et de sa classe.
- P3a : parcours réel A → B → A avec trois sessions TCP,
  chacune transportant 20 métiers `ipz` valides.
- P3b : dans trois connexions successives A → B → A,
  `f5-set-equal=yes levels-equal=yes xp-equal=yes`.
- Les métiers sont partagés au niveau **compte + serveur**.
  Aucun message déjà décodé n'établit l'identité fiable
  du compte ou celle du serveur du trafic observé.

**Ne jamais déduire le compte** de la similarité des
métiers, du nom de personnage, du `ksc`/`ksb`,
d'un numéro de session TCP, d'une adresse IP ou du
serveur sélectionné manuellement dans l'application.

## Deux informations distinctes

1. **Serveur** : `CurrentServerState` contient un serveur
   explicitement choisi par l'utilisateur et un bail
   générationnel protégeant les messages en attente.
   Ce bail n'authentifie pas le serveur du jeu.
2. **Compte** : aucune `AccountKey` vérifiée
   n'est actuellement déduite du protocole.
   Un compte n'est pas la même chose qu'un personnage
   et ne doit pas être confondu avec le client TCP.
   Une détection fiable devra être établie indépendamment,
   sans publier d'identifiant réel ou de secret.

## Étape d'exploration passive, sans changement de code

La prochaine observation utilise uniquement le
résumeur de formes protobuf existant :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --character-shape --shape-keys knc,kod,ksc,krg,ipg
```

Capturer depuis l'écran de sélection, puis charger
un personnage, revenir à la sélection et charger
un deuxième personnage du même compte et serveur.

**N'envoyer que les lignes `shape=[...]` des cinq
opcodes ciblés et les numéros de session anonymes.**
Le mode `--character-shape` ne révèle ni chaînes,
ni nombres du protocole. Les structures de
`knc` et `kod` pourraient contenir des champs
liés à l'authentification : ils doivent rester
strictement opaques. **Ne pas utiliser `--all`,
une capture PCAP, ou un dump brut** pour ce travail.

Une forme protobuf seule ne suffit pas à identifier
l'usage sémantique d'un champ. La présence d'un
champ numérique n'autorise pas à le déclarer
identifiant de compte ou de serveur.

## Suite conditionnelle

Après caractérisation, construire si utile un
diagnostic expérimental **opt-in** qui compare
uniquement des indicateurs égal/différent à travers
plusieurs scénarios *connus de l'opérateur* :

- personnage A → personnage B sur le même compte
  et serveur : compte stable, serveur stable ;
- compte A → compte B sur le même serveur :
  le candidat de compte doit changer ;
- même compte sur serveur A → serveur B :
  le candidat de serveur doit changer ;
- deux clients connectés simultanément :
  aucune attribution croisée, aucune session
  ne peut emprunter l'identité de l'autre ;
- déconnexion/reconnexion, changement de fenêtre
  et messages tardifs : pas de réemploi d'un
  compte ou d'un serveur périmé.

Les scénarios nécessitant plusieurs comptes ou
serveurs seront réalisés seulement si l'opérateur
dispose de ces environnements de test. Ne jamais
demander de mot de passe, jeton ou ID réel dans
le terminal partagé ou dans les documents versionnés.

## Critères d'acceptation

- Signaux sémantiquement indépendants et
  validés sur les scénarios négatifs, non de
  simples identifiants de personnages.
- Portée de chaque observation bornée par
  la connexion, le client concerné, le cycle
  de capture et le serveur.
- Absence de logs, hashes, identifiants
  personnels, authentifiants, payloads
  ou persistance avant validation.
- Échec ou doute : conserver
  `account=unverified server=unverified`.
- Mettre ensuite les métiers dans un état
  **compte + serveur**, distinct de
  `CharacterSessionProfile`; ne pas modifier
  ce modèle de production avant vérification.

P3c est **ouvert mais non validé**.


## Observation réelle P3c.1 — formes seules

Client `3.6.11.15`, diagnostic opt-in `--character-shape`,
opcodes ciblés `knc,kod,ksc,krg,ipg`.

Sur trois retours à l'écran de sélection, connexions
TCP **#3, #5 et #7** :

- `knc` C→S : `root=[1:len 2:len]` à chaque fois ;
- `kod` C→S : `root=[1:len]` à chaque fois ;
- `ksc` S→C : `root=[2:lenx2]` à chaque fois,
  puis échantillonnage des champs de fiches déjà
  observés, notamment `f2.f3`, `f2.f4` ;
- `krg` S→C : échantillonnage interne stable et
  un unique champ longueur en racine, mais tailles
  de message **815, 848, 815 octets**
  respectivement en sessions **#1, #3, #5** ;
- `ipg` S→C : 20 éléments `f1` et taille
  **664 octets** à chacun des chargements observés.

La session #7 s'est arrêtée après la sélection
(`ksc`) : **aucun `krg` ni `ipg` sur cette
session n'apparaît dans la trace soumise**.
Les différences de taille de `krg` pourraient
suivre le changement de personnage, mais il
s'agit uniquement d'une corrélation temporelle ;
elles ne démontrent ni un champ de classe, ni
un champ de compte, ni un serveur.

**Conclusion négative importante :** une structure
identique n'implique pas des valeurs identiques
et ne valide aucun identifiant. L'étude P3c
n'a établi **aucune identité de compte ni
de serveur** à ce stade.

### Confidentialité et ciblage de l'étude suivante

`knc` et `kod` sont des messages client → serveur
du parcours de connexion. Leur contenu pourrait
comporter des données d'authentification :
**ne pas afficher, extraire, hacher ou comparer
leurs valeurs**, ni chercher à créer une
empreinte d'authentifiant, même éphémère.

Pour chercher des preuves de compte et serveur,
privilégier un diagnostic passif et borné des
messages **serveur → client sans secret**,
validé sur plusieurs scénarios contrôlés,
avec uniquement des résultats catégoriels
et des tests synthétiques. Ne pas assimiler
`ipg` à `ipz` : les deux opcodes sont distincts,
même si les deux peuvent contenir vingt lignes.

**Avant d'implémenter une recherche comparative,**
vérifier que l'opérateur dispose de scénarios
négatifs utiles (au moins deux comptes distincts,
idéalement sur un même serveur) ; sinon
un simple candidat de compte corrélé risque
d'être présenté prématurément comme fiable.
Les clés de compte et de serveur demeurent
`unverified` jusqu'à preuve sémantique
et isolation multiclient.
