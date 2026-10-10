# Phase C — Nettoyage isolé du code mort restant

## Référence et portée

Branche `refactor/codebase-cleanup`, base `a8e2084b14648a80cef489c6f03e7d7b6fb2c2bc`.
Checkout propre au départ. L'utilisateur a validé le checkpoint **automatisé** B6 :
build/test réussi, 203 tests réussis, 0 échec, 4 avertissements.
Ce retour n'est pas présenté comme une exécution des scénarios manuels B6.

Phase C réalisée en un lot : membres sans consommateurs, un ancien modèle, un
service orphelin et sa seule inscription DI, trois images de détection historiques.
Aucune intégration de `experiment/market-history-probe`, aucun changement de main,
aucune Phase D. Aucun changement de version, schéma ou migration.

## Analyse et suppressions justifiées

| Élément supprimé | Preuve de non-utilisation / consommateurs examinés |
|---|---|
| `BestCrush.Domain.Models.CurrentVersion` | Aucune référence qualifiée, navigation, DbSet, configuration Entity, migration ou snapshot EF ne le référence. Le commentaire « EF ctor » ne correspond plus au modèle EF effectif. Pas de scan d'assembly/configuration dynamique trouvé. L'homonyme `BestCrush.CurrentVersion`, utilisé par App, Footer, Splash et le bootstrap, reste intact. |
| `RunesService.ClearCachesAsync` | Seule déclaration, aucune référence C#/Razor/test/Probe ; corps sans effet retournant `Task.CompletedTask`. |
| `MarketPriceService.GetLatestObservationsAsync` | Seule déclaration ; les consommateurs actuels utilisent les observations par serveur ou une observation ciblée. |
| `MarketPriceService.GetLatestUnitPriceAsync` | Seule déclaration ; son appel interne à `GetLatestObservationAsync` disparaît mais cette dernière reste utilisée par les tests et est conservée. |
| `MarketPriceService.ClearManualAsync` | Aucun appelant ; les saisies actives utilisent `ClearLocalAsync`, conservée avec son comportement d'effacement actuel. |
| `CoefficientService.ClearManualAsync` | Même constat, aucun appelant ; aucun changement de `ClearLocalAsync`. |
| `CraftCostService.CalculateAsync` | Aucun consommateur de cette méthode sur une instance de CraftCostService. Les appels homonymes de Server/Overlay ciblent EquipmentProfitabilityService, conservé. |
| `CraftCostService.Calculate` à deux arguments | Tous les appels vérifiés dans EquipmentProfitabilityService et les tests CraftCost utilisent trois arguments. L'ancien commentaire « conservé pour les appelants/tests » est devenu inexact ; supprimé avec cette façade. |
| `CharacteristicsService` entier | Unique référence extérieure : `MauiProgram.AddSingleton`. Pas d'injection, résolution IServiceProvider, instance, interface ou callback. Les trois méthodes et le cache/sémaphore interne n'ont plus de racine active. |
| `CharacteristicExtensions.ToDofusDbKeyword` | Mapping inverse sans appelant dans le dépôt ; le mapping actif `CharacteristicFromDofusDbKeyword` reste identique, de même que poids et noms affichés. |
| `AsyncProgressExtensions.DeriveStep` | Aucune référence ; `DeriveSubtask`, `ReportStep`, `Report` et `ToMultiSearchProgress` restent utilisés et conservés. |
| `FocusedEquipmentState.HasEquipment` | Accesseur calculé sans consommateur C#, Razor/XAML, sérialisation ou réflexion trouvé ; Equipment/SetEquipment/Clear restent identiques. |
| `OverlayService.WmMouseWheel`, `WmMouseHWheel` | Constantes sans référence. Aucun changement de hook, clic molette, F7, F8 ou F9. |
| `crush-result-header.png`, `hdv-offers-header.png`, `hdv-sell-header.png` | Uniquement chargées par les détecteurs retirés en B5. Aucun nom, chemin ou mécanisme d'énumération/chargement restant ne les consomme. L'inclusion MauiAsset générique empaquetait encore ces fichiers sans les rendre utilisés. |

Douze méthodes retirées au total : les neuf méthodes/façades listées hors service
orphelin, plus les trois méthodes de `CharacteristicsService`
(`GetDofusDbCharacteristicsAsync`, `GetCharacteristicFromDofusDb`,
`GetCharacteristicFromDofocusAsync`). Les deux constructeurs de l'ancien modèle
ne sont pas inclus dans ce décompte.

## Étendue des recherches

Inventaire des déclarations C# de l'application et du domaine (méthodes, types,
champs/propriétés/constants), recherche des références dans le dépôt entier,
puis examen des candidats au lieu de supprimer à partir du seul nombre de résultats.
Les homonymes et surcharges ont été distingués par leurs consommateurs.

Vérifications croisées : C#, Razor/XAML, DI explicite, GetRequiredService/GetService,
constructeurs, événements/callbacks, classes partial, branches Windows, tests,
NetworkProbe et liens Compile des projets/props/targets. Aucun mécanisme de
réflexion/activation dynamique vers les éléments supprimés trouvé. Les usages
`JsonElement.TryGetProperty` du Probe sont des lectures de protocole, pas des
références par réflexion aux candidats.

Pour CurrentVersion, contrôle spécifique de BestCrushDbContext, configurations,
navigations, migrations et snapshots : aucun enregistrement du type domaine.
Aucun fichier EF n'est modifié et aucune migration n'est requise.

Les occurrences dans les rapports historiques et patches archivés ne sont pas des
consommateurs exécutables. Ces archives sont conservées et le présent rapport
consigne l'état courant.

## Éléments examinés et conservés

| Élément | Raison de conservation |
|---|---|
| `RunesService.GetRunesAsync`, `GetRunesByCharacteristicAsync`, `IsPowerVariant` | Appels réels depuis Server.razor ; le paramètre forceRefresh historique n'est pas retiré. |
| `CraftCostService.Calculate` à trois arguments et `BuildEquipmentPurchase` | Rentabilité active et tests ; corps conservés exactement. |
| GetLatestObservationAsync, GetLatestObservationsForServerAsync, GetHistoryAsync, CalculateValue, CalculateMinimumPurchaseCost, ClearLocalAsync | Appels métier et/ou tests A2. Aucun algorithme économique modifié. |
| `AsyncProgressExtensions`, `ImageCacheExtensions`, `TextColorExtensions` | Un nom de classe d'extensions sans référence explicite ne prouve pas son inutilité : leurs méthodes sont utilisées. |
| Champs Win32 `Monitor`, `MouseData`, `Time`, `ExtraInfo` | Contrat StructLayout/marshalling ; absence de lecture C# insuffisante pour les retirer. |
| Modèles EF, DTO d'historique/réseau, presets sérialisés | Contrats persistés, conventions EF et sérialisation ; aucune suppression par simple comptage lexical. |
| Compteurs/snapshots hérités dans CrushSessionService | Lecteurs actifs conservés en B4 ; pas de simplification des gardes ni du contrat de snapshot en C. |
| Options Equipment/Rune/Resource/CoefficientCapture et debug | Toujours utilisées par le réseau et/ou le nettoyage F8 ; interface et clés de préférences inchangées. |
| DofusOcrService et RecognizeTooltipLotQuantityAsync | Pipeline F8 confirmé en B6, inchangé. |
| Packages DoFocus, DofusDB, EF, Semver, OpenCV, WinRT/Direct3D | Usages actifs : coefficient de secours, catalogues, persistence/version et F8. Aucun package supprimé. |
| README domaine, rapports A/B, patches historiques | Toujours explicatifs ou archivés ; ne pas les traiter comme du code compilé. |
| NetworkProbe et tout traitement réseau | Hors refactoring C ; aucune modification, même si un candidat local paraît peu utilisé. |

Les suppressions constituent le périmètre prouvé de ce lot ; ce rapport ne prétend
pas établir une preuve formelle d'absence de tout code mort dans la totalité du dépôt.

## Fichiers concernés

Supprimés :

- `BestCrush.Domain/Models/CurrentVersion.cs`
- `BestCrush.Domain/Services/CharacteristicsService.cs`
- `BestCrush/Resources/Raw/crush-result-header.png`
- `BestCrush/Resources/Raw/hdv-offers-header.png`
- `BestCrush/Resources/Raw/hdv-sell-header.png`

Modifiés :

- `BestCrush.Domain/Models/Characteristic.cs`
- `BestCrush.Domain/Models/ProgressMessage.cs`
- `BestCrush.Domain/Services/RunesService.cs`
- `BestCrush.Domain/Services/MarketPriceService.cs`
- `BestCrush.Domain/Services/CoefficientService.cs`
- `BestCrush.Domain/Services/CraftCostService.cs`
- `BestCrush/MauiProgram.cs` : une inscription singleton supprimée uniquement.
- `BestCrush/Services/FocusedEquipmentState.cs`
- `BestCrush/Services/OverlayService.cs`

Ajouté : ce rapport. Aucun projet, test, fichier de solution ou fichier de migration
modifié ; aucune modification de lifetime ou de configuration DbContext.

## Vérifications

- Vérification exacte : chaque fichier C# modifié est identique à B6 après retrait
  des seuls blocs inventoriés. Zéro ligne ajoutée dans le code de production.
- Absence de références résiduelles aux symboles uniques et images supprimés dans
  les fichiers suivis restants, hors documentation/patches historiques.
- Les surcharges actives de calcul craft, les interfaces, les consommateurs Razor
  et les 203 tests déclarés restent inchangés.
- `git diff --check` : réussi.
- `dotnet --version` : échec immédiat, `dotnet: command not found`.
- Build/tests Windows non exécutés ici ; validation différée au checkpoint unique.

Aucun bug traité. SQLite NU1903, chevauchement TCP, attribution serveur,
revalorisation du concassage, lots estimés History, lifetimes et lacunes de
routage A3 avant Phase E restent réservés. Aucun warning corrigé opportunément.

## Checkpoint unique de fin de Phase C

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Référence : 203 tests réussis, 0 échec. Vérifier également démarrage/chargement du
catalogue, recherche et rentabilité craft, saisie puis effacement de prix/coefficient,
F8 et focus, clic molette, F7 et concassage réseau/historique ; F9 reste libre.
Les scénarios manuels B6 restent pertinents, leur validation n'ayant pas été déclarée
avec le seul checkpoint automatisé B6.

Arrêt après Phase C. Ne pas commencer D avant validation de l'utilisateur.
