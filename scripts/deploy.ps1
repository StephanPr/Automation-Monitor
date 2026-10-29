param(
    [Parameter(Mandatory = $true)]
    [string]$XrmToolBoxDirectory,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputDir = Join-Path $projectRoot "src/AutomationMonitor.Plugin/bin/$Configuration/net48"
$assemblyName = 'AutomationMonitor.Plugin'
$exe = Join-Path $XrmToolBoxDirectory 'XrmToolBox.exe'
$pluginsDir = Join-Path $XrmToolBoxDirectory 'Plugins'

if (!(Test-Path $exe)) {
    throw "XrmToolBox.exe not found in $XrmToolBoxDirectory"
}
if (!(Test-Path (Join-Path $outputDir "$assemblyName.dll"))) {
    throw 'Plugin DLL not found. Run the build task first.'
}
if (Get-Process -Name XrmToolBox -ErrorAction SilentlyContinue) {
    throw 'Close XrmToolBox before replacing the plugin assembly.'
}

New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
foreach ($ext in @('dll', 'pdb')) {
    $candidate = Join-Path $outputDir "$assemblyName.$ext"
    if (Test-Path $candidate) {
        Copy-Item -Path $candidate -Destination $pluginsDir -Force
    }
}
Write-Host "Deployed plugin to $pluginsDir"

if ($Launch) {
    Start-Process -FilePath $exe -WorkingDirectory $XrmToolBoxDirectory -ArgumentList '/overridepath:.'
}
