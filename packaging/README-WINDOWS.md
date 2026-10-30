# Automation Monitor @@VERSION@@ - install on Windows

A read-only Power Automate and Logic Apps monitor that runs inside
[XrmToolBox](https://www.xrmtoolbox.com/). This package installs the plugin for the
current user; it does not need Visual Studio, the .NET SDK, Git or source code, and it
does not need administrator rights.

## What you need

| Requirement | Detail |
|---|---|
| XrmToolBox | The host application. Automation Monitor is a plugin, not a standalone program. |
| XrmToolBox version | **@@MINIMUM_XRMTOOLBOX_VERSION@@ or newer.** Older hosts bind to an older connection assembly and the plugin will not load. |
| .NET Framework | 4.8. Already installed on Windows 10 version 1903 and later, and on Windows 11. |
| Visual Studio / .NET SDK | **Not required.** Nothing here is compiled on your machine. |
| Rights | Standard user account. The plugin is installed under your own profile folder. |

If you do not have XrmToolBox yet: download it, extract it, **unblock** it (see below)
and start it once so its storage folder is created. Then install this plugin.

## Install

1. Download `@@ZIP_NAME@@` from the GitHub Release page and verify the checksum
   (`SHA256SUMS.txt` inside the zip, `<zip file name>.sha256` next to the download).
2. Extract the zip to a folder (do not run the installer from inside the zip view).
3. **Unblock the extracted files.** Windows marks downloaded assemblies as blocked and a
   blocked plugin can be skipped silently. In PowerShell:
   `Get-ChildItem -Path . -Recurse | Unblock-File`
   Or right-click each file: *Properties* → *Unblock*.
4. Close XrmToolBox if it is open (the installer refuses to run while it is).
5. Double-click **`Install.cmd`**, or run in PowerShell:
   `PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1`
6. Read the final line, then **restart XrmToolBox**. The plugin list is built at startup,
   so a plugin installed while the host is running will not appear until you restart it.

Expected output ends with:

```text
[AutomationMonitor.Install] installed Automation Monitor Plugin v@@VERSION@@ (2 files)
[AutomationMonitor.Install] Restart XrmToolBox, then open Automation Monitor from the tool list.
```

### Installing into a custom XrmToolBox storage location

By default the installer targets `%APPDATA%\MscrmTools\XrmToolBox\Plugins`. If you start
XrmToolBox with `/overridepath:<folder>` (including `/overridepath:.` for "next to the
exe"), the tools live under that folder instead, so point the installer at the same root:

```powershell
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -StorageRoot 'D:\XrmToolBoxStorage'
```

The installer prints the resolved storage folder and target folder before it writes
anything - check those two lines if XrmToolBox is installed somewhere unusual.

To see exactly where it would write, without writing anything, add `-DryRun`:

```powershell
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -DryRun
```

It performs every check (package integrity, checksums, host closed, target reachable)
and then stops before touching a file.

## Find the plugin

After restarting XrmToolBox: connect to an environment, then type **Automation Monitor**
in the tool search box (it is also listed under the *Automation Monitor* author group).

## Install an update

Run `Install.cmd` from the new package. The installer replaces only Automation Monitor's
own assemblies and first copies the ones it replaces to:

```text
<storage>\PluginBackups\AutomationMonitor\<date-time>\
```

Those backups are never deleted, and no other plugin's files are read or touched.

## Remove or uninstall

Close XrmToolBox, then either run the installer from an extracted package with
`-Uninstall`, or delete these two files from `<storage>\Plugins`:

```text
AutomationMonitor.Plugin.dll
AutomationMonitor.Core.dll
```

Nothing else is written by this plugin: no registry entries, no services, no scheduled
tasks, and settings are removed by XrmToolBox when the tool is forgotten.

## If the plugin does not appear

| Symptom | Cause and fix |
|---|---|
| Tool not listed, no error | Files still blocked → unblock (step 3), or XrmToolBox was not restarted. |
| Tool not listed, older host | XrmToolBox older than @@MINIMUM_XRMTOOLBOX_VERSION@@ → update the host; this plugin binds to the connection assembly the newer host ships. |
| "Could not load file or assembly 'AutomationMonitor.Core'" | Only one DLL was copied. Reinstall from the full package so both files land in `Plugins`. |
| "Could not load file or assembly 'McTools.Xrm.Connection', Version 1.2025.9.64" | The host is too old for this build → update XrmToolBox. |
| `FileLoadException` / version mismatch | A stray second copy of a host assembly in `Plugins`. Remove copies of `Microsoft.*`, `XrmToolBox.*` or `Newtonsoft.*` that other tools may have placed there; this package never ships such files. |
| Installer says `HOST_RUNNING` | Close XrmToolBox and run again. |
| Installer says `PACKAGE_INCOMPLETE` / `PACKAGE_HASH_MISMATCH` | Damaged or partly extracted download → re-download and re-extract, then check the SHA-256. |
| Installer says `STORAGE_ROOT_MISSING` | XrmToolBox has not created its storage folder yet, or you use `/overridepath` and must pass `-StorageRoot`. |

XrmToolBox keeps its own log under `<storage>\Logs`, which is where a plugin load failure
is recorded in more detail.

## What this package contains

```text
Install.ps1                       installer (Windows PowerShell 5.1, no admin rights)
Install.cmd                       double-click entry point
README-WINDOWS.md                 this guide
SHA256SUMS.txt                    checksums of everything under plugin/
plugin/plugin.manifest.json       the file list the installer trusts, with checksums
plugin/AutomationMonitor.Plugin.dll
plugin/AutomationMonitor.Core.dll
```

Two assemblies is the complete runtime set: everything else the plugin uses is provided by
XrmToolBox itself, which is why copying extra DLLs into `Plugins` breaks plugins and why
this package deliberately ships none.
