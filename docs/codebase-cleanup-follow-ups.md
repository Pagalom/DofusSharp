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
Constat par lecture du code ; un test A3 décrit le comportement, mais son
exécution doit être confirmée au checkpoint Windows.

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
