<#
.SYNOPSIS
    Terrafa Continuum installer for Windows.

.DESCRIPTION
    irm https://raw.githubusercontent.com/JamesParkinsonTerrafa/Terrafa.Continuum.Frontend/main/install.ps1 | iex

    Files fetched by Invoke-WebRequest do not carry the Mark-of-the-Web, so the
    app installed this way starts without a SmartScreen prompt — unlike the same
    build downloaded through a browser.

    When piping to iex, pass options through the environment instead of
    parameters:
        $env:TERRAFA_VERSION = 'v0.0.3'
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version = $env:TERRAFA_VERSION,
    [string]$InstallDir = $(if ($env:TERRAFA_INSTALL_DIR) { $env:TERRAFA_INSTALL_DIR }
        else { Join-Path $env:LOCALAPPDATA 'Programs\Terrafa Continuum' })
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # Invoke-WebRequest is far faster without it.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo = 'JamesParkinsonTerrafa/Terrafa.Continuum.Frontend'
$AppName = 'Terrafa Continuum'
$ExeName = 'Terrafa.Continuum.Frontend.exe'
$Rid = 'win-x64'   # runs natively on x64 and under emulation on arm64

function Write-Info { param($Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Warn { param($Message) Write-Host "warning: $Message" -ForegroundColor Yellow }

# --- resolve version --------------------------------------------------------

if ($Version) {
    $tag = "v$($Version -replace '^v', '')"
} else {
    Write-Info 'Resolving latest release'
    try {
        $latest = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" `
            -Headers @{ 'User-Agent' = 'terrafa-installer' }
    } catch {
        throw "Could not reach GitHub: $($_.Exception.Message)"
    }
    $tag = $latest.tag_name
    if (-not $tag) { throw 'No releases have been published yet.' }
}
$resolved = $tag -replace '^v', ''

$asset = "Terrafa.Continuum-$resolved-$Rid.zip"
$base = "https://github.com/$Repo/releases/download/$tag"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

try {
    # --- download and verify ------------------------------------------------

    Write-Info "Downloading $asset"
    $archive = Join-Path $tmp $asset
    try {
        Invoke-WebRequest "$base/$asset" -OutFile $archive -UseBasicParsing
    } catch {
        throw "No build for $Rid in release ${tag}: $($_.Exception.Message)"
    }

    try {
        $sums = Join-Path $tmp 'SHA256SUMS'
        Invoke-WebRequest "$base/SHA256SUMS" -OutFile $sums -UseBasicParsing
        $line = Get-Content $sums | Where-Object { $_ -match "\s\*?$([regex]::Escape($asset))$" }
        if ($line) {
            $expected = ($line -split '\s+')[0]
            $actual = (Get-FileHash $archive -Algorithm SHA256).Hash
            if ($expected -ne $actual) { throw "Checksum mismatch for $asset." }
            Write-Info 'Checksum verified'
        } else {
            Write-Warn "no checksum listed for $asset"
        }
    } catch [Net.WebException] {
        Write-Warn "no SHA256SUMS in release $tag - skipping verification"
    }

    # --- install ------------------------------------------------------------

    Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Info "Closing running $AppName"
            $_ | Stop-Process -Force
        }

    Expand-Archive -Path $archive -DestinationPath (Join-Path $tmp 'extract') -Force
    $source = Join-Path $tmp "extract\$ExeName"
    if (-not (Test-Path $source)) { throw "Archive did not contain $ExeName." }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $exe = Join-Path $InstallDir $ExeName
    Copy-Item $source $exe -Force
    Unblock-File $exe   # no-op for IWR downloads; harmless insurance

    # Start Menu shortcut
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $InstallDir
    $link.Description = $AppName
    $link.Save()

    # Uninstaller, so it appears in Apps & features
    @'
$ErrorActionPreference = 'SilentlyContinue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Get-Process -Name 'Terrafa.Continuum.Frontend' | Stop-Process -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Terrafa Continuum.lnk') -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TerrafaContinuum' -Recurse -Force
Start-Process powershell -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-Command',
    "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$dir' -Recurse -Force"
)
'@ | Set-Content -Path (Join-Path $InstallDir 'uninstall.ps1') -Encoding UTF8

    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TerrafaContinuum'
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty $key DisplayName     $AppName
    Set-ItemProperty $key DisplayVersion  $resolved
    Set-ItemProperty $key Publisher       'Terrafa'
    Set-ItemProperty $key InstallLocation $InstallDir
    Set-ItemProperty $key DisplayIcon     $exe
    Set-ItemProperty $key UninstallString "powershell -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
    Set-ItemProperty $key NoModify 1 -Type DWord
    Set-ItemProperty $key NoRepair 1 -Type DWord

    Write-Info "Installed $AppName $resolved to $InstallDir"
    Write-Info 'Launch it from the Start Menu, or run:'
    Write-Host "    & '$exe'"
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
