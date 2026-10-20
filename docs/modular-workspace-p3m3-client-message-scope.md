# P3m.3a — Contexte fenêtre des messages, sans attribution d'identité

Date : 20 octobre 2026. Branche : `feature/modular-workspace`.

## État acquis

- P3l : détection, suivi du focus, sélection manuelle et fermeture des fenêtres confirmés dans BestCrush.
- P3k : deux flux TCP/5555 observés sont associés de manière distincte aux deux fenêtres Dofus sur le poste Windows.
- P3m.1 : les marqueurs de liaison TCP sont confirmés en fonctionnement réel.
- P3m.2 : **721 tests réussis, zéro échec** sur Windows, dont l'isolation des caches d'achats/d'UID par connexion.

## P3m.3a — Ce qui est modifié

1. `LiveDofusClientNetworkBindings` associe le tuple TCP exact à un identifiant **opaque et croissant** de connexion issu de P3m.2. Le plus récent remplace un ancien identifiant réutilisant le même socket.
2. `DofusWireMessage.CandidateWindow` peut porter un HWND/PID validé par le socket Windows, uniquement en **mémoire volatile**, dans une métadonnée de message.
3. Le worker revérifie, avant le processeur sémantique, si cette fenêtre correspond **encore** au même identifiant de connexion et à la même génération de capture. S'il n'y a plus de preuve, `CandidateWindow` est remis à null.
4. La révocation fonctionne aussi au changement de propriétaire, à la fermeture de fenêtre et à la réinitialisation de capture, via les mécanismes P3m.1.
5. Les logs et les fichiers de debug ne consignent ni HWND, ni PID, ni tuple, ni identifiant de compte. Aucun nouveau profil n'est conservé sur disque.

**Important :** `CandidateWindow` est un **contexte de client local**, pas une identité du personnage. Le processeur métier ne l'utilise pas encore pour les inventaires, kamas, coefficients, prix ou métiers, et le `CharacterIdentityCorrelator` reste fermé aux captures de production. Aucun nom, aucune classe, aucun serveur choisi manuellement ne peut remplacer les preuves réseau nécessaires.

## Validation Windows

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```

Les cinq nouveaux tests unitaires vérifient : liens distincts pour deux connexions, refus d'un ancien identifiant de connexion, absence de correspondance, révocation à la fermeture/redémarrage et impossibilité de créer une identité depuis une fenêtre candidate.

Essai visuel : ouvrir BestCrush et deux clients Dofus sur le poste Windows ; vérifier boutons, focus et marqueurs de liaison TCP en faisant ouvrir un inventaire dans chaque client. La sélection et les données de marché ne doivent pas être perturbées. Ne pas tester de nouvelles attributions de métiers : elles ne sont pas activées.

## P3m.3b — Vérifier le protocole du client actuel (Dofus 3.7.12.12)

Les études P2/P3 sur `ksb`, `ksc`, `ipz` et l'annuaire public de métiers ont été effectuées sur Dofus **3.6.11.15**. Le fichier `protocol-map.json` contient la version de référence ; la ligne de démarrage du probe **ne garantit pas** la version réellement lancée. Le probe indique maintenant expressément que ce build est une référence.

### Essai 1 — vérifier les messages d'identité, *un client par essai*

**But :** vérifier que les deux familles de messages `ksb` et `ksc` conservent les champs structurels étudiés sur 3.6, sans dévoiler les noms, niveaux ou identifiants.

1. Déconnecter et fermer les autres clients Dofus pour que le probe ne voie qu'une connexion.
2. Noter localement **le nom et niveau exacts** du personnage qui sera connecté. Ne jamais les transmettre dans la sortie.
3. Avant de connecter le personnage dans le jeu, lancer :

   ```powershell
   dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --identity-fields
   ```

4. À l'invite `Référence P2b`, saisir localement `NomDuPersonnage:Niveau` (sans guillemets), puis ouvrir le personnage dans Dofus. Repasser éventuellement par la sélection des personnages pour observer les deux familles `ksc` et `ksb`.
5. Arrêter avec `Ctrl+C`. Conserver **uniquement les comptes rendus anonymisés** `ksb/ksc`, les chemins et les statuts `identity=unverified`. Ne pas envoyer des traces brutes, noms de personnage, identifiants ou fichiers de capture.

**Succès provisoire :** `ksb` et `ksc` sont tous deux reconnus sur une même session, la référence nom/niveau est unique et le rapport de comparaison présente des chemins numériques compatibles. Cela prouve la stabilité de **candidats structurels**, pas encore l'identité sémantique du compte/personnage.

### Essai 2 — refaire avec l'autre personnage / client

Arrêter puis relancer le probe avec une nouvelle référence locale. Répéter les mêmes étapes pour le second personnage. On compare uniquement les **statuts et les chemins**, pas les valeurs privées.

**Règle de décision :** tant que le caractère réellement identifiant du champ, le serveur et la correspondance `CharacterId` ne sont pas validés par plusieurs preuves indépendantes, **aucune mise à jour de** `CurrentCharacterState` **ou** `CurrentAccountProfessionsState` **n'est permise**.

### Essai ultérieur — professions, après l'identité

La vérification `--profession-job-study --check-public-job-ids` (roster `ipz` et références de crafts) ne commencera qu'après validation des résultats précédents. Les équivalences entre IDs publics et métiers sont **hypothétiques** hors métiers directement testés ; 3.7.12.12 n'est pas validée par extrapolation.

## Sécurité et limites

- Aucun profil persistant ; reconstruction exclusivement en mémoire.
- Pas de modification de `main`, pas de release ou de changement de version.
- Aucune sortie du programme ne doit divulguer identifiants de compte, noms, jetons, IP, ports éphémères, payloads de jeu.
- Le comportement multicomptes reste conservé, sans réduction à une session globale.
- Les nouveaux tests doivent être exécutés sur Windows avant d'approuver P3m.3a.
