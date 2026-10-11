# P6 — Bilan final technique du refactoring BestCrush v0.2.0

**Branche :** `refactor/codebase-cleanup`  
**HEAD contrôlé :** `1be9030c96780ccd12dbbd14c5cd14f84b1375cd`  
**Statut :** compilation, tests et fonctionnalités Windows **validés** ; **audit NuGet final encore à exécuter** ; **aucune approbation de merge/release demandée ou accordée**.

## Preuves de validation

### Vérification du dépôt

- `refactor/codebase-cleanup` est **50 commits devant `main`**, zéro derrière, selon la comparaison GitHub réalisée au début de P6. Aucun merge n'a été initié.
- `BestCrush/BestCrush.csproj` contient déjà `Version=0.2.0`, `ApplicationDisplayVersion=0.2.0`, `ApplicationVersion=8`. Ces valeurs sont **préexistantes** : P6 ne les change pas.
- `BestCrush/README.release.txt` documente déjà les évolutions v0.2.0. Une vérification finale du contenu sera nécessaire **avant toute publication**, sans publier maintenant.
- Les captures brutes `wire.jsonl` et la base locale `bestcrush.db` n'ont pas été ajoutées à l'arbre Git versionné consulté.

### Checkpoint Windows fourni par l'utilisateur — `1be9030`

| Contrôle | Résultat |
| --- | --- |
| `BestCrush.Network` | Build réussi |
| `BestCrush.NetworkProbe` | Build réussi |
| BestCrush Windows `net10.0-windows10.0.19041.0` | Build réussi |
| `Tests.BestCrush` | **394/394 réussis**, 0 échec, 0 ignoré |
| Avertissements `CA2014` Protobuf | **Disparus** |
| Avertissements restants | `NU1701` OpenCvSharp4.WpfExtensions et `CS8600/CS8629` Razor (préexistants) |

Le test `SqliteNativeVersionTest.NativeLibraryMustIncludeTheFixForCve20256965` fait partie de la suite et contrôle que la version de SQLite **réellement chargée** est au minimum **3.50.2**.

### Checkpoint manuel F5

Après le build et les 394 tests, l'utilisateur a confirmé **« Tout fonctionne »** sur les fonctionnalités usuelles précédemment définies :

- sélection du serveur puis capture HDV des équipements, runes et ressources ;
- décodage `jzs`, focus du dernier équipement par molette et F8 OCR ;
- concassage réseau `kch/kag`, runes, coefficients et overlays ;
- F7 et consultation de l'historique ;
- absence de régression visible après P5.1 sur ce parcours.

Cette validation F5 est **globale**, sans traces vidéo individuelles de chaque scénario. Aucun essai de saturation réel n'était demandé ; les limites mémoire sont couvertes par des tests synthétiques.

## Corrections acquises

- A–H : responsabilités, services, fenêtres et capture réseau restructurés ; suppression de l'ancien F9/OCR de concassage.
- S1–S4 : SQLite 2.1.13, sélection explicite de serveur, générations de capture, Stop/Start Npcap, chevauchements TCP, caches bornés.
- P1–P3 : protocole courant `jzs/kch/kag`, prix HDV et concassage en jeu.
- P4.0/P4.2 : caractère figé du snapshot historique et format des reliquats estimés.
- P5.1 : `_pending` limité à 8 Mio / 4096 segments par flux, trames tamponnées sous plafond et blocs de 64 Kio ; avertissements CA2014 corrigés.

## Réserves connues — volontairement non bloquantes dans le plan actuel

1. **P4.1 :** revalorisation automatique du dernier résultat à la mise à jour de prix. Tests unitaires réussis ; essai F5 du 11/10 avec montant inchangé, cause non tranchée (saisie encore en focus, événement `OnChange`, priorité de source). L'utilisateur a demandé de **ne pas bloquer** le refactoring dessus. Ne pas présenter cette fonctionnalité comme validée fonctionnellement.
2. **File réseau `Channel<DofusWireMessage>` non bornée** : risque théorique si le worker prend un retard prolongé sur la capture. Toute limite nécessite une politique explicite de pertes/d'attente pour éviter de supprimer des concassages ; P5.1 n'a pas modifié ce canal.
3. **Captures multi-adaptateurs** : pas de déduplication inter-adaptateurs prouvée ; doublons potentiels non reproduits.
4. **Avertissements `NU1701` et `CS8600/CS8629`** : pas supprimés ni masqués.
5. **Captures debug** : les dumps Base64 ne sont pas purgés automatiquement si le debug est activé ; à conserver hors du dépôt.

Ces réserves sont inventoriées dans `docs/codebase-cleanup-p5-p0-final-robustness-audit.md` et `docs/codebase-cleanup-p5-p1-network-memory-bounds.md`.

## Vérification P6 restant à recevoir : audit NuGet des dépendances résolues

Les projets `BestCrush`, `BestCrush.Domain`, `BestCrush.Migrations` et `Tests.BestCrush` référencent explicitement `SQLitePCLRaw.bundle_e_sqlite3 2.1.13`, mais le dernier checkpoint montre surtout **build + tests**. Vérifier qu'aucune dépendance transitivement résolue n'est encore signalée vulnérable, en particulier dans la cible Windows et le projet de migrations :

```powershell
dotnet list .\BestCrush\BestCrush.csproj package --vulnerable --include-transitive --framework net10.0-windows10.0.19041.0

dotnet list .\BestCrush.Migrations\BestCrush.Migrations.csproj package --vulnerable --include-transitive
```

Cette commande nécessite un accès au service d'audit NuGet. **Une commande en échec, une impossibilité de contacter le service ou un résultat incomplet ne constitue pas un audit « sans vulnérabilité ».** Signaler les sorties en cas d'avertissements `NU190x` ; ne pas modifier les versions des packages sans nouveau lot approuvé.

### Après l'audit

- Si l'audit résout les dépendances sans alerte nouvelle : **P6 clôturable techniquement avec réserves documentées**, sous réserve de l'acceptation explicite de la clôture par l'utilisateur.
- Si vulnérabilité ou échec de résolution : **ne pas déclarer P6 terminé**, examiner l'origine et proposer un correctif limité.
- Dans tous les cas : **ne pas merger `main`, ne pas changer la version, ne pas publier de release ou de package**, ni écraser les travaux/stashes locaux. Ces opérations nécessitent un accord distinct.
