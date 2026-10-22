#requires -Version 5.1
<#
.SYNOPSIS
  P3m.10: observes remote TCP endpoints owned by Dofus/Ankama processes
  during manual A -> B -> A game server connections, across all ports.
.DESCRIPTION
  Windows Get-NetTCPConnection only; no packet payload, no files.
  Short-lived sockets between polls, child processes with other names,
  UDP/QUIC and launcher/server selection outside this process group
  may be missed. Endpoints are not logical game server identities.
#>
[CmdletBinding()]
param(
    [ValidateRange(250, 3000)] [int]$PollMilliseconds = 500,
    [ValidateRange(10, 200)] [int]$MaxEventsPerPhase = 120
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'P3m.10 necessite Windows.' }
if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
    throw 'Get-NetTCPConnection est indisponible.'
}

$watch = {
    param([int]$Poll, [int]$Limit)
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $emitted = 0
    while ($true) {
        $processes = @(Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -match 'dofus|ankama' })
        foreach ($proc in $processes) {
            $pidValue = [int]$proc.Id
            $name = [string]$proc.ProcessName
            try {
                $sockets = @(Get-NetTCPConnection -OwningProcess $pidValue -ErrorAction Stop)
            }
            catch { continue }
            foreach ($socket in $sockets) {
                if ($socket.RemotePort -le 0 -or
                    $socket.RemoteAddress -in @('0.0.0.0', '::', '*')) {
                    continue
                }
                $key = '{0}|{1}|{2}|{3}' -f $pidValue,
                    $socket.LocalPort, $socket.RemoteAddress, $socket.RemotePort
                if (-not $seen.Add($key)) { continue }
                if ($emitted -ge $Limit) {
                    [PSCustomObject]@{ Type = 'limit' }
                    return
                }
                $emitted++
                [PSCustomObject]@{
                    Type = 'socket'
                    WhenUtc = [DateTime]::UtcNow.ToString('HH:mm:ss.fff')
                    ProcessName = $name
                    ProcessId = $pidValue
                    RemoteAddress = [string]$socket.RemoteAddress
                    RemotePort = [int]$socket.RemotePort
                    State = [string]$socket.State
                }
            }
        }
        Start-Sleep -Milliseconds $Poll
    }
}

$stages = @(
    @{ Label = 'A1'; Description = 'Serveur A (Draconiros)' },
    @{ Label = 'B'; Description = 'Serveur B (Tylezia)' },
    @{ Label = 'A2'; Description = 'Serveur A (Draconiros) de nouveau' }
)
Write-Host 'P3m.10: connexions TCP Dofus/Ankama sur tous les ports.'
Write-Host 'Aucun payload, aucun fichier; IP, port et processus distants visibles.'
Write-Host 'La phase A/B est declaree manuellement; server=unverified.'
Write-Host 'Fermer toutes les fenetres Dofus avant de commencer.'
Write-Host ''

$results = @{}
foreach ($stage in $stages) {
    $label = [string]$stage.Label
    Write-Host ('PHASE {0}: {1}' -f $label, $stage.Description)
    $job = Start-Job -ScriptBlock $watch -ArgumentList @(
        $PollMilliseconds, $MaxEventsPerPhase)
    try {
        # Wait for worker startup before the user starts the client.
        Start-Sleep -Seconds 2
        if ($job.State -ne 'Running') {
            throw ('La surveillance TCP ne fonctionne pas (job={0}).' -f $job.State)
        }
        [void](Read-Host 'Ouvrir le jeu, se connecter, fermer Dofus, puis Entree')
    }
    finally {
        if ($job.State -eq 'Running') {
            Stop-Job -Job $job -ErrorAction SilentlyContinue
        }
        $items = @(Receive-Job -Job $job -ErrorAction SilentlyContinue)
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }

    $records = @($items | Where-Object { $_.Type -eq 'socket' })
    $truncated = @($items | Where-Object { $_.Type -eq 'limit' }).Count -gt 0
    $endpoints = @($records |
        Sort-Object ProcessName, RemoteAddress, RemotePort -Unique)
    $results[$label] = @($endpoints)
    Write-Host ('p3m10=stage-{0} flow-observations={1} distinct-endpoints={2} truncated={3} server=unverified' -f
        $label, $records.Count, $endpoints.Count,
        $(if ($truncated) { 'yes' } else { 'no' }))

    foreach ($endpoint in $endpoints) {
        $remote = [string]$endpoint.RemoteAddress
        if ($remote.Contains(':')) { $remote = '[' + $remote + ']' }
        Write-Host ('p3m10=endpoint stage={0} process={1} pid={2} remote={3}:{4} first-seen={5} state={6} server=unverified' -f
            $label, $endpoint.ProcessName, $endpoint.ProcessId,
            $remote, $endpoint.RemotePort, $endpoint.WhenUtc, $endpoint.State)
    }
    Write-Host ''
}

function Make-EndpointSet($entries) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($entry in @($entries)) {
        [void]$set.Add(('{0}|{1}|{2}' -f
            $entry.ProcessName, $entry.RemoteAddress, $entry.RemotePort))
    }
    return ,$set
}
$a1 = Make-EndpointSet $results['A1']
$b = Make-EndpointSet $results['B']
$a2 = Make-EndpointSet $results['A2']
$sharedA = @($a1 | Where-Object { $a2.Contains($_) })
$aNotB = @($sharedA | Where-Object { -not $b.Contains($_) })
Write-Host ('p3m10=summary shared-A-endpoints={0} A-not-B-endpoints={1} meaning=network-only server=unverified' -f
    $sharedA.Count, $aNotB.Count)
Write-Host 'Fin. Les connexions trop courtes et les autres transports peuvent ne pas apparaitre.'
