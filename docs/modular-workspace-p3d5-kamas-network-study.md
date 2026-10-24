# P3d.5 — Kamas en poche / banque : caractérisation du réseau

## Objectif

Étudier séparément les deux cases de richesse déjà présentes dans
`CharacterSheetCard` :

- **Kamas du personnage** (portefeuille / inventaire)
- **Kamas de la banque**, dont la propriété compte + serveur doit être
  prouvée indépendamment avant toute mutualisation

Aucun solde ne doit être déduit d'une dépense théorique ou de la simple
sélection d'un personnage. Les champs `PocketKamas` et `BankKamas`
existent déjà dans `CharacterSessionProfile`, mais aucun décodeur Game
ne les alimente actuellement. Les fiches expérimentales liées à une fenêtre
n'ont pas d'identité de personnage confirmée.

Les prix observés en HDV ne sont **pas** un solde de Kamas. L'absence
d'observation doit rester `null`, jamais `0`.

## Phase 1 : localisation d'opcodes sans toucher au programme

Utiliser le mode **existant** de `BestCrush.NetworkProbe` :
`--character-scan`. Ce mode affiche les métadonnées des messages
(opcode obfusqué, direction, taille, numéro de session, heure),
sans imprimer les charges réseau ni leurs valeurs.

Sur Windows, depuis la racine du dépôt :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --list
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device <INDEX_A_VERIFIER> --character-scan
```

Remplacer uniquement le numéro d'interface détecté. Le probe doit être
**démarré AVANT Dofus** pour ne pas perdre le début des messages Game.

### Recette de capture

Faire l'expérience sur **un seul client Dofus** à la fois, dans une
session de capture neuve ; ne pas activer d'autres modes de recherche.

1. Lancer le probe, puis ouvrir un seul personnage et attendre la fin
   complète du chargement ; noter localement l'heure de cette étape.
2. Ouvrir son inventaire, relever localement le solde en poche, puis
   refermer ; noter l'heure. **Ne pas publier le solde.**
3. Effectuer une action bénigne **qui modifie réellement les kamas en
   poche** (par exemple achat d'un objet bon marché), attendre le
   rafraîchissement de l'inventaire et noter l'heure de l'action.
4. Ouvrir l'interface de banque, sans changer de compte ni de
   personnage. Noter l'heure de l'ouverture. Si possible, effectuer
   un petit dépôt de kamas contrôlé, puis noter l'heure.
5. Arrêter le probe par `Ctrl+C`.

Comparer les séries de messages **S→C**, par session et par tranche
horaire. L'identification d'un opcode fréquent pendant l'action n'est
**pas** une preuve qu'il contient le solde. Une fois 1–3 familles
plausibles identifiées, les examiner à l'aide du mode de forme
`--character-shape --shape-keys <OPCODES>` dans une seconde capture
séparée. Ce mode n'affiche que les formes protobuf, pas leurs valeurs.

### Informations à partager

Uniquement les lignes de métadonnées `Session #N S→C opcode taille`
pertinentes pour chaque étape et les noms des actions (« inventaire »,
« achat », « banque »), **sans publier les montants exacts**.

Ne transmettre **aucun** PCAP, dump, corps de message, adresse IP,
nom/ID de personnage ou de compte, jeton, cookie, nom de fenêtre ou
capture affichant des informations privées. Ne pas activer `--all`.

## Conditions avant implémentation dans BestCrush

- Démontrer un champ numérique de solde par comparaison locale avant /
  après, puis par au moins un second changement de solde indépendant.
- Identifier la provenance et le type de chaque message (Game S→C
  du client correct ; banque distincte du portefeuille).
- N'accepter qu'un message associé au **bail TCP Game + fenêtre +
  époque actif**, jamais au pseudo, au focus seul ou au serveur en
  consultation SQL.
- Montants en `long` contrôlés ; zéro explicite différent d'inconnu.
  Éviter toute interpolation du prix d'un objet acheté.
- Lors de la reconnexion, de l'éviction ou du changement de personnage :
  remettre les montants à `null` tant que la nouvelle session n'apporte
  pas une donnée fiable.
- Tester : fenêtres A/B avec le même nom, Game non lié, données tardives,
  fermeture sans nouveaux paquets, banque non ouverte, et changement
  d'époque de capture.
- **Ne jamais transférer la banque au modèle commun compte+serveur** sans
  validation P3c indépendante de cette identité.
- Ne modifier ni les prix HDV / coefficients, ni SQLite, ni les règles
  de sélection manuelle de serveur.

## État

**P3d.5.0 : protocole d'observation préparé, aucun opcode de richesse
prouvé et aucun solde affiché automatiquement.** La phase suivante sera
construite à partir des observations contrôlées de l'opérateur.
