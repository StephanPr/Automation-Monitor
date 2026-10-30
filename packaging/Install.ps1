#Requires -Version 5
<#
    Automation Monitor installer for XrmToolBox.

    Target: Windows PowerShell 5.1 (the version Shipping with Windows). No administrator
    rights are required: everything happens inside the XrmToolBox storage folder, which by
    default is %APPDATA%\MscrmTools\XrmToolBox.

    The file set to copy comes from plugin\plugin.manifest.json, never from a directory
    listing, so this script can only ever place this plugin's own assemblies. Files belonging
    to other plugins are never read, modified or removed.
#>
[CmdletBinding()]
param(
    # XrmToolBox storage root: the folder that contains Plugins. Defaults to
    # %APPDATA%\MscrmTools\XrmToolBox; pass the folder you use with /overridepath to install elsewhere.
    [string] $StorageRoot,

    # Folder that contains plugin\plugin.manifest.json. Defaults to this script's folder.
    [string] $PackageRoot = $PSScriptRoot,

    # Remove this plugin's assemblies instead of installing them.
    [switch] $Uninstall,

    # Resolve and validate everything, then stop before any file is written.
    [switch] $DryRun,

    # Name of the host process that must not be running. Test seam; leave it alone in use.
    [string] $HostName = 'XrmToolBox'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$prefix = '[AutomationMonitor.Install]'
$manifestRelative = Join-Path 'plugin' 'plugin.manifest.json'

function Write-Info {
    param([string] $Message)
    Write-Host ('{0} {1}' -f $prefix, $Message)
}

function Stop-WithError {
    param([string] $Code, [string] $Message)
    Write-Host ('{0} ERROR {1}: {2}' -f $prefix, $Code, $Message) -ForegroundColor Red
    throw ('{0}: {1}' -f $Code, $Message)
}

function Get-StorageRoot {
    param([string] $Requested)

    if ($Requested) {
        $root = $Requested
    }
    elseif ($env:AUTOMATION_MONITOR_XTB_ROOT) {
        $root = $env:AUTOMATION_MONITOR_XTB_ROOT
    }
    else {
        if (-not $env:APPDATA) {
            Stop-WithError 'APPDATA_MISSING' 'The APPDATA environment variable is not available, so the default XrmToolBox storage folder cannot be found. Pass -StorageRoot.'
        }
        $root = Join-Path (Join-Path $env:APPDATA 'MscrmTools') 'XrmToolBox'
    }

    $full = [System.IO.Path]::GetFullPath($root)
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        Stop-WithError 'STORAGE_ROOT_MISSING' ("XrmToolBox storage folder was not found: '{0}'. Start XrmToolBox once so it creates this folder, or pass -StorageRoot with the folder used by /overridepath." -f $full)
    }
    return $full
}

function Read-Manifest {
    param([string] $PackageRootPath)

    $manifestPath = Join-Path $PackageRootPath $manifestRelative
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Stop-WithError 'PACKAGE_INCOMPLETE' ("The package manifest is missing: '{0}'. Extract the complete zip and run Install again." -f $manifestPath)
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        Stop-WithError 'PACKAGE_UNSUPPORTED' ("This package uses schema version {0}, which this installer does not understand. Use the Install.ps1 shipped inside that package." -f $manifest.schemaVersion)
    }
    if (-not $manifest.files -or @($manifest.files).Count -eq 0) {
        Stop-WithError 'PACKAGE_INCOMPLETE' 'The package manifest lists no files.'
    }
    return $manifest
}

function Test-PackagePayload {
    param([string] $PackageRootPath, $Manifest)

    foreach ($entry in @($Manifest.files)) {
        $source = Join-Path (Join-Path $PackageRootPath 'plugin') $entry.name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            Stop-WithError 'PACKAGE_INCOMPLETE' ("The package file '{0}' listed in the manifest is missing." -f $entry.name)
        }
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $entry.sha256.ToLowerInvariant()) {
            Stop-WithError 'PACKAGE_HASH_MISMATCH' ("'{0}' does not match the checksum in the manifest. The download or extraction is damaged; download the package again." -f $entry.name)
        }
    }
}

function Test-HostNotRunning {
    param([string] $ProcessName)

    $running = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Stop-WithError 'HOST_RUNNING' ("{0} is running. Close {0} before installing, otherwise the files are locked and the plugin list stays stale." -f $ProcessName)
    }
}

function New-BackupOfExisting {
    param([string] $PluginsDir, [string] $BackupRoot, $Manifest)

    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $map = @{}
    foreach ($entry in @($Manifest.files)) {
        $existing = Join-Path $PluginsDir $entry.name
        if (Test-Path -LiteralPath $existing -PathType Leaf) {
            $target = Join-Path (Join-Path $BackupRoot $stamp) $entry.name
            if (-not (Test-Path -LiteralPath (Split-Path -Parent $target) -PathType Container)) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            }
            Copy-Item -LiteralPath $existing -Destination $target -Force
            $map[$entry.name] = $target
            Write-Info ("previous {0} saved to {1}" -f $entry.name, $target)
        }
    }
    return $map
}

function Install-Payload {
    param([string] $PackageRootPath, $Manifest, [string] $PluginsDir, $BackupMap)

    $copied = New-Object System.Collections.ArrayList
    try {
        foreach ($entry in @($Manifest.files)) {
            $source = Join-Path (Join-Path $PackageRootPath 'plugin') $entry.name
            $destination = Join-Path $PluginsDir $entry.name
            Copy-Item -LiteralPath $source -Destination $destination -Force
            [void] $copied.Add($entry.name)
            # Windows marks downloaded files as blocked and a blocked plugin can stay
            # invisible in the tool list, so every copied file is unblocked explicitly.
            Unblock-File -LiteralPath $destination
        }

        foreach ($entry in @($Manifest.files)) {
            $destination = Join-Path $PluginsDir $entry.name
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($hash -ne $entry.sha256.ToLowerInvariant()) {
                    Stop-WithError 'VERIFY_FAILED' ("the installed '{0}' does not match the package checksum" -f $entry.name)
                }
            }
        }
    }
    catch {
        # Undo only what this run created or replaced; anything else is left untouched.
        foreach ($name in $copied) {
            $destination = Join-Path $PluginsDir $name
            try {
                if ($BackupMap.ContainsKey($name)) {
                    Copy-Item -LiteralPath $BackupMap[$name] -Destination $destination -Force
                    Write-Info ("rolled back {0} to the previous version" -f $name)
                }
                elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
                    Remove-Item -LiteralPath $destination -Force
                    Write-Info ("removed partially installed {0}" -f $name)
                }
            }
            catch {
                Write-Info ("could not restore {0}; the previous copy is safe in {1}" -f $name, $BackupRootHint)
            }
        }
        throw
    }
}

function Uninstall-Payload {
    param($Manifest, [string] $PluginsDir)

    foreach ($entry in @($Manifest.files)) {
        $target = Join-Path $PluginsDir $entry.name
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
            Write-Info ("removed {0}" -f $entry.name)
        }
    }
}

function Invoke-AutomationMonitorInstall {
    $manifest = Read-Manifest -PackageRootPath $PackageRoot
    $root = Get-StorageRoot -Requested $StorageRoot
    $pluginsDir = Join-Path $root 'Plugins'
    $backupRoot = Join-Path (Join-Path $root 'PluginBackups') 'AutomationMonitor'
    $script:BackupRootHint = $backupRoot

    Write-Info ("package  : {0} v{1}" -f $manifest.product, $manifest.version)
    Write-Info ("storage  : {0}" -f $root)
    Write-Info ("target   : {0}" -f $pluginsDir)

    Test-HostNotRunning -ProcessName $HostName
    Test-PackagePayload -PackageRootPath $PackageRoot -Manifest $manifest

    if ($DryRun) {
        Write-Info 'dry run: the package and the target path were checked, nothing was written'
        return
    }

    if ($Uninstall) {
        $null = New-BackupOfExisting -PluginsDir $pluginsDir -BackupRoot $backupRoot -Manifest $manifest
        Uninstall-Payload -Manifest $manifest -PluginsDir $pluginsDir
        Write-Info ("Automation Monitor was removed. Backups remain in {0}." -f $backupRoot)
        return
    }

    if (-not (Test-Path -LiteralPath $pluginsDir -PathType Container)) {
        New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
        Write-Info ("created {0}" -f $pluginsDir)
    }

    $backupMap = New-BackupOfExisting -PluginsDir $pluginsDir -BackupRoot $backupRoot -Manifest $manifest
    Install-Payload -PackageRootPath $PackageRoot -Manifest $manifest -PluginsDir $pluginsDir -BackupMap $backupMap

    Write-Info ("installed {0} v{1} ({2} files)" -f $manifest.product, $manifest.version, @($manifest.files).Count)
    Write-Info 'Restart XrmToolBox, then open Automation Monitor from the tool list.'
}

# Dot-sourcing must not perform an installation: tests import the functions only.
if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-AutomationMonitorInstall
        exit 0
    }
    catch {
        Write-Host ('{0} aborted: {1}' -f $prefix, $_.Exception.Message) -ForegroundColor Red
        exit 1
    }
}

