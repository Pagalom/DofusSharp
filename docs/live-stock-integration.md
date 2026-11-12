# Stock courant intégré à BestCrush

## Fonctionnement

La capture réseau de BestCrush démarre dans l'espace de travail lorsque Npcap est disponible. Il n'est plus nécessaire de lancer NetworkProbe pour synchroniser les stocks. Les messages serveur sont traités par la file ordonnée existante, après revalidation du propriétaire Windows du flux TCP.

Le service `LiveStockService` maintient un stock par lancement, époque de capture et connexion TCP, relié uniquement à la fenêtre propriétaire. Le choix d'une fenêtre dans l'interface ne peut pas attribuer les paquets d'une autre fenêtre. Le serveur, lorsqu'il est disponible, provient du bail de connexion routé par BestCrush.

La base persistante dédiée est `%LOCALAPPDATA%\BestCrush\Data\stock-current.db`. Elle contient les sources et leurs états inventaire/banque, avec quantités UInt64, UID locaux au conteneur, effets numériques et effets bruts. Les anciens relevés restent consultables comme archives. Ils ne sont pas réutilisés automatiquement comme stock courant après un redémarrage ou une reconnexion.

Pour initialiser un inventaire complet, démarrer BestCrush avant de connecter le personnage, ou reconnecter ce dernier si BestCrush vient d'être lancé. Ouvrir le coffre pour initialiser la banque. L'ouverture du panneau inventaire seule ne garantit pas l'émission d'un relevé complet par le serveur.

## Messages de stock actuellement pris en charge

Profil observé avec Dofus 3.7.12.12 :

| Opcode | Opération |
| --- | --- |
| irl | Remplacement du relevé inventaire |
| irp | Remplacement du relevé banque |
| iqx | Ajout/remplacement d'objets dans l'inventaire, quantité absolue |
| iqz | Ajout/remplacement d'objets dans la banque, quantité absolue |
| irx | Nouvelle quantité absolue d'une instance inventaire, f3.f1 quantité / f3.f3 UID |
| iqm | Retrait d'une instance inventaire, f1 UID |
| itf | Retrait d'une instance banque, f1 UID |

Ces opérations s'appliquent aux mises à jour serveur indépendamment de leur cause : loot, achat, craft ou transfert. Le bilan de combat n'est pas additionné au stock, afin d'éviter le double comptage. Une nouvelle quantité remplace la précédente ; rejouer un même ajout ne cumule pas la quantité.

Une variation de quantité sur une instance inconnue, un conflit de type d'objet ou une mise à jour non décodable marque le conteneur à resynchroniser. Les stocks incomplets ou obsolètes ne fournissent pas de quantités aux vues de disponibilité. Une nouvelle capture complète rétablit l'état. Banque et inventaire restent indépendants ; aucun UID n'est utilisé pour fusionner les deux conteneurs.

## Interface

- Bandeau de stock dans l'espace de travail et page `/stocks` accessible via « Voir les stocks ».
- Recherche par nom ou identifiant et filtre banque/inventaire.
- Affichage exclusif des stocks de la session courante ; aucun choix d’archive dans l’interface.
- Quantités sac/banque dans les prix locaux des ressources, runes et équipements, et les lignes de concassage.
- Disponibilité des ingrédients et quantités manquantes à acheter dans les détails de coût des recettes.

Les fonctions métier des autres modules qui étaient encore des écrans de préparation ne sont pas développées par cette intégration. Le service partagé peut alimenter leurs futurs calculs. Le calcul économique existant du coût d'un craft n'est pas artificiellement diminué du stock déjà possédé : la disponibilité et les quantités à acheter sont indiquées séparément.

## Validation du 25 octobre 2026

- Compilation BestCrush réussie et 1 368 tests réussis, dont 13 nouveaux tests de protocole, persistance et isolation du service et un test du délai de sélection du serveur.
- Lancement réel de BestCrush, capture Npcap active, reconnexion de Pagalom sur Kourial.
- 503 lignes inventaire et 560 lignes banque persistées et visibles dans l'application, états complets et non obsolètes.
- Transfert d'une Noix de Cajou : inventaire 3 → 4, banque 6 → 5 ; changements constatés en SQLite et à l'écran sans rafraîchissement manuel.
- Transfert retour : inventaire 4 → 3, banque 5 → 6 ; quantités initiales restaurées et intégrité SQLite vérifiée.
- La lecture des onglets de prix a révélé puis permis de corriger le passage littéral du paramètre serveur aux composants de stock.

- Après correction du paramètre serveur, contrôle réel des prix des ressources sur Kourial : Noix de Cajou, sac 3 / banque 6 ; un objet absent affiche zéro.
- Correction de la preuve de propriétaire SYN : sa durée de dix secondes était trop courte pendant le choix du serveur. Elle est maintenant bornée à deux minutes comme la preuve de sélection ; époque, jeton TCP, quadruplet, fenêtre/PID unique encore visible et absence de conflit restent requis. Une nouvelle reconnexion a bien identifié Kourial.
- Combat réel contre huit Pious à Bonta : les six types de butin, soit treize objets, ont été automatiquement enregistrés sans ouvrir à nouveau l'inventaire ni reconnecter le personnage. La banque est restée inchangée, les états sont complets/non obsolètes et `PRAGMA integrity_check` retourne `ok`.

| Butin (ID public) | Avant | Après | Gain |
| --- | ---: | ---: | ---: |
| Plume de Piou Bleu (6897) | 7 | 9 | 2 |
| Plume de Piou Violet (6898) | 9 | 11 | 2 |
| Plume de Piou Vert (6899) | 12 | 13 | 1 |
| Plume de Piou Jaune (6902) | 8 | 9 | 1 |
| Objet 17123 | 36 | 42 | 6 |
| Objet 31518 | 37 | 38 | 1 |

Les six variations correspondent aux quantités de l'écran de victoire. Ce combat a augmenté des piles existantes ; l'arrivée d'une nouvelle instance est couverte par les tests automatisés et les transferts réels déjà validés. Les autres formes éventuelles de récompenses et les changements d'opcodes lors d'une mise à jour Dofus peuvent nécessiter une nouvelle validation.

## Banque vide de l'Eniripsa — 28 octobre 2026

Le personnage de test est désormais exclusivement Pagalom l'Eniripsa, niveau 39, sur Tylezia (profil launcher Pagalomm). Sa banque n'avait jamais reçu d'objet, comme confirmé par l'utilisateur.

Le diagnostic a observé une enveloppe `Any` contenant uniquement `f1 = type.ankama.com/irp`, sans champ de contenu `f2` : liste d'objets vide et zéro kamas sont omis par protobuf. Le décodeur accepte cette forme exacte pour `irp` uniquement, puis publie une banque vide complète/non obsolète. Les corps absents des autres opcodes, champs supplémentaires incorrects et messages malformés ne bénéficient pas de cette exception. L'absence de message bancaire ne crée jamais de relevé.

Validation réelle après recompilation : banque 0 ligne, inventaire 128 lignes, serveur Tylezia, états synchronisés ; intégrité SQLite `ok`. La comparaison entre connexions porte sur les types/quantités par pile, car les UID peuvent être renouvelés à la reconnexion. Quatre tests supplémentaires couvrent l'enveloppe vide, les formes invalides, le remplacement/persistance du conteneur banque sans effacer l'inventaire, et le maintien du blocage des calculs en l'absence de relevé bancaire. Suite complète : 1 390 tests réussis.

## Banque facultative pour les opportunités — 28 octobre 2026

Le choix produit a évolué après le contrôle de banque vide : l'absence de relevé bancaire ne bloque plus un calcul disposant d'un inventaire synchronisé du bon serveur. Le moteur utilise seulement l'inventaire et signale la banque exclue par une notification non bloquante et une mention dans le résultat. Les tests précédents d'exigence des deux conteneurs ont été adaptés ; l'exclusion d'une banque périmée ou partielle est vérifiée. L'inventaire reste obligatoire et aucune archive n'est réutilisée automatiquement. Suite complète : 1 394 tests réussis.
