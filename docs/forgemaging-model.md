# Assistant de forgemagie

Sources consultées le 5 novembre 2026 :

- https://www.youtube.com/watch?v=39Sx0dJGaJ4 : « J’ai CRACKÉ le secret de la forgemagie DOFUS ». Description et chapitres accessibles ; sous-titres complets non récupérables par l’interface publique consultée.
- https://www.youtube.com/watch?v=G3-Wj5E8Tl0 : réaction de HUZ, dont la description renvoie à la première vidéo et au même simulateur.
- https://dofocus.fr/forgemagie/ : modèle empirique public consulté dans le calculateur côté client. Les règles mathématiques ont été réécrites pour le domaine BestCrush, sans reprendre le code ni l’interface du site. Ce modèle n’est pas une formule officielle Ankama.

## Probabilités

Poids : référentiel local `Characteristic.GetWeight`. Une rune de base donne `GetBasicRuneWeight / GetWeight` points ; Pa et Ra multiplient respectivement ce gain par 3 et 10, uniquement si ces runes existent au catalogue local.

Le calcul utilise le jet après gain, la position entre minimum et maximum naturels, le rapport jet courant/gain de rune et la qualité pondérée des autres lignes naturelles positives. La qualité vaut 1 en présence d’un exo PA/PM.

Avec `q` la qualité, `r` le rapport courant/gain, `o` le poids d’over après gain, la difficulté naturelle est `D = 5 + 12 max(0,r−14) + 60 q^12`. Ajouter 45 à partir de 80 % de la plage naturelle, sauf ligne fixe. En over, remplacer ce supplément par `65 + 4 max(0,o)`. Sous le niveau 170, retirer `20 max(0,1.1−q)`.

SC : `max(2,(100−D)/2)` pour une ligne naturelle. SN naturel : `min(50,100−SC)` si D ≤ 100, sinon `max(15,50−0.375(D−100))`. SN over : `max(5,40−0.375(D−100))`. Borner SN entre 0 et `100−SC` ; EC est le complément.

Pour les exos PA/PM/PO : SC 1 %, SN 0 %. Pour les autres exos, une estimation dépend du poids final : enveloppe 57 jusqu’à 2 ; décroissance linéaire à 19 au poids 15 puis à 0 au poids 25. SC = max(1,0.077 enveloppe), SN = 0.923 enveloppe, EC complémentaire. Tous ces résultats sont présentés comme des estimations.

Limiter une ligne à max(maximum naturel, partie entière de 101/poids unitaire) et le poids cumulé des overs/exos positifs à 101. Un malus négatif ne compte pas comme over positif.

## Conseil de coût

### Conseiller adaptatif (première étape)

Le 5 novembre 2026, ajout de ForgemagingCoach : une prochaine action expliquée et un ordre de remontage réévalué à partir du jet réellement présent. Sans puits positif connu, les lignes natives PA/PM/PO manquantes sont stabilisées avant les lignes plus légères. Avec du puits et d’autres remontages naturels à effectuer, le repassage de ces lignes lourdes est différé. Les overs/exos viennent après les remontages naturels. Les lignes plus lourdes sont prioritaires dans une même étape ; il s’agit d’une heuristique explicite, pas d’un ordre universel démontré optimal.

Le choix normale/Pa/Ra évite d’abord un over non nécessaire lorsqu’une variante utilisable reste naturelle. Pour le remontage naturel, une variante couverte par le puits est privilégiée lorsqu’elle existe. Les variantes chiffrées restantes sont comparées avec l’indicateur local de coût ci-dessous ; si aucune n’a de prix, le choix mécanique utilise les chances estimées et le dépassement de l’objectif. Les prix inconnus restent visibles et la comparaison est signalée incomplète. Le puits ne garantit pas la réussite.

Le budget borne les recommandations chiffrées et signale l’arrêt lorsqu’il est atteint. Si des coûts passés manquent, le reste exact demeure inconnu. Un résultat réellement effectué reste enregistrable même si son coût final dépasse le budget de préparation. Les simulations de probabilité globale et de coût moyen d’atteinte du jet sont différées, conformément à la priorité utilisateur.

### Préparation au concassage (comparaison de deux jets)

Le mode « Préparer un concassage » compare le jet actuel aux valeurs exactes du champ objectif, en gardant le même coefficient et la décote des paramètres. Il évalue sans focus et les focus natifs positifs chiffrables, avec la formule commune de CrushService. Une ligne lourde sacrifiée perd sa contribution ; le puits n’est jamais ajouté au rendement. La marge disponible pour financer la FM est la différence des deux meilleures valeurs chiffrées, uniquement lorsque les prix et le coefficient nécessaires sont complets. Une marge négative indique que le jet demandé n’améliore pas la valeur de concassage.

Cette marge est un seuil conditionnel au jet atteint, avant frais de vente, pas une prédiction de coût de FM ou de bénéfice réalisé. La date du coefficient est visible ; un coefficient inconnu n’est pas remplacé par 100 %. Les variantes Pa/Ra ne deviennent pas des runes de sortie. Aucun sacrifice, jet optimal automatique, coût moyen de préparation ou classement par cinétique n’est prétendu par cette première comparaison.

Les données de prix/coefficient sont chargées une fois en arrière-plan avec l’objet ou à l’actualisation. Les changements de jet ou de mode recalculent localement les conseils et la comparaison, sans lecture de base supplémentaire ni modification des stocks. Le suivi en jeu transmet les mêmes données économiques avec sa photographie de session, en conservant la protection contre les changements de client, serveur et connexion.

Validation de cette étape : 155 tests ciblés réussis (FM, concassage, analyses, navigation et données). Couverture de l’ordre avec/sans puits, changement de variante aux bornes naturelles, prix incomplets, budget, valeur perdue d’un PA sacrifié, focus et décote, séparation des coefficients par serveur, affichage chiffré et arrêt quand le jet visé n’apporte plus de valeur. Contrôle natif sur la session manuelle existante Bouclier du Bouftou/Kourial, sans modifier son jet : prochaine Rune Pui expliquée, passage immédiat au mode préparation et signalement du coefficient absent. Aucune rune consommée pour cette étape ; les essais en jeu antérieurs restent conservés.

L’indicateur local par variante compare `prix unitaire / (gain nominal utile × (SC+SN)/100)`. Il ne représente pas un coût moyen réel : les gains partiels et réparations futures n’y sont pas simulés. Le tableau historique de variantes reste ordonné par phase puis par coût rapporté au poids utile ; le conseiller utilise l’ordre de remontage explicite décrit ci-dessus. Les prix inconnus restent inconnus et sont affichés séparément.

Il s’agit d’un classement local de la prochaine tentative, pas d’une optimisation globale du coût final. Les réparations de lignes perdues et les futurs prix ne sont pas simulés. Un coût de succès critique peut être indiqué séparément ; il ne constitue pas une garantie d’atteindre l’objectif.

## Session et résultats

Le jet actuel est initialement inconnu et doit être renseigné. Les boutons de jet minimum/maximum sont des modèles de saisie explicites. Le puits inconnu reste inconnu ; une photographie d’un objet ne permet pas de le déduire.

Le joueur effectue les tentatives en jeu et renseigne leurs résultats. SC applique uniquement le gain de la rune. Pour SN/EC, les lignes réellement obtenues doivent être saisies. Le puits est reconstruit à partir du puits précédent connu, des pertes observées et du poids consommé (moitié si le gain potentiel reste négatif). Le coût d’une tentative est figé lors de sa préparation. Un prix inconnu ne devient jamais une dépense nulle.

Le suivi de session est conservé dans `Data/fm-sessions.json`, isolé par serveur et équipement. Aucun achat, aucune pose de rune et aucune modification du stock ou des prix de marché ne sont effectués par l’assistant. Le mode manuel et le mode en jeu restent séparés.


## Suivi en jeu (client 3.7.13.17)

Le suivi est passif, activé explicitement pour le client ciblé. Il accepte uniquement sa connexion TCP encore vérifiée et son bail de serveur actif. Un changement de fenêtre, une reconnexion, une interruption ou une saturation invalide l’état courant. PID, HWND, connexion et UIDs des objets ne deviennent pas des identités persistantes. Les sessions en jeu ont un identifiant opaque distinct des sessions manuelles et des autres exemplaires.

Le dépôt jys et le relevé kch fournissent le jet initial : objet 3 → description 1, métier dans la description au champ 4, puits absolu fixed32 à la racine au champ 2. Objet : quantité 3, type 4, UID 5, effets 7 → valeur 5 / code 10. Le catalogue public des effets fournit la caractéristique et le signe ; un effet inconnu empêche un faux relevé complet.

Une tentative associe jxu (UID de rune, quantité 1), le relevé kch de la rune, la quantité absolue irx (ou iqm pour la dernière rune), puis le résultat kco de l’objet exact. Les mises à jour d’objets iqy, parfois doublées, ne créent jamais de tentatives. Une série peut continuer avec la même rune lorsque chaque résultat est précédé d’une nouvelle consommation confirmée. Un écart de plusieurs runes suspend le suivi au lieu de fabriquer les tentatives manquées.

kco porte le résultat au champ 1, et sa description au champ 2 : puits absolu float au champ 2 (zéro protobuf si absent), sens de variation au champ 3, objet au champ 4. Les six essais réels ont confirmé le code 1 pour un échec avec pertes et le code 2 pour les deux sortes de réussite. La séparation SC/SN utilise le jet complet et le puits : une perte ou une variation du puits donne SN, l’absence des deux donne SC lorsque le serveur confirme que le puits est inchangé. Un résultat contradictoire reste « À confirmer ». Le cas neutre sans modification (code 3, défini dans la documentation du protocole) est couvert par une relecture synthétique, pas encore observé pendant ces six essais.

Chaque tentative conserve la rune et son prix à cet instant, le jet avant/après, le puits reçu et les probabilités estimées avant l’essai. Elle est enregistrée indépendamment des restrictions du moteur de recommandation : un résultat réel au-delà de l’objectif reste un résultat réel. Un doublon de résultat sans nouvelle consommation ne rajoute aucun coût. Deux vrais échecs identiques restent deux tentatives.

La table principale présente chaque variante normale, Pa et Ra disponible pour les objectifs. Le mode manuel conserve les anciens historiques et demande un choix explicite SC/SN/EC sans résultat présélectionné. Les prix sont actualisés en arrière-plan sans écraser le jet en jeu ni les coûts passés.

Validation : six Rune Vi autorisées sur Le S’Mesme non équipé, départ 15 Vitalité / 5 Chance / puits 0, arrivée 39 Vitalité / 2 Chance / puits 0,2. Trois SC, deux SN, un échec ; 37 → 31 Rune Vi. Ces essais valident l’acquisition et la séparation des résultats ; ils ne suffisent pas à valider statistiquement les probabilités empiriques du modèle. Voir fm-test-budget.md pour les consommations suivantes.

Contrôle final de l’application native reconstruite, le 5 novembre 2026 : reconnexion de Pagalom, activation du suivi en jeu, dépôt de Le S’Mesme et récupération automatique du jet 39 Vitalité / 2 Chance / puits 0,2. Deux Rune Vi supplémentaires donnent successivement 44/2/0,2 (SC) puis 45/2/0 (SN, gain partiel +1 Vitalité). Chacune crée exactement une tentative persistée et affichée, avec avant/après, rune consommée, résultat et puits corrects. L’actualisation des prix entre les essais conserve le jet, l’historique et les coûts passés. Les variantes Rune Cha, Pa Cha et Ra Cha sont visibles dans les conseils. Les prix absents restent signalés et le total est marqué incomplet. Le diagnostic brut est désactivé pendant ce contrôle final. Compilation native réussie et 127 tests ciblés réussis (FM, analyses, navigation et données).

Pour les seuls diagnostics de développement, un fichier local DebugCaptures/Forgemaging/enabled active trente minutes de relevés limités aux messages d’atelier du flux suivi ; limite 2 Mo, aucun corps de message de connexion ou de chat. Les fixtures de test remplacent les UIDs réels par des valeurs synthétiques. Ce diagnostic n’est pas activé par l’interface joueur.
