$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Path = ".\BestCrush\Services\OverlayService.cs"

if (-not (Test-Path $Path)) {
    throw "Fichier introuvable : $Path"
}

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$content = [System.IO.File]::ReadAllText(
    (Resolve-Path $Path),
    [System.Text.Encoding]::UTF8
)

$original = $content

# Les notifications Rune et Ressource avaient été insérées
# après la portée locale de runeServerName/resourceServerName.
# Le serveur courant est déjà obligatoire avant toute capture ;
# on utilise donc directement CurrentServerState ici.
$content = [regex]::Replace(
    $content,
    '(marketDataChangeNotifier\.Notify\(\s*MarketObjectType\.Rune,\s*runeRecognition\.Rune\.DofusDbId,\s*)runeServerName(\s*,)',
    '$1currentServerState.ServerName!$2'
)

$content = [regex]::Replace(
    $content,
    '(marketDataChangeNotifier\.Notify\(\s*MarketObjectType\.Resource,\s*resourceRecognition\.Resource\.DofusDbId,\s*)resourceServerName(\s*,)',
    '$1currentServerState.ServerName!$2'
)

if ($content -eq $original) {
    throw "Aucun bloc fautif runeServerName/resourceServerName n'a été trouvé. Envoie-moi OverlayService.cs avant toute autre modification."
}

[System.IO.File]::WriteAllText(
    (Resolve-Path $Path),
    $content,
    $Utf8NoBom
)

Write-Host "Correctif applique : $Path"
Write-Host ""
Write-Host "Compile maintenant avec :"
Write-Host "dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0"
