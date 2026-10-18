# P3b — Continuité anonyme des 20 métiers entre sessions TCP

## Origine

La capture P3a réelle A → B → A (trois sessions TCP)
a montré, sur chaque chargement, `ksc`, `ksb`,
puis `ipz` valide avec 20 entrées `f5` uniques.
Les trois messages `ipz` avaient la même taille,
mais **leur taille ne prouve pas la stabilité des
métiers ou des compteurs XP**.

Les métiers sont une propriété du **compte + serveur**,
pas d'un personnage. L'identité réelle du compte
et du serveur n'est pas établie dans le protocole.

## Mode expérimental

Mode **opt-in**, isolé des autres diagnostics :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-continuity
```

Utiliser **un seul client DOFUS à la fois**, et
uniquement des personnages du **même compte et du
même serveur**, connus de l'opérateur. Aucun nom,
compte, serveur ni XP n'est saisi.

Pour chaque `ipz` S→C, le probe :

1. décode exactement les 20 enregistrements
   avec `ProfessionIpzDecoder` ;
2. exige 20 candidats `f5` non nuls et distincts ;
3. garde **uniquement le premier `ipz` valide**
   comme référence temporaire en RAM ;
4. compare les messages suivants à cette
   même référence, en associant les entrées par
   `f5` et non par position ;
5. affiche uniquement des indicateurs catégoriels :
   - `f5-set-equal=yes/no` : mêmes 20 candidats ;
   - `levels-equal=yes/no/unavailable` :
     mêmes niveaux pour les candidats correspondants ;
   - `xp-equal=yes/no/unavailable` :
     mêmes compteurs d'expérience `f2` ;
   - `account=unverified server=unverified mapping=unverified`
     **sans exception**.

Les résultats `unavailable` empêchent toute
inférence sur les valeurs lorsque les candidats
`f5` ne sont pas les mêmes.

Un `ipz` malformé, incomplet ou présentant
des `f5` dupliqués ne peut ni créer ni remplacer
la référence. La première référence reste
inchangée jusqu'à la fermeture du probe.

La référence est limitée à 20 tuples numériques
en mémoire du processus. Aucun hash, ID,
XP, nom, trame brute, endpoint, valeur `f5`
ou association automatique entre comptes
n'est imprimé ni sauvegardé.

**Important :** le probe peut comparer deux
connexions TCP sans pouvoir connaître leur compte.
Même `f5-set-equal=yes levels-equal=yes xp-equal=yes`
ne constitue **aucune preuve d'identité du compte**,
car les mêmes métiers et valeurs pourraient être
présents sur plusieurs comptes. La comparaison
ne crée aucun `AccountKey`, aucun profil,
aucun lien entre joueurs et aucune persistance.

## Procédure réelle

1. Fermer les autres clients Dofus pour éviter
   les observations issues de plusieurs comptes.
2. Se connecter au personnage A du compte/serveur
   à étudier, avec le mode actif avant le chargement.
3. Attendre `baseline=created`.
4. Retourner à la sélection puis charger le
   personnage B **du même compte et serveur**.
5. Attendre `baseline=compared`, puis revenir
   à A et attendre une troisième comparaison.
6. Arrêter avec `Ctrl+C`, puis ne transmettre
   que les lignes `PROFESSION_CONTINUITY`
   et leurs numéros de session anonymisés.

Résultat attendu **si aucune progression de métier
n'intervient** :

```text
p3b=ipz decoded=yes rows=20 f5-valid=yes baseline=created scope=account-server account=unverified server=unverified mapping=unverified
p3b=ipz decoded=yes rows=20 f5-valid=yes baseline=compared f5-set-equal=yes levels-equal=yes xp-equal=yes scope=account-server account=unverified server=unverified mapping=unverified
```

Si le joueur fait progresser un métier,
`xp-equal=no` peut être normal.

## Tests et limitations

Les tests P3b utilisent **uniquement des valeurs
fictives** : ordre de lignes modifié, XP modifiée,
niveau modifié, liste `f5` différente, doublon
ou absence de `f5`, trame malformée, mode isolé,
direction C→S refusée, comparaison entre sessions
sans attribution, absence de persistance.

Le build Windows et la suite de tests P3b
restent à réaliser après le commit.
Ne pas activer l'attribution automatique des
métiers ou le stockage de compte sur cette base.

## Étape P3c

La recherche d'une identité fiable du couple
**compte + serveur** demeure indépendante
de la comparaison des métiers ; elle doit gérer
multi-compte, multi-client, changement de serveur
et contexte de fenêtre cible sans recourir
à une empreinte de métiers comme identifiant.


## Checkpoint réel P3b — trois connexions A → B → A

Mode `--profession-continuity`, client `3.6.11.15`.
Le relevé réel affiche trois messages `ipz` S→C
sur trois connexions TCP successives :

| Session TCP | Résultat P3b |
| --- | --- |
| #1 | `decoded=yes rows=20 f5-valid=yes baseline=created` |
| #3 | `baseline=compared f5-set-equal=yes levels-equal=yes xp-equal=yes` |
| #5 | `baseline=compared f5-set-equal=yes levels-equal=yes xp-equal=yes` |

Chaque corps `ipz` mesurait 294 octets, mais les
conclusions reposent ici sur **les comparaisons des valeurs
décodées**, pas sur cette seule taille. Les sessions
précédentes ont été fermées entre les chargements.

**P3b validé sur le parcours réel testé** :
même ensemble de 20 candidats `f5`, même niveau
et même compteur d'XP pour chaque candidat entre
les trois observations, dans un scénario annoncé
comme A → B → A au sein du même compte et serveur.

Cela corrobore le partage des métiers, mais ne
permet **aucune inférence automatisée d'identité** :
`account=unverified server=unverified mapping=unverified`
est resté inchangé dans les trois résultats.

Le probe exécuté a donc construit une référence
temporaire de 20 tuples en RAM et l'a comparée aux
observations suivantes sans les imprimer.

Aucune valeur réelle `f5`, niveau, XP, pseudo,
identifiant de compte, payload ou endpoint n'est
reproduite ici. La sortie P3b n'est pas un test
de preuve d'identité et ne déclenche aucune
écriture de profil ou de mapping.

La commande d'exécution a réussi. **Aucun bilan
détaillé de `dotnet test` n'a été fourni pour P3b** ;
ne pas en déduire un nombre précis de tests réussis.

## Transition vers P3c

Rechercher indépendamment l'identité du
**compte et du serveur**. La cohérence des métiers,
celle des personnages `ksc`/`ksb` et l'identité
d'une connexion TCP ne sont pas des preuves de compte.

`CurrentServerState` dispose déjà d'un bail de
serveur sélectionné manuellement. Ce bail autorise
la collecte mais ne prouve pas que la connexion
Dofus correspond au serveur choisi.

Prévoir en priorité une caractérisation passive,
sans données personnelles, des messages de
connexion/sélection concernés, une séparation
multi-client et des scénarios de changement de
compte, serveur et fenêtre active. Les éventuels
champs d'authentification/token restent opaques :
ne jamais les imprimer ni les persister.
