# GloomSpeed installer for Windows. Run through "GloomSpeed install - Windows.bat" or "GloomSpeed uninstall - Windows.bat".
param(
    [ValidateSet('install', 'uninstall')] [string] $Action = 'install',
    [string] $GameDir = '',
    [string] $BepInExZip = ''
)
$ErrorActionPreference = 'Stop'

$BepInExUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip'
$BepInExSha = '6C57A714A892EFB4E9E1FEF63D608A3391F30EC984031C465404E4EE381A0755'
$RecordName = 'GloomSpeed-installed-files.txt'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-GameDir {
    $roots = @()
    try {
        $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) {
            $roots += $steam
            $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
            if (Test-Path $vdf) {
                foreach ($m in [regex]::Matches((Get-Content -Raw $vdf), '"path"\s+"([^"]+)"')) {
                    $roots += $m.Groups[1].Value.Replace('\\', '\')
                }
            }
        }
    } catch { }
    foreach ($root in $roots) {
        $candidate = Join-Path $root 'steamapps\common\Gloomhaven'
        if (Test-Path (Join-Path $candidate 'GH.exe')) { return $candidate }
    }
    return ''
}

function Show-Check($dir) {
    $log = Join-Path $dir 'BepInEx/LogOutput.log'
    if (-not (Test-Path $log)) {
        Write-Host 'The mod has not run yet. Start the game from Steam, reach the main menu, quit, then run the installer again to check it.'
        return
    }
    $lines = Select-String -Path $log -Pattern 'GloomSpeed.*patches applied' | ForEach-Object { $_.Line }
    if ($lines) {
        Write-Host 'Result of the last game launch:'
        $lines | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host 'BepInEx ran, but GloomSpeed did not report in. Please open an issue with this file:'
        Write-Host "  $log"
    }
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'GH.exe'))) {
    Write-Host 'Could not find Gloomhaven automatically.'
    Write-Host 'In Steam: right-click Gloomhaven > Manage > Browse local files, then copy that folder path.'
    $GameDir = (Read-Host 'Paste the folder path here').Trim('" ')
    if (-not (Test-Path (Join-Path $GameDir 'GH.exe'))) { throw "GH.exe was not found in: $GameDir" }
}
Write-Host "Game folder: $GameDir"

if (Get-Process -Name 'GH' -ErrorAction SilentlyContinue) { throw 'Gloomhaven is running. Close it and try again.' }

$record = Join-Path $GameDir $RecordName
$bepinex = Join-Path $GameDir 'BepInEx'
$plugins = Join-Path $bepinex 'plugins'
$docs = [Environment]::GetFolderPath('MyDocuments')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

if ($Action -eq 'uninstall') {
    if (-not (Test-Path $record)) { throw "This installer didn't install GloomSpeed here. If you installed it by hand, follow the Remove steps in the install guide." }
    $logs = Join-Path $bepinex 'GloomSpeed'
    if (Test-Path $logs) {
        $keep = Join-Path $docs "GloomSpeed-logs-$stamp"
        Copy-Item -Recurse $logs $keep
        Write-Host "Timing logs kept at: $keep"
    }
    foreach ($file in Get-Content $record) {
        if ($file -and $file -notlike 'BepInEx*') {
            $target = Join-Path $GameDir $file
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        }
    }
    if (Test-Path $bepinex) { Remove-Item -Recurse -Force $bepinex }
    Remove-Item -Force $record
    Write-Host 'GloomSpeed is removed. Your game and saves are as they were.'
    exit 0
}

$dll = Join-Path $Here 'GloomSpeed.dll'
if (-not (Test-Path $dll)) { throw 'GloomSpeed.dll is missing. Unzip the whole package first, then run this from the unzipped folder.' }

if ((Test-Path $record) -or (Test-Path (Join-Path $plugins 'GloomSpeed.dll'))) {
    Copy-Item -Force $dll (Join-Path $plugins 'GloomSpeed.dll')
    Write-Host 'GloomSpeed was already installed, so it has been updated. Your settings are kept.'
    Show-Check $GameDir
    exit 0
}

$saves = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/FlamingFowlStudios/Gloomhaven/GloomSaves'
if (Test-Path $saves) {
    $backup = Join-Path $docs "Gloomhaven-save-backup-$stamp"
    Copy-Item -Recurse $saves $backup
    Write-Host "Saves backed up to: $backup"
} else {
    Write-Host 'No saves found to back up. Continuing.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "gloomspeed-$stamp"
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $zip = $BepInExZip
    if (-not $zip) {
        $zip = Join-Path $temp 'BepInEx.zip'
        Write-Host 'Downloading BepInEx from its official GitHub release...'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
    }
    if ((Get-FileHash -Algorithm SHA256 $zip).Hash -ne $BepInExSha) { throw 'BepInEx download failed its checksum. Nothing was installed.' }

    $unpacked = Join-Path $temp 'unpacked'
    Expand-Archive -Path $zip -DestinationPath $unpacked
    $files = Get-ChildItem -Recurse -Force -File $unpacked | ForEach-Object { $_.FullName.Substring($unpacked.Length + 1) }
    foreach ($file in $files) {
        if (Test-Path -LiteralPath (Join-Path $GameDir $file)) { throw "Refusing to overwrite an existing file: $file" }
    }
    Set-Content -Path $record -Value $files
    foreach ($file in $files) {
        $target = Join-Path $GameDir $file
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $unpacked $file) -Destination $target
    }
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    Copy-Item -Force $dll (Join-Path $plugins 'GloomSpeed.dll')
} finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Installed. Start Gloomhaven from Steam as usual, no launch options needed.'
Write-Host 'To check it worked, open Options > General in the game and look for Game Speed.'
