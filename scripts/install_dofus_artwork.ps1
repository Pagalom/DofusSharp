#requires -Version 5.1
<#
.SYNOPSIS
  Installe une copie locale des icones des metiers et des portraits de classes DOFUS.
.DESCRIPTION
  Telecharge UNE FOIS deux archives publiees par dofusdude pour DOFUS 3.7.12.12,
  controle leurs SHA-256, puis copie les PNG dans BestCrush/wwwroot.
  Aucune image n'est telechargee par BestCrush a l'execution.
  Sources : https://github.com/dofusdude/dofus3-main/releases/tag/3.7.12.12
  Images de jeu : droits reserves Ankama. Verifier les autorisations avant redistribution.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$version = '3.7.12.12'
$repoRoot = Split-Path $PSScriptRoot -Parent
$webRoot = Join-Path $repoRoot 'BestCrush\wwwroot\img\dofus'
$downloadBase = "https://github.com/dofusdude/dofus3-main/releases/download/$version"
$archives = @(
    @{ File = 'job_images_64.tar.gz'; Sha256 = '79f495b081b8ed1ba38977ac2fb5798c6f0ea9bafa5d3858d4991deb44c6bd10' },
    @{ File = 'class_head_images_64.tar.gz'; Sha256 = '9199f502a2a13070024730ffdcc5ec27ab42bb82daea80a4eca26a06d2134258' }
)
$jobIds = @(2,11,13,15,16,24,26,27,28,36,41,44,48,60,62,63,64,65,74,79)
$classIds = @(1..18) + @(20)
$temp = Join-Path ([IO.Path]::GetTempPath()) ("bestcrush-artwork-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null

function Copy-VerifiedPng {
    param([string]$Source, [string]$Destination)
    $header = New-Object byte[] 8
    $pngSignature = [byte[]]@(137,80,78,71,13,10,26,10)
    $stream = [IO.File]::OpenRead($Source)
    try {
        if ($stream.Read($header, 0, 8) -ne 8) {
            throw "Fichier PNG incomplet : $Source"
        }
    }
    finally {
        $stream.Dispose()
    }
    for ($i = 0; $i -lt 8; $i++) {
        if ($header[$i] -ne $pngSignature[$i]) {
            throw "Fichier PNG invalide : $Source"
        }
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

try {
    foreach ($entry in $archives) {
        $archive = Join-Path $temp $entry.File
        $uri = "$downloadBase/$($entry.File)"
        Write-Host "Telechargement : $($entry.File) ($version)"
        Invoke-WebRequest -Uri $uri -OutFile $archive -UseBasicParsing
        $actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $entry.Sha256) {
            throw "SHA-256 incorrect pour $($entry.File). Aucun visuel ne sera installe."
        }
        $extractTo = Join-Path $temp ($entry.File -replace '\.tar\.gz$', '')
        New-Item -ItemType Directory -Path $extractTo -Force | Out-Null
        & tar.exe -xzf $archive -C $extractTo
        if ($LASTEXITCODE -ne 0) { throw "Extraction echouee : $($entry.File)" }
    }

    $jobSource = Join-Path $temp 'job_images_64'
    $headSource = Join-Path $temp 'class_head_images_64'
    $jobFiles = @(Get-ChildItem -LiteralPath $jobSource -Filter '*.png' -File -Recurse)
    $headFiles = @(Get-ChildItem -LiteralPath $headSource -Filter '*.png' -File -Recurse)

    # No implicit fallback to an icon from another profession.
    $foundJobs = @{}
    foreach ($id in $jobIds) {
        $matches = @($jobFiles | Where-Object { $_.Name -eq "$id.png" })
        if ($matches.Count -ne 1) {
            throw "Icone de metier absente ou ambigue pour ID $id ($($matches.Count))."
        }
        $foundJobs[$id] = $matches[0].FullName
    }

    $foundHeads = @{}
    foreach ($id in $classIds) {
        $headId = $id * 10
        $wanted = @("$($headId)_1.png", "$($headId)_0.png", "$headId.png")
        $picked = $null
        foreach ($name in $wanted) {
            $matches = @($headFiles | Where-Object { $_.Name -eq $name })
            if ($matches.Count -eq 1) { $picked = $matches[0].FullName; break }
        }
        if (-not $picked) {
            throw "Portrait de classe absent pour ID $id (head=$headId)."
        }
        $foundHeads[$id] = $picked
    }

    $jobsDest = Join-Path $webRoot 'jobs'
    $headsDest = Join-Path $webRoot 'classes'
    New-Item -ItemType Directory -Path $jobsDest, $headsDest -Force | Out-Null

    foreach ($id in $jobIds) {
        Copy-VerifiedPng -Source $foundJobs[$id] -Destination (Join-Path $jobsDest "$id.png")
    }
    foreach ($id in $classIds) {
        Copy-VerifiedPng -Source $foundHeads[$id] -Destination (Join-Path $headsDest "$id.png")
    }

    Write-Host "Installation locale terminee : $($jobIds.Count) icones metiers et $($classIds.Count) portraits de classes." -ForegroundColor Green
    Write-Host "Chemin local : $webRoot"
    Write-Host "Relancer/recompiler BestCrush pour incorporer les nouveaux fichiers dans le package."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
