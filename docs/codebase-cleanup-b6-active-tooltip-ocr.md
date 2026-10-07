# B6 — Réduction OCR au pipeline actif F8

## Cartographie avant modification

Base : `bc5e9a96f70150d5bb0d799ecbd10acb6a302826`, branche `refactor/codebase-cleanup`, checkout propre. Checkpoint B5 déclaré validé par l’utilisateur : build et 203 tests OK, vérifications fonctionnelles OK.

| Membre | Consommateurs au HEAD B5 | Nécessaire à F8 | Décision |
|---|---|---|---|
| `_ocrEngine` | Constructeur ; lectures texte et anciennes lectures brute/lignes | Oui | Conserver |
| `DofusOcrService` | `BestCrush/MauiProgram.cs`, `BestCrush/Services/DofusItemTooltipDetectionService.cs` | Oui | Conserver |
| `RecognizeTextAsync` | `RecognizeUpscaledTextAsync`, `RecognizeNumberAsync` | Oui | Conserver |
| `PrepareImageForOcr` | `RecognizeUpscaledTextAsync`, `RecognizeCoefficientAsync`, `RecognizeMarketQuantityAsync` | Oui | Conserver |
| `PrepareNumberImageForOcr` | `RecognizeMarketQuantityAsync`, `RecognizeNumberAsync` | Non | Supprimer |
| `RecognizeUpscaledTextAsync` | `RecognizeTooltipLotQuantityAsync`, `RecognizeNumberAsync`, `BestCrush/Services/DofusItemTooltipDetectionService.cs` | Oui | Conserver |
| `RecognizeTooltipLotQuantityAsync` | `BestCrush/Services/DofusItemTooltipDetectionService.cs` | Oui | Conserver |
| `RecognizeCoefficientAsync` | Aucun | Non | Supprimer |
| `WriteCoefficientOcrDebugAsync` | `RecognizeCoefficientAsync` | Non | Supprimer |
| `FormatNullableCoefficient` | `WriteCoefficientOcrDebugAsync` | Non | Supprimer |
| `TryParseCoefficientText` | `RecognizeCoefficientAsync` | Non | Supprimer |
| `PrepareCoefficientNumberRegion` | `RecognizeCoefficientAsync` | Non | Supprimer |
| `RecognizePriceAsync` | Aucun | Non | Supprimer |
| `RecognizeMarketQuantityAsync` | Aucun | Non | Supprimer |
| `RecognizeMarketQuantityVisually` | `RecognizeMarketQuantityAsync` | Non | Supprimer |
| `WriteMarketQuantityVisualDebugImage` | `RecognizeMarketQuantityVisually` | Non | Supprimer |
| `RecognizeRawTextAsync` | `RecognizeCoefficientAsync`, `RecognizeMarketQuantityAsync` | Non | Supprimer |
| `WriteMarketQuantityOcrDebugAsync` | `RecognizeMarketQuantityAsync` | Non | Supprimer |
| `EscapeOcrDebugText` | `WriteCoefficientOcrDebugAsync`, `WriteMarketQuantityOcrDebugAsync` | Non | Supprimer |
| `DescribeOcrCharacters` | `WriteCoefficientOcrDebugAsync`, `WriteMarketQuantityOcrDebugAsync` | Non | Supprimer |
| `EscapeOcrDebugCharacter` | `DescribeOcrCharacters` | Non | Supprimer |
| `FormatNullableQuantity` | `WriteMarketQuantityOcrDebugAsync` | Non | Supprimer |
| `MarketQuantityVisualRecognitionResult` | `RecognizeMarketQuantityAsync`, `RecognizeMarketQuantityVisually`, `WriteMarketQuantityOcrDebugAsync` | Non | Supprimer |
| `TryParseMarketQuantity` | `RecognizeMarketQuantityAsync` | Non | Supprimer |
| `TryParseOcrNumber` | `RecognizeNumberAsync` | Non | Supprimer |
| `RecognizeNumberAsync` | `RecognizePriceAsync` | Non | Supprimer |
| `NormalizeText` | `RecognizeTextAsync`, `RecognizeCoefficientAsync`, `RecognizeMarketQuantityAsync` | Oui | Conserver |
| `RecognizeLinesAsync` | `RecognizeTooltipLinesAsync` | Non | Supprimer |
| `RecognizeTooltipLinesAsync` | Aucun | Non | Supprimer |
| `GetTileStarts` | `RecognizeTooltipLinesAsync` | Non | Supprimer |
| `DofusOcrLine` | `RecognizeLinesAsync`, `RecognizeTooltipLinesAsync` uniquement | Non | Supprimer |

## Périmètre des recherches

Cartographie de tous les membres publics/privés du service, de son champ et des
records, puis recherche de chaque identifiant dans tous les fichiers suivis Git.
Les références documentaires/patches historiques sont distinguées du code compilé.

- Appels directs et indirects F8 : seul consommateur de production extérieur à la
  classe, `DofusItemTooltipDetectionService` ; deux appels dans `DetectAsync`.
- Constructeur : injection dans ce détecteur, inscription singleton dans
  `MauiProgram.cs`, inchangée. La classe n'implémente aucune interface et n'est pas
  partial ; aucun autre fichier ne fournit de membre.
- Résolutions IServiceProvider, callbacks, événements, Razor/XAML : aucun accès aux
  membres supprimés ; pas de binding ou de delegate vers ces méthodes.
- Réflexion : aucun accès dynamique/scan d'assembly vers ces méthodes ou DTO trouvé.
  L'usage de réflexion de `CurrentVersion` concerne la version.
- Branches Windows inspectées sans filtrage : aucun consommateur supplémentaire.
- Tests et `BestCrush.NetworkProbe` : aucune référence au service ou aux membres
  supprimés. Le Probe reste indépendant et aucun de ses fichiers n'est modifié.
- Projets/props/targets : aucun lien explicite vers `DofusOcrService.cs` dans le
  Probe ou les tests ; aucune modification des liens ou packages.

## Suppressions appliquées

Six méthodes publiques sans consommateur actif : `RecognizeCoefficientAsync`,
`RecognizePriceAsync`, `RecognizeMarketQuantityAsync`, `RecognizeNumberAsync`,
`RecognizeLinesAsync`, `RecognizeTooltipLinesAsync`.

Seize helpers privés exclusivement accessibles depuis ces entrées :

- `PrepareNumberImageForOcr`
- `WriteCoefficientOcrDebugAsync`
- `FormatNullableCoefficient`
- `TryParseCoefficientText`
- `PrepareCoefficientNumberRegion`
- `RecognizeMarketQuantityVisually`
- `WriteMarketQuantityVisualDebugImage`
- `RecognizeRawTextAsync`
- `WriteMarketQuantityOcrDebugAsync`
- `EscapeOcrDebugText`
- `DescribeOcrCharacters`
- `EscapeOcrDebugCharacter`
- `FormatNullableQuantity`
- `TryParseMarketQuantity`
- `TryParseOcrNumber`
- `GetTileStarts`

Total : **22 méthodes supprimées**. Deux records supprimés : `DofusOcrLine`
et `MarketQuantityVisualRecognitionResult`. Imports devenus inutiles retirés :
`System.Globalization` et `System.Text`. `Windows.Globalization` reste nécessaire.

## Sous-graphe conservé

- Constructeur et `_ocrEngine` : moteur Windows fr-FR, fallback profil utilisateur,
  même exception si aucun moteur disponible.
- `RecognizeUpscaledTextAsync` : `DetectAsync` pour le titre, puis
  `RecognizeTooltipLotQuantityAsync` pour le lot.
- `PrepareImageForOcr` : appelé par `RecognizeUpscaledTextAsync`, même agrandissement
  ×4 cubique, même retour si source vide et même chemin `-ocr.png`.
- `RecognizeTextAsync` : appelé par `RecognizeUpscaledTextAsync`, mêmes lectures
  WinRT, formats bitmap, contrôle d'annulation et libérations `using`.
- `NormalizeText` : appelé par `RecognizeTextAsync`, regex et Trim identiques.
- `RecognizeTooltipLotQuantityAsync` : appel direct de `DetectAsync` conservé.

Les cinq méthodes conservées et le constructeur sont identiques à B5, signature et
corps inclus. Aucune réécriture ni modification de visibilité.

## Statut exact de RecognizeTooltipLotQuantityAsync

**Conservée intégralement, toujours appelée.** Au HEAD B5,
`DofusItemTooltipDetectionService.DetectAsync` l'appelle à la ligne 143,
après extraction de la région du lot et avant reconnaissance de l'équipement.
Ce fichier reste identique : l'emplacement de l'appel reste le même après B6.
Les retours anticipés zéro/plusieurs infobulles sont également inchangés.

- Le résultat est placé dans `DofusItemTooltipCandidate.LotQuantity`, même si la
  sélection du focus n'en dépend pas actuellement.
- `ExtractLotQuantityRegion` produit `tooltip-lot-quantity.png` ; la chaîne conservée
  produit ensuite `tooltip-lot-quantity-ocr.png` dans le dossier de capture.
- L'OCR du titre, son image agrandie et `WriteRecognizedTitleDebug` restent inchangés.
- La méthode vérifie l'annulation à l'entrée et attend réellement le second OCR.
  Aucun délai artificiel n'a été trouvé dans ce chemin ; la durée de cette lecture
  reste partie du traitement F8. Aucun raccourcissement/optimisation effectué.
- Les exceptions ne sont pas masquées dans la méthode. Le worker F8 conserve son
  traitement d'`OperationCanceledException`, son diagnostic `ShowCaptureFailed`
  avec `ex.Message` et son bloc `finally`.
- Le nettoyage demeure `DeleteCaptureArtifacts` puis `TryDeleteCaptureDirectory` :
  suppression conditionnée par `DevTool_RemoveScreenshotsByDefault`, avec les mêmes
  contrôles de chemin. Les `Mat`, streams et `SoftwareBitmap` gardent leurs `using`.

Supprimer cet appel aurait modifié lectures, artefacts, exceptions et durée du
parcours : ce changement est explicitement exclu de B6.

## Preuves après modification

- Toutes les lignes restantes de `DofusOcrService.cs` sont une sous-séquence ordonnée
  exacte du fichier B5 : suppression uniquement.
- Vérification exacte du constructeur et des cinq méthodes conservées réussie.
- Aucune référence aux membres/types supprimés dans le code, les projets, la
  configuration, les tests ou le Probe restants.
- Seuls `BestCrush/Services/DofusOcrService.cs` et ce rapport changent. Donc capture,
  détecteur, reconnaissance, sélection des candidats, textes/erreurs/debug F8,
  nettoyage, overlays, focus/génération/sémaphore, réseau et historique restent
  identiques à B5. F7/molette inchangés, F9 reste libre.
- Aucune modification de DI, projet, package, solution, ressource ou test.
- `git diff --check` : réussi.
- `dotnet --version` échoue avec `dotnet: command not found`. Aucun build, test ou
  scénario Windows exécuté ici. Les comparaisons de source ne remplacent pas cette
  validation.

Aucun correctif OCVS002, nullable, SQLite NU1903, TCP, serveur, revalorisation,
History, routage A3, lifetime/DbContext ou performance. Les lacunes A3 restent
ouvertes avant la Phase E. Aucune Phase C commencée.

## Checkpoint Windows B6

Depuis la racine :

```powershell
git switch refactor/codebase-cleanup
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Référence attendue : 203 tests réussis, 0 échec. Les tests existants n'exécutent
pas le pipeline Windows OCR ; la validation manuelle reste nécessaire.

Rejouer les scénarios A4 correspondants, en comparant au comportement B5 :

1. F8 sans serveur : même refus/diagnostic.
2. F8 sur équipement connu, serveur choisi : même identification et focus.
3. F8 sans infobulle : même diagnostic, pas de focus inventé.
4. Plusieurs infobulles si facilement reproductible : même diagnostic ; sinon
   noter non exécuté, ne pas déclarer le scénario réussi.
5. Équipement inconnu/non reconnu : même résultat et message.
6. F8 successifs : ordre de traitement et focus final identiques.
7. Overlay Mise à jour marché masqué puis rouvert : mêmes diagnostics conservés.
8. Focus Rentabilité : valeurs et actualisations identiques.
9. Conservation des artefacts F8 activée : vérifier titre, région lot et images
   agrandies dans le dossier de capture.
10. Conservation désactivée : même nettoyage du dossier après traitement.
11. Clic molette : dernier équipement réseau fiable du serveur courant, sans OCR.
12. F7 : masquage/restauration habituels.
13. F9 : aucune action BestCrush.

En cas d'écart : relever serveur, équipement, étape, message exact, journal et
artefacts si conservés. Arrêt après B6 ; attendre le checkpoint Windows avant C.
