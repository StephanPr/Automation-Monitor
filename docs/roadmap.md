# Automation Monitor - roadmap

**This file is the authoritative roadmap.** It is version-controlled on purpose, so the Windows and
Arch Linux machines stay synchronized. Milestone state, the active work track, the sequence and the
acceptance gates all live here. `docs/specification.md` owns product behaviour,
`docs/distribution.md` owns packaging and release rules, and `docs/provider-capabilities.md` owns
verified source facts. The maintainer-local `docs/development-plan.md` keeps the original planning
narrative and estimates, and defers to this file wherever sequence or a gate is concerned.

Last updated: 2026-11-15 - AM-40, the defect the first live save-and-reopen exposed: a persisted watchlist
was on disk and valid but missing from the Saved drop-down. `AutomationMonitorControl` had been answering
"have I read this environment's lists already?" from a string initialised to the empty value, and a connection
that reports no environment id (now measured, see `docs/provider-capabilities.md`) uses exactly that value - so
the read that fills the drop-down never ran, while a save appeared to work because it binds from memory. The
rule is Core's `WatchlistScope.NeedsRead` now, over a read state that a usable read alone arms, and the
per-environment line is `SettingsStatus.DescribeForEnvironment`. The operator's own file proved the read
itself was healthy (loaded, one list, 59 of 59 members), which is why the fix is one state distinction rather
than a format or storage change: 17 new tests, 539 total, and the environment-scope question it uncovered is
recorded as AM-41 rather than answered here.

Earlier (2026-11-14): M4 C6 done on top of C5. C5 gave the plugin the storage root
(`<Paths.SettingsPath>\AutomationMonitor`, the `AUTOMATION_MONITOR_XTB_ROOT` override, a refusal when
neither is available), the real-`System.IO` `ISettingsFileAccess`, and the `WatchlistStorage` composition
root. The disk layer is proven against a temporary storage root on two layers - 38 real-filesystem tests in
the `net10` suite (518 total) and a 31-check `net48` harness under .NET Framework 4.8 (first
`DataContractJson` round trip on 4.8, the `File.Move`-onto-existing-backup trap, read-only refusal,
crash recovery, quarantine plus preserved refusal). C6 then gave the store its first caller: a compact
saved-watchlist section on the discovery screen saves the built list, lists what is saved for the connected
environment, and opens one with no solution query - all through `WatchlistManager`. The fuller Watchlists view
(reconciliation preview, management) is still future work; a real save-reopen-reload run is the maintainer's
gate. The 2026-11-12 state still stands: the WF-A.8
probe answered `TextOnly` (the lookup zero recorded as unresolved filter behaviour, not an invalidated
documented join), the direct flow-discovery report and gate item 4 (scaling, keyboard) are still owed by
the maintainer, and there is still no run grid.

## 1. Where the project stands

| Area | State | Evidence layer |
|---|---|---|
| Core models, normalization, merge, dedupe, filters, correlation | Implemented and covered | 480 xUnit tests + smoke test, executed on **Windows** (317 before WF-A, 369 after the AM-32/33 cast and projection fixes, 421 after the AM-34 identity fix, 450 after the AM-35 probe rules, 480 after the AM-36 diagnostic hand-back) |
| M1 solution and cloud-flow discovery | Implemented in core and gateway; **cast and projection defects confirmed live and fixed; the identity defect is fixed and a flow reached the probe on 2026-11-12** | Mapping and identity decisions unit tested; the SDK glue proven against the host's own `Microsoft.Xrm.Sdk` in a net48 harness; the solution read observed live in XrmToolBox; the flow read answered with 59 rows live but every one of them lost its id (AM-34); on 2026-11-12 a flow was selectable and probeable, so a flow row and its identity did reach the UI - the displayed row count and the identity-source tally are still unreported |
| M3 run history (model, provider, merge, correlation) | Implemented in core and gateway; still no run grid. The only run-history UI is the AM-35 diagnostic probe, whose result AM-36 made readable and copyable in the plugin itself and which answered on 2026-11-12 | Core decisions unit tested (480 tests, smoke `PASS`); the production run read has **never** been executed against a tenant; the AM-35 *diagnostic* read of the same table has (2026-11-12, one flow: the text filter returned 5 rows) - that proves the projected column shapes and the filter choice, not normalization, aggregation or display, and the result window is proven only by the maintainer's run |
| M4 saved watchlists and settings | Core + **C5 disk storage** + **C6 minimal save/open wired into the discovery screen** (the `WatchlistManager` caller); the fuller Watchlists view is future; **AM-40 fixed the reload defect C6's first live run exposed** | Core layer (522 xUnit tests at C6, 539 now, incl. 38 real-filesystem adapter tests + the C6 wording and interrupted-rotation tests) and the net48 plugin compiles Release clean (0W/0E); deployed 2026-11-14 18:19 through `scripts\deploy.ps1` (Release, host closed) as ProductVersion `0.1.0+fcd2b28` - `Plugins\AutomationMonitor.Plugin.dll` 57856 B sha256 `f9a50382…1121`, `Plugins\AutomationMonitor.Core.dll` 186368 B sha256 `469c79af…32d8`, previous copies backed up to `PluginBackups\AutomationMonitor\20261114-181938`; no live save-reopen run yet. **Updated 2026-11-15 (AM-40):** a live save did happen and its file was audited - it loads cleanly - but the drop-down showed nothing, and that reload defect is fixed at L1-L3 (539 xUnit tests incl. 17 restart/read-state tests over a real temporary directory, failing on the old rule at exactly the two AM-40 assertions; net48 Release 0W/0E; smoke `PASS`). The save-close-reopen observation on screen is still owed by the maintainer. Redeployed 2026-11-15 11:19 with the host closed (`scripts\deploy.ps1`, exit 0) as ProductVersion `0.1.0+be935b0` - `Plugin.dll` 57856 B sha256 `E1C0F259…`, `Core.dll` 186880 B sha256 `C9C7EA82…`, both byte-identical to the build, prior copies backed up to `PluginBackups\AutomationMonitor\20261115-111910`; the existing `watchlists.json` re-hashed afterwards as unchanged. **L4 observed 2026-11-15 on the primary
host:** after that redeploy the saved watchlist is visible in the Saved drop-down and `Open` binds its 59
stored members with no solution query, so C6's save-reopen-reload acceptance is met on screen. Not reported
and therefore still open: closing and reopening the plugin tab separately from a host restart, and gate item 4
(layout at 100/150/200 percent, keyboard navigation) |
| M5 dashboard, M6 run details, M7 reports | Not started | - |
| M9-M11 Azure Logic Apps and APIM | Not started; `ProviderKinds.IsImplemented` already reports them unimplemented | - |
| Packaging, manifest, validator, release gate | Implemented and audited | Python packaging tests plus a real 65-check package audit |
| Installer (`Install.ps1` / `Install.cmd`) | Written; last read CI run reported 33 of 35 passing with two harness defects; the 36-test suite on `eb6bf1c` was not read | Windows CI job only - never executed locally, by decision |
| Portal deep links (spec section 16) | **Not implemented** - no URI construction or browser-launch code exists anywhere in `src/` | Confirmed by search 2026-11-09 |

## 2. Development environments (dual host)

| | Windows 11 - primary | Arch Linux - fully supported |
|---|---|---|
| Role | Implementation, builds, UI work, direct XrmToolBox runtime testing | Core development, automated tests, packaging tests, CI parity |
| Editor | Code OSS + Cline | Code OSS + Cline |
| Shell | Windows PowerShell 5.1 | fish (POSIX escape hatch: `bash -lc`) |
| .NET | .NET 10 SDK to be installed by the maintainer; .NET Framework 4.8 is present | .NET 10 SDK installed |
| XrmToolBox | Installed (1.2026.9.78): plugin load and live-data layers are reachable | Not available: those layers stay unverified |
| Installer tests | GitHub Actions only | GitHub Actions only |
| Framework targets | Core `netstandard2.0`, Plugin `net48`, tests `net10.0` | identical |

Rules that keep both hosts honest:

- Neither machine may become the only supported path. No architecture, project or script change may
  remove the Linux workflow for Core and the automated gates.
- No Visual Studio Community, Build Tools or visual designer is required. If one ever becomes a
  genuine blocker, the constraint and the workaround are recorded in this roadmap first.
- Pester is never installed locally on either machine; the installer suite stays a CI-only layer.

## 3. The Windows runtime gate

A successful compile is not acceptance. For any change that touches a WinForms control, the
XrmToolBox host, the Dataverse gateway or a provider call, the change is complete only after a
Windows session records:

1. the plugin build identity (the `ProductVersion` the deployed DLL carries, including its commit hash);
2. the XrmToolBox version and storage root used. On the primary Windows host the version no longer needs a
   session report: the storage folder `1.2026.9.78` and `Settings\XrmToolBox.Settings.xml` were read on
   2026-11-11, and that file says `<LogLevel>Warning</LogLevel>`. Name the install and storage root anyway,
   because an `/overridepath` install moves both;
3. the action taken and the behaviour observed, including coverage and error wording;
4. layout at 100 / 150 / 200 percent scaling and keyboard navigation;
5. anything that could not be exercised, named as unverified rather than assumed;
6. for a list that came back empty or degraded, the diagnostic the plugin builds for that read
   (`DescribeDiagnostics()`: the columns asked for, rows read, the cloud-flow selector value, whether the
   identity column was projected, whether the query was distinct, where each row's identity came from, and the
   distinct `category` values seen). **Corrected 2026-11-11 (AM-36):** that line is written at Information, and
   `XrmToolBox.Extensibility.LogManager` only writes entries at or above its log level - `Warning` by default,
   `Warning` on this host - so an Information entry never reaches `Logs\AutomationMonitor.Plugin.log`. That is
   why no such file existed after the first probe session. The discovery line stays in the log by decision (the
   status line describes the environment), which makes reading it conditional on the operator raising the host's
   log level first; making it reachable without that step is WF-A.9, and it is not this gate's requirement;
7. for the AM-35 identity probe (WF-A.8), the result the plugin shows on its own after each run: the copyable
   "Run-history identity probe - result" window added by AM-36, carrying the time window, both row counts, the
   overlap, the verdict, what it means and any query failure. **Use that window, not the log panel.** Its text
   carries no identifier, no name and no provider message by construction, so it is safe to paste back verbatim
   - and only the maintainer's run of it counts as the answer. The same block also goes to the host log at
   Information, Warning or Error according to what the environment did, so a refusal is never quieter than a
   plain empty answer.
   **Satisfied for the probe on 2026-11-12:** the maintainer copied the whole block out of that window and pasted
   it back, and it carried column names, CLR type names, counts and UTC instants only - no flow id, run id,
   environment name, url or provider message. Item 4 (layout at 100/150/200 percent, keyboard navigation) was not
   reported and stays open.

The same gate is the definition of done for the WF checkpoints marked **Windows gate** below.

**Who runs it:** the maintainer. The agent's part ends at a deployed build plus the numbered
checklist and the build identity to compare against; it then records what the maintainer reports.
No agent session connects to a Dataverse or Azure environment - the test environment holds sensitive
business data - and none automates the host's UI to obtain a result. Reported findings are recorded as
behaviour, coverage state and wording; environment names, tenant identifiers, flow or run identifiers
and payloads stay out of the repository, the logs and the conversation.

## 4. The Windows feedback track

### 4.1 Origin and baseline identity

The maintainer installed the plugin on Windows 11 and reported three problems: solution
discovery failing with an InvalidCastException that cannot cast System.String to
Microsoft.Xrm.Sdk.AliasedValue, workflows only discoverable after picking a Dataverse
solution, and a WinForms UI that is not acceptable long term. The report predates the local
work, so the deployed build was identified before anything was planned.

| Item | Value |
|---|---|
| Deployed plugin identity | `0.1.0-ci.8+eb6bf1c4208734d50e559c8fcdcc5d9c873be4c1` |
| Baseline that identity names | `eb6bf1c`, the `origin/main` tip |
| Working branch when re-planned | `feature/m4-persistent-watchlists` at `a23f216`, 4 ahead / 0 behind |
| Commits the report could not see | `d581be2` run-history core, `3ee7a34` gateway run rows, `bfdedf5` docs, `a23f216` M4 persistence |
| Host as measured 2026-11-09 | XrmToolBox 1.2026.9.78, Microsoft.Xrm.Sdk.dll 9.2.49.14828, PowerShell 5.1 |
| Compile-time SDK reference | Microsoft.Xrm.Sdk 9.0.2.2 - a version-sensitive gap |

### 4.2 The discovery failure: four defects, escalating visibility — the last two only visible live

Measured on the primary host on 2026-11-09, against the `Microsoft.Xrm.Sdk.dll` copy this XrmToolBox
install loads (assembly 9.0.0.0, file 9.2.49.14828), driven by a throwaway net48 harness that
source-links the real `DataverseGateway.cs`. No tenant was involved in items 1 and 2; items 3 and 4
could only be found by a real environment answering.

1. **Trigger - confirmed.** `DataverseGateway.Unwrap` obtained every attribute through the generic
   accessor parameterised with `AliasedValue`. That accessor **throws** `InvalidCastException` for any
   attribute that is not an `AliasedValue`: a plain `String` reproduces the reported message verbatim,
   and so does every other shape (an `OptionSetValue`, a `Boolean`, a `DateTime`). Because neither the
   solution query nor the run query joins another table, all three readers - solutions, workflows and
   runs - were broken, not only solutions. `value as AliasedValue` never throws and returns the wrapper
   only for a genuine `AliasedValue`; a missing attribute and a present-with-null-value attribute both
   read as null.
2. **Misclassification - confirmed and fixed.** `DataverseSolutionFlowSource.Fail` passed the exception
   to `ProviderErrorClassifier`, which had no marker for a failure inside this tool, so a local cast
   failure fell through to `Unavailable` and told the operator the environment had not answered. The
   classifier now recognises a local mapping defect (`DataverseMappingException`, or the raw cast and
   overflow types that pre-date it), keeps the coverage model and the separate last-checked timestamp,
   reports it as `Unsupported`, and words the advice as a defect in this build naming the column and the
   CLR type - never as an outage and never as a missing table.
3. **Rejected projection - found live on 2026-11-09, fixed, rerun outstanding.** With the cast gone the
   solution read worked end to end (`complete - 975 row(s) over 4 page(s)`), and the flow read was
   answered with `The 'workflow' entity doesn't contain attribute with Name = 'primarycategory'`. That
   column was projected by the gateway and read by nothing; Dataverse validates every projected column
   and refuses the **entire** query when one is unknown, so the fault was ours from end to end. The
   classifier then made it worse: it fell through to `Unavailable` and told the operator the environment
   had not answered and to check the connection - about a platform that had answered precisely. Since
   AM-33, `DataverseSchema.WorkflowAttributes` is the single workflow projection, the speculative columns
   and the unread model fields behind them are gone, and the classifier tells a rejected query apart
   from a misread value so neither can borrow the other's advice.
4. **Identity discarded by the query - found live on the rerun the same day, fixed, rerun owed again.**
   With the projection accepted, the flow read returned **59 rows** for one solution and the grid said
   `0 cloud flow(s) - 59 row(s) without an id skipped`. The query set `Distinct = true`, which Learn
   documents as returning no primary key values per record, and its projection never named `workflowid`;
   the link-entity asked for `ColumnSet(false)`, so `solutioncomponent.objectid` was not there either.
   Every `Entity.Id` therefore arrived as `Guid.Empty` and the gateway's only identity source was gone.
   Note what this defect did *not* do: it did not lie about coverage. The rows really were returned, the
   skips were counted and shown, and the state was `Complete` because the request had genuinely succeeded -
   what was missing was any statement that the query had thrown the key away. The fix is the pair
   (`workflowid` projected, `WorkflowQueryDistinct = false`) plus a rule that no longer trusts a single
   source: `WorkflowIdentity` resolves the id (projected column first, row id as fallback, refusal when the
   two disagree, an all-zero `Guid` never an id), each row records where its id came from, and
   `DescribeDiagnostics()` reports the projection, the distinct flag and the source tally. The duplicates
   `Distinct` appeared to remove are collapsed in Core, where removing one is counted and visible.

### 4.3 Phase verdicts against the current branch

| Reported phase | Verdict | Why |
|---|---|---|
| A - discovery cast | **Fixed on the primary host and confirmed live (2026-11-09); the two defects behind it also fixed, rerun still owed** | Root cause reproduced against the host's own SDK assembly, adapter and classifier changed, 421 unit tests green, deployed through the shipped installer; the first live session read 975 solutions over 4 pages with the cast absent. Flows then failed on the rejected projection (WF-A.5), and after that fix on identity the query had discarded (WF-A.7) - both fixed the same day. **Rerunning flow discovery remains the gate for the direct report**: the 2026-11-12 probe run implies a flow row and its identity reached the UI, but the displayed row count and the identity-source tally were never reported |
| B - solution-independent browsing | **Valid, better prepared** | Normalization, category-5 selection, dedupe, paging and coverage exist and are tested; the solution-less query, the multi-select UI and the first portal deep link do not |
| C - Azure Logic Apps discovery | **Valid, nothing exists** | The M3 rejection of the undocumented Power Platform run-list surface is the precedent to repeat |
| D - modern UI feasibility | **Newly feasible** | XrmToolBox is installed on the primary host, so a proof of concept can be loaded and measured, not only compiled |
| E - UI design direction | **Deferred behind D** | It collides with the paused M4 Watchlists view; sequence is fixed in section 6 |

## 5. Checkpoints

Each checkpoint is sized to be one implementation prompt. **Windows gate** below means the
runtime gate in section 3, never a successful compile.

### 5.1 WF-0 - bring the primary host to parity

| ID | Scope | Gate |
|---|---|---|
| WF-0.1 | .NET 10 SDK installed on Windows by the maintainer; dotnet --info recorded | host fact, no code |
| WF-0.2 | Re-baseline the gates on Windows: core unit tests, the smoke PASS line, the net48 build, the packaging tests | the recorded numbers reproduce (317 unit tests, smoke PASS, 0 warnings and 0 errors, 48 packaging tests); a silently smaller count is a finding, not a pass |
| WF-0.3 | Confirm a real Python for the packaging tests (the WindowsApps python.exe may be a Store stub) and that autocrlf plus .gitattributes still hand the packager LF assets that ship as CRLF | packaging tests plus a real package audit |
| WF-0.4 | Re-confirm the documented minimum host against the installed XrmToolBox 1.2026.9.78 and the strong-named McTools.Xrm.Connection binding | docs/distribution.md and the manifest, no code change |

WF-0.2 and WF-0.3 were measured on 2026-11-09: the unit suite (317 then, 369 after WF-A), the smoke
`PASS:` line and the `net48` build (`0 Warning(s) 0 Error(s)`) all reproduce on Windows. There is no
real Python on that host - `python` and `python3` resolve to the WindowsApps store stub and no `py`
launcher is installed - so the packaging suite stays an Ubuntu/CI gate and is reported as not executed
on Windows rather than as a pass.

### 5.2 WF-A - make discovery work on the real host

| ID | Scope | Host and gate | Status (2026-11-09) |
|---|---|---|---|
| WF-A.1 | Probe the SDK behaviour directly: load the Microsoft.Xrm.Sdk.dll that XrmToolBox actually loads and call the aliased-value accessor with AliasedValue on an Entity whose attributes hold plain strings. Record whether it throws or returns null. No tenant needed. | Windows, net48; the durable fact goes in docs/provider-capabilities.md | **Done.** It throws `InvalidCastException`, for every non-`AliasedValue` shape; the fact is recorded in `docs/provider-capabilities.md` |
| WF-A.2 | **Simple fix (chosen assumption).** Keep the type-aware unwrapping inside DataverseGateway: accept an AliasedValue, a plain value or null instead of forcing AliasedValue, for the solution mapping and the workflow-connection mapping alike. The richer Core-side row model that would make the gateway directly testable is a separate decision: it changes a seam every provider uses and belongs with WF-C, not with a bug fix. | Core compile and unit suite stay green; gateway compiles; Windows gate on discovery | **Done, with one stated deviation.** The gateway still does all SDK-shape reduction (`Unwrap` type-tests instead of casting). What moved to Core is only the *decision about a plain CLR value* - `DataverseValue.AsText/AsInt32/AsBoolean/AsUtc` plus `DataverseMappingException` - because that decision was untestable without a tenant. No seam changed, so this is not the WF-C row model. |
| WF-A.3 | Mark local mapping and normalization failures so ProviderErrorClassifier returns Unsupported with a message naming this tool; only a request that left the process may read as Unavailable. Keep the coverage model and the separate last-checked timestamp. | New unit theory cases in the existing classification file | **Done.** `ProviderErrorClassifier` takes an `isLocalDefect` flag, recognises `DataverseMappingException` and the raw cast/overflow types, returns `Unsupported` with advice that names this tool, and keeps coverage plus the separate checked-at stamp; the four message sites pass the flag. **AM-33 (2026-11-09)** replaced the bool with a typed
`DefectKind` (`None` / `Mapping` / `RejectedAttribute`) after the live session produced a second local
defect with an opposite cause; every message site now passes the kind, so the two can never share a
sentence again |
| WF-A.4 | Reproduce first: discovery fails at WF-A.1 or in XrmToolBox before the fix, and the same action succeeds after it. Record both results and the build identity of each DLL. | Windows gate | **Done at the harness layer, open at the tenant layer.** Pre-fix the harness throws on the first solution row; post-fix every case maps (solutions with sparse columns, workflows with option sets, runs with BigInt durations) and an unexpected type raises `DataverseMappingException`. Deployed build: ProductVersion `0.1.0+a2f32ebc468d40bc893e8c21e81c9b8ebe5f18aa` (Release), `Plugin.dll` 28160 bytes sha256 `c5725aa4…0117`, `Core.dll` 161280 bytes sha256 `9270c799…6a0e`, installed by `packaging\Install.ps1` into `%APPDATA%\MscrmTools\XrmToolBox\Plugins` with the previous copies backed up to `PluginBackups\AutomationMonitor\20261109-153539`. **The operator's discovery run against a real environment is still required** - see section 3. Partially met on 2026-11-09: solutions were observed complete; flows were not, for the WF-A.5 reason. That rerun is still owed after AM-33. Committed as `4be6992` and redeployed 2026-11-09 20:10 through `scripts\deploy.ps1 -Configuration Release -StorageRoot %APPDATA%\MscrmTools\XrmToolBox` (exit 0): `Plugin.dll` 28160 bytes sha256 `fbfe592d…8f55`, `Core.dll` 162816 bytes sha256 `ddc6367b…8519`, both ProductVersion `0.1.0+4be6992aa2ef…` - the AM-33 commit itself, rebuilt after committing so the identity names the code, previous copies backed up to `PluginBackups\AutomationMonitor\20261109-201043` |
| WF-A.5 | Stop guessing at schema in the query: `DataverseSchema.WorkflowAttributes` becomes the only workflow projection, and the unread `primarycategory`/`secondarycategory` pair leaves the query, the raw model, the reference model and the mapper with it. | Guard test fails before the change and passes after; unit suite and plugin compile stay green | **Done (AM-33, 2026-11-09).** Seven documented columns, one list, one consumer; AM-34 added an eighth, the `workflowid` identity column, so see WF-A.7. `DataverseSchemaTests` pins the set and rejects the removed names in either casing; the plugin compiles `0 Warning(s) 0 Error(s)` |
| WF-A.6 | Make the next empty list explainable from the log without widening the status line: `CloudFlowSelectionResult.DescribeDiagnostics()` (columns asked for, rows read, selector value, distinct `category` values with counts, rows with no category) written through `LogInfo` when discovery yields nothing or is not usable | Unit test plus code read; the logged line is what the maintainer copies back | **Done (AM-33, 2026-11-09).** Log-only - the status line and `DescribeCoverage()` are untouched, because detail belongs in the log and honesty about coverage belongs on screen. AM-34 widened it with the identity facts (projection, distinct flag, per-source tally) and extended the trigger to rows dropped at identity, because the status line can count those but cannot explain them |
| WF-A.7 | Make workflow identity independent of the query shape: project the documented primary id (`workflowid`), stop asking for distinct rows, and resolve the id in Core (`WorkflowIdentity`) with the row id only as a fallback and a refusal when the two disagree. Record on every row where its id came from, and report the projection, the distinct flag and the source tally in the log. | Guard test fails before the change and passes after; de-duplication stays the single `WorkflowKey` rule; unit suite and plugin compile stay green; **Windows gate** on the rerun | **Done at the core layer, rerun owed (AM-34, 2026-11-09).** Red before green after: at `7ee801b` the projection guard fails with `Expected: ["workflowid", …]  Actual: ["name", …]` (`Failed: 1, Passed: 384`); after the change **421 of 421** pass on Windows - 36 new cases covering every identity source, aliasing, `Guid.Empty`, conflicting ids, the observed 59-row shape, cross-source de-duplication and the diagnostics wording. `DescribeCoverage()` gained only the new "row(s) with conflicting ids skipped" segment; the status line still describes the environment. **Deployed 2026-11-09 21:01** through `scripts\deploy.ps1 -Configuration Release -StorageRoot %APPDATA%\MscrmTools\XrmToolBox` (exit 0, `installed … (2 files)`): `Plugins\AutomationMonitor.Plugin.dll` 28672 B sha256 `3102F38749B220B5…` and `Plugins\AutomationMonitor.Core.dll` 165376 B sha256 `C8B4054276726091…`, both `ProductVersion 0.1.0+4908d5d4bcc5…` and byte-identical to the built pair, previous copies backed up to `PluginBackups\AutomationMonitor\20261109-210152`. Whether a live row now carries its id into the grid is **L4 and unverified until the maintainer restarts XrmToolBox and reruns discovery** |
| WF-A.8 | **AM-35 identity spike (bounded, read-only).** Settle which `flowrun` column carries the identity discovery produces: `workflowid` (String 100 - what the monitoring query filters on today) or `workflow` (Lookup - the documented join, whose referenced key is `workflow.workflowid`). Two isolated queries over the selected flow, seven-day window, five-row ceiling each, compared in memory by `flowrunid`, reported as counts and CLR type names only. The production criteria, normalizer and correlation resolver are deliberately untouched: a spike measures, a later task changes. | Windows plus a test environment; the spike writes a fact in docs/provider-capabilities.md, and any change to the monitoring query is its own task with its own tests | **Answered for one flow (built 2026-11-10, answered 2026-11-12).** `DataverseFlowRunProbe` (plugin side) plus `FlowRunProbeDiagnostics` (core side), one labelled diagnostic action in the discovery view, and the `net48` build clean. **AM-36 (2026-11-11) repaired the hand-back**, not the measurement: the first probe runs were unreadable because their verdict travelled only in an Information-level log entry, and `LogManager` does not write plugin entries below the host's `Warning` level (no `Logs\AutomationMonitor.Plugin.log` existed). `FlowRunProbeReport.Build` now returns a copyable block that the plugin shows in its own window after every run - window, both counts, overlap, verdict, meaning, failures - with a severity that keeps a routine empty answer at Information and gives a refusal Warning or Error. 480 unit tests, smoke `PASS`, Release `net48` 0 warning / 0 error. What the runs say, in order: several flows answered `no rows either way`; the AM-36 window was pasted back on 2026-11-12 and delivered the one-sided verdict its fix existed to recover - `TextOnly` for one flow, with the text filter returning 5 rows / 5 distinct run ids (ceiling reached, so a sample) and every `workflowid` value equal to the requested identity, while the lookup filter returned 0 rows - answered rather than refused - and the `workflow` column itself arrived as an `EntityReference` on sampled rows. **Decision: the monitoring filter stays on the text column, and no production change task is opened**; the two verdicts that would have forced one did not occur. The lookup's zero is recorded as **unresolved filter behaviour, not an invalidated relationship** - whether the reference carries another identifier, targets another table, or whether `Equal` on a lookup column of an elastic table simply behaves differently is not established, and re-measuring it stays a legitimate later task with its own evidence. Scope is one flow, one environment, one seven-day window, a five-row sample; the observed shapes retire the guessed-column-type risk class only, while production normalization, aggregation and display still await the run-grid pass. The verdict table and the full answer live in "The AM-35 identity probe" in docs/provider-capabilities.md |
| WF-A.9 | Give the discovery diagnostic (the `DescribeDiagnostics()` line WF-A.6 and WF-A.7 write) the same reachability AM-36 gave the probe: a copyable on-screen form, or a level that survives the host default for degraded reads only. The status line and `DescribeCoverage()` wording stay untouched - a status line describes the environment, not this build's reasoning. | Core text is already unit tested; Windows gate confirms an operator can read it without changing any setting | **Not started.** Found by AM-36 on 2026-11-11: roadmap gate item 6 asked the maintainer to copy back a line the host never writes at its default level, so every empty-list handback since WF-A.6 has depended on a settings change nobody was told about |
| WF-A.10 | Considered and deferred: if the A/B answer comes back `no rows either way`, two more bounded reads turn that into a measured cause - the same identity predicates without the time predicate, and a newest-rows read with no identity predicate. Same five-row ceiling, no `TopCount`, `RetrieveMultiple` only. | Windows gate; the fact lands in docs/provider-capabilities.md | **Deliberately not built (2026-11-11), and still not needed (2026-11-12).** The maintainer will decide only after the `no rows either way` population needs explaining; the 2026-11-12 answer came from a flow that *did* return rows, so it neither triggers nor retires this checkpoint. Building it now would add tenant-wide reads to a diagnostic whose first question is already answered |

### 5.3 WF-B - browse workflows without picking a solution

| ID | Scope | Host and gate |
|---|---|---|
| WF-B.1 | **Capability spike first.** Record in docs/provider-capabilities.md whether RetrieveMultiple on workflow returns the caller-visible cloud flows without a solution filter, and confirm the server-side filter shape: workflow.category equals 5 for cloud flows, so the tested client-side category-5 selection in DataverseCloudFlowSelector stays a safety net rather than the primary filter. Only then touch code, and only on a documented surface. | Windows plus a test environment; the spike writes a fact, not a guess |
| WF-B.2 | Solution-less discovery behind the existing ISolutionAwareFlowSource seam so paging, dedupe and the coverage rollup stay one path; the chosen solution filter still applies in parallel. | Unit tests over a faked row source; Windows gate on real rows |
| WF-B.3 | Environment-wide scope must read as scoped to what the caller can see and may come back Partial or Unauthorized; blank results never read as a healthy empty environment. | Unit assertions on the coverage state |
| WF-B.4 | Multi-select environment and solution filters plus the All cloud flows option in the existing discovery UI. | Windows gate |
| WF-B.5 | First portal deep link: a run opens the correct Power Automate page, built only from a trusted host pattern plus retained provider ids, validated for https and the expected host before the browser receives the URI. | Windows gate |

### 5.4 WF-C - Azure Logic Apps discovery (spike, then build)

| ID | Scope | Gate |
|---|---|---|
| WF-C.1 | Spike the documented ARM discovery surface with sanitized fixtures: scopes, list-by-resource-group and list-by-subscription behaviour, pagination, and the identity fields that feed RunKey. No undocumented endpoint and no write scope. | recorded spike plus a fixture-backed test |
| WF-C.2 | Implement LogicAppsDiscoverySource over that evidence and a real provider kind, so ProviderKinds.IsImplemented stops answering Unsupported for Consumption and Standard discovery. | Core unit tests over a faked ARM seam; no live tenant in any test |
| WF-C.3 | Scope, tenant and subscription selection UI, with honest coverage and a per-source failure that never discards results already collected from Dataverse. | Windows gate |
| WF-C.4 | Record the spike answers in docs/provider-capabilities.md in the same change as the code. | docs match code |

### 5.5 WF-D - prove a modern UI can live inside XrmToolBox

Nothing here approves a rewrite. The phase ends in a written recommendation.

| ID | Scope | Gate |
|---|---|---|
| WF-D.1 | Build **both** shapes on the primary host, not one: a XAML-based WPF control (App.xaml plus ResourceDictionary theming) and a code-only WPF control (no XAML, so no BAML, no designer, no pack URI), each hosted through System.Windows.Forms.Integration.ElementHost. | both compile; each is measured, not assumed |
| WF-D.2 | Load each candidate in the local XrmToolBox install and probe what could break silently: assembly resolution inside the plugin host, the WPF dispatcher running inside a WinForms message loop, keyboard focus and mnemonics, binding converters, and DPI behaviour under ElementHost (Window.AccessKeyConfiguration, KeyboardNavigation.Mode, a PresentationSource.FromVisual check on Loaded). | Windows gate - the load result is the spike |
| WF-D.3 | Measure both: time to first render, scrolling 5000 rows through VirtualizingStackPanel, memory held after a refresh, layout at 100, 150 and 200 percent scaling. | a recorded comparison table |
| WF-D.4 | Record the recommendation with the honest fallback: if a hosted WPF surface cannot be proven, the design language moves to owner-drawn WinForms rendering plus TableLayoutPanel layouts and one shared visual-language helper, not to a rewrite of every view. | docs/roadmap.md and docs/specification.md in the same change |

### 5.6 WF-E - UI design direction, behind the decision

| ID | Scope | Gate |
|---|---|---|
| WF-E.1 | Apply the approved visual direction (dark theme, typography, card surfaces, colour-coded status) as shared styling, never as per-control colour choices. | Windows gate |
| WF-E.2 | Settings and Rules as a tabbed page with inline validation, replacing the modal dialog path. | Windows gate |
| WF-E.3 | Every workflow row clickable into its details with a visible back path, rendered by the one shared renderer so coverage and freshness stay visible. | Windows gate |

WF-E starts only after WF-D.4 and after the paused M4 Watchlists view (C6) has a shape, so the two reorganisations do not collide inside the same navigation host.

### 5.7 WF-F - environment identity, then multi-environment watchlists

Opened by AM-40's first live run, which found a saved watchlist whose scope, 59 member `scopeId`s and
environment profile were all empty because the connection reported no environment id. The question is which
value may name an environment inside a stored identity - a `WorkflowKey` decision, so it carries data-loss
risk and needs its own evidence before any derivation changes.

| ID | Scope | Host and gate | Status |
|---|---|---|---|
| AM-41 | Establish which environment identifiers this host and this SDK really expose, and whether an absent `ConnectionDetail.EnvironmentId` means no identity is reachable at all. | Assembly metadata, the restored NuGet packages, the host's application directory, Microsoft Learn, the connection library's own source; the operator's connection record read for element names only, never values | **Done 2026-11-15 as investigation, documentation only.** An absent `EnvironmentId` element in a *saved* connection does not prove the *running* client cannot supply an environment id; the facts and their limits are in `docs/provider-capabilities.md`. No persisted identity changed on this evidence |
| AM-42 | Read-only identity probe of the active connection: for each candidate, report presence and shape - never the value. Candidates: `ConnectionDetail.EnvironmentId`, `CrmServiceClient.EnvironmentId` (reachable through `ConnectionDetail.ServiceClient`), `TenantId`, `OrganizationUrlName`, `OrganizationFriendlyName`, `ConnectionId`, and `WhoAmIResponse.OrganizationId` over the `IOrganizationService` the host already provides. Compare availability and consistency; state whether a Power Platform environment id is actually present at runtime. | Windows gate. It must not write or migrate any settings file, must not alter any `WorkflowKey`, and must not open or create a connection merely to read a property. Output is shape only (populated / kind / length / equality), so no tenant identifier can reach a log, an export or a commit | **Next implementation task. Not started** |
| AM-43 | Multi-environment watchlists: one list holding workflows from several Dataverse environments, using XrmToolBox's multi-connection facilities, with per-environment coverage so one unreachable environment degrades its own source and never discards the others. The document format, how an environment set is represented, and what becomes of today's one-environment-per-list rule are **this checkpoint's design decisions, not AM-41's** | Core unit tests over faked per-environment sources; Windows gate with two environments | Later - blocked behind AM-42 |
| AM-44 | Give the operator an explicit, verified way to attach a stored identity to a list saved while its scope was empty. Verification is required and the currently connected environment is never assigned automatically, because an empty scope means *unverified*, not "this environment" | Migration tests over a populated legacy file (the real shape is one list of 59 unscoped members) plus the Windows gate | Later - blocked behind AM-42 and AM-43 |
| AM-45 | Independent Azure connection contexts, so a Logic Apps or APIM source is scoped to its own tenant and subscription instead of inheriting the Dataverse connection's identity | Spike, then provider contract tests | Later, with WF-C |

Two rules come out of AM-41 and bind every checkpoint above.

- **A Power Platform environment id and a Dataverse organization id are different identifiers.**
  `WhoAmIResponse.OrganizationId` is the documented source of the organization identity and it is not the
  environment id; `CrmServiceClient.EnvironmentId` is documented as the Power Platform environment id. Both
  are preserved when both are known, each under its own identity kind, and one is never written into the
  other's field.
- **A connection is not an environment.** `ConnectionId` names one saved connection record, so two records
  for one environment yield two values and re-creating a record yields a new one. Tenant id, friendly name,
  URL name and service URL are context and display, never authoritative resource identity.

## 6. Sequence, dependencies and what stays paused

1. WF-0 host parity: nothing on Windows can be measured before it.
2. WF-A: discovery must work on the real host before anything else is visible there. WF-A.1 to A.6, A.7 and A.8
   are done: the identity probe answered on 2026-11-12 (`TextOnly`), the monitoring filter stays on the text
   column, and no production change task is opened by it. What is left on this track is reporting, not measuring -
   the direct flow-discovery numbers and `category` tally the AM-34 build would show, gate item 4 (scaling and
   keyboard), the unexplained `no rows either way` population behind WF-A.10, and the WF-A.9 decision on the
   discovery diagnostic's reachability.
3. WF-B.1 spike, then WF-B.2 to B.5.
4. M4 continues from C6: C5 (the plugin storage root, disk `ISettingsFileAccess` and composition root) and
   C6 (a minimal save/open wired into the discovery screen through `WatchlistManager`) are done, and AM-40
   fixed the reload defect its first live run exposed: saved watchlists are read again after a restart
   because the read state is now nullable and a usable read alone arms it. Next: the fuller Watchlists view
   (tab host, reconciliation preview, management), then the save-reopen-reload runtime gate on a live
   environment; C7 the full gate with an unchanged package audit and C8 the
   documentation inventory follow. AM-41 investigated the environment-identity follow-up AM-40 opened and is
   recorded as section 5.7: the host does expose further identity sources, and a saved connection that carries
   no `EnvironmentId` element does **not** prove the running client cannot supply an environment id, so nothing
   is concluded about the operator's existing unscoped 59-member list until AM-42 measures live values read-only.
   AM-43 carries the mandatory multi-environment watchlist capability and AM-44 the verified, never-automatic
   re-identity of legacy unscoped lists.
5. WF-F: identity first (AM-42), then multi-environment watchlists (AM-43), then the verified re-identity of
   legacy unscoped lists (AM-44). No checkpoint on this track may move a stored `WorkflowKey.ScopeId` before
   AM-42 has reported, because changing scope derivation re-keys every stored member.
6. WF-D: a dual WPF evaluation, which needs a working discovery path to have real rows to show.
7. WF-E design direction, then WF-C Azure discovery, which is independent of the UI decision but
   obeys the same spike-before-code rule.

M4 is partly delivered, not obsolete. The core is committed at `a23f216`; the disk storage layer (C5) and its
first caller (C6) on
top of it, each green at its layer, and the open checkpoints and contract changes carry forward
unchanged: the settings kind discriminator, Clear moving content to a displaced file instead of
deleting it, the store notes on WatchlistStoreResult, and a serializer that refuses a payload which
is not exactly one object.

## 7. Standing rules that bind every WF checkpoint

- Roadmap governance: this file is tracked, `docs/development-plan.md` stays local and defers
  to it. When a sequence or a gate changes, this file changes in the same commit.
- Dual-host discipline: every claim names its host and its evidence layer. Windows proves the
  runtime gate; Linux proves the core gates and the packaging gates. Neither stands in for the
  other, and no change may remove the Linux workflow.
- Read-only boundaries, UTC storage, raw plus normalized status, provider-scoped identity,
  correlation only on evidence, honest coverage and sanitized exports are not changed by this
  track. A checkpoint that seems to need one relaxed is a design error to raise, not a line to
  cross.
- WF-A stays the simple adapter fix until evidence says otherwise. Moving the row abstraction
  into Core is a separate decision with its own risk, never a side effect of a bug fix.
- A provider projects only columns its own code reads and whose documentation it can name. An unknown
  column is not a blank cell - Dataverse refuses the whole query - so `DataverseSchema.WorkflowAttributes`
  is the one projection list, `DataverseSchemaTests` is its guard, and a gateway builds its `ColumnSet`
  from that list alone. Guessing at a schema name is a defect, not a hedge.
- Environment identity is measured, never assumed, and never inferred from what a saved file happens to
  contain. A Power Platform environment id, a Dataverse organization id, a tenant id, a connection id, a URL
  name and a friendly name are six different things; only the first two may serve as resource identity, each
  recorded under its own kind and neither written into the other's field. An empty scope means *unverified*:
  it is not a value to match against, and it is never filled in with whatever environment happens to be
  connected.

- Identity never depends on a query flag. A `Distinct` query returns no primary key values (documented
  behaviour, and the cause of the 59 skipped rows on 2026-11-09), so the workflow projection names the
  table's documented primary id, `DataverseSchema.WorkflowQueryDistinct` is `false`, and both are pinned by
  `DataverseSchemaTests`. `WorkflowIdentity` alone resolves the value: projected column first, row id as
  fallback, an all-zero `Guid` is not an id, and two disagreeing ids refuse the row. Every row records its
  source; de-duplication stays the single `WorkflowKey` rule in the selector; an unidentifiable row is
  counted and dropped - never keyed by a name, a join key or a generated value.
- Two columns can claim the same relationship. `flowrun` documents `workflowid` (String 100) and `workflow`
  (Lookup), and the monitoring query filters on the text one. Until the live probe (WF-A.8) says otherwise the
  filter stays where it is and the probe carries the question: a documented join is not proof that a tenant
  populates it, and a diagnostic never changes what the monitoring path sends.
- Wording follows blame. Only a request that left the process may read as "the environment did not
  answer"; a rejected query or a refused value names this build. A status line describes the environment,
  never the plugin's internals - the internals go to the log.
- WF-D ends in a recommendation and a measurement, never in a converted view.

## 8. Explicitly out of scope for this pass

- No WF-B to WF-E code. This pass stops at what the live sessions proved: the projection fix, the identity
  the query was discarding, the wording that follows blame, and the diagnostic that makes the next empty
  list explainable. The solution-less query, the multi-select UI, the Azure spike, the WPF proof of concept
  and the design reorganisation all stay behind their own checkpoints.
- No speculative column in either direction, and no tenant data in this repository: the recorded facts
  name column behaviour and coverage, never an environment name, url, identifier or payload.
- Nothing from the AM-35 probe reached the monitoring path. `RunQueryCriteria`, `DataverseRunNormalizer`,
  `RunCorrelationResolver` and the production `RunAttributes` projection are what AM-34 left them, and a test
  now pins those thirteen columns so a diagnostic cannot widen them by accident. `parentrunid`'s parent/child
  semantics stay a separate task - the probe does not read that column, and its five-row ceiling makes no
  claim about any window being complete.
- No Pester or PowerShell tooling on either workstation, and no CI workflow run or read.
- No push, merge, tag, release or history rewrite. Every local commit stays local; the M4
  implementation stays paused at `a23f216`, with this planning commit on top of it.
- No force-add of an excluded file: docs/roadmap.md is the only new tracked document.

## 9. Keeping the two machines in step

| Path | Git | Purpose |
|---|---|---|
| docs/roadmap.md | tracked | the plan both hosts must agree on |
| README.md, docs/specification.md, docs/distribution.md, docs/provider-capabilities.md | tracked | published product documentation |
| .clinerules/, .cline/, AGENTS.md | excluded, local | host rules and skills, intended to be identical; read them on the host doing the work |
| PROGRESS.md, docs/agent-handoff.md, docs/development-plan.md | excluded, local | durable handoff, kept per maintainer |
| .agent-state/current-task.md | excluded, local | the per-prompt recovery cursor of whichever host is working |

An agent that finds these out of step updates the copy it can see and says so; it never assumes
the other machine holds the truth.

## 10. References

- docs/specification.md sections 5, 16 and 19: UI direction, portal deep links, acceptance criteria.
- docs/distribution.md: package layout, installer boundaries, Windows sign-off checklist.
- docs/provider-capabilities.md: where WF-A.1, WF-B.1 and WF-C.1 must record their findings.
- docs/development-plan.md (local, not published) section 27: the pointer into this track.

Only the published files are linked as documents above. The development-plan file is named for the
maintainer and is deliberately not in the repository, exactly as section 9 records.
