$ErrorActionPreference = "Stop"

function Get-PathEntries([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return @() }
    return @($value -split ';' | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
}

function Get-SearchEntries {
    $machine = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $user = [Environment]::GetEnvironmentVariable("Path", "User")
    $process = [Environment]::GetEnvironmentVariable("Path", "Process")
    return @(Get-PathEntries "$machine;$user;$process")
}

function Find-Tool([string]$name, [string[]]$entries) {
    foreach ($dir in $entries) {
        $candidate = Join-Path $dir "$name.exe"
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    return $null
}

function Test-Ready([string[]]$entries) {
    return (Find-Tool "ffmpeg" $entries) -and (Find-Tool "ffprobe" $entries)
}

function Show-Found([string[]]$entries) {
    Write-Host "ffmpeg:  $(Find-Tool 'ffmpeg' $entries)"
    Write-Host "ffprobe: $(Find-Tool 'ffprobe' $entries)"
    Write-Host "Easy Tagger einmal über das Tray-Menü beenden und neu starten."
}

function Add-UserPath([string]$bin) {
    $full = [System.IO.Path]::GetFullPath($bin)
    $user = [Environment]::GetEnvironmentVariable("Path", "User")
    $existing = @(Get-PathEntries $user)
    foreach ($dir in $existing) {
        try {
            if ([string]::Equals([System.IO.Path]::GetFullPath($dir), $full, [StringComparison]::OrdinalIgnoreCase)) {
                return
            }
        } catch {
            continue
        }
    }
    $updated = if ([string]::IsNullOrWhiteSpace($user)) { $full } else { "$user;$full" }
    [Environment]::SetEnvironmentVariable("Path", $updated, "User")
    $env:Path = "$( [Environment]::GetEnvironmentVariable('Path', 'Machine') );$( [Environment]::GetEnvironmentVariable('Path', 'User') )"
}

function Install-FromZip {
    $zipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    $tempRoot = Join-Path $env:TEMP ("ffmpeg-extract-" + [guid]::NewGuid().ToString("N"))
    $zip = Join-Path $tempRoot "ffmpeg-release-essentials.zip"
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    Write-Host "Lade FFmpeg Essentials ..."
    Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $tempRoot -Force
    $ffmpeg = Get-ChildItem -LiteralPath $tempRoot -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    if (-not $ffmpeg) { throw "ffmpeg.exe wurde im Archiv nicht gefunden." }
    $sourceBin = $ffmpeg.DirectoryName
    if (-not (Test-Path -LiteralPath (Join-Path $sourceBin "ffprobe.exe"))) {
        throw "ffprobe.exe fehlt im Archiv."
    }
    $dest = Join-Path $env:LOCALAPPDATA "Programs\ffmpeg\bin"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Path (Join-Path $sourceBin "*") -Destination $dest -Force
    Add-UserPath $dest
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$entries = Get-SearchEntries
if (Test-Ready $entries) {
    Write-Host "FFmpeg liegt schon im PATH."
    Show-Found $entries
    exit 0
}

$winget = Get-Command winget -ErrorAction SilentlyContinue
if ($winget) {
    Write-Host "Installiere FFmpeg Essentials mit winget ..."
    & winget install --id Gyan.FFmpeg.Essentials -e --accept-package-agreements --accept-source-agreements
    $entries = Get-SearchEntries
    if (Test-Ready $entries) {
        Write-Host "FFmpeg ist installiert."
        Show-Found $entries
        exit 0
    }
    Write-Host "winget hat ffmpeg nicht in den PATH gelegt."
}

Install-FromZip
$entries = Get-SearchEntries
if (-not (Test-Ready $entries)) { throw "FFmpeg ist installiert, aber nicht im PATH auffindbar." }
Write-Host "FFmpeg ist installiert."
Show-Found $entries
exit 0
