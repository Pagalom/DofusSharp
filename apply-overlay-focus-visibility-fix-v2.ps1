$ErrorActionPreference = "Stop"

$expectedBranch = "fix/overlay-focus-visibility"
$currentBranch = (git branch --show-current).Trim()

if ($currentBranch -ne $expectedBranch) {
    throw "Branche actuelle : '$currentBranch'. Branche attendue : '$expectedBranch'."
}

$market = ".\BestCrush\Services\MarketCaptureOverlayService.cs"
$control = ".\BestCrush\Services\OverlayControlBarService.cs"
$overlay = ".\BestCrush\Services\OverlayService.cs"

foreach ($file in @($market, $control, $overlay)) {
    if (-not (Test-Path $file)) {
        throw "Fichier introuvable : $file"
    }
}

function Replace-RegexOnce {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Replacement,
        [string]$Label
    )

    $content = [System.IO.File]::ReadAllText($Path)
    $regex = [System.Text.RegularExpressions.Regex]::new(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    $matches = $regex.Matches($content)

    if ($matches.Count -ne 1) {
        throw "Attendu 1 correspondance pour '$Label' dans $Path, trouvé : $($matches.Count)."
    }

    $content = $regex.Replace(
        $content,
        $Replacement,
        1
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $content,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Host "OK - $Label"
}

# ---------------------------------------------------------------------------
# 1. L'overlay "Mise à jour marché" ne doit plus être créé
#    simplement parce qu'une capture envoie un diagnostic.
# ---------------------------------------------------------------------------

Replace-RegexOnce `
    -Path $market `
    -Label "Ajout du dernier diagnostic en mémoire" `
    -Pattern '    private Window\? _window;\s*private MarketCaptureOverlayPage\? _page;' `
    -Replacement @'
    private Window? _window;
    private MarketCaptureOverlayPage? _page;

    // Les captures peuvent mettre à jour leur diagnostic
    // sans provoquer l'ouverture de cette fenêtre.
    private Action<MarketCaptureOverlayPage>?
        _pendingUpdate;
'@

Replace-RegexOnce `
    -Path $market `
    -Label "Update sans ouverture automatique" `
    -Pattern '    private void Update\(\s*Action<MarketCaptureOverlayPage> update\)\s*\{.*?\n    \}\s*private void EnsureWindow\(\)' `
    -Replacement @'
    private void Update(
        Action<MarketCaptureOverlayPage> update)
    {
        void Apply()
        {
            // Toujours mémoriser le dernier diagnostic.
            _pendingUpdate = update;

            // Une capture ne doit jamais créer ou ouvrir
            // l'overlay "Mise à jour marché".
            if (_page is null)
            {
                return;
            }

            update(_page);
        }

        if (MainThread.IsMainThread)
        {
            Apply();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Apply);
        }
    }

    private void EnsureWindow()
'@

Replace-RegexOnce `
    -Path $market `
    -Label "Restaurer le dernier diagnostic à l'ouverture manuelle" `
    -Pattern '        _page = page;\s*_window = window;\s*window\.Created \+=' `
    -Replacement @'
        _page = page;
        _window = window;

        // Si des captures ont eu lieu pendant que cette
        // fenêtre était fermée, afficher le dernier état
        // lorsque l'utilisateur l'ouvre volontairement.
        _pendingUpdate?.Invoke(page);

        window.Created +=
'@

# ---------------------------------------------------------------------------
# 2. Permettre à l'overlay Rentabilité de demander un
#    rafraîchissement immédiat de la couleur du bouton.
# ---------------------------------------------------------------------------

Replace-RegexOnce `
    -Path $control `
    -Label "Ajout RefreshState sur la barre" `
    -Pattern '    public void OpenSettings\(\)\s*\{\s*_bindings\?\.OpenSettings\(\);\s*\}\s*public void Shutdown\(\)' `
    -Replacement @'
    public void OpenSettings()
    {
        _bindings?.OpenSettings();
    }

    public void RefreshState()
    {
        _page?.RefreshState();
    }

    public void Shutdown()
'@

# ---------------------------------------------------------------------------
# 3. Quand un VRAI équipement devient le focus, ouvrir
#    automatiquement Rentabilité après son recalcul.
#
#    Rune / ressource / capture ratée => aucune ouverture.
# ---------------------------------------------------------------------------

Replace-RegexOnce `
    -Path $overlay `
    -Label "Focus infobulle => ouvrir Rentabilité" `
    -Pattern '(focusedEquipmentState\s*\.SetEquipment\(\s*tooltipEquipment\s*\);\s*await RefreshFocusedProfitabilityAsync\(\);)' `
    -Replacement @'
$1

                                await MainThread
                                    .InvokeOnMainThreadAsync(
                                        Show
                                    );
'@

Replace-RegexOnce `
    -Path $overlay `
    -Label "Focus équipement HDV => ouvrir Rentabilité" `
    -Pattern '(focusedEquipmentState\.SetEquipment\(marketEquipment\);.*?await RefreshFocusedProfitabilityAsync\(\);)' `
    -Replacement @'
$1

                            await MainThread
                                .InvokeOnMainThreadAsync(
                                    Show
                                );
'@

Replace-RegexOnce `
    -Path $overlay `
    -Label "Focus concasseur => ouvrir Rentabilité" `
    -Pattern '(focusedEquipmentState\.SetEquipment\(equipment\);.*?await RefreshFocusedProfitabilityAsync\(\);)' `
    -Replacement @'
$1

                await MainThread
                    .InvokeOnMainThreadAsync(
                        Show
                    );
'@

Replace-RegexOnce `
    -Path $overlay `
    -Label "Bouton Rentabilité vert immédiatement" `
    -Pattern '        _isOverlayVisible = true;\s*#endif\s*    \}\s*    public void Hide\(\)' `
    -Replacement @'
        _isOverlayVisible = true;

        overlayControlBarService
            .RefreshState();
#endif
    }

    public void Hide()
'@

Replace-RegexOnce `
    -Path $overlay `
    -Label "Bouton Rentabilité gris immédiatement" `
    -Pattern '        _isOverlayVisible = false;\s*#endif\s*    \}\s*    public void BeginDrag\(\)' `
    -Replacement @'
        _isOverlayVisible = false;

        overlayControlBarService
            .RefreshState();
#endif
    }

    public void BeginDrag()
'@

# Les backups laissés par le script v1 n'ont plus d'utilité
# et risqueraient d'être inclus par erreur dans "git add .".
foreach ($file in @($market, $control, $overlay)) {
    Remove-Item "$file.before-overlay-focus-fix" `
        -Force `
        -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Correctif applique."
Write-Host ""
Write-Host "Verifier les changements :"
Write-Host "git diff -- BestCrush/Services/MarketCaptureOverlayService.cs BestCrush/Services/OverlayControlBarService.cs BestCrush/Services/OverlayService.cs"
Write-Host ""
Write-Host "Puis compiler :"
Write-Host 'dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0'
