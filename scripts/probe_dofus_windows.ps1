#requires -Version 5.1
<#
.SYNOPSIS
  Diagnostic local des titres et du focus des fenetres Dofus.
.DESCRIPTION
  Liste les fenetres Windows visibles dont le processus contient "dofus".
  Pendant une courte periode, suit les apparitions, changements de titre et
  changements de fenetre active. Aucune capture reseau, aucun fichier cree.
  Le titre peut contenir un nom de personnage : le conserver localement.
.EXAMPLE
  powershell -NoProfile -File .\scripts\probe_dofus_windows.ps1
.EXAMPLE
  powershell -NoProfile -File .\scripts\probe_dofus_windows.ps1 -RedactTitles
#>
[CmdletBinding()]
param(
    [ValidateRange(3, 180)]
    [int]$WatchSeconds = 30,
    [switch]$RedactTitles
)

if ($env:OS -ne 'Windows_NT') {
    throw "Ce diagnostic utilise user32.dll et doit etre execute sur Windows."
}

$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class BestCrushDofusWindowProbe
{
    public sealed class WindowInfo
    {
        public long Handle { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string Title { get; set; }
        public bool Foreground { get; set; }
        public int Order { get; set; }
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr window, StringBuilder buffer, int maxCount);

    public static WindowInfo[] Scan()
    {
        var windows = new List<WindowInfo>();
        IntPtr foreground = GetForegroundWindow();

        EnumWindows(delegate (IntPtr window, IntPtr unused)
        {
            if (!IsWindowVisible(window))
                return true;

            uint id;
            GetWindowThreadProcessId(window, out id);
            if (id == 0 || id > int.MaxValue)
                return true;

            try
            {
                using (Process process = Process.GetProcessById((int)id))
                {
                    string name = process.ProcessName;
                    if (name.IndexOf("dofus", StringComparison.OrdinalIgnoreCase) < 0)
                        return true;

                    int titleLength = GetWindowTextLength(window);
                    var buffer = new StringBuilder(Math.Max(titleLength + 1, 1));
                    if (titleLength > 0)
                        GetWindowText(window, buffer, buffer.Capacity);

                    windows.Add(new WindowInfo
                    {
                        Handle = window.ToInt64(),
                        ProcessId = (int)id,
                        ProcessName = name,
                        Title = buffer.ToString(),
                        Foreground = window == foreground,
                        Order = windows.Count + 1
                    });
                }
            }
            catch (ArgumentException)
            {
                // Le processus peut s'arreter entre la lecture des PID et le scan.
            }
            catch (InvalidOperationException)
            {
                // Fenetre ou processus disparu pendant la collecte.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Lecture du processus non autorisee.
            }
            return true;
        }, IntPtr.Zero);

        return windows.ToArray();
    }
}
'@

if (-not ('BestCrushDofusWindowProbe' -as [type])) {
    Add-Type -TypeDefinition $source -Language CSharp -ErrorAction Stop
}

function Format-WindowTitle([string]$Title) {
    if ([string]::IsNullOrEmpty($Title)) { return "<vide>" }
    if ($RedactTitles) { return "<masque; longueur=$($Title.Length)>" }
    return $Title
}

function Show-Window([string]$Event, $Window) {
    $focusMark = if ($Window.Foreground) { "ACTIF" } else { "inactif" }
    $title = Format-WindowTitle $Window.Title
    Write-Host ("{0} | PID={1} | HWND={2} | {3} | titre={4}" -f
        $Event, $Window.ProcessId, $Window.Handle, $focusMark, $title)
}

Write-Host "Diagnostic Dofus (aucune ecriture disque, aucune capture reseau)."
Write-Host "Le titre peut contenir ton nom de personnage : ne le partage pas tel quel."
Write-Host ""
$first = [BestCrushDofusWindowProbe]::Scan()
Write-Host "Fenetres visibles Dofus au lancement : $($first.Length)"
foreach ($window in $first) {
    Show-Window "fenetre#$($window.Order)" $window
}

if ($first.Length -eq 0) {
    Write-Host "Aucune fenetre reconnue : verifier que Dofus est lance."
    Write-Host "Processus dont le nom contient 'dofus' :"
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -match 'dofus' } |
        Select-Object Id, ProcessName |
        Format-Table -AutoSize
}

Write-Host ""
Write-Host "Pendant $WatchSeconds secondes, active une fenetre Dofus, puis une autre."
Write-Host "Le script observe le focus et les changements de titres."
$seen = @{}
foreach ($window in $first) {
    $seen[[string]$window.Handle] = $window.Title
}
$lastFocus = ""
$timer = [System.Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $WatchSeconds) {
    Start-Sleep -Milliseconds 300
    $windows = [BestCrushDofusWindowProbe]::Scan()

    foreach ($window in $windows) {
        $key = [string]$window.Handle
        if (-not $seen.ContainsKey($key)) {
            Show-Window "apparition" $window
            $seen[$key] = $window.Title
        }
        elseif ($seen[$key] -cne $window.Title) {
            Show-Window "titre-modifie" $window
            $seen[$key] = $window.Title
        }
    }

    $active = @($windows | Where-Object { $_.Foreground } | Select-Object -First 1)
    $currentFocus = if ($active.Count -gt 0) { [string]$active[0].Handle } else { "" }
    if ($currentFocus -cne $lastFocus) {
        if ($active.Count -gt 0) {
            Show-Window "focus-dofus" $active[0]
        }
        elseif ($lastFocus -ne "") {
            Write-Host "focus-quitte-dofus"
        }
        $lastFocus = $currentFocus
    }
}
Write-Host ""
Write-Host "Fin du diagnostic. Rien n'a ete enregistre."
Write-Host "Pour partager le rapport, masquer les noms de personnages contenus dans les titres."
