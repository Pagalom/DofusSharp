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

function Write-Source([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText(
        (Resolve-Path $Path),
        $Content,
        $Utf8NoBom
    )
}

function Get-Nl([string]$Text) {
    if ($Text.Contains("`r`n")) { return "`r`n" }
    return "`n"
}

function Replace-Once(
    [string]$Text,
    [string]$Old,
    [string]$New,
    [string]$Description
) {
    $index = $Text.IndexOf($Old)
    if ($index -lt 0) {
        throw "Patch impossible ($Description) : texte attendu introuvable."
    }

    return $Text.Substring(0, $index) +
        $New +
        $Text.Substring($index + $Old.Length)
}

function Insert-Before(
    [string]$Text,
    [string]$Marker,
    [string]$Insertion,
    [string]$Description,
    [int]$StartIndex = 0
) {
    $index = $Text.IndexOf($Marker, $StartIndex)
    if ($index -lt 0) {
        throw "Patch impossible ($Description) : point d'insertion introuvable."
    }

    return $Text.Substring(0, $index) +
        $Insertion +
        $Text.Substring($index)
}

function Insert-After(
    [string]$Text,
    [string]$Marker,
    [string]$Insertion,
    [string]$Description,
    [int]$StartIndex = 0
) {
    $index = $Text.IndexOf($Marker, $StartIndex)
    if ($index -lt 0) {
        throw "Patch impossible ($Description) : point d'insertion introuvable."
    }

    $after = $index + $Marker.Length

    return $Text.Substring(0, $after) +
        $Insertion +
        $Text.Substring($after)
}

function Patch-PricePage(
    [string]$Path,
    [string]$ObjectType,
    [string]$QuantityExpression
) {
    $content = Read-Source $Path
    $nl = Get-Nl $content

    if (-not $content.Contains("@using BestCrush.Services")) {
        $usingMarker = "@using BestCrush.Domain.Services" + $nl
        $content = Insert-After `
            $content `
            $usingMarker `
            ("@using BestCrush.Services" + $nl) `
            "$Path using Services"
    }

    if (-not $content.Contains("@implements IDisposable")) {
        $pageEnd = $content.IndexOf($nl)
        if ($pageEnd -lt 0) {
            throw "Patch impossible ($Path implements IDisposable)."
        }

        $content =
            $content.Substring(0, $pageEnd + $nl.Length) +
            "@implements IDisposable" + $nl +
            $content.Substring($pageEnd + $nl.Length)
    }

    if (-not $content.Contains("@inject MarketDataChangeNotifier MarketDataChangeNotifier")) {
        $injectMarker = "@inject MarketPriceService MarketPriceService" + $nl
        $content = Insert-After `
            $content `
            $injectMarker `
            ("@inject MarketDataChangeNotifier MarketDataChangeNotifier" + $nl) `
            "$Path inject notifier"
    }

    if (-not $content.Contains('@onclick="RefreshAsync"')) {
        $pattern =
            '(?s)<a class="btn btn-outline-secondary"\s+' +
            'href="@\(\$"/servers/\{ServerName\}"\)">\s*' +
            'Retour\s*</a>'

        $match = [regex]::Match($content, $pattern)

        if (-not $match.Success) {
            throw "Patch impossible ($Path bouton Rafraichir) : lien Retour introuvable."
        }

        $replacement =
            '<div class="d-flex gap-2">' + $nl +
            '            <button class="btn btn-outline-primary"' + $nl +
            '                    type="button"' + $nl +
            '                    @onclick="RefreshAsync">' + $nl +
            '                Rafraîchir' + $nl +
            '            </button>' + $nl +
            '            ' + $match.Value + $nl +
            '        </div>'

        $content =
            $content.Substring(0, $match.Index) +
            $replacement +
            $content.Substring($match.Index + $match.Length)
    }

    if (-not $content.Contains("private async Task LoadRowsAsync()")) {
        $startMarker = "    protected override async Task OnInitializedAsync()"
        $saveMarker = "    private async Task SavePriceAsync("

        $methodStart = $content.IndexOf($startMarker)
        $saveStart = $content.IndexOf($saveMarker, $methodStart)

        if ($methodStart -lt 0 -or $saveStart -lt 0) {
            throw "Patch impossible ($Path chargement) : methode OnInitialized/Save introuvable."
        }

        $segment =
            $content.Substring(
                $methodStart,
                $saveStart - $methodStart
            )

        $open = $segment.IndexOf("{")
        $close = $segment.LastIndexOf("}")

        if ($open -lt 0 -or $close -le $open) {
            throw "Patch impossible ($Path chargement) : corps de methode invalide."
        }

        $body =
            $segment.Substring(
                $open + 1,
                $close - $open - 1
            )

        $replacement =
            "    private readonly System.Threading.SemaphoreSlim" + $nl +
            "        _refreshLock = new(1, 1);" + $nl + $nl +
            "    protected override async Task OnInitializedAsync()" + $nl +
            "    {" + $nl +
            "        MarketDataChangeNotifier.Changed +=" + $nl +
            "            OnMarketDataChanged;" + $nl + $nl +
            "        await LoadRowsAsync();" + $nl +
            "    }" + $nl + $nl +
            "    private async Task LoadRowsAsync()" + $nl +
            "    {" +
            $body +
            "    }" + $nl + $nl +
            "    private Task RefreshAsync()" + $nl +
            "    {" + $nl +
            "        return ReloadFromMarketAsync();" + $nl +
            "    }" + $nl + $nl +
            "    private void OnMarketDataChanged(" + $nl +
            "        object? sender," + $nl +
            "        MarketDataChangedEventArgs e)" + $nl +
            "    {" + $nl +
            "        if (e.ObjectType !=" + $nl +
            "                MarketObjectType.$ObjectType ||" + $nl +
            "            !string.Equals(" + $nl +
            "                e.ServerName," + $nl +
            "                ServerName," + $nl +
            "                StringComparison.Ordinal))" + $nl +
            "        {" + $nl +
            "            return;" + $nl +
            "        }" + $nl + $nl +
            "        _ = InvokeAsync(" + $nl +
            "            ReloadFromMarketAsync" + $nl +
            "        );" + $nl +
            "    }" + $nl + $nl +
            "    private async Task ReloadFromMarketAsync()" + $nl +
            "    {" + $nl +
            "        if (!await _refreshLock.WaitAsync(0))" + $nl +
            "        {" + $nl +
            "            return;" + $nl +
            "        }" + $nl + $nl +
            "        try" + $nl +
            "        {" + $nl +
            "            await LoadRowsAsync();" + $nl +
            "            StateHasChanged();" + $nl +
            "        }" + $nl +
            "        finally" + $nl +
            "        {" + $nl +
            "            _refreshLock.Release();" + $nl +
            "        }" + $nl +
            "    }" + $nl + $nl +
            "    public void Dispose()" + $nl +
            "    {" + $nl +
            "        MarketDataChangeNotifier.Changed -=" + $nl +
            "            OnMarketDataChanged;" + $nl +
            "        _refreshLock.Dispose();" + $nl +
            "    }" + $nl + $nl

        $content =
            $content.Substring(0, $methodStart) +
            $replacement +
            $content.Substring($saveStart)
    }

    # Ajoute la notification a la fin des deux branches de SavePriceAsync :
    # 1) effacement manuel, 2) enregistrement manuel.
    $saveStart = $content.IndexOf("    private async Task SavePriceAsync(")
    $nextMethod = $content.IndexOf("    private static string GetSourceLetter(", $saveStart)

    if ($saveStart -lt 0 -or $nextMethod -lt 0) {
        throw "Patch impossible ($Path notifications manuelles)."
    }

    $saveBody = $content.Substring(
        $saveStart,
        $nextMethod - $saveStart
    )

    if (-not $saveBody.Contains("MarketDataChangeNotifier.Notify(")) {
        $firstMessage = $saveBody.IndexOf("_lastSavedMessage =")
        if ($firstMessage -lt 0) {
            throw "Patch impossible ($Path notification clear)."
        }

        $firstSemi = $saveBody.IndexOf(";", $firstMessage)
        if ($firstSemi -lt 0) {
            throw "Patch impossible ($Path notification clear semicolon)."
        }

        $notify =
            $nl +
            "            MarketDataChangeNotifier.Notify(" + $nl +
            "                MarketObjectType.$ObjectType," + $nl +
            "                row.DofusDbId," + $nl +
            "                ServerName," + $nl +
            "                $QuantityExpression" + $nl +
            "            );"

        $saveBody =
            $saveBody.Substring(0, $firstSemi + 1) +
            $notify +
            $saveBody.Substring($firstSemi + 1)

        $secondMessage = $saveBody.IndexOf(
            "_lastSavedMessage =",
            $firstSemi + $notify.Length + 1
        )

        if ($secondMessage -lt 0) {
            throw "Patch impossible ($Path notification save)."
        }

        $secondSemi = $saveBody.IndexOf(";", $secondMessage)
        if ($secondSemi -lt 0) {
            throw "Patch impossible ($Path notification save semicolon)."
        }

        $saveBody =
            $saveBody.Substring(0, $secondSemi + 1) +
            $notify.Replace("            ", "        ") +
            $saveBody.Substring($secondSemi + 1)

        $content =
            $content.Substring(0, $saveStart) +
            $saveBody +
            $content.Substring($nextMethod)
    }

    Write-Source $Path $content
    Write-Host "OK : $Path"
}

# ------------------------------------------------------------------
# 1. Enregistrement DI
# ------------------------------------------------------------------
$mauiPath = ".\BestCrush\MauiProgram.cs"
$maui = Read-Source $mauiPath
$nl = Get-Nl $maui

if (-not $maui.Contains("MarketDataChangeNotifier")) {
    $marker =
        "            builder.Services.AddSingleton<BestCrush.Services.CrushSessionService>();"

    $maui = Replace-Once `
        $maui `
        $marker `
        ($marker + $nl +
         "            builder.Services.AddSingleton<BestCrush.Services.MarketDataChangeNotifier>();") `
        "MauiProgram notifier"

    Write-Source $mauiPath $maui
    Write-Host "OK : $mauiPath"
}

# ------------------------------------------------------------------
# 2. Overlay principal : ecoute + emission des changements
# ------------------------------------------------------------------
$overlayPath = ".\BestCrush\Services\OverlayService.cs"
$overlay = Read-Source $overlayPath
$nl = Get-Nl $overlay

if (-not $overlay.Contains("MarketDataChangeNotifier marketDataChangeNotifier")) {
    $old =
        "    FocusedEquipmentState focusedEquipmentState," + $nl +
        "    CrushSessionService crushSessionService)"

    $new =
        "    FocusedEquipmentState focusedEquipmentState," + $nl +
        "    CrushSessionService crushSessionService," + $nl +
        "    MarketDataChangeNotifier marketDataChangeNotifier)"

    $overlay = Replace-Once `
        $overlay `
        $old `
        $new `
        "OverlayService constructeur notifier"
}

if (-not $overlay.Contains("OnMarketDataChanged")) {
    $subscribeMarker =
        "        crushSessionService.CoefficientsUpdated +=" + $nl +
        "            OnCrushSessionCoefficientsUpdated;"

    $overlay = Insert-After `
        $overlay `
        $subscribeMarker `
        ($nl + $nl +
         "        marketDataChangeNotifier.Changed -=" + $nl +
         "            OnMarketDataChanged;" + $nl + $nl +
         "        marketDataChangeNotifier.Changed +=" + $nl +
         "            OnMarketDataChanged;") `
        "OverlayService abonnement notifier"

    $handlerMarker =
        "    private void OnCrushSessionCoefficientsUpdated("

    $handler =
        "    private void OnMarketDataChanged(" + $nl +
        "        object? sender," + $nl +
        "        MarketDataChangedEventArgs e)" + $nl +
        "    {" + $nl +
        "        string? serverName =" + $nl +
        "            currentServerState.ServerName;" + $nl + $nl +
        "        if (string.IsNullOrWhiteSpace(serverName) ||" + $nl +
        "            !string.Equals(" + $nl +
        "                serverName," + $nl +
        "                e.ServerName," + $nl +
        "                StringComparison.Ordinal))" + $nl +
        "        {" + $nl +
        "            return;" + $nl +
        "        }" + $nl + $nl +
        "        _ = RefreshFocusedProfitabilityAsync();" + $nl +
        "    }" + $nl + $nl

    $overlay = Insert-Before `
        $overlay `
        $handlerMarker `
        $handler `
        "OverlayService handler notifier"

    $shutdownMarker =
        "        crushSessionService.CoefficientsUpdated -=" + $nl +
        "            OnCrushSessionCoefficientsUpdated;"

    $shutdownIndex = $overlay.IndexOf(
        $shutdownMarker,
        $overlay.IndexOf("    public void Shutdown()")
    )

    if ($shutdownIndex -lt 0) {
        throw "Patch impossible (OverlayService desabonnement notifier)."
    }

    $shutdownAfter = $shutdownIndex + $shutdownMarker.Length
    $overlay =
        $overlay.Substring(0, $shutdownAfter) +
        $nl + $nl +
        "        marketDataChangeNotifier.Changed -=" + $nl +
        "            OnMarketDataChanged;" +
        $overlay.Substring($shutdownAfter)
}

# Notification vente rune.
if (-not $overlay.Contains("sellServerName," + $nl + "                                    0")) {
    $start = $overlay.IndexOf("string sellServerName =")
    $post = $overlay.IndexOf("                                PostUi(", $start)

    if ($start -lt 0 -or $post -lt 0) {
        throw "Patch impossible (notification rune Vente)."
    }

    $insert =
        "                                marketDataChangeNotifier.Notify(" + $nl +
        "                                    MarketObjectType.Rune," + $nl +
        "                                    sellRuneRecognition.Rune.DofusDbId," + $nl +
        "                                    sellServerName," + $nl +
        "                                    0" + $nl +
        "                                );" + $nl

    $overlay =
        $overlay.Substring(0, $post) +
        $insert +
        $overlay.Substring($post)
}

# Notification rune Achat.
if (-not $overlay.Contains("runeServerName," + $nl + "                                        0")) {
    $start = $overlay.IndexOf("string runeServerName =")
    if ($start -ge 0) {
        $post = $overlay.IndexOf("                                    PostUi(", $start)
        if ($post -lt 0) {
            throw "Patch impossible (notification rune Achat)."
        }

        $insert =
            "                                    marketDataChangeNotifier.Notify(" + $nl +
            "                                        MarketObjectType.Rune," + $nl +
            "                                        runeRecognition.Rune.DofusDbId," + $nl +
            "                                        runeServerName," + $nl +
            "                                        0" + $nl +
            "                                    );" + $nl

        $overlay =
            $overlay.Substring(0, $post) +
            $insert +
            $overlay.Substring($post)
    }
}

# Notification ressource Achat.
if (-not $overlay.Contains("resourceServerName," + $nl + "                                        0")) {
    $start = $overlay.IndexOf("string resourceServerName =")
    if ($start -ge 0) {
        $post = $overlay.IndexOf("                                    PostUi(", $start)
        if ($post -lt 0) {
            throw "Patch impossible (notification ressource Achat)."
        }

        $insert =
            "                                    marketDataChangeNotifier.Notify(" + $nl +
            "                                        MarketObjectType.Resource," + $nl +
            "                                        resourceRecognition.Resource.DofusDbId," + $nl +
            "                                        resourceServerName," + $nl +
            "                                        0" + $nl +
            "                                    );" + $nl

        $overlay =
            $overlay.Substring(0, $post) +
            $insert +
            $overlay.Substring($post)
    }
}

# Notification prix equipement.
if (-not $overlay.Contains("hdvServerName," + $nl + "                                1")) {
    $captured = $overlay.IndexOf("MarketPriceObservation capturedPrice =")
    $effective = $overlay.IndexOf(
        "                            MarketPriceObservation? effectivePrice =",
        $captured
    )

    if ($captured -lt 0 -or $effective -lt 0) {
        throw "Patch impossible (notification equipement HDV)."
    }

    $insert =
        "                            marketDataChangeNotifier.Notify(" + $nl +
        "                                MarketObjectType.Equipment," + $nl +
        "                                marketEquipment.DofusDbId," + $nl +
        "                                hdvServerName," + $nl +
        "                                1" + $nl +
        "                            );" + $nl

    $overlay =
        $overlay.Substring(0, $effective) +
        $insert +
        $overlay.Substring($effective)
}

Write-Source $overlayPath $overlay
Write-Host "OK : $overlayPath"

# ------------------------------------------------------------------
# 3. Resultat concassage : revalorisation automatique des runes
# ------------------------------------------------------------------
$crushPath = ".\BestCrush\Services\CrushSessionService.cs"
$crush = Read-Source $crushPath
$nl = Get-Nl $crush

if (-not $crush.Contains("MarketDataChangeNotifier marketDataChangeNotifier")) {
    $old =
        "    IServiceScopeFactory serviceScopeFactory," + $nl +
        "    CurrentServerState currentServerState)"

    $new =
        "    IServiceScopeFactory serviceScopeFactory," + $nl +
        "    CurrentServerState currentServerState," + $nl +
        "    MarketDataChangeNotifier marketDataChangeNotifier)"

    $crush = Replace-Once `
        $crush `
        $old `
        $new `
        "CrushSessionService constructeur notifier"
}

if (-not $crush.Contains("_marketRefreshLock")) {
    $stateMarker =
        "    private readonly object" + $nl +
        "        _stateLock = new();"

    $crush = Insert-After `
        $crush `
        $stateMarker `
        ($nl + $nl +
         "    private readonly System.Threading.SemaphoreSlim" + $nl +
         "        _marketRefreshLock = new(1, 1);") `
        "CrushSessionService refresh lock"
}

if (-not $crush.Contains("OnMarketDataChanged")) {
    $startMarker = "    public void StartNew()" + $nl + "    {"

    $crush = Insert-After `
        $crush `
        $startMarker `
        ($nl +
         "        marketDataChangeNotifier.Changed -=" + $nl +
         "            OnMarketDataChanged;" + $nl + $nl +
         "        marketDataChangeNotifier.Changed +=" + $nl +
         "            OnMarketDataChanged;" + $nl) `
        "CrushSessionService abonnement notifier"

    $marker =
        "    private bool IsAlreadyScannedLocked("

    $method =
        "    private void OnMarketDataChanged(" + $nl +
        "        object? sender," + $nl +
        "        MarketDataChangedEventArgs e)" + $nl +
        "    {" + $nl +
        "        if (e.ObjectType !=" + $nl +
        "                MarketObjectType.Rune ||" + $nl +
        "            !string.Equals(" + $nl +
        "                e.ServerName," + $nl +
        "                currentServerState.ServerName," + $nl +
        "                StringComparison.Ordinal))" + $nl +
        "        {" + $nl +
        "            return;" + $nl +
        "        }" + $nl + $nl +
        "        lock (_stateLock)" + $nl +
        "        {" + $nl +
        "            if (_runes.Count == 0 ||" + $nl +
        "                _errorMessage is not null)" + $nl +
        "            {" + $nl +
        "                return;" + $nl +
        "            }" + $nl +
        "        }" + $nl + $nl +
        "        _ = RefreshRuneValuesFromMarketAsync();" + $nl +
        "    }" + $nl + $nl +
        "    private async Task RefreshRuneValuesFromMarketAsync()" + $nl +
        "    {" + $nl +
        "        if (!await _marketRefreshLock.WaitAsync(0))" + $nl +
        "        {" + $nl +
        "            return;" + $nl +
        "        }" + $nl + $nl +
        "        try" + $nl +
        "        {" + $nl +
        "            string? serverName =" + $nl +
        "                currentServerState.ServerName;" + $nl + $nl +
        "            if (string.IsNullOrWhiteSpace(serverName))" + $nl +
        "            {" + $nl +
        "                return;" + $nl +
        "            }" + $nl + $nl +
        "            using IServiceScope scope =" + $nl +
        "                serviceScopeFactory.CreateScope();" + $nl + $nl +
        "            MarketPriceService marketPriceService =" + $nl +
        "                scope.ServiceProvider.GetRequiredService<MarketPriceService>();" + $nl + $nl +
        "            IReadOnlyDictionary<" + $nl +
        "                (long DofusDbId, int Quantity)," + $nl +
        "                MarketPriceObservation> observations =" + $nl +
        "                    await marketPriceService" + $nl +
        "                        .GetLatestObservationsForServerAsync(" + $nl +
        "                            MarketObjectType.Rune," + $nl +
        "                            serverName" + $nl +
        "                        );" + $nl + $nl +
        "            lock (_stateLock)" + $nl +
        "            {" + $nl +
        "                if (_errorMessage is not null)" + $nl +
        "                {" + $nl +
        "                    return;" + $nl +
        "                }" + $nl + $nl +
        "                foreach (KeyValuePair<long, AccumulatedRune> rune in _runes)" + $nl +
        "                {" + $nl +
        "                    MarketValueResult? value =" + $nl +
        "                        marketPriceService.CalculateValue(" + $nl +
        "                            rune.Key," + $nl +
        "                            rune.Value.Quantity," + $nl +
        "                            observations" + $nl +
        "                        );" + $nl + $nl +
        "                    rune.Value.Value =" + $nl +
        "                        value?.Value;" + $nl +
        "                }" + $nl +
        "            }" + $nl + $nl +
        "            PublishSnapshot();" + $nl +
        "        }" + $nl +
        "        finally" + $nl +
        "        {" + $nl +
        "            _marketRefreshLock.Release();" + $nl +
        "        }" + $nl +
        "    }" + $nl + $nl

    $crush = Insert-Before `
        $crush `
        $marker `
        $method `
        "CrushSessionService revalorisation marche"
}

Write-Source $crushPath $crush
Write-Host "OK : $crushPath"

# ------------------------------------------------------------------
# 4. Pages de prix : bouton Rafraichir + auto-refresh
# ------------------------------------------------------------------
Patch-PricePage `
    ".\BestCrush\Components\Pages\RunePrices.razor" `
    "Rune" `
    "quantity"

Patch-PricePage `
    ".\BestCrush\Components\Pages\ResourcePrices.razor" `
    "Resource" `
    "quantity"

Patch-PricePage `
    ".\BestCrush\Components\Pages\EquipmentPrices.razor" `
    "Equipment" `
    "1"

Write-Host ""
Write-Host "Patch BestCrush 7/8 applique."
Write-Host "Compilation :"
Write-Host "dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0"
