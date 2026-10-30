#Requires -Version 5
<#
    Installer tests for packaging\Install.ps1 and packaging\Install.cmd.

    These run ONLY on the GitHub Actions Windows runner (windows-2022, Windows
    PowerShell 5.1) with a pinned Pester 5, against throwaway folders under the
    agent's temp directory. They never touch a real XrmToolBox installation and
    never need a tenant. Pester is a test-time tool and is not part of the
    distribution package or any runtime dependency.

    The installer is invoked as a child process the same way a user invokes it, so
    these tests cover the real entry point, the exit codes and the printed codes,
    not a re-implementation of the logic.
#>

Set-StrictMode -Version Latest

$script:RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script:InstallerPath = Join-Path $script:RepoRoot 'packaging\Install.ps1'
$script:CmdPath = Join-Path $script:RepoRoot 'packaging\Install.cmd'

function New-TempRoot {
    param([string] $Name)
    # Spaces and parentheses are the point: real Windows paths have both.
    $root = Join-Path $env:TEMP ('AutomationMonitor.Installer (' + $Name + ' ' + (Get-Random) + ')')
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function Get-Sha256 {
    param([string] $Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-TestPackage {
    <#
        Builds a package folder shaped exactly like the shipped zip: plugin\ with the
        two assemblies and a manifest whose checksums are computed from the bytes on
        disk. Content is fake; the shape is real.
    #>
    param(
        [string] $Root,
        [string] $Version = '0.1.0-test',
        [byte[]] $CoreBytes = [System.Text.Encoding]::ASCII.GetBytes('CORE-ASSEMBLY-BYTES'),
        [byte[]] $PluginBytes = [System.Text.Encoding]::ASCII.GetBytes('PLUGIN-ASSEMBLY-BYTES'),
        [byte[]] $ExtraBytes = $null,
        [int] $SchemaVersion = 1
    )

    $payload = Join-Path $Root 'plugin'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $payload 'AutomationMonitor.Core.dll'), $CoreBytes)
    [System.IO.File]::WriteAllBytes((Join-Path $payload 'AutomationMonitor.Plugin.dll'), $PluginBytes)
    if ($null -ne $ExtraBytes) {
        # A stray host assembly in the build output must never reach Plugins.
        [System.IO.File]::WriteAllBytes((Join-Path $payload 'Newtonsoft.Json.dll'), $ExtraBytes)
    }

    $files = @(
        [ordered]@{ name = 'AutomationMonitor.Core.dll'; sha256 = (Get-Sha256 (Join-Path $payload 'AutomationMonitor.Core.dll')); size = (Get-Item (Join-Path $payload 'AutomationMonitor.Core.dll')).Length; target = 'Plugins' }
        [ordered]@{ name = 'AutomationMonitor.Plugin.dll'; sha256 = (Get-Sha256 (Join-Path $payload 'AutomationMonitor.Plugin.dll')); size = (Get-Item (Join-Path $payload 'AutomationMonitor.Plugin.dll')).Length; target = 'Plugins' }
    )
    $manifest = [ordered]@{
        schemaVersion            = $SchemaVersion
        product                  = 'Automation Monitor'
        component                = 'AutomationMonitor.Plugin'
        version                  = $Version
        assemblyVersion          = '0.1.0'
        targetFramework          = 'net48'
        minimumXrmToolBoxVersion = '1.2025.10.74'
        files                    = $files
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'plugin.manifest.json') -Encoding UTF8
    return $Root
}

function Invoke-Installer {
    <#
        Runs the installer the way a customer's double-click does: a real Windows
        PowerShell 5.1 child process with -File. That keeps `exit` inside the
        installer from ending this test run, and it proves the -File argument
        binding path that Install.cmd relies on instead of a dot-sourced shortcut.
    #>
    param([hashtable] $Parameters = @{})

    $arguments = @()
    foreach ($key in $Parameters.Keys) {
        $value = $Parameters[$key]
        if ($value -is [switch]) {
            if ($value.IsPresent) { $arguments += "-$key" }
            continue
        }
        if ($value -is [bool]) {
            if ($value) { $arguments += "-$key" }
            continue
        }
        $arguments += "-$key"
        $arguments += [string] $value
    }

    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script:InstallerPath @arguments 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}


Describe 'Automation Monitor installer' {

    Context 'Fresh installation' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'fresh'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            New-Item -ItemType Directory -Path $script:storage -Force | Out-Null
            $script:result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $script:plugins = Join-Path $script:storage 'Plugins'
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'succeeds and reports the standard Plugins directory it wrote to' {
            $script:result.ExitCode | Should -Be 0
            $script:result.Output | Should -Match ([regex]::Escape($script:plugins))
            $script:result.Output | Should -Match 'installed Automation Monitor'
        }

        It 'creates the Plugins directory under the XrmToolBox storage root' {
            Test-Path -LiteralPath $script:plugins -PathType Container | Should -BeTrue
        }

        It 'places exactly the two manifest assemblies, byte-identical to the package' {
            $installed = @(Get-ChildItem -LiteralPath $script:plugins -File | Select-Object -ExpandProperty Name)
            $installed | Should -Be @('AutomationMonitor.Core.dll', 'AutomationMonitor.Plugin.dll')
            (Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Core.dll')) | Should -Be (Get-Sha256 (Join-Path $script:package 'plugin\AutomationMonitor.Core.dll'))
            (Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Plugin.dll')) | Should -Be (Get-Sha256 (Join-Path $script:package 'plugin\AutomationMonitor.Plugin.dll'))
        }

        It 'tells the user to restart XrmToolBox' {
            $script:result.Output | Should -Match 'Restart XrmToolBox'
        }

        It 'leaves no backup folder when there was nothing to back up' {
            Test-Path -LiteralPath (Join-Path $script:storage 'PluginBackups') | Should -BeFalse
        }
    }

    Context 'A stray host assembly in the package payload' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'strayhost'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package') -ExtraBytes ([System.Text.Encoding]::ASCII.GetBytes('HOST-ASSEMBLY'))
            New-Item -ItemType Directory -Path $script:storage -Force | Out-Null
            $script:result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $script:plugins = Join-Path $script:storage 'Plugins'
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'installs only the manifest entries and never the host assembly' {
            $script:result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $script:plugins 'Newtonsoft.Json.dll') | Should -BeFalse
            @(Get-ChildItem -LiteralPath $script:plugins -File).Count | Should -Be 2
        }
    }

    Context 'Neighbouring plugins' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'neighbours'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:plugins = Join-Path $script:storage 'Plugins'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            New-Item -ItemType Directory -Path $script:plugins -Force | Out-Null

            # Another tool's assembly, plus a folder-based tool. Fixed content and a
            # deliberate timestamp make any touch by our installer visible.
            $script:otherDll = Join-Path $script:plugins 'OtherPlugin.dll'
            [System.IO.File]::WriteAllBytes($script:otherDll, [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $script:otherBefore = Get-Sha256 $script:otherDll
            $script:otherTimeBefore = (Get-Item -LiteralPath $script:otherDll).LastWriteTime
            $script:otherSub = Join-Path $script:plugins 'AnotherTool'
            New-Item -ItemType Directory -Path $script:otherSub -Force | Out-Null
            $script:subDll = Join-Path $script:otherSub 'AutomationMonitor.Core.dll'
            [System.IO.File]::WriteAllBytes($script:subDll, [System.Text.Encoding]::ASCII.GetBytes('SOMEONE-ELSES-COPY'))
            $script:subBefore = Get-Sha256 $script:subDll

            $script:result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'does not change another plugin''s assembly content or timestamp' {
            (Get-Sha256 $script:otherDll) | Should -Be $script:otherBefore
            (Get-Item -LiteralPath $script:otherDll).LastWriteTime | Should -Be $script:otherTimeBefore
        }

        It 'does not touch a same-named file inside another tool''s subfolder' {
            (Get-Sha256 $script:subDll) | Should -Be $script:subBefore
        }

        It 'installs into the Plugins directory at the storage root' {
            Test-Path -LiteralPath (Join-Path $script:plugins 'AutomationMonitor.Plugin.dll') | Should -BeTrue
        }
    }

    Context 'Upgrade over an earlier version' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'upgrade'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:plugins = Join-Path $script:storage 'Plugins'
            $script:firstPackage = New-TestPackage -Root (Join-Path $script:root 'package1') -Version '0.1.0-test' `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-PLUGIN-BYTES'))
            $null = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:firstPackage }

            $script:secondPackage = New-TestPackage -Root (Join-Path $script:root 'package2') -Version '0.2.0-test' `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-PLUGIN-BYTES'))
            $script:result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:secondPackage }
            $script:oldCore = Get-Sha256 (Join-Path $script:firstPackage 'plugin\AutomationMonitor.Core.dll')
            $script:newCore = Get-Sha256 (Join-Path $script:secondPackage 'plugin\AutomationMonitor.Core.dll')
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'installs the new bytes' {
            $script:result.ExitCode | Should -Be 0
            (Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Core.dll')) | Should -Be $script:newCore
        }

        It 'backs up the previous assemblies outside the Plugins directory' {
            $backupRoot = Join-Path $script:storage 'PluginBackups\AutomationMonitor'
            Test-Path -LiteralPath $backupRoot -PathType Container | Should -BeTrue
            $backedUp = @(Get-ChildItem -LiteralPath $backupRoot -Recurse -File -Filter 'AutomationMonitor.Core.dll')
            $backedUp.Count | Should -Be 1
            (Get-Sha256 $backedUp[0].FullName) | Should -Be $script:oldCore
            # A backup folder inside Plugins could be scanned by the host and load twice.
            @(Get-ChildItem -LiteralPath $script:plugins -Recurse -Directory | Where-Object { $_.Name -like '*backup*' }).Count | Should -Be 0
        }

        It 'says where the previous version was saved' {
            $script:result.Output | Should -Match 'previous AutomationMonitor.Core.dll saved to'
        }
    }

    Context 'Repeating the same installation' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'repeat'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:plugins = Join-Path $script:storage 'Plugins'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            $script:first = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $script:second = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'succeeds both times' {
            $script:first.ExitCode | Should -Be 0
            $script:second.ExitCode | Should -Be 0
        }

        It 'leaves exactly two assemblies, with no duplicates or temporaries' {
            $names = @(Get-ChildItem -LiteralPath $script:plugins -Recurse -File | Select-Object -ExpandProperty Name)
            $names | Should -Be @('AutomationMonitor.Core.dll', 'AutomationMonitor.Plugin.dll')
        }

        It 'produces byte-identical files' {
            (Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Plugin.dll')) | Should -Be (Get-Sha256 (Join-Path $script:package 'plugin\AutomationMonitor.Plugin.dll'))
        }
    }

    Context 'Rollback when a copy fails halfway' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'rollback'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:plugins = Join-Path $script:storage 'Plugins'
            $script:oldPackage = New-TestPackage -Root (Join-Path $script:root 'package1') `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-PLUGIN-BYTES'))
            $null = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:oldPackage }

            [System.IO.File]::WriteAllBytes((Join-Path $script:plugins 'OtherPlugin.dll'), [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $script:otherBefore = Get-Sha256 (Join-Path $script:plugins 'OtherPlugin.dll')
            $script:oldCore = Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Core.dll')
            $script:oldPlugin = Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Plugin.dll')

            $script:newPackage = New-TestPackage -Root (Join-Path $script:root 'package2') `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-PLUGIN-BYTES'))

            # A read handle that denies writers makes the second copy (Plugin.dll sorts
            # last) fail for real, after Core.dll has already been replaced.
            $script:lock = [System.IO.File]::Open((Join-Path $script:plugins 'AutomationMonitor.Plugin.dll'), 'Open', 'Read', 'Read')
            $script:result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:newPackage }
        }

        AfterAll {
            if ($script:lock) { $script:lock.Dispose() }
            Remove-TempRoot -Root $script:root
        }

        It 'reports failure with a non-zero exit code' {
            $script:result.ExitCode | Should -Not -Be 0
            $script:result.Output | Should -Match 'aborted'
        }

        It 'restores the assembly it had already replaced' {
            (Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Core.dll')) | Should -Be $script:oldCore
            $script:result.Output | Should -Match 'rolled back AutomationMonitor.Core.dll'
        }

        It 'leaves the assembly it could not write untouched' {
            (Get-Sha256 (Join-Path $script:plugins 'AutomationMonitor.Plugin.dll')) | Should -Be $script:oldPlugin
        }

        It 'does not leave the new package bytes installed anywhere' {
            $newCore = Get-Sha256 (Join-Path $script:newPackage 'plugin\AutomationMonitor.Core.dll')
            @(Get-ChildItem -LiteralPath $script:plugins -Recurse -File | Where-Object { (Get-Sha256 $_.FullName) -eq $newCore }).Count | Should -Be 0
        }

        It 'never touches the neighbouring plugin' {
            (Get-Sha256 (Join-Path $script:plugins 'OtherPlugin.dll')) | Should -Be $script:otherBefore
        }
    }

    Context 'Refusing a broken package without leaving state behind' {
        BeforeEach {
            $script:root = New-TempRoot -Name 'refuse'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:plugins = Join-Path $script:storage 'Plugins'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            New-Item -ItemType Directory -Path $script:storage -Force | Out-Null
        }

        AfterEach { Remove-TempRoot -Root $script:root }

        It 'stops on a missing package assembly' {
            Remove-Item -LiteralPath (Join-Path $script:package 'plugin\AutomationMonitor.Core.dll') -Force
            $result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_INCOMPLETE'
            Test-Path -LiteralPath $script:plugins | Should -BeFalse
        }

        It 'stops on a missing manifest' {
            Remove-Item -LiteralPath (Join-Path $script:package 'plugin\plugin.manifest.json') -Force
            $result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_INCOMPLETE'
            Test-Path -LiteralPath $script:plugins | Should -BeFalse
        }

        It 'stops when a payload file does not match its checksum' {
            [System.IO.File]::WriteAllBytes((Join-Path $script:package 'plugin\AutomationMonitor.Core.dll'), [System.Text.Encoding]::ASCII.GetBytes('SWAPPED-BYTES'))
            $result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_HASH_MISMATCH'
            Test-Path -LiteralPath $script:plugins | Should -BeFalse
        }

        It 'refuses a package from a newer schema instead of guessing' {
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package') -SchemaVersion 2
            $result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_UNSUPPORTED'
            Test-Path -LiteralPath $script:plugins | Should -BeFalse
        }

        It 'refuses to run while the host is still open' {
            # The child process is itself powershell, so naming it exercises the
            # running-host branch for real instead of mocking across a process boundary.
            $result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package; HostName = 'powershell' }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'HOST_RUNNING'
            Test-Path -LiteralPath $script:plugins | Should -BeFalse
        }

        It 'explains a storage root that does not exist' {
            $missing = Join-Path $script:root 'Not Installed (yet)'
            $result = Invoke-Installer -Parameters @{ StorageRoot = $missing; PackageRoot = $script:package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'STORAGE_ROOT_MISSING'
        }
    }

    Context 'Storage location' {
        BeforeEach {
            $script:root = New-TempRoot -Name 'storage'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            $script:createdDefault = $false
        }

        AfterEach {
            $env:AUTOMATION_MONITOR_XTB_ROOT = $null
            if ($script:createdDefault -and $script:defaultRoot) {
                Remove-Item -LiteralPath $script:defaultParent -Recurse -Force -ErrorAction SilentlyContinue
            }
            Remove-TempRoot -Root $script:root
        }

        It 'uses the standard roaming storage when nothing overrides it' {
            $script:defaultParent = Join-Path $env:APPDATA 'MscrmTools'
            $script:defaultRoot = Join-Path $script:defaultParent 'XrmToolBox'
            if (Test-Path -LiteralPath $script:defaultRoot) {
                # Never write into a real XrmToolBox installation on this machine.
                Set-ItResult -Skipped -Because 'the default storage folder already exists here'
                return
            }
            New-Item -ItemType Directory -Path $script:defaultRoot -Force | Out-Null
            $script:createdDefault = $true

            $result = Invoke-Installer -Parameters @{ PackageRoot = $script:package }
            $result.ExitCode | Should -Be 0
            $result.Output | Should -Match ([regex]::Escape((Join-Path $script:defaultRoot 'Plugins')))
            Test-Path -LiteralPath (Join-Path $script:defaultRoot 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
        }

        It 'honours the environment override for a portable installation' {
            $portable = Join-Path $script:root 'XrmToolBox (portable)'
            New-Item -ItemType Directory -Path $portable -Force | Out-Null
            $env:AUTOMATION_MONITOR_XTB_ROOT = $portable

            $result = Invoke-Installer -Parameters @{ PackageRoot = $script:package }
            $result.ExitCode | Should -Be 0
            $result.Output | Should -Match ([regex]::Escape((Join-Path $portable 'Plugins')))
            Test-Path -LiteralPath (Join-Path $portable 'Plugins\AutomationMonitor.Core.dll') | Should -BeTrue
        }

        It 'lets -StorageRoot win over the environment for an /overridepath install' {
            $explicit = Join-Path $script:root 'chosen (by hand)'
            New-Item -ItemType Directory -Path $explicit -Force | Out-Null
            $env:AUTOMATION_MONITOR_XTB_ROOT = (Join-Path $script:root 'ignored')

            $result = Invoke-Installer -Parameters @{ StorageRoot = $explicit; PackageRoot = $script:package }
            $result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $explicit 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $script:root 'ignored') | Should -BeFalse
        }

        It 'previews the target with -DryRun and writes nothing' {
            $target = Join-Path $script:root 'preview (nothing here)'
            New-Item -ItemType Directory -Path $target -Force | Out-Null

            $result = Invoke-Installer -Parameters @{ StorageRoot = $target; PackageRoot = $script:package; DryRun = $true }
            $result.ExitCode | Should -Be 0
            $result.Output | Should -Match ([regex]::Escape((Join-Path $target 'Plugins')))
            $result.Output | Should -Match 'dry run'
            Test-Path -LiteralPath (Join-Path $target 'Plugins') | Should -BeFalse
        }
    }

    Context 'Uninstall' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'uninstall'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:plugins = Join-Path $script:storage 'Plugins'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            $null = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package }
            [System.IO.File]::WriteAllBytes((Join-Path $script:plugins 'OtherPlugin.dll'), [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $script:result = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package; Uninstall = $true }
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'removes only this plugin''s assemblies' {
            $script:result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $script:plugins 'AutomationMonitor.Core.dll') | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $script:plugins 'AutomationMonitor.Plugin.dll') | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $script:plugins 'OtherPlugin.dll') | Should -BeTrue
        }

        It 'does not delete the backups it made' {
            Test-Path -LiteralPath (Join-Path $script:storage 'PluginBackups\AutomationMonitor') -PathType Container | Should -BeTrue
        }

        It 'is safe to repeat' {
            $again = Invoke-Installer -Parameters @{ StorageRoot = $script:storage; PackageRoot = $script:package; Uninstall = $true }
            $again.ExitCode | Should -Be 0
        }
    }

    Context 'Install.cmd entry point (unattended)' {
        BeforeAll {
            $script:root = New-TempRoot -Name 'cmd'
            $script:storage = Join-Path $script:root 'XrmToolBox'
            $script:package = New-TestPackage -Root (Join-Path $script:root 'package')
            New-Item -ItemType Directory -Path $script:storage -Force | Out-Null
        }

        AfterAll { Remove-TempRoot -Root $script:root }

        It 'finishes without waiting for a key when AM_NO_PAUSE is set' {
            $log = Join-Path $script:root 'install.log'
            $line = '"' + $script:CmdPath + '" -StorageRoot "' + $script:storage + '" -PackageRoot "' + $script:package + '"'
            $env:AM_NO_PAUSE = '1'
            try {
                $proc = Start-Process -FilePath $env:ComSpec -ArgumentList ('/c ' + $line) -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
                if (-not $proc.WaitForExit(90000)) {
                    try { $proc.Kill() } catch { }
                    throw 'Install.cmd never returned: it is still waiting for interactive input.'
                }
                $proc.ExitCode | Should -Be 0
                (Get-Content -LiteralPath $log -Raw) | Should -Match 'installed Automation Monitor'
                Test-Path -LiteralPath (Join-Path $script:storage 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
            }
            finally {
                $env:AM_NO_PAUSE = $null
            }
        }

        It 'propagates the failure exit code without blocking' {
            $log = Join-Path $script:root 'failed.log'
            $missing = Join-Path $script:root 'not installed (yet)'
            $line = '"' + $script:CmdPath + '" -StorageRoot "' + $missing + '" -PackageRoot "' + $script:package + '"'
            $env:AM_NO_PAUSE = '1'
            try {
                $proc = Start-Process -FilePath $env:ComSpec -ArgumentList ('/c ' + $line) -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
                if (-not $proc.WaitForExit(90000)) {
                    try { $proc.Kill() } catch { }
                    throw 'Install.cmd blocked on a failure path instead of returning the code.'
                }
                $proc.ExitCode | Should -Not -Be 0
                (Get-Content -LiteralPath $log -Raw) | Should -Match 'STORAGE_ROOT_MISSING'
            }
            finally {
                $env:AM_NO_PAUSE = $null
            }
        }
    }
}






