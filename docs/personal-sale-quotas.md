# Repère de mise en vente personnelle

Le quota indicatif est la moitié de la moyenne quotidienne des articles vendus sur les sept derniers jours, arrondie au supérieur : `ceil(articles vendus sur 7 jours / 14)`. Par exemple, 35 articles donnent un repère de 3 par période glissante de 24 heures. Il ne prédit ni la saturation réelle ni la part de marché du joueur.

Une confirmation de dépôt HDV ajoute la quantité exacte du lot dans `global-sales.db/personal_market_listings`. Les événements sont dédupliqués par serveur et identifiant d’annonce. Les requêtes de dépôt, retraits d’inventaire et simples consultations de l’HDV ne sont pas comptés. Les annonces antérieures au suivi ne sont pas reconstituées.

La lecture additionne uniquement les dépôts dans `]maintenant − 24 heures, maintenant]`. Les âmes de boss utilisent l’identité du boss issue des offres ou historiques vérifiés, sur le même serveur, afin de réunir leurs identifiants d’objet connus. Une correspondance contradictoire ne permet pas ce regroupement. Les stocks et prix ne sont pas modifiés.

La quantité restante apparaît dans les résultats Achat / Vente et Pierres d’âme, et dans les lignes d’activités utilisant le même modèle. À zéro, le repère devient la croix rouge déjà employée pour l’arrêt des achats. Il ne retire pas la ligne. L’infobulle indique le volume habituel, le repère conseillé et les dépôts détectés. Un changement de dépôts actualise ces compteurs sans relancer l’analyse économique ; une minuterie vérifie chaque minute l’expiration des dépôts et des historiques. Sans historique exploitable, le repère reste absent.

## Validation du protocole

Le dépôt réel de 10 Conques marines à 910 K sur Tylezia, le 11 novembre 2026 à 00:21:27 UTC, a confirmé les routes actuelles `jyq` (demande) et `jxt` (confirmation). Le profil central conserve les deux anciennes routes, séparément, et ajoute `market_listing_request_current` et `market_listing_created_current`. La requête contient l’UID physique, la quantité et le prix ; la confirmation donne le prix du lot, l’identifiant de l’objet, l’UID propre à l’annonce et la quantité.

Un dépôt n’est compté qu’après une demande du client et une confirmation du serveur sur la même connexion, dans les 15 secondes, avec la même quantité et le même prix et un périmètre de capture encore actif. L’UID d’instance n’est jamais assimilé à celui de l’annonce. La confirmation consommée et la clé unique de stockage empêchent les doubles comptes. Les anciennes routes exigent également une demande correspondante pour ce nouveau compteur ; leur comportement antérieur de diagnostic reste conservé.

Les octets réels du test sont repris dans `CurrentMarketListingTest`. Le rejeu vérifie le décodage, l’enregistrement unique, les directions, les prix/quantités différents, les messages incomplets, les connexions différentes, le délai et la révocation du serveur. Ce test réel concernait une ressource ; un dépôt réel d’âme de boss reste une vérification complémentaire possible. Le regroupement des variantes d’âme et le passage du quota à zéro sont couverts par les tests automatisés.

Le dépôt de test ayant précédé la correction, son enregistrement unique peut être repris depuis cette paire exacte de messages avec le script local `resale-quota/recover_conque_sale.py` (aperçu par défaut, `--apply` pour la reprise). Il n’invente aucune annonce et ne modifie ni les prix ni les stocks.

## Routine

L’activité « Capturer des âmes de boss », priorité 2, ouvre `/modules/pierres-ame`. Elle s’ajoute aux anciennes routines sans modifier leurs coches, leurs activités personnalisées ou leur historique. Son identité stable empêche les doublons ; une suppression utilisateur est conservée.
