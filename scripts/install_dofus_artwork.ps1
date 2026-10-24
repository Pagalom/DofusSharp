#requires -Version 5.1
<#
.SYNOPSIS
  Installe une copie locale des icones des metiers et des portraits de classes DOFUS.
.DESCRIPTION
  Telecharge les deux archives et leur catalogue jobs.json (DOFUS 3.7.12.12),
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
    @{ File = 'class_head_images_64.tar.gz'; Sha256 = '9199f502a2a13070024730ffdcc5ec27ab42bb82daea80a4eca26a06d2134258' },
    @{ File = 'jobs.json'; Sha256 = '7340c83f7f0cd75aecaccf4bc6113e9519d507acd007488d4ee7a873bbaab0e6' }
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

# DOFUS stores a job id and a separate job iconId.
# jobs.json may be represented as a list or as a map keyed by job id.
# Only explicit job-id/iconId pairs from a SHA-256-verified file are used.
function Read-JobIconRecords {
    param([object]$Node, [string]$Key = '', [int]$Depth = 0)

    if ($null -eq $Node -or $Depth -gt 6) { return }

    if ($Node -is [System.Array]) {
        foreach ($entry in $Node) {
            Read-JobIconRecords -Node $entry -Depth ($Depth + 1)
        }
        return
    }

    if ($Node -isnot [pscustomobject]) { return }

    $iconProperty = $Node.PSObject.Properties['iconId']
    if ($null -ne $iconProperty) {
        $idProperty = $Node.PSObject.Properties['id']
        $idText = if ($null -ne $idProperty) {
            [string]$idProperty.Value
        } else {
            $Key
        }

        $jobId = 0
        $iconId = 0
        if ([int]::TryParse($idText, [ref]$jobId) -and
            [int]::TryParse([string]$iconProperty.Value, [ref]$iconId) -and
            $jobId -gt 0 -and $iconId -gt 0) {
            [pscustomobject]@{ JobId = $jobId; IconId = $iconId }
        }
        return
    }

    foreach ($property in $Node.PSObject.Properties) {
        Read-JobIconRecords -Node $property.Value -Key $property.Name -Depth ($Depth + 1)
    }
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
        if ($entry.File.EndsWith('.tar.gz', [StringComparison]::Ordinal)) {
            $extractTo = Join-Path $temp ($entry.File -replace '\.tar\.gz
    }

    $jobSource = Join-Path $temp 'job_images_64'
    $headSource = Join-Path $temp 'class_head_images_64'
    $jobFiles = @(Get-ChildItem -LiteralPath $jobSource -Filter '*.png' -File -Recurse)
    $headFiles = @(Get-ChildItem -LiteralPath $headSource -Filter '*.png' -File -Recurse)

    # Load the ID -> iconId relation from this exact Dofus release.
    # Job 11 (Forgeron) for example refers to image 10, not image 11.
    $jobsMetadata = Get-Content -LiteralPath (Join-Path $temp 'jobs.json') -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $records = @(Read-JobIconRecords -Node $jobsMetadata)
    if ($records.Count -lt 20) {
        $topFields = if ($jobsMetadata -is [pscustomobject]) {
            ($jobsMetadata.PSObject.Properties.Name | Select-Object -First 12) -join ', '
        } else {
            'JSON root array'
        }
        throw "jobs.json : moins de 20 correspondances id/iconId ($($records.Count)). Champs racine : $topFields"
    }

    $jobIcons = @{}
    foreach ($record in $records) {
        if ($jobIcons.ContainsKey([int]$record.JobId)) {
            if ($jobIcons[[int]$record.JobId] -ne [int]$record.IconId) {
                throw "jobs.json : icones contradictoires pour le metier ID $($record.JobId)."
            }
        } else {
            $jobIcons[[int]$record.JobId] = [int]$record.IconId
        }
    }

    $foundJobs = @{}
    $missingJobs = @()
    foreach ($id in $jobIds) {
        if (-not $jobIcons.ContainsKey($id)) {
            $missingJobs += "$id (pas de correspondance dans jobs.json)"
            continue
        }
        $iconId = $jobIcons[$id]
        $wanted = "$($iconId)-64.png"
        $matches = @($jobFiles | Where-Object { $_.Name -eq $wanted })
        if ($matches.Count -ne 1) {
            $missingJobs += "$id (icone $iconId : $($matches.Count) fichier(s))"
            continue
        }
        $foundJobs[$id] = $matches[0].FullName
    }

    $foundHeads = @{}
    $missingHeads = @()
    foreach ($id in $classIds) {
        # Candidate class-head IDs used by this art pack (breed x 10).
        # Never substitute another breed's face when an image is absent.
        $headId = $id * 10
        $wanted = "$($headId)-64.png"
        $matches = @($headFiles | Where-Object { $_.Name -eq $wanted })
        if ($matches.Count -ne 1) {
            $missingHeads += "$id (image $wanted : $($matches.Count) fichier(s))"
            continue
        }
        $foundHeads[$id] = $matches[0].FullName
    }

    if ($foundJobs.Count -eq 0) {
        $examples = ($jobFiles | Select-Object -First 20 -ExpandProperty Name) -join ', '
        throw "Aucune icone de metier resolue. Fichiers disponibles : $examples"
    }

    # Stage and verify all selected images before touching the destination.
    $stagedJobs = Join-Path $temp 'install-jobs'
    $stagedHeads = Join-Path $temp 'install-heads'
    New-Item -ItemType Directory -Path $stagedJobs, $stagedHeads -Force | Out-Null
    foreach ($id in $foundJobs.Keys) {
        Copy-VerifiedPng -Source $foundJobs[$id] -Destination (Join-Path $stagedJobs "$id.png")
    }
    foreach ($id in $foundHeads.Keys) {
        Copy-VerifiedPng -Source $foundHeads[$id] -Destination (Join-Path $stagedHeads "$id.png")
    }

    $jobsDest = Join-Path $webRoot 'jobs'
    $headsDest = Join-Path $webRoot 'classes'
    New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
    if (Test-Path -LiteralPath $jobsDest) {
        Remove-Item -LiteralPath $jobsDest -Recurse -Force
    }
    if (Test-Path -LiteralPath $headsDest) {
        Remove-Item -LiteralPath $headsDest -Recurse -Force
    }
    Copy-Item -LiteralPath $stagedJobs -Destination $jobsDest -Recurse
    Copy-Item -LiteralPath $stagedHeads -Destination $headsDest -Recurse

    Write-Host "Installation locale : $($foundJobs.Count)/$($jobIds.Count) metiers, $($foundHeads.Count)/$($classIds.Count) portraits." -ForegroundColor Green
    if ($missingJobs.Count -gt 0) {
        Write-Warning ("Icones indisponibles (emojis conserves) : " + ($missingJobs -join ', '))
    }
    if ($missingHeads.Count -gt 0) {
        Write-Warning ("Portraits indisponibles (avatars generiques conserves) : " + ($missingHeads -join ', '))
    }
    Write-Host "Chemin local : $webRoot"
    Write-Host "Relancer/recompiler BestCrush pour incorporer les nouveaux fichiers dans le package."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
, '')
            New-Item -ItemType Directory -Path $extractTo -Force | Out-Null
            & tar.exe -xzf $archive -C $extractTo
            if ($LASTEXITCODE -ne 0) { throw "Extraction echouee : $($entry.File)" }
        }
    }

    $jobSource = Join-Path $temp 'job_images_64'
    $headSource = Join-Path $temp 'class_head_images_64'
    $jobFiles = @(Get-ChildItem -LiteralPath $jobSource -Filter '*.png' -File -Recurse)
    $headFiles = @(Get-ChildItem -LiteralPath $headSource -Filter '*.png' -File -Recurse)

    # doduda exports 2x image names with their resolution suffix:
    # e.g. job 2 -> 2-64.png; class head 70 -> 70-64.png.
    # This is the upstream naming convention, not a guessed ID.
    # Never silently choose a similarly named but different image.
    $foundJobs = @{}
    foreach ($id in $jobIds) {
        $wanted = "$($id)-64.png"
        $matches = @($jobFiles | Where-Object { $_.Name -eq $wanted })
        if ($matches.Count -ne 1) {
            $examples = ($jobFiles | Select-Object -First 12 -ExpandProperty Name) -join ', '
            throw "Icone de metier absente ou ambigue pour ID $id, fichier attendu $wanted ($($matches.Count)). Exemples archives : $examples"
        }
        $foundJobs[$id] = $matches[0].FullName
    }

    $foundHeads = @{}
    foreach ($id in $classIds) {
        # DOFUS class-head picture IDs are breed ID x 10 (default male).
        $headId = $id * 10
        $wanted = "$($headId)-64.png"
        $matches = @($headFiles | Where-Object { $_.Name -eq $wanted })
        if ($matches.Count -ne 1) {
            $examples = ($headFiles | Select-Object -First 12 -ExpandProperty Name) -join ', '
            throw "Portrait de classe absent ou ambigu pour ID $id, fichier attendu $wanted ($($matches.Count)). Exemples archives : $examples"
        }
        $foundHeads[$id] = $matches[0].FullName
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
