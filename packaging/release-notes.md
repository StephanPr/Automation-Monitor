# Automation Monitor @@VERSION@@ (Windows package)

Distribution build for the XrmToolBox plugin. This release is a **prerelease**: the
plugin has not yet been verified by a person inside a real XrmToolBox session, so
treat the tool as functional but unproven in production.

## Download and install

Download `@@ZIP_NAME@@` below, extract it, unblock the files, close XrmToolBox and run
`Install.cmd`. Full instructions, including a custom `/overridepath` storage folder and
what to do when the tool does not appear in the list, are in `README-WINDOWS.md` inside
the zip.

- Requires **XrmToolBox @@MINIMUM_XRMTOOLBOX_VERSION@@ or newer** and .NET Framework 4.8.
- Does **not** require Visual Studio, the .NET SDK, Git, source code, or administrator rights.

| File | SHA-256 |
|---|---|
| `@@ZIP_NAME@@` | `@@SHA256@@` |

## What is in this build

- **Solution-aware flow discovery.** Pick the connected environment, list solutions, select
  one and see its modern cloud flows in a searchable grid, with loading, empty, partial and
  unauthorized states shown as themselves rather than as a blank grid.
- **Watchlist building in memory.** Select flows and assemble a solution-derived watchlist.
- **Honest coverage.** Every list carries the provider state, the last check time and whether
  the result is complete or partial.

No run history, no Logic Apps, no APIM and no saved watchlists yet - those are later
milestones, not missing pieces of this one.

## Packaging notes for this release

- The package contains exactly `AutomationMonitor.Plugin.dll` and `AutomationMonitor.Core.dll`.
  Everything else the plugin uses is supplied by XrmToolBox itself; shipping a second copy of a
  host assembly into `Plugins` is a well-known cause of load failures, so this package never does.
- Binaries are **not code-signed**. Windows marks downloaded files as blocked, which can hide a
  plugin from the tool list; the installer calls `Unblock-File` on what it copies and the guide
  explains the manual step and the SmartScreen prompt.
- Debug symbols are not part of this zip; they are attached to the CI run.

## Known limitations

- Loading inside XrmToolBox, live Dataverse reads and grid rendering are **not verified** by CI.
  The Windows runner builds the package and runs the installer against temporary folders only.
- Read-only by design: the plugin never enables, disables, cancels or resubmits a flow.

## Build provenance

CI run: @@RUN_URL@@ (`@@DATE@@`). Produced by `.github/workflows/release.yml`, which builds the
plugin, packages it, validates the archive, and runs the installer test suite in Windows
PowerShell 5.1 on a `windows-2022` runner before this release is created.
