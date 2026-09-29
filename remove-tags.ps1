param(
    [string]$Folder
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms | Out-Null

if ([string]::IsNullOrWhiteSpace($Folder)) {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Ordner, in dem [Tags] aus den Dateinamen entfernt werden"
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        exit 0
    }
    $Folder = $dialog.SelectedPath
}

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
    Write-Host "Ordner nicht gefunden: $Folder"
    exit 1
}

function Get-FreeTarget {
    param(
        [string]$Directory,
        [string]$Stem,
        [string]$Extension,
        [string]$CurrentPath,
        [System.Collections.Generic.HashSet[string]]$Reserved
    )

    $number = 2
    $candidate = Join-Path $Directory ($Stem + $Extension)
    while ($true) {
        $full = [System.IO.Path]::GetFullPath($candidate)
        $sameFile = [string]::Equals($full, $CurrentPath, [System.StringComparison]::OrdinalIgnoreCase)
        $onDisk = [System.IO.File]::Exists($full) -or [System.IO.Directory]::Exists($full)
        if (-not $sameFile -and ($onDisk -or $Reserved.Contains($full))) {
            $candidate = Join-Path $Directory "$Stem ($number)$Extension"
            $number++
            continue
        }

        [void]$Reserved.Add($full)
        return $full
    }
}

$reserved = New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)
$plans = New-Object System.Collections.Generic.List[object]
Get-ChildItem -LiteralPath $Folder -File | ForEach-Object {
    $stem = $_.BaseName
    if ($stem -notmatch "\[[^\]]+\]") {
        return
    }

    $clean = [regex]::Replace($stem, "\s*\[[^\]]+\]", "")
    $clean = $clean.Trim().TrimEnd("_", ".", " ")
    if ([string]::IsNullOrWhiteSpace($clean)) {
        Write-Host "Uebersprungen, der Name waere leer: $($_.Name)"
        return
    }

    $current = [System.IO.Path]::GetFullPath($_.FullName)
    $target = Get-FreeTarget -Directory $Folder -Stem $clean -Extension $_.Extension -CurrentPath $current -Reserved $reserved
    if ([string]::Equals($target, $current, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    $plans.Add([pscustomobject]@{
        From = $current
        To = $target
        Name = $_.Name
        NewName = [System.IO.Path]::GetFileName($target)
    })
}

if ($plans.Count -eq 0) {
    Write-Host "Keine Dateien mit [Tags]."
    exit 0
}

foreach ($plan in $plans) {
    Write-Host "$($plan.Name)  ->  $($plan.NewName)"
}

Write-Host ""
$answer = Read-Host "$($plans.Count) Datei(en) umbenennen? (j/n)"
if ($answer -notin @("j", "J", "y", "Y")) {
    exit 0
}

$done = 0
$failed = 0
foreach ($plan in $plans) {
    try {
        Move-Item -LiteralPath $plan.From -Destination $plan.To -ErrorAction Stop
        $done++
    }
    catch {
        $failed++
        Write-Host "Nicht umbenannt: $($plan.Name)"
        Write-Host $_.Exception.Message
    }
}

Write-Host "Fertig: $done umbenannt, $failed nicht umbenannt."
