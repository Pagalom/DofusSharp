$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Read-Source([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Fichier introuvable : $Path"
    }

    $text = [System.IO.File]::ReadAllText(
        (Resolve-Path $Path),
        [System.Text.Encoding]::UTF8
    )

    return $text.Replace("`r`n", "`n")
}

function Write-Source(
    [string]$Path,
    [string]$Content
) {
    $windowsText = $Content.Replace("`n", "`r`n")

    [System.IO.File]::WriteAllText(
        (Resolve-Path $Path),
        $windowsText,
        $Utf8NoBom
    )
}

function Replace-Exact(
    [string]$Text,
    [string]$Old,
    [string]$New,
    [string]$Description
) {
    if (-not $Text.Contains($Old)) {
        throw "Patch impossible ($Description) : texte attendu introuvable."
    }

    return $Text.Replace($Old, $New)
}

# ================================================================
# 1. ItemsService.cs
# ================================================================
$itemsPath = '.\BestCrush.Domain\Services\ItemsService.cs'
$items = Read-Source $itemsPath

if (-not $items.Contains('GetAllEquipmentsAsync')) {
    $marker = @'
    public async Task<IReadOnlyCollection<Resource>>
'@

    $method = @'
    public async Task<IReadOnlyCollection<Equipment>>
        GetAllEquipmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return await context.Equipments
            .Include(equipment => equipment.Characteristics)
            .Include(equipment => equipment.Recipe)
                .ThenInclude(entry => entry.Resource)
            .AsNoTracking()
            .OrderBy(equipment => equipment.Name)
            .ToArrayAsync(cancellationToken);
    }

'@

    $items = Replace-Exact `
        $items `
        $marker `
        ($method + $marker) `
        'ItemsService GetAllEquipmentsAsync'

    Write-Source $itemsPath $items
}

Write-Host "OK : $itemsPath"

# ================================================================
# 2. DofusItemRecognitionService.cs
# ================================================================
$recognitionPath = '.\BestCrush\Services\DofusItemRecognitionService.cs'
$recognition = Read-Source $recognitionPath

$recognition = $recognition.Replace(
    'await itemsService.GetEquipmentsAsync();',
    'await itemsService.GetAllEquipmentsAsync();'
)

if (-not $recognition.Contains('Equipment? exactMatch =')) {
    $marker = @'
        // Sinon on conserve la reconnaissance floue
'@

    $exactBlock = @'
        Equipment? exactMatch =
            equipments.FirstOrDefault(
                equipment =>
                    string.Equals(
                        Normalize(equipment.Name),
                        normalizedInput,
                        StringComparison.Ordinal
                    )
            );

        if (exactMatch is not null)
        {
            return new ItemRecognitionResult(
                exactMatch,
                1.0
            );
        }

'@

    $recognition = Replace-Exact `
        $recognition `
        $marker `
        ($exactBlock + $marker) `
        'DofusItemRecognitionService exact match'
}

Write-Source $recognitionPath $recognition
Write-Host "OK : $recognitionPath"

# ================================================================
# 3. OverlayPage.cs
# ================================================================
$overlayPath = '.\BestCrush\Overlay\OverlayPage.cs'
$overlay = Read-Source $overlayPath

# Résumé des données manquantes en rouge.
$overlay = $overlay.Replace(
    '            TextColor = Colors.Orange,' + "`n" +
    '            FontSize = 13',
    '            TextColor = Colors.Red,' + "`n" +
    '            FontSize = 13'
)

# ----------------------------------------------------------------
# Coefficient : absent = rouge ; DoFocus = bleu.
# ----------------------------------------------------------------
$oldCoefficient = @'
        if (result.Coefficient is null)
        {
            _coefficientLine.Text =
                "Coefficient : À scanner";
            _coefficientLine.TextColor =
                Colors.White;
        }
        else
        {
            SetFreshnessLine(
                _coefficientLine,
                $"Coefficient : " +
                $"{result.Coefficient.CoefficientPercent:0.##} %",
                DataFreshnessEvaluator.Evaluate(
                    result.Coefficient.ObservedAtUtc
                )
            );
        }
'@

$newCoefficient = @'
        if (result.Coefficient is null)
        {
            SetFreshnessLine(
                _coefficientLine,
                "Coefficient : À scanner",
                null
            );
        }
        else if (
            result.Coefficient.Source ==
                CoefficientSource.DofocusInitial)
        {
            SetColoredLine(
                _coefficientLine,
                $"Coefficient : " +
                $"{result.Coefficient.CoefficientPercent:0.##} %",
                Color.FromArgb("#5AB0FF")
            );
        }
        else
        {
            SetFreshnessLine(
                _coefficientLine,
                $"Coefficient : " +
                $"{result.Coefficient.CoefficientPercent:0.##} %",
                DataFreshnessEvaluator.Evaluate(
                    result.Coefficient.ObservedAtUtc
                )
            );
        }
'@

$overlay = Replace-Exact `
    $overlay `
    $oldCoefficient `
    $newCoefficient `
    'OverlayPage coefficient'

# ----------------------------------------------------------------
# Si aucun scénario n'est calculable, les catégories sans donnée
# passent aussi par SetFreshnessLine afin d'avoir la pastille rouge.
# ----------------------------------------------------------------
$oldScenarioNull = @'
        if (scenario is null)
        {
            _runeValueLine.Text =
                "Valeur runes : indisponible";

            _purchaseLine.Text =
                $"Achat équipement : {equipmentPrice}";

            _purchaseResultLine.Text = "";

            _craftLine.Text =
                $"Craft : {craftPrice}";

            _craftResultLine.Text = "";
            _partialLine.Text =
                missingDataCount > 0
                    ? $"⚠ {missingDataCount} donnée(s) manquante(s)"
                    : "";

            return;
        }
'@

$newScenarioNull = @'
        if (scenario is null)
        {
            SetFreshnessLine(
                _runeValueLine,
                "Valeur runes : indisponible",
                null
            );

            SetFreshnessLine(
                _purchaseLine,
                $"Achat équipement : {equipmentPrice}",
                result.EquipmentCost is null
                    ? null
                    : DataFreshnessEvaluator.Evaluate(
                        result.EquipmentCost.ObservedAtUtc
                    )
            );

            _purchaseResultLine.Text = "";

            SetFreshnessLine(
                _craftLine,
                $"Craft : {craftPrice}",
                GetCraftFreshness(
                    result.CraftCost
                )
            );

            _craftResultLine.Text = "";

            _partialLine.Text =
                missingDataCount > 0
                    ? $"⚠ {missingDataCount} donnée(s) manquante(s)"
                    : "";

            _partialLine.TextColor =
                missingDataCount > 0
                    ? Colors.Red
                    : Colors.Transparent;

            return;
        }
'@

$overlay = Replace-Exact `
    $overlay `
    $oldScenarioNull `
    $newScenarioNull `
    'OverlayPage scenario null'

# Résultat partiel en rouge.
$oldPartial = @'
        if (result.IsPartial)
        {
            _partialLine.Text =
                $"⚠ Résultat partiel — " +
                $"{missingDataCount} donnée(s) manquante(s)";
        }
'@

$newPartial = @'
        if (result.IsPartial)
        {
            _partialLine.TextColor =
                Colors.Red;

            _partialLine.Text =
                $"⚠ Résultat partiel — " +
                $"{missingDataCount} donnée(s) manquante(s)";
        }
'@

$overlay = Replace-Exact `
    $overlay `
    $oldPartial `
    $newPartial `
    'OverlayPage partial red'

# ----------------------------------------------------------------
# SetFreshnessLine :
# - donnée présente = pastille fraîcheur + texte blanc
# - donnée manquante = pastille rouge + texte rouge
# ----------------------------------------------------------------
$oldFreshness = @'
    private static void SetFreshnessLine(
        Label label,
        string text,
        DataFreshness? freshness)
    {
        FormattedString formatted =
            new();
        if (freshness is DataFreshness value)
        {
            formatted.Spans.Add(
                new Span
                {
                    Text = "● ",
                    TextColor =
                        GetFreshnessColor(
                            value
                        )
                }
            );
        }

        formatted.Spans.Add(
            new Span
            {
                Text = text,
                TextColor = Colors.White
            }
        );
        label.FormattedText =
            formatted;
    }

'@

$newFreshness = @'
    private static void SetFreshnessLine(
        Label label,
        string text,
        DataFreshness? freshness)
    {
        FormattedString formatted =
            new();

        Color color =
            freshness is DataFreshness value
                ? GetFreshnessColor(
                    value
                )
                : Colors.Red;

        formatted.Spans.Add(
            new Span
            {
                Text = "● ",
                TextColor = color
            }
        );

        formatted.Spans.Add(
            new Span
            {
                Text = text,
                TextColor =
                    freshness is null
                        ? Colors.Red
                        : Colors.White
            }
        );

        label.FormattedText =
            formatted;
    }

    private static void SetColoredLine(
        Label label,
        string text,
        Color color)
    {
        FormattedString formatted =
            new();

        formatted.Spans.Add(
            new Span
            {
                Text = "● ",
                TextColor = color
            }
        );

        formatted.Spans.Add(
            new Span
            {
                Text = text,
                TextColor = color
            }
        );

        label.FormattedText =
            formatted;
    }

'@

$overlay = Replace-Exact `
    $overlay `
    $oldFreshness `
    $newFreshness `
    'OverlayPage SetFreshnessLine'

# ----------------------------------------------------------------
# Une seule ressource manquante => Craft rouge.
# ----------------------------------------------------------------
$oldCraftFreshness = @'
    private static DataFreshness? GetCraftFreshness(
        CraftCostResult craftCost)
    {
        DataFreshness[] freshness =
            craftCost.Resources
                .Select(resource =>
                    resource.Purchase?.Freshness)
                .Where(value =>
                    value is not null)
                .Select(value =>
                    value!.Value)
                .ToArray();
        return freshness.Length == 0
            ? null
            : freshness.Max();
    }
'@

$newCraftFreshness = @'
    private static DataFreshness? GetCraftFreshness(
        CraftCostResult craftCost)
    {
        if (craftCost.Resources.Any(
            resource =>
                resource.Purchase is null))
        {
            return null;
        }

        DataFreshness[] freshness =
            craftCost.Resources
                .Select(resource =>
                    resource.Purchase?.Freshness)
                .Where(value =>
                    value is not null)
                .Select(value =>
                    value!.Value)
                .ToArray();

        return freshness.Length == 0
            ? null
            : freshness.Max();
    }
'@

$overlay = Replace-Exact `
    $overlay `
    $oldCraftFreshness `
    $newCraftFreshness `
    'OverlayPage GetCraftFreshness'

# ----------------------------------------------------------------
# Détail runes manquantes en rouge.
# ----------------------------------------------------------------
$overlay = $overlay.Replace(
@'
                            : Colors.White
                };

            Label unitPriceLabel =
'@,
@'
                            : Colors.Red
                };

            Label unitPriceLabel =
'@
)

$overlay = $overlay.Replace(
@'
                    Text =
                        unitPriceText,
                    FontSize = 12,
                    TextColor = Colors.White
                };
'@,
@'
                    Text =
                        unitPriceText,
                    FontSize = 12,
                    TextColor =
                        hasRuneValue
                            ? Colors.White
                            : Colors.Red
                };
'@
)

$overlay = $overlay.Replace(
@'
                right.Text =
                    "À scanner";
                right.TextColor =
                    Colors.White;
'@,
@'
                right.Text =
                    "À scanner";
                right.TextColor =
                    Colors.Red;
'@
)

$overlay = $overlay.Replace(
@'
                    Text = runeName,
                    TextColor = Colors.White,
                    FontSize = 12
'@,
@'
                    Text = runeName,
                    TextColor = Colors.Red,
                    FontSize = 12
'@
)

$overlay = $overlay.Replace(
@'
                    Text = " (À scanner)",
                    TextColor = Colors.White,
'@,
@'
                    Text = " (À scanner)",
                    TextColor = Colors.Red,
'@
)

$overlay = $overlay.Replace(
@'
                    Text = "À scanner",
                    TextColor = Colors.White,
                    FontSize = 12,
'@,
@'
                    Text = "À scanner",
                    TextColor = Colors.Red,
                    FontSize = 12,
'@
)

$overlay = $overlay.Replace(
@'
            totalValue.TextColor =
                Colors.White;
'@,
@'
            totalValue.TextColor =
                Colors.Red;
'@
)

# ----------------------------------------------------------------
# Détail ressources manquantes en rouge.
# ----------------------------------------------------------------
$overlay = $overlay.Replace(
@'
                                : Colors.White
                    };

            string resourceName =
'@,
@'
                                : Colors.Red
                    };

            string resourceName =
'@
)

$oldResourcePrice = @'
                Label price =
                new()
                {
                    FontSize = 12,
                    TextColor = Colors.White,
                    HorizontalTextAlignment =
                        TextAlignment.End,
                    Text =
                        resource.Purchase is null
                            ? "À scanner"
                            : $"{resource.Purchase.TotalCost:N0} K"
                };
'@

$newResourcePrice = @'
                Label price =
                new()
                {
                    FontSize = 12,
                    TextColor =
                        resource.Purchase is null
                            ? Colors.Red
                            : Colors.White,
                    HorizontalTextAlignment =
                        TextAlignment.End,
                    Text =
                        resource.Purchase is null
                            ? "À scanner"
                            : $"{resource.Purchase.TotalCost:N0} K"
                };
'@

$overlay = Replace-Exact `
    $overlay `
    $oldResourcePrice `
    $newResourcePrice `
    'OverlayPage resource price red'

# Liste générale des données manquantes en rouge.
$overlay = $overlay.Replace(
@'
                    Text = item,
                    TextColor = Colors.White,
                    FontSize = 12
'@,
@'
                    Text = item,
                    TextColor = Colors.Red,
                    FontSize = 12
'@
)

Write-Source $overlayPath $overlay
Write-Host "OK : $overlayPath"

Write-Host ''
Write-Host 'Correctif reconnaissance/couleurs appliqué.'
Write-Host 'Compile avec :'
Write-Host 'dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0'
