# Distribution: how the Windows package is built, checked and shipped

Audience: whoever builds, publishes or troubleshoots a downloadable build of the
plugin. Product behaviour is in `specification.md`, and what each provider really
supports is in `provider-capabilities.md`. This file owns packaging, installers, CI and releases.

Scope of this milestone: **distribution only**. It adds no monitoring capability; the
feature set of a packaged build is whatever `main` implements at that commit.

## The package

One zip per release: `AutomationMonitor-v<VERSION>-win.zip`, flat at the archive root.

| Entry | Purpose |
|---|---|
| `Install.ps1` | Installer. Windows PowerShell 5.1, current user, no elevation. |
| `Install.cmd` | Double-click entry point; adds the process-scoped execution-policy override. |
| `README-WINDOWS.md` | The end-user guide: install, upgrade, uninstall, troubleshooting. |
| `SHA256SUMS.txt` | Checksums of everything under `plugin/`. |
| `plugin/plugin.manifest.json` | `schemaVersion` 1: the file list, checksums and sizes the installer trusts. |
| `plugin/AutomationMonitor.Plugin.dll` | The plugin. |
| `plugin/AutomationMonitor.Core.dll` | The monitoring core. |

**Two managed assemblies is the complete runtime closure.** `AutomationMonitor.Core`
targets `netstandard2.0` and references no package of its own; everything else the plugin
needs (`Microsoft.Xrm.Sdk`, `XrmToolBox.Extensibility`, the connection library,
`Newtonsoft.Json`, WebView2) is what the host already loads. `scripts/package_windows.py`
therefore stages an explicit allow-list and the validator refuses an archive that contains
anything else.

That rule exists because the build output used to be the packaging input: it held 44 files
including `XrmToolBox.exe`, the CRM SDK and `Newtonsoft.Json.dll`. Copying a second copy of
a host assembly into `Plugins` is a standard way to make XrmToolBox fail to load a plugin.
`<CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>` in the plugin project now
keeps the build output equal to the distribution set, and the allow-list means a packager can
never reintroduce the problem. Debug symbols, sources, `obj/` and the RID-native
`runtimes/**/WebView2Loader.dll` files that remain in the output are never packaged; symbols
are published as a separate CI artifact instead.

`Install.ps1` and `Install.cmd` are stored with LF in the repository and written into the
archive with **CRLF** - `cmd.exe` misbehaves on LF-only batch files. `.gitattributes` keeps
the working tree LF so that decision stays inside the packager instead of depending on a
developer's git config.

## Versioning and identity

- The single source of truth is `<Version>` in `src/AutomationMonitor.Plugin/AutomationMonitor.Plugin.csproj`.
  `AutomationMonitor.Core` carries the same number so both shipped assemblies agree with what
  the manifest advertises.
- CI builds with `-p:Version=<full> -p:AssemblyVersion=<x.y.z> -p:FileVersion=<x.y.z>.0`. Those
  are global properties, so the Core project receives them through the project reference.
  `<Version>` alone would leave `AssemblyVersion` fixed at `0.1.0.0`, which is what XrmToolBox
  reads for the tool list.
- A CI build on `main` is versioned `<x.y.z>-ci.<run_number>`: the numeric core always equals
  the project version, so a build never looks like a release.
- A release is a tag `v<x.y.z>[-label]`. `scripts/check_release_version.py` refuses a tag whose
  numeric core differs from the project version, and - while runtime testing is outstanding -
  refuses a tag with no prerelease label. That second rule is the
  `RELEASE_REQUIRE_PRERELEASE_LABEL` variable in `release.yml`; flip it to `false` only after
  the runtime sign-off below.

### Minimum XrmToolBox version

`plugin.manifest.json` states `minimumXrmToolBoxVersion: 1.2025.10.74`, and `README-WINDOWS.md`
repeats it. It is not a guess: `AutomationMonitor.Plugin.dll` binds to
**`McTools.Xrm.Connection` 1.2025.9.64**, which is strong-named, and `XrmToolBox.exe`
1.2025.10.74 references that same version - verified by reading the assembly metadata of both
(`monodis --assemblyref`). A host older than that ships an earlier connection assembly and the
plugin fails to load with a `FileLoadException`. `XrmToolBox.Extensibility` is *not* strong-named,
so its version is not enforced at load time.

## Building a package here (Linux)

```bash
bash scripts/linux-build.sh                                    # all gates, incl. packaging
dotnet build src/AutomationMonitor.Plugin/...Plugin.csproj -c Release
python3 scripts/package_windows.py --version 0.1.0 --output dist
python3 scripts/validate_windows_package.py dist/AutomationMonitor-v0.1.0-win.zip --version 0.1.0
unzip -l dist/AutomationMonitor-v0.1.0-win.zip
```

Python is used for packaging and validation precisely because it runs on this workstation and
on both CI runners; PowerShell is used only where Windows is required anyway.

The validator recomputes every hash from the bytes in the archive instead of trusting
`SHA256SUMS.txt` or the manifest, and it also checks layout, path safety, that no host or debug
file sneaked in, that the installer still targets 5.1 without a `pwsh` fallback, that the
`pause` in `Install.cmd` stays guarded, and that the guide still contains the statements users
depend on (unblock, restart, custom storage path, minimum host version, uninstall).


## Installing on Windows

End users follow `README-WINDOWS.md` inside the zip. The maintainer-relevant facts:

- Default target is the roaming storage the host uses: `%APPDATA%\MscrmTools\XrmToolBox\Plugins`.
  A host started with `/overridepath:<folder>` keeps its tools under that folder instead, so
  `Install.ps1 -StorageRoot <folder>` (or `AUTOMATION_MONITOR_XTB_ROOT`) is the supported way to
  reach it. The old `scripts/deploy.ps1` convention - install next to the exe and start with
  `/overridepath:.` - is still correct for development and is now *implemented* by the same
  installer rather than by a second copy routine.
- Downloaded files carry a `Zone.Identifier`; a blocked plugin can be silently absent from the
  tool list. The installer unblocks what it copies and the guide explains the manual step.
- The installer refuses to run while `XrmToolBox` is running, copies only manifest names, backs
  up the previous copies of *its own* files to `<storage>\PluginBackups\AutomationMonitor\<stamp>`
  (outside `Plugins`, so the host can never load a backup as a second copy), rolls back the files
  it already replaced if a later copy fails, and removes only its own files on `-Uninstall`.
- Binaries are unsigned. SmartScreen will warn on first run; that is expected, and the guide says
  so rather than pretending otherwise.

## CI layout

| Workflow | Trigger | Runs | Permissions |
|---|---|---|---|
| `build.yml` | push to `main`, PR, manual | Ubuntu: core unit tests, smoke test, packaging tests, Linux-built package + validation | `contents: read` |
| `_windows-package.yml` | called by the two below | `windows-2022`: release-identity build, output-set assertion, package, validate, assembly-identity and manifest assertions, **Pester installer suite**, artifact upload | `contents: read` |
| `release.yml` | tag `v*`, or manual with a tag | gate the tag, call the packaging workflow, re-verify checksum and re-validate the downloaded artifact, create a **prerelease** | `contents: write` on the publish job only |

The Windows job installs Pester at a pinned version (`5.6.1`) inside the runner, runs the suite
and fails the build if any test fails **or if fewer than 30 tests ran** - a suite that silently
stopped executing must not look like a pass. Pester is a test-time tool: it is never a runtime
dependency and never part of a package.

Releases are gated behind that job: `publish` has `needs: [prepare, package]`, so a failed
installer test means no release exists, not a release with a warning.

## What is verified, and where

| Layer | Evidence | Where |
|---|---|---|
| Core behaviour | 133 xUnit tests + smoke `PASS:` | Linux and Ubuntu CI |
| Packaging rules | 39 Python tests (packager, validator, release gate) | Linux and Ubuntu CI |
| Archive integrity, layout, no host/debug files, manifest self-consistency | 63 validator checks on the real zip | Linux and both CI runners |
| `net48` compile of the plugin | `0 Warning(s) 0 Error(s)` | Linux (cross-compile) and Windows CI |
| Build output equals the distribution set | CI step that fails on any unexpected assembly | Windows CI |
| Assembly name/version identity | `[AssemblyName]::GetAssemblyName` assertions | Windows CI only |
| Installer behaviour | 35 Pester tests: fresh, upgrade, repeat, spaces+parens, custom and default storage, blocked file, missing/damaged/foreign package, rollback, uninstall, unattended `Install.cmd` | **Windows CI only - not yet executed** |
| Plugin loads in XrmToolBox; grid renders; live Dataverse reads; portal deep links | none | **no environment available** |

## Runtime sign-off checklist (still to be done by a person, on Windows)

Until these are ticked, every release stays a prerelease:

1. Install XrmToolBox >= 1.2025.10.74, extract a package, run `Install.cmd` from Explorer.
2. Confirm `Automation Monitor` appears in the tool list after a restart, under the expected author.
3. Connect to a test environment and run the M1 discovery flow end to end.
4. Upgrade over the previous version; confirm the backup folder and that no other plugin broke.
5. `-Uninstall`, restart, confirm the tool is gone and neighbours survived.
6. Repeat once on a host with `/overridepath:.`, and once on a machine with a non-default
   `%APPDATA%` location.

## Maintainer troubleshooting

- **`MSB3277` / "found conflicts between different versions"** - check that
  `MSCrmTools.Xrm.Connection` still has `ExcludeAssets="runtime"` and that
  `CopyLocalLockFileAssemblies` has not been turned back on.
- **Plugin missing from the list** - blocked files, host not restarted, host older than the
  manifest's minimum, or a stray host assembly in `Plugins`. `README-WINDOWS.md` has the table.
- **`gh release create` fails** - the release already exists; this workflow never overwrites or
  deletes a release.
- **Artifact vanished** - workflow artifacts expire on repository retention settings (30 days by
  default). A release asset does not; that is the difference between the two and why releases exist.
- **`Install.cmd` hangs in a script** - `AM_NO_PAUSE` is not set to `1`; the pause is there for
  double-click users and must stay guarded.

## Known gaps

- The Pester suite has **never run**: no PowerShell on this workstation and the CI run has to
  happen on GitHub. Expect the first execution to surface fixes.
- The package is unsigned and no code-signing is planned without an explicit decision.
- Nothing publishes to the XrmToolBox Tool Library or NuGet, by design.
