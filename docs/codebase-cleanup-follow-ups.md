# Suivis séparés du refactoring BestCrush

## Dépendance SQLite native — avertissement de vulnérabilité élevée

- Signalement utilisateur reçu le 3 octobre 2026, après le checkpoint Windows A1
  (`61bcada`, SDK .NET 10.0.400, build Windows réussi et 24 tests réussis).
- Paquet signalé : `SQLitePCLRaw.lib.e_sqlite3` **2.1.10**.
- Gravité signalée par l'avertissement : **élevée**.
- Statut : **à analyser dans un lot de sécurité distinct** ; aucune dépendance
  mise à jour et aucun avertissement masqué dans le lot A2.

Le journal Windows A2 fourni le 4 octobre 2026 confirme **NU1903** pour la version
**2.1.10**, avec l'avis **GHSA-2m69-gcr7-jv3q** :
https://github.com/advisories/GHSA-2m69-gcr7-jv3q.
Le build Windows est réussi et les suites ciblée/complète passent avec 68/92
tests au HEAD `38f1978`. Cela ne résout pas la vulnérabilité. La restauration et
l'audit NuGet n'ont pas été exécutés dans l'environnement de préparation A2/A3,
qui ne dispose pas du SDK .NET.

Le dépôt référence `Microsoft.EntityFrameworkCore.Sqlite` 9.0.8 dans
`BestCrush/BestCrush.csproj`, `BestCrush.Domain/BestCrush.Domain.csproj` et
`BestCrush.Migrations/BestCrush.Migrations.csproj`, ainsi que
`Microsoft.Data.Sqlite.Core` 9.0.8 dans `Tests.BestCrush/Tests.BestCrush.csproj`.

Pour le lot ultérieur : identifier la chaîne transitive et examiner l'avis de
sécurité, puis choisir une mise à jour compatible.
Valider séparément le chargement de SQLite natif sous Windows, les migrations
sur une copie de base existante, les lectures/écritures et les tests BestCrush.

## BUG EXISTANT — CORRECTION FONCTIONNELLE À VALIDER : chevauchement TCP en attente

Source : `BestCrush.NetworkProbe/Capture/TcpReassembler.cs`, méthode `Push`.
Constat par lecture du code, caractérisé par un test A3. L'utilisateur a déclaré
le checkpoint Windows A3 validé au HEAD `7b42061` : 111 tests réseau et 203 tests
au total réussis, aucun échec. Cette exécution n'a pas été reproduite dans
l'environnement de préparation, dépourvu de `dotnet`.

Séquence minimale (numéros de séquence TCP, longueurs en octets) :

1. `Push(100, 2 octets)` émet 100–101 ; prochain attendu : 102.
2. `Push(104, 4 octets)` met 104–107 en attente.
3. `Push(102, 4 octets)` émet 102–105 ; prochain attendu : 106.
4. Le segment en attente commence toujours à 104. La recherche par clé exacte
   106 ne le trouve pas : ses octets 106–107 ne sont pas émis.
5. `Push(108, 2 octets)` reste bloqué tant qu'aucun nouveau segment ne fournit
   les octets attendus à partir de 106.

Le test `Tests.BestCrush/Network/TcpReassemblerTest.cs`,
`OverlappingQueuedSegmentCurrentlyRemainsBlockedAfterGapIsFilled`, fige ce
comportement et sa reprise après réception d'un segment à 106. Il ne décrit pas
un comportement cible corrigé. Aucune modification du réassemblage dans A3.
La correction éventuelle devra avoir son propre accord et son propre commit.

## Préalable à la Phase E — routage métier encore non caractérisé

La validation Windows A3 ne couvre pas les méthodes privées Windows de
`DofusNetworkCaptureService`. Le [rapport A3](codebase-cleanup-a3-network-tests.md)
conserve les obstacles et les adaptations minimales proposées, non appliquées.
Le [protocole A4](codebase-cleanup-a4-windows-manual-validation.md) observe leurs
effets dans l'UI, sans remplacer les tests d'intégration manquants.

Avant toute extraction des méthodes de traitement réseau, caractériser le vrai
dispatch et ses effets : minimum positif `jzn` par lot et absence d'offres ;
corrélation `kei/kef/kbd` par OfferId et limite inclusive de cinq secondes ;
exclusion du rafraîchissement des équipements par `kef` ; associations UID/ItemId
et validation du type ; lignes `kci` inconnues, ordre des coefficients et
conversions ; indépendance du flag coefficient et du résultat ; écritures et
notifications après déduplication ; agrégation/valorisation/constitution du
snapshot ; ordre du Channel, horodatages, retours anticipés et erreurs.

Statut A4 : **ouvert, préalable obligatoire à la Phase E**. Aucun point d'entrée
de test ni autre adaptation de production n'est ajouté par le lot documentaire.

## BUG EXISTANT — CORRECTION FONCTIONNELLE À VALIDER : changement de serveur

Constat A4 par lecture, à confirmer sous Windows via A4-INT-02 :

- `BestCrush/Services/CurrentServerState.cs::SelectServer` assigne le nom sans
  événement ni effacement d'état. `Server.razor::OnParametersSet` l'appelle sans
  demander de recalcul du focus. Un R déjà affiché peut donc conserver les
  valeurs du serveur précédent jusqu'à un nouveau focus ou une notification.
- `DofusNetworkCaptureService` utilise `currentServerState.ServerName` au
  traitement pour étiqueter observations et dernier équipement ; il ne vérifie
  pas leur correspondance au serveur de la connexion DOFUS. Les caches ne sont
  pas purgés par `SelectServer`. Une sélection BestCrush différente du client
  peut donc attribuer de nouvelles données au mauvais serveur.

Le filtre ordinal de `LastNetworkEquipmentState.GetForServer` protège seulement
la lecture de son snapshot étiqueté ; ce n'est pas une identification du serveur
réseau. Aucun changement de sélection, cache, attribution ou rafraîchissement
dans A4. Ne pas créer volontairement d'observations mal attribuées sur la base
utilisateur pour reproduire ce risque.

## BUG EXISTANT — CORRECTION FONCTIONNELLE À VALIDER : revalorisation de C

Constat A4 par lecture, à confirmer via A4-CR-05 : dans
`BestCrush/Services/CrushSessionService.cs`, seul l'ancien `StartNew` s'abonne à
`MarketDataChangeNotifier.Changed`. Le parcours réseau appelle
`ApplyNetworkCrushAsync`, qui ne crée pas cet abonnement ; `Show` ne le fait pas
non plus. Les prix ultérieurs ne déclenchent donc pas
`RefreshRuneValuesFromMarketAsync` dans ce parcours, même si cette méthode existe.

Le résultat courant garde sa valorisation initiale jusqu'à un nouveau résultat.
L'historique doit, lui, conserver son snapshot initial dans tous les cas.
Une éventuelle correction de l'overlay ne doit pas recalculer les anciennes
opérations. Aucun abonnement ni calcul modifié dans A4.

## BUG EXISTANT — CORRECTION FONCTIONNELLE À VALIDER : lot estimé dans History

Constat A4 par lecture, variante à confirmer via A4-CR-04 :

- `CrushSessionService.BuildRuneLotBreakdown` représente un reliquat estimé par
  `Count = nombre d'unités restantes`, `LotQuantity = taille du lot de secours`,
  `LotPrice = prix de ce lot`, `IsEstimated = true`.
- `CrushSessionOverlayPage.BuildExcelTerm` exprime correctement ce cas par
  `Count * LotPrice / LotQuantity` lorsque la taille du lot dépasse 1.
- La boucle de lots des détails de concassage dans
  `BestCrush/Components/Pages/History.razor` affiche pour tous les cas
  `Count × LotQuantity = Count × LotPrice`, puis la mention « estimé », sans
  appliquer ce dénominateur au sous-total affiché.

Exemple déduit du code, non présenté comme une capture réelle : reliquat de
3 unités, seul prix de lot x10 à 1 000 K. Valeur estimée 300 K ; la ligne
historique affiche `3×10 = 3 000 K`. Le total du snapshot est stocké séparément
et n'est pas recalculé par cette ligne UI. Aucune formule ni valeur modifiée
dans A4, aucune comparaison Excel exécutée.

## État des lots réservés à la fin de la préparation A4

SQLite **NU1903**, `SQLitePCLRaw.lib.e_sqlite3` **2.1.10**, avis
**GHSA-2m69-gcr7-jv3q**, et le défaut de chevauchement TCP restent ouverts pour
des lots distincts. A4 n'a modifié ni dépendance, ni protocole, ni code produit.
L'exécution manuelle Windows A4 reste en attente ; aucune Phase B n'est engagée.
