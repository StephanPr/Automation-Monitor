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

    # 5.1 only: -LiteralPath has its own parameter set, and that set has no -Parent switch (-Parent
    # belongs to the Parent Set, whose path parameter is -Path). Pairing them throws
    # ParameterBindingException before the cmdlet runs, and here it would take the whole container
    # down. $PSScriptRoot is a checkout path with no wildcard characters, so -Path is safe.
    $RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
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
        [System.IO.Directory]::CreateDirectory($root) | Out-Null
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

    function Receive-CapturedStream {
        <#
            Collects one ReadToEndAsync() task started on a redirected child stream. The reads are
            started before WaitForExit so a full pipe buffer can never defeat the timeout: .NET
            Framework 4.8 has no ProcessStartInfo.RedirectStandardOutputToFile to fall back on.
        #>
        param($Task, [int] $TimeoutMs = 5000)

        if ($null -eq $Task) { return '' }
        if (-not $Task.Wait($TimeoutMs)) { return '<stream read timed out>' }
        if ($Task.IsFaulted) { return '' }
        return $Task.Result
    }

    function Stop-CmdTree {
        <#
            A batch entry point spawns a child - Install.cmd starts powershell.exe - and
            Process.Kill() stops only cmd.exe, leaving that child holding the temp root the AfterAll
            is about to delete. Terminating the tree is the difference between a failing test and a
            runner that cannot clean up. It never throws: it runs on the timeout path, where the
            reported problem has to stay the timeout and not become a teardown error.
        #>
        param([System.Diagnostics.Process] $Process)

        try {
            & (Join-Path $env:SystemRoot 'System32\taskkill.exe') /F /T /PID $Process.Id 2>&1 | Out-Null
        }
        catch {
            try { $Process.Kill() } catch { }
        }
        try { $null = $Process.WaitForExit(5000) } catch { }
    }

    function Invoke-CmdEntry {
        <#
            Runs a .cmd entry point the way an unattended caller does: in its own cmd.exe, with a
            finite timeout, both output streams captured and the child's real exit code. /d keeps any
            machine AutoRun out of the way.

            Command line. cmd keeps the quotes of a /c payload only when that payload holds exactly
            two quote characters and no special characters between them, and '&' '|' '(' ')' count as
            special - the temp roots in this suite deliberately contain parentheses, so cmd always
            falls back to its heuristics: strip the leading quote, remove the last quote of the line.
            /S replaces those heuristics with one documented rule - strip the first and last quote
            around the string and leave everything else unchanged - so the payload is wrapped in
            exactly ONE pair, and the batch path contributes a pair of its own:

                /d /s /c ""D:\a b\Install.cmd" -StorageRoot "C:\c d (e)""

            A second wrapper pair hands cmd the token ""D:\a b\Install.cmd" and it answers
            "The filename, directory name, or volume label syntax is incorrect." without ever starting
            the batch (AM-23). The quoting is built here, from an argument array, so no call site can
            get the nesting wrong.

            Process, not Start-Process. Start-Process -PassThru hands back an object for a child that
            has frequently already exited, and Windows PowerShell 5.1 then surfaces no exit code at
            all: CI reported "Expected 0 ... but got $null" while TimedOut was already false. Starting
            the Process here keeps the handle alive until Dispose, so ExitCode is the child's own
            code, and a code that still cannot be read is raised as a harness defect instead of being
            returned as $null. Stdin is deliberately not redirected: Install.cmd's pause must stay
            able to block, or "returns by itself when AM_NO_PAUSE is set" would prove nothing.
        #>
        param(
            [string] $BatchPath,
            [string[]] $Arguments = @(),
            [int] $TimeoutMs = 90000
        )

        if (-not (Test-Path -LiteralPath $BatchPath)) { throw "batch file not found: $BatchPath" }

        $payload = '"' + $BatchPath + '"'
        foreach ($argument in $Arguments) {
            # A Windows path cannot contain a quote, so one in a value is a caller bug, not a cmd quirk.
            if ($argument.Contains('"')) { throw "batch argument must not contain a quote: $argument" }
            if ($argument.StartsWith('-')) {
                $payload = $payload + ' ' + $argument
            }
            else {
                $payload = $payload + ' "' + $argument + '"'
            }
        }
        $commandLine = '/d /s /c "' + $payload + '"'

        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = if ($env:ComSpec) { $env:ComSpec } else { 'cmd.exe' }
        $startInfo.Arguments = $commandLine
        # Redirection requires UseShellExecute=false; a window style alongside it is a conflict.
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true

        $proc = $null
        try {
            $proc = [System.Diagnostics.Process]::Start($startInfo)
        }
        catch {
            throw "cmd.exe could not be started with: $commandLine`n$($_.Exception.Message)"
        }

        try {
            $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
            $stderrTask = $proc.StandardError.ReadToEndAsync()

            $timedOut = -not $proc.WaitForExit($TimeoutMs)
            if ($timedOut) {
                Stop-CmdTree -Process $proc
            }
            else {
                # Flushes the asynchronous exit processing the timed overload does not perform; only
                # after it are ExitCode and the drained streams guaranteed to be settled.
                $proc.WaitForExit()
            }

            $stdout = Receive-CapturedStream -Task $stdoutTask
            $stderr = Receive-CapturedStream -Task $stderrTask
            $exitCode = $null
            if (-not $timedOut) {
                try {
                    $exitCode = $proc.ExitCode
                }
                catch {
                    throw ('the exit code of cmd.exe could not be read, which is a harness defect: ' +
                        'cmd line: ' + $commandLine + ' | output: ' + $stdout + $stderr)
                }
            }

            return [pscustomobject]@{
                TimedOut    = $timedOut
                ExitCode    = $exitCode
                StdOut      = $stdout
                StdErr      = $stderr
                Output      = ($stdout + $stderr)
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
        [System.IO.Directory]::CreateDirectory($payload) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
            [System.IO.Directory]::CreateDirectory($plugins) | Out-Null

            # Another tool's assembly, plus a folder-based tool. Fixed content and a
            # deliberate timestamp make any touch by our installer visible.
            $otherDll = Join-Path $plugins 'OtherPlugin.dll'
            [System.IO.File]::WriteAllBytes($otherDll, [System.Text.Encoding]::ASCII.GetBytes('OTHER-PLUGIN-BYTES'))
            $otherBefore = Get-Sha256 $otherDll
            $otherTimeBefore = (Get-Item -LiteralPath $otherDll).LastWriteTime
            $otherSub = Join-Path $plugins 'AnotherTool'
            [System.IO.Directory]::CreateDirectory($otherSub) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
            $oldPackage = New-TestPackage -Root (Join-Path $root 'package1') `
                -CoreBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-CORE-BYTES')) `
                -PluginBytes ([System.Text.Encoding]::ASCII.GetBytes('OLD-PLUGIN-BYTES'))
            $null = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $oldPackage }

            # The neighbouring plugin is planted by the fixture, not by the installer, so the
            # Plugins directory must exist here whether or not the first install succeeded.
            [System.IO.Directory]::CreateDirectory($plugins) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
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
            [System.IO.Directory]::CreateDirectory($defaultRoot) | Out-Null
            $createdDefault = $true

            $result = Invoke-Installer -Parameters @{ PackageRoot = $package }
            $result.ExitCode | Should -Be 0
            (Get-InstallerReportedPath -Text $result.Output -Field 'target') |
                Should -Be (Get-CanonicalPath -Path (Join-Path $defaultRoot 'Plugins'))
            Test-Path -LiteralPath (Join-Path $defaultRoot 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
        }

        It 'honours the environment override for a portable installation' {
            $portable = Join-Path $root 'XrmToolBox (portable)'
            [System.IO.Directory]::CreateDirectory($portable) | Out-Null
            $env:AUTOMATION_MONITOR_XTB_ROOT = $portable

            $result = Invoke-Installer -Parameters @{ PackageRoot = $package }
            $result.ExitCode | Should -Be 0
            (Get-InstallerReportedPath -Text $result.Output -Field 'target') |
                Should -Be (Get-CanonicalPath -Path (Join-Path $portable 'Plugins'))
            Test-Path -LiteralPath (Join-Path $portable 'Plugins\AutomationMonitor.Core.dll') | Should -BeTrue
        }

        It 'lets -StorageRoot win over the environment for an /overridepath install' {
            $explicit = Join-Path $root 'chosen (by hand)'
            [System.IO.Directory]::CreateDirectory($explicit) | Out-Null
            $env:AUTOMATION_MONITOR_XTB_ROOT = (Join-Path $root 'ignored')

            $result = Invoke-Installer -Parameters @{ StorageRoot = $explicit; PackageRoot = $package }
            $result.ExitCode | Should -Be 0
            Test-Path -LiteralPath (Join-Path $explicit 'Plugins\AutomationMonitor.Plugin.dll') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $root 'ignored') | Should -BeFalse
        }

        It 'previews the target with -DryRun and writes nothing' {
            $target = Join-Path $root 'preview (nothing here)'
            [System.IO.Directory]::CreateDirectory($target) | Out-Null

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
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
            $null = Invoke-Installer -Parameters @{ StorageRoot = $storage; PackageRoot = $package }
            # The neighbour file is planted by the fixture, so its directory has to exist
            # independently of whatever the install above managed to do.
            [System.IO.Directory]::CreateDirectory($plugins) | Out-Null
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

    Context 'Batch harness self-check' {
        BeforeAll {
            # This context separates 'cmd could not parse the command line' from 'the installer is
            # broken'. The probe sits two directories deep under names carrying spaces and
            # parentheses, prints the command line cmd.exe itself was started with (%CMDCMDLINE%),
            # the path it ran as (%~f0) and the arguments it received (%*, %~2, %~4), writes to both
            # streams, and exits with a code no installer would ever use.
            $root = New-TempRoot -Name 'probe'
            $dir = Join-Path $root 'probe dir (nested)'
            [System.IO.Directory]::CreateDirectory($dir) | Out-Null
            $probeCmd = Join-Path $dir 'probe.cmd'
            $probeArg = Join-Path $dir 'first arg (paren)'
            $probeMarker = Join-Path $root 'second arg'

            # ASCII, never UTF8: Windows PowerShell 5.1 writes UTF8 with a BOM, and cmd.exe will not
            # run a batch file that begins with one. Set-Content supplies the CRLF endings cmd wants.
            # Every printed value keeps its parentheses balanced, so echoing it stays inert.
            @(
                '@echo off'
                'echo CMDCMDLINE=%CMDCMDLINE%'
                'echo BATCH=%~f0'
                'echo ARGS=%*'
                'echo ARG2=%~2'
                'echo ARG4=%~4'
                'echo probe-stdout-marker'
                'echo probe-stderr-marker 1>&2'
                'exit /b 43'
            ) | Set-Content -LiteralPath $probeCmd -Encoding ASCII

            $run = Invoke-CmdEntry -BatchPath $probeCmd -Arguments @('-StorageRoot', $probeArg, '-Marker', $probeMarker)
            $because = 'temp: ' + $env:TEMP + ' | cmd line: ' + $run.CommandLine + ' | output: ' + $run.Output
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'starts a spaced and parenthesised batch, captures both streams and returns its exit code' {
            # The batch really ran. %~f0 is matched by suffix because it canonicalises the 8.3
            # spelling $env:TEMP arrives with, exactly as AM-21 requires.
            $run.TimedOut | Should -BeFalse -Because ('the probe must return by itself; ' + $because)
            $run.Output | Should -Match 'BATCH=.*probe dir \(nested\)\\probe\.cmd' -Because $because
            # cmd received the whole spaced and parenthesised path as one quoted token rather than
            # cutting it at the first space: %CMDCMDLINE% is cmd reporting its own command line.
            $run.Output | Should -Match ('CMDCMDLINE=.*"' + [regex]::Escape($probeCmd) + '"') -Because $because
            # The quoting survived the trip: %* keeps the quotes, %~2 and %~4 hand back the values.
            $run.Output | Should -Match ('ARGS=-StorageRoot "' + [regex]::Escape($probeArg) + '"') -Because $because
            $run.Output | Should -Match ('ARG2=' + [regex]::Escape($probeArg) + '(\r|\n|$)') -Because $because
            $run.Output | Should -Match ('ARG4=' + [regex]::Escape($probeMarker) + '(\r|\n|$)') -Because $because
            # Two captures, not one merged blob.
            $run.StdOut | Should -Match 'probe-stdout-marker' -Because $because
            $run.StdOut | Should -Not -Match 'probe-stderr-marker' -Because $because
            $run.StdErr | Should -Match 'probe-stderr-marker' -Because $because
            # A fast-exiting child's code reaches the caller instead of arriving as $null.
            $run.ExitCode | Should -Be 43 -Because ('a batch exit code must survive the harness; ' + $because)
        }
    }

    Context 'Install.cmd entry point (unattended)' {
        BeforeAll {
            $root = New-TempRoot -Name 'cmd'
            $storage = Join-Path $root 'XrmToolBox'
            $package = New-TestPackage -Root (Join-Path $root 'package')
            [System.IO.Directory]::CreateDirectory($storage) | Out-Null
        }

        AfterAll { Remove-TempRoot -Root $root }

        It 'finishes without waiting for a key when AM_NO_PAUSE is set' {
            $env:AM_NO_PAUSE = '1'
            try {
                $run = Invoke-CmdEntry -BatchPath $CmdPath -Arguments @('-StorageRoot', $storage, '-PackageRoot', $package)
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
            $missing = Join-Path $root 'not installed (yet)'
            $env:AM_NO_PAUSE = '1'
            try {
                $run = Invoke-CmdEntry -BatchPath $CmdPath -Arguments @('-StorageRoot', $missing, '-PackageRoot', $package)
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






