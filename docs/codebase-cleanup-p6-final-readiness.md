# P6 — Bilan final technique du refactoring BestCrush v0.2.0

**Branche :** `refactor/codebase-cleanup`  
**HEAD contrôlé :** `1be9030c96780ccd12dbbd14c5cd14f84b1375cd`  
**Statut :** **P6 clôturé techniquement le 12/10/2026**, avec les réserves ci-dessous ; compilations Windows, **394/394 tests**, contrôle F5 et **audits NuGet des projets Windows et migrations réussis**. **Aucune approbation de merge/release demandée ou accordée**.

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

## Audit NuGet final — contrôlé le 12/10/2026

Sorties PowerShell fournies par l'utilisateur, interrogations avec `https://api.nuget.org/v3/index.json` :

| Projet | Audit lancé | Résultat |
| --- | --- | --- |
| `BestCrush/BestCrush.csproj` | `--vulnerable --include-transitive --framework net10.0-windows10.0.19041.0` | **Aucun package vulnérable selon les sources actuelles** |
| `BestCrush.Migrations/BestCrush.Migrations.csproj` | `--vulnerable --include-transitive` | **Aucun package vulnérable selon les sources actuelles** |

Une tentative d'exécution du second audit a échoué parce que deux lignes PowerShell avaient été concaténées (`--include-transitivedotnet`). **Une nouvelle exécution complète du second audit a ensuite réussi et affiché le résultat ci-dessus**. Ce n'est ni une erreur du projet ni un échec d'audit restant.

La restauration du projet BestCrush affiche encore **NU1701** : compatibilité ciblée `OpenCvSharp4.WpfExtensions`, **pas** une vulnérabilité `NU190x`. Ce warning et les avertissements nullable déjà documentés restent suivis en dette technique.

Ces contrôles attestent de l'absence de packages **connus comme vulnérables selon les sources NuGet interrogées au moment du test**. Ils ne constituent pas une garantie générale de sécurité.

## Conclusion de P6

**Clôture technique du refactoring v0.2.0 avec réserves documentées.** L'utilisateur a confirmé la réussite du parcours F5 après P5.1, et les checkpoints Windows montrent des compilations réussies et **394 tests sur 394**. Les deux audits NuGet ci-dessus ne signalent aucune vulnérabilité. Aucun obstacle démontré ne justifie un nouveau chantier correctif immédiat.

Les réserves **P4.1 rafraîchissement courant non confirmé en F5**, **canal de messages non borné**, **risque de doublons entre adaptateurs**, **warnings de compatibilité/nullabilité** et **rétention des traces de debug** demeurent ouvertes et non dissimulées.

**P6 est clôturé techniquement, sans fusion ni livraison :** rester sur `refactor/codebase-cleanup`, ne pas toucher à `main`, ne pas changer la version, ne pas produire de release ni de package sans nouvelle autorisation explicite. Toute préparation de publication doit revoir `BestCrush/README.release.txt` et organiser un checkpoint de packaging distinct.
