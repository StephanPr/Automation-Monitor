# Automation Monitor - roadmap

**This file is the authoritative roadmap.** It is version-controlled on purpose, so the Windows and
Arch Linux machines stay synchronized. Milestone state, the active work track, the sequence and the
acceptance gates all live here. `docs/specification.md` owns product behaviour,
`docs/distribution.md` owns packaging and release rules, and `docs/provider-capabilities.md` owns
verified source facts. The maintainer-local `docs/development-plan.md` keeps the original planning
narrative and estimates, and defers to this file wherever sequence or a gate is concerned.

Last updated: 2026-11-15 - **AM-42's live identity read has been reported, and AM-44.1 is implemented.** The
contract.** The maintainer ran the connection-identity diagnostic on the build deployed on 2026-11-15
(ProductVersion `0.1.0+729f485…`) and pasted back the whole result block at 11:06:15 UTC. What it reported: the
connection record's `EnvironmentId` stated nothing, the already-connected client's `EnvironmentId` stated nothing
either, `WhoAmIRequest` succeeded, and `WhoAmIResponse.OrganizationId` was present with a valid GUID shape - so the
two environment-id sources are **not comparable** for this connection because neither supplied an identifier, and the
organization id is the one resource identifier this connection does provide. Section 3 item 8 is closed for the
identity read; item 4 (layout at 100/150/200 percent, keyboard navigation) stays open for this build, the AM-40 one
and this one, and the multi-environment gates stay open. **AM-44.1 is done (2026-11-15): the contract AM-43 recorded
is now in Core** - the `dataverse-organization:<canonical-guid>` grammar with GUID canonicalisation, the states that
keep a recognized token apart from a connected or associated one, one scope label that carries no identifier, the rule
that content this build cannot represent can never be dropped by a later save, and the previous-build round-trip proof
that settles `schemaVersion` at 1 (`COMPATIBILITY: PASS`, recorded in `docs/provider-capabilities.md`). It writes no
scope value from the running plugin, re-keys no member and changes no discovery identity.
**AM-44.2 is done (2026-11-15): the verification read, the evidence and the association exist - applying them is held
back on purpose.** A saved list can now be checked against the organization the connection is authenticated against
(one bounded read-only query per batch of ids, ten evidence classes, only `Verified` licensing anything), and the
association it produces is previewed with what changes and what cannot, bound to the exact preview the operator
confirmed, refused on any weaker answer, inert when repeated and undoable from the `watchlists.json.bak` the save
keeps. **Re-keying a stored list stays deferred in this build**: the plugin cannot yet resolve a qualified scope when
it loads, lists and matches saved watchlists, so a re-keyed 59-member list would name an identity its own monitoring
path cannot match. `ScopeResolutionSupport` states that deferral and names AM-44.3 as the checkpoint that lifts it, and
neither adding a flow nor importing a solution can attach a qualified scope to an unverified list. 684 Core unit tests
(52 new), the smoke run and the `net48` Release build are green on Windows 11; every file case ran on an isolated copy
under `%TEMP%` and the operator's `watchlists.json` is untouched. **AM-44.2 is deployed on the primary host**
(2026-11-15 15:53, ProductVersion `0.1.0+5503270…`, host closed, previous copies kept in
`PluginBackups\AutomationMonitor\20261115-155339`); the operator's `watchlists.json` re-hashed byte-identical
afterwards (`28114FEA…`, 22899 bytes, modification time unchanged). Section 3 item 9 stays **open** until the
maintainer reports the saved-list check on that build; no AM-44.3 code was written to produce that deployment.
AM-44.3 is unstarted.

Earlier (2026-11-15): **AM-43 recorded as a design decision, documentation only.** The canonical Dataverse
resource scope, the four identity states that keep "recognized", "connected" and "associated" apart, the safeguards on
attaching an identity to a legacy unscoped list, and the condition on which `schemaVersion` 1 stays unchanged are now
written up in `docs/provider-capabilities.md` and tested against acceptance criteria AC-25 to AC-31 in
`docs/specification.md`. No stored identity had moved and the operator's `watchlists.json` was untouched when that
pass closed. Implementation belongs to AM-44, in the order section 6 now records.

Earlier (2026-11-15): AM-40, the defect the first live save-and-reopen exposed: a persisted watchlist
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
| Environment identity and scope (AM-41 to AM-44) | AM-41 investigated, AM-42 implemented, deployed **and reported by the maintainer**, AM-43 designed, **AM-44.1 and AM-44.2 implemented**, AM-44.2 **deployed 2026-11-15 15:53** (the scope contract, the read-loss refusal, the verification read and the association preview, with applying an association gated off). No stored identity has moved | Core xUnit suite plus the smoke `PASS` on **Windows 11**, `net48` Release 0W/0E. The identity read's runtime gate (section 3, item 8) is **closed for this connection**: neither environment-id source stated an identifier, `WhoAmIRequest` succeeded and `WhoAmIResponse.OrganizationId` arrived with a valid GUID shape. The saved-list gate (section 3, item 9) is **open**: the build is deployed and the operator's file is proven unchanged across that deployment, but the observation is still owed |
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
8. for the AM-42 connection-identity diagnostic, the same copyable window reached the same way: press the identity
   action with the intended connection already open and paste back the whole "result" block. It reports presence,
   identifier **shape** and one same-kind comparison for the connection record's environment id, the already-connected
   client's environment id and `WhoAmIResponse.OrganizationId`, so it is safe to paste verbatim and cannot leak a
   value. What is wanted is which of the three is populated and whether the two environment-id sources agree.
   **Satisfied for this connection on 2026-11-15:** the maintainer ran the build deployed that day
   (ProductVersion `0.1.0+729f485…`) and reported the block at 11:06:15 UTC. `ConnectionDetail.EnvironmentId`:
   *not stated*. `CrmServiceClient.EnvironmentId`, read from the service the host already connected: *not stated* -
   so the connector's own value is empty here too, which is the answer AM-41 left open and confirms the type test
   reached the client rather than failing it. `WhoAmIRequest`: **succeeded**. `WhoAmIResponse.OrganizationId`:
   **present, valid GUID shape**. Comparison of the two environment-id sources: **not comparable**, because neither
   supplied an identifier. Nothing was written and no settings file changed during the read. One consequence is
   recorded rather than acted on: on this connection only the organization id can ever name a resource scope, so a
   later checkpoint that qualifies a scope must read it through a value-carrying read-only action - the probe
   deliberately cannot hold a value. Item 4 (layout at 100/150/200 percent, keyboard navigation) was not reported and
   stays open for this build too. The probe does not need re-running: a new run would need a new technical
   requirement, such as a different connection type or an on-premises or ADX environment, neither of which is
   concluded about by this single observation.
9. for the AM-44.2 saved-list check, the same copyable window: pick a saved watchlist in the **Saved** box, press
   "Verify saved list (diagnostic)" and paste back the whole block. It carries the connected organization as a
   fingerprint, the verdict of the bounded identity read, the counts (members considered, ids asked about, found, not
   found, rows with no usable id, reads / pages / rows) and the association preview with its decision - by construction
   no workflow id, no organization value, no flow name and no provider message, so it is safe to paste verbatim. What
   is wanted: how many stored ids the organization confirmed, and that the decision line reads
   `DEFERRED ... AM-44.3` on this build (a `REFUSED` line is also a correct answer and names its blocker). The action
   writes nothing and the plugin offers no way to apply the association yet, so the file must be unchanged afterwards;
   report too where the third diagnostic button sits at 100 / 150 / 200 percent scaling and whether Tab reaches it in
   row order, which is item 4 for this build.
   **Deployed for this gate on 2026-11-15 15:53 and NOT yet satisfied.** The build is ProductVersion
   `0.1.0+5503270…` (commit `5503270`, Release 0W/0E on Windows 11 after 684 unit tests and both smoke `PASS` lines),
   installed by `scripts\deploy.ps1` into `C:\Users\Stephan\Desktop\XrmToolBox` with storage root
   `%APPDATA%\MscrmTools\XrmToolBox` while no `XrmToolBox*` process was running; the replaced copies went to
   `PluginBackups\AutomationMonitor\20261115-155339`. `Plugins\AutomationMonitor.Plugin.dll` 67584 B sha256
   `B446776B…` and `Plugins\AutomationMonitor.Core.dll` 230912 B sha256 `43F66E32…` are byte-identical to the build
   output, and the operator's `watchlists.json` re-hashed afterwards as `28114FEA…` (22899 B, modification time
   unchanged) with no `.bak`, `.new` or quarantined file appearing beside it. The deployed assemblies carry the action
   (`Verify saved list (diagnostic)`, `DataverseWorkflowIdentitySource`, `VerifySavedList`), and the deployed Core
   answers `Shipped.CanReassignStoredScopes = False` with `RequiredCheckpoint = AM-44.3` when it is read back by
   reflection. **What is still owed is only the maintainer's observation**: the counts the environment returned, the
   decision line, where the third diagnostic button sits at 100 / 150 / 200 percent, and whether Tab reaches it.

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
| AM-42 | Read-only identity probe of the active connection: for each candidate, report presence and shape - never the value. Candidates: `ConnectionDetail.EnvironmentId`, `CrmServiceClient.EnvironmentId` (read from the client the host already connected — never through `ConnectionDetail.ServiceClient`, whose getter can construct a client and start a sign-in), `TenantId`, `OrganizationUrlName`, `OrganizationFriendlyName`, `ConnectionId`, and `WhoAmIResponse.OrganizationId` over the `IOrganizationService` the host already provides. Compare availability and consistency; state whether a Power Platform environment id is actually present at runtime. | Windows gate. It must not write or migrate any settings file, must not alter any `WorkflowKey`, and must not open or create a connection merely to read a property. Output is shape only (populated / kind / length / equality), so no tenant identifier can reach a log, an export or a commit | **Implemented, deployed 2026-11-15 and reported by the maintainer the same day (11:06:15 UTC) - the runtime gate at section 3, item 8 is closed for this connection.** Three sources are measured, not seven: the connection record's environment id, the connector client's environment id read from the service the host already connected, and `WhoAmIResponse.OrganizationId` from one read-only `WhoAmIRequest` on that same service. `TenantId`, `ConnectionId`, `OrganizationUrlName` and `OrganizationFriendlyName` are deliberately not probed - AM-41 settled that none of them can name an environment, so their shape answers no question AM-43 has to ask. The answer is shown in the existing copyable window as presence, identifier shape and one same-kind comparison; the two identity kinds are never compared with each other. 571 Core unit tests, the smoke test and the `net48` Release build are green on Windows 11. No scope, stored member key, settings file or run-history query is touched by it. **Reported shapes, no values:** connection-record environment id *not stated*; connector-client environment id *not stated* (the type test reached the client, so this connection genuinely carries no Power Platform environment id); `WhoAmIRequest` succeeded; `WhoAmIResponse.OrganizationId` present with a valid GUID shape; the two environment-id sources *not comparable* because neither supplied an identifier. One connection type only - on-premises and ADX stay unrecorded |
| AM-43 | **Design half - done 2026-11-15, documentation only:** the canonical Dataverse resource scope (`dataverse-organization:<canonical-guid>` written into the `scopeId` and `environmentId` fields that already exist), the four states - unverified, recognized, connected, associated - that keep a parseable token from being called proof, the safeguards on attaching an identity to a legacy unscoped list, the rule that an uninterpretable scope is preserved rather than dropped, and the condition on which `schemaVersion` stays 1. **Build half - later:** multi-environment watchlists: one list holding workflows from several Dataverse environments, using XrmToolBox's multi-connection facilities, with per-environment coverage so one unreachable environment degrades its own source and never discards the others | Design: the contract recorded in `docs/provider-capabilities.md` and acceptance criteria AC-25 to AC-31 in `docs/specification.md`; no code, no file, no schema change in this half. Build: Core unit tests over faked per-environment sources; Windows gate with two environments | **Design done 2026-11-15.** Build later, after AM-44.1 to 44.3, and it needs a second environment to prove. Whether one list may hold several organizations, how the Saved list filters such a list, and how it serializes stay **open** decisions of that build, not approved here |
| AM-44 | Give the operator an explicit, evidence-backed way to attach a recognized identity to a list saved while its scope was empty - and only after that, let derivation produce one. Sequencing approved 2026-11-15: this track lands **before** multi-environment connection management, because all three of its checkpoints are provable with a single environment while AM-43's build is not | See AM-44.1 to 44.3 below | **AM-44.1 done 2026-11-15; AM-44.2 is next.** AM-44.3's wait on AM-42's reported answer is closed - the maintainer reported that read on 2026-11-15 - so nothing blocks the rest of the track but its own order |
| AM-44.1 | The identity contract with **no behaviour change**: the scope token grammar and its canonicalisation (a qualified scope must collapse GUID spellings the way `CanonicalId.Identifier` does for workflow ids - `CanonicalId.Text`, applied to the scope today, only trims and lower-cases); a display label resolved without ever keying on a name or url; the parsed scope exposed to `Watchlist` and `RunKey` consumers; a scope this build cannot interpret preserved on save; and the unknown-`provider` member replaced by a refusal to modify rather than a silent skip. **Writes no value: every stored scope stays empty** | xUnit over a populated legacy fixture - grammar, GUID spellings, unknown-scope preservation, unscoped-vs-scoped inequality, label wording - plus the **previous-build round-trip proof** of AC-30 in a scratch `git worktree`: if the older build parses a qualified scope and rewrites or loses it, stop and revisit versioning before anything writes. Core build, whole smoke run, `net48` Release 0W/0E; Windows gate for how the label renders | **Done 2026-11-15.** 632 Core unit tests, the smoke run and the `net48` Release build (0 errors) are green on Windows 11, and the previous-build round trip passed, so `schemaVersion` stays 1. How the label renders inside XrmToolBox is the one part still waiting on section 3, item 4 |
| AM-44.2 | The operator action, single-environment: read the connected organization through the read-only seam AM-42 proved, verify the stored member ids with a **completed** read, show a preview that says what changes and what cannot, apply only on explicit confirmation, and move that environment's active selection in the same write. Refuse on partial, unauthorized, unavailable or unresolved evidence, on a list already associated with a different organization, and when the connection reports no organization id. A workflow-id hit is evidence about that id, never proof of where the list came from - a solution import preserves ids - and the 59-member list is never migrated automatically | **Done 2026-11-15 - Core logic proved, `net48` build clean, applying it gated off.** `DataverseWorkflowIdentityProbe` (bounded: 20 ids per read, 12 reads, 4 pages per read, 500 rows, one projected column, never distinct) over `IWorkflowIdentityRowSource`, ten evidence classes of which only `Verified` licenses anything, `WatchlistAssociationPlanner` preview, `WatchlistManager.AssociateScope` bound to the exact preview that was shown, refusals for weak evidence / another organization / any member whose scope is already recorded / a file this build cannot write back, inert re-apply, selection and environment profile moved in one write, recovery proven from `watchlists.json.bak`. **The maintainer's sequencing rule is implemented as `ScopeResolutionSupport.Shipped`: re-keying a stored list is deferred until AM-44.3 can resolve the qualified scope on load and match, and neither adding a flow nor importing a solution may attach a qualified scope to an unverified list.** 684 Core unit tests (52 new), the smoke run and the Release build green on Windows 11; every file case ran against an isolated copy of the 59-member legacy shape under `%TEMP%`, never the operator's file | The maintainer's run of the read-only action (section 3, item 9), including item 4 for the third diagnostic button. **Deployed for that run on 2026-11-15 15:53 as ProductVersion `0.1.0+5503270…` with the operator's `watchlists.json` proven byte-identical across the deployment; the run has been requested and its report is pending, so this gate is NOT closed.** Nothing else on this checkpoint stays open |
| AM-44.3 | **Integration requirements (what has to change, in this order).** (1) Resolve the connected scope: the plugin derives its environment scope from the organization id `DataverseOrganizationIdentity` already reads - `AutomationMonitorControl.EnvironmentScope` and the gateway's scope for discovered flows - and never from `ConnectionDetail.EnvironmentId`, which AM-42 measured as *not stated* on this connection. (2) Derive qualified identities: discovery produces `WorkflowKey`s whose scope is `dataverse-organization:<canonical-guid>`; the run-history query keeps filtering on the text `workflowid` column it was proven against. (3) Match stored against connected scope: `WatchlistCatalog.ForEnvironment` / `ActiveWatchlistFor`, `Watchlist.FindMember`, `SetIncluded` / `RemoveWorkflow` / `AddWorkflow`, `MembershipReconciler` and preference/profile lookup resolve a stored qualified scope against the connected one instead of relying on textual equality alone, and a stored member whose scope is *unverified* stays visible and is reported as **not matched by this read**, never as "no longer in the environment". (4) Presentation: the Environment column and the watchlist lines show a friendly environment label beside the fingerprint; a display name, URL name or friendly name never enters a `WorkflowKey`, a `RunKey`, a merge, a cache key or a query filter. (5) Only then may `ScopeResolutionSupport` change from `Shipped` to `Resolvable()`, and the same commit must update the test that pins the shipped answer plus AM-44.2's file-level deferral cases. Adding a flow and importing a solution stay gated as AM-44.2 left them. **(6) One connection per action, and no race.** Every asynchronous action captures the service and the organization identity it started against and refuses to publish a result once the connection has changed or closed, so a discovery, a verification read or an association can never resolve against an environment other than the one the operator was looking at; a switch or a disconnect mid-operation is cancelled and reported, never silently re-scoped, and no second source of connected scope may appear beside the one named in (1). **(7) Legacy unscoped lists stay visible and reachable.** A stored list whose members carry no scope, and an unscoped member inside a list that does, must still appear in the Saved box, still open, still save, and still be reported as not matched by this read rather than as gone - including at 100, 150 and 200 percent scaling and under keyboard navigation. **(8) The explicit association is the last thing enabled.** No apply path may be reachable from the UI until (1) to (3) and (6) are all in place and proven by the regression suite below; until then `ScopeResolutionSupport.Shipped` stays exactly as AM-44.2 left it, and a display name stays presentation in every view | **Acceptance criteria.** AC-32 to AC-37 in `docs/specification.md` all pass; a stored list qualified to the connected organization is listed, opened, monitored and matched (a regression suite that would fail if step 3 were skipped - that is the gate this checkpoint exists to close); an unscoped stored member is reported as unmatched rather than absent; no path re-keys a stored list without the operator's confirmed association (no silent re-key in reconciliation or de-duplication); a test asserts a display name cannot reach any identity key; the friendly label is asserted as wording only; the AM-44.1 previous-build compatibility harness is re-run against whatever commit is then the previous release and must pass **before** the shipped path writes its first qualified scope, otherwise stop and revisit versioning; then the Windows gate - the maintainer re-opens their 59-member list, sees it in the Saved box with its members intact, discovers flows that carry the qualified scope, and reports the environment column, the scaling at 100/150/200 percent and keyboard navigation | Next; the only checkpoint allowed to lift the deferral. Its wait on AM-42 closed when that read was recorded on 2026-11-15 |
| AM-45 | Independent Azure connection contexts, so a Logic Apps or APIM source is scoped to its own tenant and subscription instead of inheriting the Dataverse connection's identity | Spike, then provider contract tests | Later, with WF-C |

Five rules come out of AM-41, AM-42 and AM-43 and bind every checkpoint above.

- **A Power Platform environment id and a Dataverse organization id are different identifiers.**
  `WhoAmIResponse.OrganizationId` is the documented source of the organization identity and it is not the
  environment id; `CrmServiceClient.EnvironmentId` is documented as the Power Platform environment id. Both
  are preserved when both are known, each under its own identity kind, and one is never written into the
  other's field.
- **A connection is not an environment.** `ConnectionId` names one saved connection record, so two records
  for one environment yield two values and re-creating a record yields a new one. Tenant id, friendly name,
  URL name and service URL are context and display, never authoritative resource identity.
- **A scope that parses is not a scope that is confirmed.** Unverified, recognized, connected and associated are
  four different claims, and only a completed verification read plus explicit operator confirmation may change
  stored identity. No wording may call a recognized token verified, and no existence check on a workflow id proves
  where a list came from - a solution import preserves ids across environments.
- **Content a build cannot interpret is preserved, not dropped.** A member whose scope text is unknown to the
  reader survives that build's next save unchanged, or the build refuses to modify that list. The same rule now applies
  to an unknown `provider` token, which today is preserved on read and blocks the save instead of vanishing on it.
- **Association is never automatic.** Adding a flow whose scope is known to a list whose scope is not must not
  re-key the list; same-scope handling stays as it is, and moving from unverified to a named scope happens only
  through the confirmed action of AM-44.2 - which is itself deferred until AM-44.3 can resolve a qualified scope when
  it loads, lists and matches saved watchlists, because a stored list that names an identity the monitoring path
  cannot match is a list the operator has lost. Storing a qualified scope is a change of value inside existing fields and
  keeps `schemaVersion` 1 **only while the previous build's read-and-write round trip is proven** — that proof ran and
  passed on 2026-11-15, so version 1 stands and no new field may be added under it.

AM-44.1's compatibility proof was executed, not reasoned about: a scratch `git worktree` at the previous commit
(`be8ea8b`) plus a harness kept outside the repository that generates populated version-1 fixtures with this build and
opens them with the previous build's own persistence code. It covers a file whose six scope values mix the canonical
qualified form, an unknown kind, a damaged value and an empty scope; the real legacy shape (59 unscoped members whose
empty scopes stayed empty, keeping 45 included, 14 excluded, 8 hand-added provenances, its active selection, its
environment profile and its preferences); and a small mixed-spelling list. Every scope value came back and was written
back verbatim and `schemaVersion` stayed 1; the only differences between the bytes read and the bytes re-written were
`savedAtUtc` and `writtenBy`, which move on any save, so the evidence is the per-value checks rather than a hash. The
method is not production code and it is not AM-44.2's gate; any checkpoint that would let the shipped path write a
scope value re-runs it against whatever commit is then the previous release, and a failed round trip means stop and
revisit versioning rather than code around it. AM-44.2 landed on 2026-11-15 with its shipped path writing no qualified
scope at all - applying an association is deferred - so that harness did not need re-running for it; AM-44.3, which
does flip derivation, must re-run it before its first write. AM-43's multi-environment membership still waits
on AM-43.3 — what a Consumption connector really propagates — and AM-43.5, the second-environment proof, which needs
two environments to measure; AM-44.3 no longer waits on AM-42, whose identity read was recorded on 2026-11-15.

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
5. WF-F: AM-42 is implemented, deployed and **reported by the maintainer** (section 3, item 8) - three identifier
   sources, shape only. AM-43's **design** half is done and recorded in `docs/provider-capabilities.md`, AM-44.1
   (the scope contract, which writes no value) is implemented with its previous-build round-trip proof passed, and
   AM-44.2 (the verification read, the evidence classes and the operator-confirmed association) is implemented with
   **applying it deferred** - `ScopeResolutionSupport.Shipped` refuses to re-key a stored list, because this build
   cannot yet resolve a qualified scope when it loads and matches watchlists. The order the maintainer approved on
   2026-11-15 therefore continues with AM-44.3, the only checkpoint allowed to lift that deferral: derivation produces
   qualified scopes, the saved-watchlist path resolves and matches them, a friendly environment label appears as
   presentation, and adding a flow still never associates a list. Only then AM-43's build - multi-environment membership and
   query fan-out, which needs a second environment to prove - followed by AM-45's independent Azure contexts. The
   single-environment identity work therefore precedes multi-environment connection management: every AM-44
   checkpoint is provable with one environment and AM-43's build is not. Now that the AM-42 identity read is recorded,
   no checkpoint may move a stored
   `WorkflowKey.ScopeId` before its own gate has passed, because changing scope
   derivation re-keys every stored member.
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
  connected. A stored scope names the **kind** of identifier it holds (`dataverse-organization:<canonical-guid>`
  today), because two GUIDs of different kinds look identical and mean different things - and a scope that merely
  parses is *recognized*, never *verified*. Labels for humans come from display context and never from the key;
  the key never comes from a label.

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

### AM-43 identity and migration design (2026-11-15)

- Documentation only. No `ResourceScope` type, no re-keyed member, no change to any `watchlists.json`, no new UI
  action, no multi-connection code, no schema change, no new dependency.
- No scope derivation change and no association code. The token exists in these pages, not in a file: AM-44.1 to
  44.3 own the first write, and AM-44.1 owns the compatibility proof that decides whether it stays that way.
- No multi-environment membership, per-scope catalogue filtering or multi-environment serialization decision. The
  capability stays mandatory and the token makes it expressible; approving the token approves none of that.
- No further hunt for a Power Platform environment id, no admin-plane call and no new endpoint of any kind.
- No push, merge, tag, release or deploy, and no CI run read.

### Recorded for the 2026-11-12 discovery pass

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
