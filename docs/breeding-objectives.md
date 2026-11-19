# Objectifs d’élevage

L’onglet **À faire** utilise le classement économique existant : toutes les générations,
coûts d’obtention et d’XP, revente comparable, quotas quotidiens et hebdomadaires.
Le prix de vente et le bénéfice affichés sont des estimations avant taxe, pas des ventes encaissées.

## Suivi

- Chaque objectif (espèce, niveau de vente, profil de fertilité) conserve son parcours et ses étapes.
- Le pourcentage correspond aux étapes terminées, avec un poids égal par étape.
- Les acquisitions et les objectifs de préparation/XP sont rapprochés des montures synchronisées.
  Une correspondance ambiguë ne termine pas automatiquement une étape. Le sexe demandé est respecté.
- **Fait** confirme les actions non observables. Pour un croisement, cela confirme que la naissance recherchée a été obtenue.
- **Mise en vente effectuée** confirme un dépôt, termine le cycle, remet la progression à zéro et incrémente le numéro du cycle.
  Une disparition de stock n’est jamais assimilée à une vente.
- Le parcours est conservé lorsqu’il a commencé ; un parcours non commencé est actualisé selon le stock et les nouveaux calculs.
- Les anciens objectifs sont conservés dans le journal même si leurs données de marché ne permettent plus de les proposer.

Le fichier `Data/breeding-objectives.json` contient uniquement du suivi utilisateur. Son périmètre
est le serveur consulté et un hash du nom de personnage affiché dans la fenêtre sélectionnée.
Ce titre Windows n’est **pas** une identité réseau vérifiée ; aucun événement économique n’est attribué sur cette base.
Sans titre de personnage, le suivi reste en mémoire et une indication le précise.
Le journal est écrit atomiquement. Un fichier illisible est conservé et n’est jamais remplacé par un journal vierge.

## Attente d’écoulement

Une confirmation de mise en vente ajoute une attente pour **l’espèce entière**, quel que soit
le niveau de vente choisi. Le délai estime l’absorption d’une monture : `7 jours / ventes sur 7 jours
au prix visé ou plus`, avec un minimum d’une heure. Les dépôts supplémentaires prolongent cette attente.

L’expiration du délai ne prouve aucune vente. Les quotas existants restent nécessaires et l’historique
des volumes doit dater de moins de deux jours. Le journal ne modifie ni les prix ni les ventes observées.
Il n’efface pas l’attente sur une simple disparition de monture.

Un objectif en attente est grisé. Une confirmation explicite permet de **relancer un cycle malgré l’alerte**.
L’avertissement reste visible, l’attente précédente est conservée et le dépôt suivant prolonge cette attente.
La dérogation concerne un seul cycle et ne permet pas de fabriquer des données de marché manquantes ou périmées.

## Jauges

Les alertes de Mon élevage et À faire concernent uniquement l’enclos actuellement relevé en jeu,
sur un état datant de moins de 30 secondes. Elles signalent une jauge active devenue inutile
(enclos vide, objectifs atteints, limite de sérénité), une plage inutilisable, une sérénité à couper
ou un déplacement à anticiper avant la sortie de plage. Une entrée en plage déjà prévue ne déclenche
pas une consigne de couper la jauge. Une valeur inconnue n’est jamais considérée pleine.

Les conseils ne changent aucune jauge ni monture dans le jeu. Les achats/fabrications et réglages
existants restent accessibles ; le bouton de copie HDV conserve la file de Ctrl+V, avec les noms contenant « et » en dernier.

## Priorité et seuil de reproduction

Dans **Paramètres → Élevage** :

- **Économie** par défaut : minimise les dépenses moyennes attendues, XP et préparation comprises, sans pénalité de temps.
- **Vitesse** : privilégie les acquisitions immédiates à l’HDV, puis les parcours demandant moins de reproductions/captures et d’XP.
  C’est une priorité de parcours, pas une estimation du temps de capture. Les quotas de marché restent appliqués.
- **Réussite minimale de la génération cible** : 65 % par défaut, sauvegardé entre les lancements.
  Le réglage s’applique à tous les croisements du parcours et peut se modifier sans relancer l’application.
- Le coût et le craft des filets sont exclus. Aucun relevé de filet n’est demandé pour comparer une capture.

La formule sans bonus est `30 + 0,15 × (niveau du premier parent + niveau du second)` en pourcentage.
117 + 117 donnent 65,1 %, 116 + 117 donnent 64,95 % (donc moins que le seuil).
Le calcul compare les niveaux accessibles et leur coût d’XP ; il peut monter davantage les parents si cela réduit
le coût moyen des tentatives. Un parent déjà niveau 200 permet par exemple d’atteindre 65,1 % avec l’autre au niveau 34.
Les prix d’XP manquants ne deviennent jamais gratuits.

**Le seuil vise la génération, pas une couleur précise.** Les probabilités individuelles restent dépendantes de la généalogie.
Le coût attendu d’une couleur conserve le partage entre les issues directes du modèle existant ; l’aperçu Dofus reste
nécessaire pour connaître les probabilités exactes avec les ancêtres réels. Un bonus Optimakina ou Almanax
n’est pas supposé sans information vérifiée. Le niveau seul atteint au plus 90 % : au-delà, le parcours par reproduction
sans bonus n’est pas proposé ; un achat reste comparable.

Changer le mode ou le seuil recalcule les étapes. Les acquisitions et étapes déjà terminées sont conservées lorsqu’elles
satisfont encore le nouveau parcours ; une ancienne préparation de niveau insuffisant est rouverte. Les attentes de marché
et dérogations de cycle ne sont pas effacées.

Recherche consultée le 19 novembre 2026 :
[Guide de l’éleveur, actualisé le 13 octobre 2026](https://www.dofuspourlesnoobs.com/guide-de-l-eleveur.html).
Le guide indique +0,15 point par niveau de parent, le partage entre couleurs et le bonus Optimakina de +20 points depuis la 3.7.
Les anciens guides indiquant encore +10 points sont périmés sur ce dernier point.
