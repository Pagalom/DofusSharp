$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Read-Source([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Fichier introuvable : $Path"
    }

    return [System.IO.File]::ReadAllText(
        (Resolve-Path $Path),
        [System.Text.Encoding]::UTF8
    )
}

function Write-Source(
    [string]$Path,
    [string]$Content
) {
    [System.IO.File]::WriteAllText(
        (Resolve-Path $Path),
        $Content,
        $Utf8NoBom
    )
}

function Get-Nl([string]$Text) {
    if ($Text.Contains("`r`n")) {
        return "`r`n"
    }

    return "`n"
}

# ================================================================
# 1. ItemsService :
#    reconnaissance OCR sur TOUS les équipements connus.
# ================================================================
$itemsPath =
    ".\BestCrush.Domain\Services\ItemsService.cs"

$items =
    Read-Source $itemsPath

$nl =
    Get-Nl $items

if (-not $items.Contains(
    "GetAllEquipmentsAsync"))
{
    $marker =
        "    public async Task<IReadOnlyCollection<Resource>>"

    $index =
        $items.IndexOf(
            $marker
        )

    if ($index -lt 0)
    {
        throw "ItemsService : point d'insertion introuvable."
    }

    $method =
        "    public async Task<IReadOnlyCollection<Equipment>>" + $nl +
        "        GetAllEquipmentsAsync(" + $nl +
        "            CancellationToken cancellationToken = default)" + $nl +
        "    {" + $nl +
        "        return await context.Equipments" + $nl +
        "            .Include(equipment => equipment.Characteristics)" + $nl +
        "            .Include(equipment => equipment.Recipe)" + $nl +
        "                .ThenInclude(entry => entry.Resource)" + $nl +
        "            .AsNoTracking()" + $nl +
        "            .OrderBy(equipment => equipment.Name)" + $nl +
        "            .ToArrayAsync(cancellationToken);" + $nl +
        "    }" + $nl + $nl

    $items =
        $items.Substring(
            0,
            $index
        ) +
        $method +
        $items.Substring(
            $index
        )

    Write-Source `
        $itemsPath `
        $items
}

Write-Host "OK : $itemsPath"

# ================================================================
# 2. DofusItemRecognitionService :
#    exact normalisé d'abord, fuzzy ensuite.
# ================================================================
$recognitionPath =
    ".\BestCrush\Services\DofusItemRecognitionService.cs"

$recognition =
    Read-Source $recognitionPath

$nl =
    Get-Nl $recognition

$recognition =
    $recognition.Replace(
        "await itemsService.GetEquipmentsAsync();",
        "await itemsService.GetAllEquipmentsAsync();"
    )

if (-not $recognition.Contains(
    "Equipment? exactMatch ="))
{
    $marker =
        "        // Sinon on conserve la reconnaissance floue"

    $index =
        $recognition.IndexOf(
            $marker
        )

    if ($index -lt 0)
    {
        throw "DofusItemRecognitionService : point exact-match introuvable."
    }

    $exact =
        "        Equipment? exactMatch =" + $nl +
        "            equipments.FirstOrDefault(" + $nl +
        "                equipment =>" + $nl +
        "                    string.Equals(" + $nl +
        "                        Normalize(equipment.Name)," + $nl +
        "                        normalizedInput," + $nl +
        "                        StringComparison.Ordinal" + $nl +
        "                    )" + $nl +
        "            );" + $nl + $nl +
        "        if (exactMatch is not null)" + $nl +
        "        {" + $nl +
        "            return new ItemRecognitionResult(" + $nl +
        "                exactMatch," + $nl +
        "                1.0" + $nl +
        "            );" + $nl +
        "        }" + $nl + $nl

    $recognition =
        $recognition.Substring(
            0,
            $index
        ) +
        $exact +
        $recognition.Substring(
            $index
        )
}

Write-Source `
    $recognitionPath `
    $recognition

Write-Host "OK : $recognitionPath"

# ================================================================
# 3. OverlayPage : DoFocus bleu + toute donnée manquante rouge.
# ================================================================
$overlayPath =
    ".\BestCrush\Overlay\OverlayPage.cs"

$overlay =
    Read-Source $overlayPath

$nl =
    Get-Nl $overlay

# Le résumé de données manquantes est rouge.
$overlay =
    $overlay.Replace(
        "_partialLine = new Label" + $nl +
        "        {" + $nl +
        "            TextColor = Colors.Orange,",
        "_partialLine = new Label" + $nl +
        "        {" + $nl +
        "            TextColor = Colors.Red,"
    )

# ---- Coefficient -------------------------------------------------
$coefficientPattern =
    '(?s)        if \(result\.Coefficient is null\)\s*' +
    '\{\s*' +
    '_coefficientLine\.Text =\s*' +
    '"Coefficient : À scanner";\s*' +
    '_coefficientLine\.TextColor =\s*' +
    'Colors\.White;\s*' +
    '\}\s*' +
    'else\s*' +
    '\{\s*' +
    'SetFreshnessLine\(\s*' +
    '_coefficientLine,\s*' +
    '\$"Coefficient : " \+\s*' +
    '\$"\{result\.Coefficient\.CoefficientPercent:0\.##\} %",\s*' +
    'DataFreshnessEvaluator\.Evaluate\(\s*' +
    'result\.Coefficient\.ObservedAtUtc\s*' +
    '\)\s*' +
    '\);\s*' +
    '\}'

$coefficientMatch =
    [regex]::Match(
        $overlay,
        $coefficientPattern
    )

if (-not $coefficientMatch.Success)
{
    throw "OverlayPage : bloc coefficient introuvable."
}

$coefficientReplacement =
    "        if (result.Coefficient is null)" + $nl +
    "        {" + $nl +
    "            SetFreshnessLine(" + $nl +
    "                _coefficientLine," + $nl +
    "                ""Coefficient : À scanner""," + $nl +
    "                null" + $nl +
    "            );" + $nl +
    "        }" + $nl +
    "        else if (" + $nl +
    "            result.Coefficient.Source ==" + $nl +
    "                CoefficientSource.DofocusInitial)" + $nl +
    "        {" + $nl +
    "            SetColoredLine(" + $nl +
    "                _coefficientLine," + $nl +
    "                $""Coefficient : " +" + $nl +
    "                $""{result.Coefficient.CoefficientPercent:0.##} %""," + $nl +
    "                Color.FromArgb(""#5AB0FF"")" + $nl +
    "            );" + $nl +
    "        }" + $nl +
    "        else" + $nl +
    "        {" + $nl +
    "            SetFreshnessLine(" + $nl +
    "                _coefficientLine," + $nl +
    "                $""Coefficient : " +" + $nl +
    "                $""{result.Coefficient.CoefficientPercent:0.##} %""," + $nl +
    "                DataFreshnessEvaluator.Evaluate(" + $nl +
    "                    result.Coefficient.ObservedAtUtc" + $nl +
    "                )" + $nl +
    "            );" + $nl +
    "        }"

$overlay =
    $overlay.Substring(
        0,
        $coefficientMatch.Index
    ) +
    $coefficientReplacement +
    $overlay.Substring(
        $coefficientMatch.Index +
        $coefficientMatch.Length
    )

# ---- Cas où aucun scénario de runes ne peut encore être calculé --
$scenarioPattern =
    '(?s)        if \(scenario is null\)\s*' +
    '\{.*?' +
    '            return;\s*' +
    '        \}'

$scenarioMatch =
    [regex]::Match(
        $overlay,
        $scenarioPattern
    )

if (-not $scenarioMatch.Success)
{
    throw "OverlayPage : bloc scenario null introuvable."
}

$scenarioReplacement =
    "        if (scenario is null)" + $nl +
    "        {" + $nl +
    "            SetFreshnessLine(" + $nl +
    "                _runeValueLine," + $nl +
    "                ""Valeur runes : indisponible""," + $nl +
    "                null" + $nl +
    "            );" + $nl + $nl +
    "            SetFreshnessLine(" + $nl +
    "                _purchaseLine," + $nl +
    "                $""Achat équipement : {equipmentPrice}""," + $nl +
    "                result.EquipmentCost is null" + $nl +
    "                    ? null" + $nl +
    "                    : DataFreshnessEvaluator.Evaluate(" + $nl +
    "                        result.EquipmentCost.ObservedAtUtc" + $nl +
    "                    )" + $nl +
    "            );" + $nl + $nl +
    "            _purchaseResultLine.Text = """";" + $nl + $nl +
    "            SetFreshnessLine(" + $nl +
    "                _craftLine," + $nl +
    "                $""Craft : {craftPrice}""," + $nl +
    "                GetCraftFreshness(" + $nl +
    "                    result.CraftCost" + $nl +
    "                )" + $nl +
    "            );" + $nl + $nl +
    "            _craftResultLine.Text = """";" + $nl +
    "            _partialLine.Text =" + $nl +
    "                missingDataCount > 0" + $nl +
    "                    ? $""⚠ {missingDataCount} donnée(s) manquante(s)""" + $nl +
    "                    : """";" + $nl +
    "            _partialLine.TextColor =" + $nl +
    "                missingDataCount > 0" + $nl +
    "                    ? Colors.Red" + $nl +
    "                    : Colors.Transparent;" + $nl + $nl +
    "            return;" + $nl +
    "        }"

$overlay =
    $overlay.Substring(
        0,
        $scenarioMatch.Index
    ) +
    $scenarioReplacement +
    $overlay.Substring(
        $scenarioMatch.Index +
        $scenarioMatch.Length
    )

# Résultat partiel : rouge.
$overlay =
    $overlay.Replace(
        "        if (result.IsPartial)" + $nl +
        "        {" + $nl +
        "            _partialLine.Text =" + $nl,
        "        if (result.IsPartial)" + $nl +
        "        {" + $nl +
        "            _partialLine.TextColor =" + $nl +
        "                Colors.Red;" + $nl + $nl +
        "            _partialLine.Text =" + $nl
    )

# ---- Pastille rouge si freshness null ----------------------------
$setFreshnessOld =
    "        if (freshness is DataFreshness value)" + $nl +
    "        {" + $nl +
    "            formatted.Spans.Add(" + $nl +
    "                new Span" + $nl +
    "                {" + $nl +
    "                    Text = ""● ""," + $nl +
    "                    TextColor =" + $nl +
    "                        GetFreshnessColor(" + $nl +
    "                            value" + $nl +
    "                        )" + $nl +
    "                }" + $nl +
    "            );" + $nl +
    "        }"

$setFreshnessNew =
    "        Color indicatorColor =" + $nl +
    "            freshness is DataFreshness value" + $nl +
    "                ? GetFreshnessColor(" + $nl +
    "                    value" + $nl +
    "                )" + $nl +
    "                : Colors.Red;" + $nl + $nl +
    "        formatted.Spans.Add(" + $nl +
    "            new Span" + $nl +
    "            {" + $nl +
    "                Text = ""● ""," + $nl +
    "                TextColor =" + $nl +
    "                    indicatorColor" + $nl +
    "            }" + $nl +
    "        );"

if (-not $overlay.Contains(
    $setFreshnessOld))
{
    throw "OverlayPage : SetFreshnessLine attendu introuvable."
}

$overlay =
    $overlay.Replace(
        $setFreshnessOld,
        $setFreshnessNew
    )

# Helper ligne intégralement colorée, utilisé pour DoFocus.
if (-not $overlay.Contains(
    "private static void SetColoredLine("))
{
    $marker =
        "    private static Color GetFreshnessColor("

    $index =
        $overlay.IndexOf(
            $marker
        )

    if ($index -lt 0)
    {
        throw "OverlayPage : insertion SetColoredLine impossible."
    }

    $helper =
        "    private static void SetColoredLine(" + $nl +
        "        Label label," + $nl +
        "        string text," + $nl +
        "        Color color)" + $nl +
        "    {" + $nl +
        "        FormattedString formatted =" + $nl +
        "            new();" + $nl + $nl +
        "        formatted.Spans.Add(" + $nl +
        "            new Span" + $nl +
        "            {" + $nl +
        "                Text = ""● ""," + $nl +
        "                TextColor = color" + $nl +
        "            }" + $nl +
        "        );" + $nl + $nl +
        "        formatted.Spans.Add(" + $nl +
        "            new Span" + $nl +
        "            {" + $nl +
        "                Text = text," + $nl +
        "                TextColor = color" + $nl +
        "            }" + $nl +
        "        );" + $nl + $nl +
        "        label.FormattedText =" + $nl +
        "            formatted;" + $nl +
        "    }" + $nl + $nl

    $overlay =
        $overlay.Substring(
            0,
            $index
        ) +
        $helper +
        $overlay.Substring(
            $index
        )
}

# ---- Rune manquante : rouge dans le détail -----------------------
$overlay =
    $overlay.Replace(
        "                            : Colors.White" + $nl +
        "                };" + $nl + $nl +
        "            Label unitPriceLabel =",
        "                            : Colors.Red" + $nl +
        "                };" + $nl + $nl +
        "            Label unitPriceLabel ="
    )

$overlay =
    $overlay.Replace(
        "                    TextColor = Colors.White" + $nl +
        "                };" + $nl + $nl +
        "            string runeName =",
        "                    TextColor =" + $nl +
        "                        hasRuneValue" + $nl +
        "                            ? Colors.White" + $nl +
        "                            : Colors.Red" + $nl +
        "                };" + $nl + $nl +
        "            string runeName ="
    )

$overlay =
    $overlay.Replace(
        "                right.TextColor =" + $nl +
        "                    Colors.White;" + $nl +
        "            }" + $nl + $nl +
        "            row.Add(",
        "                right.TextColor =" + $nl +
        "                    Colors.Red;" + $nl +
        "            }" + $nl + $nl +
        "            row.Add("
    )

# Les lignes explicitement manquantes.
$overlay =
    $overlay.Replace(
        "                    TextColor = Colors.White," + $nl +
        "                    FontSize = 12" + $nl +
        "                };" + $nl +
        "            Label missingLabel =",
        "                    TextColor = Colors.Red," + $nl +
        "                    FontSize = 12" + $nl +
        "                };" + $nl +
        "            Label missingLabel ="
    )

$overlay =
    $overlay.Replace(
        "                    Text = "" (À scanner)""," + $nl +
        "                    TextColor = Colors.White,",
        "                    Text = "" (À scanner)""," + $nl +
        "                    TextColor = Colors.Red,"
    )

$overlay =
    $overlay.Replace(
        "                    Text = ""À scanner""," + $nl +
        "                    TextColor = Colors.White," + $nl +
        "                    FontSize = 12," + $nl +
        "                    HorizontalTextAlignment =",
        "                    Text = ""À scanner""," + $nl +
        "                    TextColor = Colors.Red," + $nl +
        "                    FontSize = 12," + $nl +
        "                    HorizontalTextAlignment ="
    )

$overlay =
    $overlay.Replace(
        "            totalValue.TextColor =" + $nl +
        "                Colors.White;",
        "            totalValue.TextColor =" + $nl +
        "                Colors.Red;"
    )

# ---- Ressources manquantes : rouge -------------------------------
$overlay =
    $overlay.Replace(
        "                                : Colors.White" + $nl +
        "                    };" + $nl + $nl +
        "            string resourceName =",
        "                                : Colors.Red" + $nl +
        "                    };" + $nl + $nl +
        "            string resourceName ="
    )

$overlay =
    $overlay.Replace(
        "                    TextColor = Colors.White," + $nl +
        "                    HorizontalTextAlignment =" + $nl +
        "                        TextAlignment.End," + $nl +
        "                    Text =" + $nl +
        "                        resource.Purchase is null",
        "                    TextColor =" + $nl +
        "                        resource.Purchase is null" + $nl +
        "                            ? Colors.Red" + $nl +
        "                            : Colors.White," + $nl +
        "                    HorizontalTextAlignment =" + $nl +
        "                        TextAlignment.End," + $nl +
        "                    Text =" + $nl +
        "                        resource.Purchase is null"
    )

# Tooltip général des éléments manquants.
$overlay =
    $overlay.Replace(
        "                    Text = item," + $nl +
        "                    TextColor = Colors.White,",
        "                    Text = item," + $nl +
        "                    TextColor = Colors.Red,"
    )

# ---- Craft : si UNE ressource manque, la catégorie est rouge ----
$craftPattern =
    '(?s)    private static DataFreshness\? GetCraftFreshness\(' +
    '\s*CraftCostResult craftCost\)\s*' +
    '\{.*?' +
    '    \}(?=\s*    private static int GetMissingDataCount)'

$craftMatch =
    [regex]::Match(
        $overlay,
        $craftPattern
    )

if (-not $craftMatch.Success)
{
    throw "OverlayPage : GetCraftFreshness introuvable."
}

$craftReplacement =
    "    private static DataFreshness? GetCraftFreshness(" + $nl +
    "        CraftCostResult craftCost)" + $nl +
    "    {" + $nl +
    "        if (craftCost.Resources.Any(" + $nl +
    "            resource =>" + $nl +
    "                resource.Purchase is null))" + $nl +
    "        {" + $nl +
    "            return null;" + $nl +
    "        }" + $nl + $nl +
    "        DataFreshness[] freshness =" + $nl +
    "            craftCost.Resources" + $nl +
    "                .Select(resource =>" + $nl +
    "                    resource.Purchase?.Freshness)" + $nl +
    "                .Where(value =>" + $nl +
    "                    value is not null)" + $nl +
    "                .Select(value =>" + $nl +
    "                    value!.Value)" + $nl +
    "                .ToArray();" + $nl + $nl +
    "        return freshness.Length == 0" + $nl +
    "            ? null" + $nl +
    "            : freshness.Max();" + $nl +
    "    }"

$overlay =
    $overlay.Substring(
        0,
        $craftMatch.Index
    ) +
    $craftReplacement +
    $overlay.Substring(
        $craftMatch.Index +
        $craftMatch.Length
    )

Write-Source `
    $overlayPath `
    $overlay

Write-Host "OK : $overlayPath"

Write-Host ""
Write-Host "Patch applique."
Write-Host "Compilation :"
Write-Host "dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0"
