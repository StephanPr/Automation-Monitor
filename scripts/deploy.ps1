param(
    [Parameter(Mandatory = $true)]
    [string]$XrmToolBoxDirectory,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Launch
)

<#
    Developer deploy into a local XrmToolBox installation (storage next to the exe,
    as started with /overridepath:).

    This is deliberately NOT a second installation implementation. It stages a
    plugin.manifest.json beside a copy of the build output and then runs the very
    same packaging\Install.ps1 that end users run, so the path being tested is the
    path being shipped. The only difference from a release package is that debug
    symbols are copied alongside when present, because a developer wants stack
    traces. No Python and no .NET SDK are required here.
#>
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputDir = Join-Path $projectRoot "src/AutomationMonitor.Plugin/bin/$Configuration/net48"
$installer = Join-Path $projectRoot 'packaging/Install.ps1'
$exe = Join-Path $XrmToolBoxDirectory 'XrmToolBox.exe'

if (!(Test-Path $exe)) {
    throw "XrmToolBox.exe not found in $XrmToolBoxDirectory"
}
if (!(Test-Path $installer)) {
    throw "Installer script not found: $installer"
}

# The manifest names are the only files the installer will place in Plugins.
$payloadNames = @('AutomationMonitor.Plugin.dll', 'AutomationMonitor.Core.dll')
$supportNames = @('AutomationMonitor.Plugin.pdb', 'AutomationMonitor.Core.pdb')

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('AutomationMonitor.Deploy\' + (Get-Date).ToString('yyyyMMdd-HHmmss'))
$payload = Join-Path $staging 'plugin'
New-Item -ItemType Directory -Path $payload -Force | Out-Null

$entries = @()
foreach ($name in $payloadNames) {
    $source = Join-Path $outputDir $name
    if (!(Test-Path $source)) {
        throw "Plugin assembly $name not found in $outputDir. Run the build task first."
    }
    Copy-Item -Path $source -Destination $payload -Force
    $entries += [ordered]@{
        name   = $name
        sha256 = (Get-FileHash -Path $source -Algorithm SHA256).Hash.ToLowerInvariant()
        size   = (Get-Item $source).Length
        target = 'Plugins'
    }
}

# Symbols travel with the deployment but are not manifest entries: the installer
# copies only what the manifest lists, so uninstall never removes a pdb it did not
# create and the release package can never inherit one from here.
foreach ($name in $supportNames) {
    $source = Join-Path $outputDir $name
    if (Test-Path $source) {
        Copy-Item -Path $source -Destination $payload -Force
    }
}

$fileVersion = (Get-Item (Join-Path $payload 'AutomationMonitor.Plugin.dll')).VersionInfo.FileVersion
$manifest = [ordered]@{
    schemaVersion            = 1
    product                  = 'Automation Monitor'
    component                = 'AutomationMonitor.Plugin'
    version                  = if ($fileVersion) { $fileVersion } else { '0.0.0-dev' }
    assemblyVersion          = if ($fileVersion) { $fileVersion } else { '0.0.0' }
    targetFramework          = 'net48'
    minimumXrmToolBoxVersion = '1.2025.10.74'
    files                    = $entries
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $payload 'plugin.manifest.json') -Encoding UTF8

Write-Host "Staging developer deployment from $outputDir"
# Same entry point a user gets, in its own process so the installer's exit code is
# the thing being checked rather than a side effect of dot-sourcing.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -StorageRoot $XrmToolBoxDirectory -PackageRoot $staging
if ($LASTEXITCODE -ne 0) {
    throw "Installation failed with exit code $LASTEXITCODE."
}

if ($Launch) {
    Start-Process -FilePath $exe -WorkingDirectory $XrmToolBoxDirectory -ArgumentList '/overridepath:.'
}
