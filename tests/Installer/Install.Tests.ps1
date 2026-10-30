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

BeforeAll {
    # Pester 5 executes this file body during Discovery only, while BeforeAll, It and
    # AfterAll run in the Run phase. Shared paths and helper functions therefore have to be
    # established inside a top-level BeforeAll, whose scope every child block inherits.

    $RepoRoot = Split-Path (Split-Path -LiteralPath $PSScriptRoot -Parent) -Parent
    $InstallerPath = Join-Path $RepoRoot 'packaging\Install.ps1'
    $CmdPath = Join-Path $RepoRoot 'packaging\Install.cmd'

    # Fail loudly here rather than producing 35 confusing failures downstream.
    if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "installer not found at $InstallerPath" }
    if (-not (Test-Path -LiteralPath $CmdPath)) { throw "Install.cmd not found at $CmdPath" }

    # Record how the runner spells TEMP next to that folder's real name: on a GitHub Windows
    # runner these two differ (8.3 vs long form) and every path comparison in this suite has to
    # cope with that, so the log should show the condition rather than hide it.
    if ($env:TEMP -and (Test-Path -LiteralPath $env:TEMP)) {
        Write-Host ('temp as offered   : ' + $env:TEMP)
        Write-Host ('temp as resolved  : ' + (Get-Item -LiteralPath $env:TEMP).FullName)
    }

    function New-TempRoot {
        param([string] $Name)
        # Spaces and parentheses are the point: real Windows paths have both.
        $root = Join-Path $env:TEMP ('AutomationMonitor.Installer (' + $Name + ' ' + (Get-Random) + ')')
        New-Item -ItemType Directory -LiteralPath $root -Force | Out-Null
        return $root
    }

    function Get-CanonicalPath {
        <#
            The same directory has two spellings on a Windows runner: the 8.3 form
            $env:TEMP arrives in (C:\Users\RUNNER~1) and the long form
            (C:\Users\runneradmin). Install.ps1 runs under Windows PowerShell 5.1, where
            [System.IO.Path]::GetFullPath goes through the Win32 full-path API and returns
            long names for components that exist, while a path a test builds with Join-Path
            stays exactly as $env:TEMP spelled it. Comparing those two as text is meaningless,
            so resolve the deepest existing ancestor to its true name and re-append the part
            that does not exist yet - which is also how a dry-run target must be compared.
        #>
        param([string] $Path)

        $full = [System.IO.Path]::GetFullPath($Path)
        $existing = $full
        while ($existing -and -not (Test-Path -LiteralPath $existing)) {
            $parent = [System.IO.Path]::GetDirectoryName($existing)
            if (-not $parent -or $parent -eq $existing) { break }
            $existing = $parent
        }
        if (-not $existing -or -not (Test-Path -LiteralPath $existing)) { return $full }

        $resolved = (Get-Item -LiteralPath $existing).FullName
        $tail = $full.Substring($existing.Length).Replace('\', '/').Trim('/')
        foreach ($segment in $tail.Split('/')) {
            if ($segment) { $resolved = [System.IO.Path]::Combine($resolved, $segment) }
        }
        return $resolved
    }

    function Get-InstallerReportedPath {
        <#
            Reads one labelled path out of the installer's own transcript ('storage' or
            'target') instead of searching the whole output for a substring. Returns $null
            when the installer never printed that line, so a missing statement fails the
            test instead of quietly matching some other text.
        #>
        param([string] $Text, [string] $Field)

        $pattern = '(?m)^\[AutomationMonitor\.Install\]\s*' + [regex]::Escape($Field) + '\s*:\s*(.+?)\s*$'
        $match = [regex]::Match($Text, $pattern)
        if (-not $match.Success) { return $null }
        return $match.Groups[1].Value
    }

    function Read-CapturedStream {
        param([string] $OutFile, [string] $ErrFile)
        $text = ''
        foreach ($file in @($OutFile, $ErrFile)) {
            if (Test-Path -LiteralPath $file) {
                $chunk = Get-Content -LiteralPath $file -Raw
                if ($chunk) { $text = $text + $chunk + [System.Environment]::NewLine }
            }
        }
        return $text
    }

    function Invoke-CmdEntry {
        <#
            Runs Install.cmd the way an unattended caller would: through cmd.exe with the batch
            path and every argument quoted. cmd removes the outermost quote pair of a /c payload
            that begins with a quote and holds more than two quotes, so the whole payload needs a
            pair of its own - without it the batch path is cut at its first space and cmd answers
            "The filename, directory name, or volume label syntax is incorrect." without ever
            starting the installer. /d keeps any machine AutoRun out of the way.
            The exit code is only read once the process is confirmed to have exited, because the
            timed WaitForExit overload alone does not refresh the exit information ExitCode reads.
        #>
        param(
            [string] $Arguments,
            [string] $Log,
            [string] $ErrorLog,
            [int] $TimeoutMs = 90000
        )

        $commandLine = '/d /c ""' + $Arguments + '"'
        $proc = $null
        try {
            # No -WindowStyle here: redirecting the streams forces UseShellExecute=false, and a
            # window-style request alongside that is a parameter conflict, not a preference.
            $proc = Start-Process -FilePath $env:ComSpec -ArgumentList $commandLine -PassThru `
                -RedirectStandardOutput $Log -RedirectStandardError $ErrorLog
        }
        catch {
            throw "Start-Process could not launch cmd.exe with: $commandLine`n$($_.Exception.Message)"
        }
        try {
            if (-not $proc.WaitForExit($TimeoutMs)) {
                try { if (-not $proc.HasExited) { $proc.Kill() } } catch { }
                try { $null = $proc.WaitForExit(5000) } catch { }
                return [pscustomobject]@{
                    TimedOut   = $true
                    ExitCode   = $null
                    Output     = (Read-CapturedStream -OutFile $Log -ErrFile $ErrorLog)
                    CommandLine = $commandLine
                }
            }
            $proc.WaitForExit()
            return [pscustomobject]@{
                TimedOut   = $false
                ExitCode   = $proc.ExitCode
                Output     = (Read-CapturedStream -OutFile $Log -ErrFile $ErrorLog)
                CommandLine = $commandLine
            }
        }
        finally {
            if ($proc) { $proc.Dispose() }
        }
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
        New-Item -ItemType Directory -LiteralPath $payload -Force | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $payload 'AutomationMonitor.Core.dll'), $CoreBytes)
        [System.IO.File]::WriteAllBytes((Join-Path $payload 'AutomationMonitor.Plugin.dll'), $PluginBytes)
        if ($null -ne $ExtraBytes) {
            # A stray host assembly in the build output must never reach Plugins.
            [System.IO.File]::WriteAllBytes((Join-Path $payload 'Newtonsoft.Json.dll'), $ExtraBytes)
        }

        $files = @(
            [ordered]@{ name = 'AutomationMonitor.Core.dll'; sha256 = (Get-Sha256 (Join-Path $payload 'AutomationMonitor.Core.dll')); size = (Get-Item -LiteralPath (Join-Path $payload 'AutomationMonitor.Core.dll')).Length; target = 'Plugins' }
            [ordered]@{ name = 'AutomationMonitor.Plugin.dll'; sha256 = (Get-Sha256 (Join-Path $payload 'AutomationMonitor.Plugin.dll')); size = (Get-Item -LiteralPath (Join-Path $payload 'AutomationMonitor.Plugin.dll')).Length; target = 'Plugins' }
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

        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $InstallerPath @arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    function Remove-TempRoot {
        <#
            Teardown for the throwaway folders this suite creates under $env:TEMP.

            Two rules make this safe to call from any AfterAll or AfterEach:
            - it deletes nothing outside the system temp directory, so an empty or
              wrong -Root can never reach an XrmToolBox installation or a source tree;
            - it never throws. A teardown exception would mask the real test result,
              while a leftover TEMP folder is harmless to the runner's own cleanup.
        #>
        param([string] $Root)

        if ([string]::IsNullOrWhiteSpace($Root)) { return }

        $temp = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        $full = [System.IO.Path]::GetFullPath($Root)
        if (-not $full.StartsWith($temp + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning ("refusing to remove '{0}': outside the temp directory" -f $full)
            return
        }

        if (-not (Test-Path -LiteralPath $full)) { return }
        try {
            Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
        }
        catch {
            # A sibling teardown may still be releasing a handle on the last file.
            Start-Sleep -Milliseconds 250
            try {
                Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
            }
            catch {
                Write-Warning ("could not remove '{0}': {1}" -f $full, $_.Exception.Message)
            }
        }
    }
}

Describe 'Automation Monitor installer' {

    Context 'Fresh installation' {
        BeforeAll {
            $root = New-TempRoot -Name 'fresh'
            $storage = Join-Path $root 'XrmToolBox'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $plugins = Join-Path $storage 'Plugins'
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'succeeds and reports the standard Plugins directory it wrote to' {
            $result.ExitCode | Should -Be 0
            $because = 'installer output: ' + $result.Output
            (Get-InstallerReportedPath -Text $result.Output -Field 'storage') |
                Should -Be (Get-CanonicalPath -Path $storage) -Because $because
            (Get-InstallerReportedPath -Text $result.Output -Field 'target') |
                Should -Be (Get-CanonicalPath -Path $plugins) -Because $because
            $result.Output | Should -Match 'installed Automation Monitor'
        }

        It 'creates the Plugins directory under the XrmToolBox storage root' {
            Test-Path -LiteralPath $plugins -PathType Container | Should -BeTrue
        }

        It 'places exactly the two manifest assemblies, byte-identical to the package' {
            $installed = @(Get-ChildItem -LiteralPath $plugins -File | Select-Object -ExpandProperty Name)
            $installed | Should -Be @('AutomationMonitor.Core.dll', 'AutomationMonitor.Plugin.dll')
            (Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Core.dll')) | Should -Be (Get-Sha256 (Join-Path $package 'plugin\AutomationMonitor.Core.dll'))
            (Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Plugin.dll')) | Should -Be (Get-Sha256 (Join-Path $package 'plugin\AutomationMonitor.Plugin.dll'))
        }

        It 'tells the user to restart XrmToolBox' {
            $result.Output | Should -Match 'Restart XrmToolBox'
        }

        It 'leaves no backup folder when there was nothing to back up' {
            Test-Path -LiteralPath (Join-Path $storage 'PluginBackups') | Should -BeFalse
        }
    }

    Context 'A stray host assembly in the package payload' {
        BeforeAll {
            $root = New-TempRoot -Name 'strayhost'
            $storage = Join-Path $root 'XrmToolBox'
            $package = New-TestPackage -Root (Join-Path $root 'package') -ExtraBytes ([System.Text.Encoding]::ASCII.GetBytes('HOST-ASSEMBLY'))
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $plugins = Join-Path $storage 'Plugins'
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'installs only the manifest entries and never the host assembly' {
            $result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $plugins 'Newtonsoft.Json.dll') | Should -BeFalse
            @(Get-ChildItem -LiteralPath $plugins -File).Count | Should -Be 2
        }
    }

    Context 'Neighbouring plugins' {
        BeforeAll {
            $root = New-TempRoot -Name 'neighbours'
            $storage = Join-Path $root 'XrmToolBox'
            $plugins = Join-Path $storage 'Plugins'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            New-Item -ItemType Directory -LiteralPath $plugins -Force | Out-Null

            # Another tool's assembly, plus a folder-based tool. Fixed content and a
            # deliberate timestamp make any touch by our installer visible.
            $otherDll = Join-Path $plugins 'OtherPlugin.dll'
            [System.IO.File]::WriteAllBytes($otherDll, [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $otherBefore = Get-Sha256 $otherDll
            $otherTimeBefore = (Get-Item -LiteralPath $otherDll).LastWriteTime
            $otherSub = Join-Path $plugins 'AnotherTool'
            New-Item -ItemType Directory -LiteralPath $otherSub -Force | Out-Null
            $subDll = Join-Path $otherSub 'AutomationMonitor.Core.dll'
            [System.IO.File]::WriteAllBytes($subDll, [System.Text.Encoding]::ASCII.GetBytes('SOMEONE-ELSES-COPY'))
            $subBefore = Get-Sha256 $subDll

            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'does not change another plugin''s assembly content or timestamp' {
            (Get-Sha256 $otherDll) | Should -Be $otherBefore
            (Get-Item -LiteralPath $otherDll).LastWriteTime | Should -Be $otherTimeBefore
        }

        It 'does not touch a same-named file inside another tool''s subfolder' {
            (Get-Sha256 $subDll) | Should -Be $subBefore
        }

        It 'installs into the Plugins directory at the storage root' {
            Test-Path -LiteralPath (Join-Path $plugins 'AutomationMonitor.Plugin.dll') | Should -BeTrue
        }
    }

    Context 'Upgrade over an earlier version' {
        BeforeAll {
            $root = New-TempRoot -Name 'upgrade'
            $storage = Join-Path $root 'XrmToolBox'
            $plugins = Join-Path $storage 'Plugins'
            # A context that simulates an earlier installation has to lay the storage root down
            # itself: the installer refuses to invent one, and that refusal is deliberate.
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            $firstPackage = New-TestPackage -Root (Join-Path $root 'package1') -Version '0.1.0-test' `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-PLUGIN-BYTES'))
            $null = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $firstPackage }

            $secondPackage = New-TestPackage -Root (Join-Path $root 'package2') -Version '0.2.0-test' `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-PLUGIN-BYTES'))
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $secondPackage }
            $oldCore = Get-Sha256 (Join-Path $firstPackage 'plugin\AutomationMonitor.Core.dll')
            $newCore = Get-Sha256 (Join-Path $secondPackage 'plugin\AutomationMonitor.Core.dll')
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'installs the new bytes' {
            $result.ExitCode | Should -Be 0
            (Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Core.dll')) | Should -Be $newCore
        }

        It 'backs up the previous assemblies outside the Plugins directory' {
            $backupRoot = Join-Path $storage 'PluginBackups\AutomationMonitor'
            Test-Path -LiteralPath $backupRoot -PathType Container | Should -BeTrue
            $backedUp = @(Get-ChildItem -LiteralPath $backupRoot -Recurse -File -Filter 'AutomationMonitor.Core.dll')
            $backedUp.Count | Should -Be 1
            (Get-Sha256 $backedUp[0].FullName) | Should -Be $oldCore
            # A backup folder inside Plugins could be scanned by the host and load twice.
            @(Get-ChildItem -LiteralPath $plugins -Recurse -Directory | Where-Object { $_.Name -like '*backup*' }).Count | Should -Be 0
        }

        It 'says where the previous version was saved' {
            $result.Output | Should -Match 'previous AutomationMonitor.Core.dll saved to'
        }
    }

    Context 'Repeating the same installation' {
        BeforeAll {
            $root = New-TempRoot -Name 'repeat'
            $storage = Join-Path $root 'XrmToolBox'
            $plugins = Join-Path $storage 'Plugins'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            $first = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $second = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'succeeds both times' {
            $first.ExitCode | Should -Be 0
            $second.ExitCode | Should -Be 0
        }

        It 'leaves exactly two assemblies, with no duplicates or temporaries' {
            $names = @(Get-ChildItem -LiteralPath $plugins -Recurse -File | Select-Object -ExpandProperty Name)
            $names | Should -Be @('AutomationMonitor.Core.dll', 'AutomationMonitor.Plugin.dll')
        }

        It 'produces byte-identical files' {
            (Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Plugin.dll')) | Should -Be (Get-Sha256 (Join-Path $package 'plugin\AutomationMonitor.Plugin.dll'))
        }
    }

    Context 'Rollback when a copy fails halfway' {
        BeforeAll {
            $root = New-TempRoot -Name 'rollback'
            $storage = Join-Path $root 'XrmToolBox'
            $plugins = Join-Path $storage 'Plugins'
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            $oldPackage = New-TestPackage -Root (Join-Path $root 'package1') `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-PLUGIN-BYTES'))
            $null = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $oldPackage }

            # The neighbouring plugin is planted by the fixture, not by the installer, so the
            # Plugins directory must exist here whether or not the first install succeeded.
            New-Item -ItemType Directory -LiteralPath $plugins -Force | Out-Null
            [System.IO.File]::WriteAllBytes((Join-Path $plugins 'OtherPlugin.dll'), [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $otherBefore = Get-Sha256 (Join-Path $plugins 'OtherPlugin.dll')
            $oldCore = Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Core.dll')
            $oldPlugin = Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Plugin.dll')

            $newPackage = New-TestPackage -Root (Join-Path $root 'package2') `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('NEW-PLUGIN-BYTES'))

            # A read handle that denies writers makes the second copy (Plugin.dll sorts
            # last) fail for real, after Core.dll has already been replaced.
            $lock = [System.IO.File]::Open((Join-Path $plugins 'AutomationMonitor.Plugin.dll'), 'Open', 'Read', 'Read')
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $newPackage }
        }

        AfterAll {
            if ($lock) { $lock.Dispose() }
            Remove-TempRoot -Root $root
        }

        It 'reports failure with a non-zero exit code' {
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'aborted'
        }

        It 'restores the assembly it had already replaced' {
            (Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Core.dll')) | Should -Be $oldCore
            $result.Output | Should -Match 'rolled back AutomationMonitor.Core.dll'
        }

        It 'leaves the assembly it could not write untouched' {
            (Get-Sha256 (Join-Path $plugins 'AutomationMonitor.Plugin.dll')) | Should -Be $oldPlugin
        }

        It 'does not leave the new package bytes installed anywhere' {
            $newCore = Get-Sha256 (Join-Path $newPackage 'plugin\AutomationMonitor.Core.dll')
            @(Get-ChildItem -LiteralPath $plugins -Recurse -File | Where-Object { (Get-Sha256 $_.FullName) -eq $newCore }).Count | Should -Be 0
        }

        It 'never touches the neighbouring plugin' {
            (Get-Sha256 (Join-Path $plugins 'OtherPlugin.dll')) | Should -Be $otherBefore
        }
    }

    Context 'Refusing a broken package without leaving state behind' {
        BeforeEach {
            $root = New-TempRoot -Name 'refuse'
            $storage = Join-Path $root 'XrmToolBox'
            $plugins = Join-Path $storage 'Plugins'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
        }

        AfterEach { Remove-TempRoot -Root $root }

        It 'stops on a missing package assembly' {
            Remove-Item -LiteralPath (Join-Path $package 'plugin\AutomationMonitor.Core.dll') -Force
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_INCOMPLETE'
            Test-Path -LiteralPath $plugins | Should -BeFalse
        }

        It 'stops on a missing manifest' {
            Remove-Item -LiteralPath (Join-Path $package 'plugin\plugin.manifest.json') -Force
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_INCOMPLETE'
            Test-Path -LiteralPath $plugins | Should -BeFalse
        }

        It 'stops when a payload file does not match its checksum' {
            [System.IO.File]::WriteAllBytes((Join-Path $package 'plugin\AutomationMonitor.Core.dll'), [System.Text.Encoding]::ASCII.GetBytes('SWAPPED-BYTES'))
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_HASH_MISMATCH'
            Test-Path -LiteralPath $plugins | Should -BeFalse
        }

        It 'refuses a package from a newer schema instead of guessing' {
            $package = New-TestPackage -Root (Join-Path $root 'package') -SchemaVersion 2
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'PACKAGE_UNSUPPORTED'
            Test-Path -LiteralPath $plugins | Should -BeFalse
        }

        It 'refuses to run while the host is still open' {
            # The child process is itself powershell, so naming it exercises the
            # running-host branch for real instead of mocking across a process boundary.
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package; HostName = 'powershell' }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'HOST_RUNNING'
            Test-Path -LiteralPath $plugins | Should -BeFalse
        }

        It 'explains a storage root that does not exist' {
            $missing = Join-Path $root 'Not Installed (yet)'
            $result = Invoke-Installer -Parameters @{ StorageRoot = $missing; PackageRoot = $package }
            $result.ExitCode | Should -Not -Be 0
            $result.Output | Should -Match 'STORAGE_ROOT_MISSING'
        }
    }

    Context 'Storage location' {
        BeforeEach {
            $root = New-TempRoot -Name 'storage'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            # The one test that installs into the default roaming folder sets $createdDefault
            # together with $defaultParent/$defaultRoot. Pester runs a single test's BeforeEach,
            # body and AfterEach in one scope, so the teardown below sees exactly what that test
            # created - and never a sibling test's leftovers. The flag is what stops AfterEach
            # from ever deleting a real XrmToolBox storage folder this run did not create.
            $createdDefault = $false
        }

        AfterEach {
            $env:AUTOMATION_MONITOR_XTB_ROOT = $null
            if ($createdDefault -and $defaultRoot) {
                Remove-Item -LiteralPath $defaultParent -Recurse -Force -ErrorAction SilentlyContinue
            }
            Remove-TempRoot -Root $root
        }

        It 'uses the standard roaming storage when nothing overrides it' {
            $defaultParent = Join-Path $env:APPDATA 'MscrmTools'
            $defaultRoot = Join-Path $defaultParent 'XrmToolBox'
            if (Test-Path -LiteralPath $defaultRoot) {
                # Never write into a real XrmToolBox installation on this machine.
                Set-ItResult -Skipped -Because 'the default storage folder already exists here'
                return
            }
            New-Item -ItemType Directory -LiteralPath $defaultRoot -Force | Out-Null
            $createdDefault = $true

            $result = Invoke-Installer -Parameters @{ PackageRoot = $package }
            $result.ExitCode | Should -Be 0
            (Get-InstallerReportedPath -Text $result.Output -Field 'target') |
                Should -Be (Get-CanonicalPath -Path (Join-Path $defaultRoot 'Plugins'))
            Test-Path -LiteralPath (Join-Path $defaultRoot 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
        }

        It 'honours the environment override for a portable installation' {
            $portable = Join-Path $root 'XrmToolBox (portable)'
            New-Item -ItemType Directory -LiteralPath $portable -Force | Out-Null
            $env:AUTOMATION_MONITOR_XTB_ROOT = $portable

            $result = Invoke-Installer -Parameters @{ PackageRoot = $package }
            $result.ExitCode | Should -Be 0
            (Get-InstallerReportedPath -Text $result.Output -Field 'target') |
                Should -Be (Get-CanonicalPath -Path (Join-Path $portable 'Plugins'))
            Test-Path -LiteralPath (Join-Path $portable 'Plugins\AutomationMonitor.Core.dll') | Should -BeTrue
        }

        It 'lets -StorageRoot win over the environment for an /overridepath install' {
            $explicit = Join-Path $root 'chosen (by hand)'
            New-Item -ItemType Directory -LiteralPath $explicit -Force | Out-Null
            $env:AUTOMATION_MONITOR_XTB_ROOT = (Join-Path $root 'ignored')

            $result = Invoke-Installer -Parameters @{ StorageRoot = $explicit; PackageRoot = $package }
            $result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $explicit 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $root 'ignored') | Should -BeFalse
        }

        It 'previews the target with -DryRun and writes nothing' {
            $target = Join-Path $root 'preview (nothing here)'
            New-Item -ItemType Directory -LiteralPath $target -Force | Out-Null

            $result = Invoke-Installer -Parameters @{ StorageRoot = $target; PackageRoot = $package; DryRun = $true }
            $result.ExitCode | Should -Be 0
            # Plugins does not exist in a dry run, so the comparison is against the canonical
            # form of an existing parent plus the directory the installer says it would create.
            (Get-InstallerReportedPath -Text $result.Output -Field 'target') |
                Should -Be (Get-CanonicalPath -Path (Join-Path $target 'Plugins'))
            $result.Output | Should -Match 'dry run'
            Test-Path -LiteralPath (Join-Path $target 'Plugins') | Should -BeFalse
        }
    }

    Context 'Uninstall' {
        BeforeAll {
            $root = New-TempRoot -Name 'uninstall'
            $storage = Join-Path $root 'XrmToolBox'
            $plugins = Join-Path $storage 'Plugins'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
            $null = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            # The neighbour file is planted by the fixture, so its directory has to exist
            # independently of whatever the install above managed to do.
            New-Item -ItemType Directory -LiteralPath $plugins -Force | Out-Null
            [System.IO.File]::WriteAllBytes((Join-Path $plugins 'OtherPlugin.dll'), [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $result = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package; Uninstall = $true }
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'removes only this plugin''s assemblies' {
            $result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $plugins 'AutomationMonitor.Core.dll') | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $plugins 'AutomationMonitor.Plugin.dll') | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $plugins 'OtherPlugin.dll') | Should -BeTrue
        }

        It 'does not delete the backups it made' {
            Test-Path -LiteralPath (Join-Path $storage 'PluginBackups\AutomationMonitor') -PathType Container | Should -BeTrue
        }

        It 'is safe to repeat' {
            $again = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package; Uninstall = $true }
            $again.ExitCode | Should -Be 0
        }
    }

    Context 'Install.cmd entry point (unattended)' {
        BeforeAll {
            $root = New-TempRoot -Name 'cmd'
            $storage = Join-Path $root 'XrmToolBox'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            New-Item -ItemType Directory -LiteralPath $storage -Force | Out-Null
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'finishes without waiting for a key when AM_NO_PAUSE is set' {
            $log = Join-Path $root 'install.log'
            $errLog = Join-Path $root 'install.err.log'
            $arguments = '"' + $CmdPath + '" -StorageRoot "' + $storage + '" -PackageRoot "' + $package + '"'
            $env:AM_NO_PAUSE = '1'
            try {
                $run = Invoke-CmdEntry -Arguments $arguments -Log $log -ErrorLog $errLog
                $because = 'temp: ' + $env:TEMP + ' | cmd line: ' + $run.CommandLine + ' | output: ' + $run.Output
                $run.TimedOut | Should -BeFalse -Because ('Install.cmd must return by itself when AM_NO_PAUSE is set; ' + $because)
                $run.ExitCode | Should -Be 0 -Because $because
                $run.Output | Should -Match 'installed Automation Monitor' -Because $because
                Test-Path -LiteralPath (Join-Path $storage 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
            }
            finally {
                $env:AM_NO_PAUSE = $null
            }
        }

        It 'propagates the failure exit code without blocking' {
            $log = Join-Path $root 'failed.log'
            $errLog = Join-Path $root 'failed.err.log'
            $missing = Join-Path $root 'not installed (yet)'
            $arguments = '"' + $CmdPath + '" -StorageRoot "' + $missing + '" -PackageRoot "' + $package + '"'
            $env:AM_NO_PAUSE = '1'
            try {
                $run = Invoke-CmdEntry -Arguments $arguments -Log $log -ErrorLog $errLog
                $because = 'temp: ' + $env:TEMP + ' | cmd line: ' + $run.CommandLine + ' | output: ' + $run.Output
                $run.TimedOut | Should -BeFalse -Because ('Install.cmd must return the code, not wait for a key, on a failure path; ' + $because)
                $run.ExitCode | Should -Not -Be 0 -Because $because
                $run.Output | Should -Match 'STORAGE_ROOT_MISSING' -Because $because
            }
            finally {
                $env:AM_NO_PAUSE = $null
            }
        }
    }
}






