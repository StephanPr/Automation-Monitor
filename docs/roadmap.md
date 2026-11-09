# Automation Monitor - roadmap

**This file is the authoritative roadmap.** It is version-controlled on purpose, so the Windows and
Arch Linux machines stay synchronized. Milestone state, the active work track, the sequence and the
acceptance gates all live here. `docs/specification.md` owns product behaviour,
`docs/distribution.md` owns packaging and release rules, and `docs/provider-capabilities.md` owns
verified source facts. The maintainer-local `docs/development-plan.md` keeps the original planning
narrative and estimates, and defers to this file wherever sequence or a gate is concerned.

Last updated: 2026-11-09 - Windows field report triage, dual-host workflow adopted, WF track opened.
**No implementation was performed in that pass**; every WF checkpoint below is unstarted.

## 1. Where the project stands

| Area | State | Evidence layer |
|---|---|---|
| Core models, normalization, merge, dedupe, filters, correlation | Implemented and covered | 317 xUnit tests + smoke test, executed on Linux |
| M1 solution and cloud-flow discovery | Implemented in core and gateway; **reported failing in XrmToolBox on Windows** (WF-A) | Core logic verified; the gateway mapping was never unit tested |
| M3 run history (model, provider, merge, correlation) | Implemented in core and gateway; no UI caller | Core layer only; never executed against a tenant |
| M4 saved watchlists and settings | Core implemented (`a23f216`); plugin storage root and Watchlists view **paused** | Core layer only, against an in-memory file seam |
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
2. the XrmToolBox version and storage root used;
3. the action taken and the behaviour observed, including coverage and error wording;
4. layout at 100 / 150 / 200 percent scaling and keyboard navigation;
5. anything that could not be exercised, named as unverified rather than assumed.

The same gate is the definition of done for the WF checkpoints marked **Windows gate** below.

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

### 4.2 The discovery failure: two defects, one trigger

Both remain a **hypothesis with a local proof path** until WF-A.1 and WF-A.4 close. Nothing
has been reproduced or fixed yet.

1. **Trigger.** `DataverseGateway.Unwrap` obtains every attribute by asking the SDK accessor
   for an AliasedValue (the generic getter parameterised with that type). The solution query
   has no joined entity, so every attribute arrives as a plain root value. If the SDK build the
   host loads casts instead of type-testing, the first solution row throws exactly the reported
   message. The code is identical on `eb6bf1c` and on the current branch, so the defect is live
   on both. That version-sensitive assumption is what must be measured, not argued: WF-A.1
   tests it against the very assembly the host loads, with no tenant required.
2. **Misclassification.** `DataverseSolutionFlowSource.Fail` passes the exception to
   `ProviderErrorClassifier`, which carries no marker for a failure inside this tool, so it
   falls through to `Unavailable` and the operator is told the environment did not answer and
   should be re-checked. A local defect must never read as the environment being down: the
   coverage model stays, the failure category and the wording become accurate.

### 4.3 Phase verdicts against the current branch

| Reported phase | Verdict | Why |
|---|---|---|
| A - discovery cast | **Still fully valid** | Neither the gateway mapping nor the classifier changed after `eb6bf1c` |
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

### 5.2 WF-A - make discovery work on the real host

| ID | Scope | Host and gate |
|---|---|---|
| WF-A.1 | Probe the SDK behaviour directly: load the Microsoft.Xrm.Sdk.dll that XrmToolBox actually loads and call the aliased-value accessor with AliasedValue on an Entity whose attributes hold plain strings. Record whether it throws or returns null. No tenant needed. | Windows, net48; the durable fact goes in docs/provider-capabilities.md |
| WF-A.2 | **Simple fix (chosen assumption).** Keep the type-aware unwrapping inside DataverseGateway: accept an AliasedValue, a plain value or null instead of forcing AliasedValue, for the solution mapping and the workflow-connection mapping alike. The richer Core-side row model that would make the gateway directly testable is a separate decision: it changes a seam every provider uses and belongs with WF-C, not with a bug fix. | Core compile and unit suite stay green; gateway compiles; Windows gate on discovery |
| WF-A.3 | Mark local mapping and normalization failures so ProviderErrorClassifier returns Unsupported with a message naming this tool; only a request that left the process may read as Unavailable. Keep the coverage model and the separate last-checked timestamp. | New unit theory cases in the existing classification file |
| WF-A.4 | Reproduce first: discovery fails at WF-A.1 or in XrmToolBox before the fix, and the same action succeeds after it. Record both results and the build identity of each DLL. | Windows gate |

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

## 6. Sequence, dependencies and what stays paused

1. WF-0 host parity: nothing on Windows can be measured before it.
2. WF-A: discovery must work on the real host before anything else is visible there.
3. WF-B.1 spike, then WF-B.2 to B.5.
4. Resume M4: C5 plugin storage root and the disk ISettingsFileAccess, C6 the Watchlists view,
   tab host and reconciliation preview, C7 the full gate with an unchanged package audit, C8 the
   documentation inventory.
5. WF-D: a dual WPF evaluation, which needs a working discovery path to have real rows to show.
6. WF-E design direction, then WF-C Azure discovery, which is independent of the UI decision but
   obeys the same spike-before-code rule.

M4 is paused, not obsolete. It is committed at `a23f216`, green at the core layer, and its open
checkpoints and contract changes carry forward unchanged: the settings kind discriminator,
Clear moving content to a displaced file instead of deleting it, the store notes on
WatchlistStoreResult, and a serializer that refuses a payload which is not exactly one object.

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
- WF-D ends in a recommendation and a measurement, never in a converted view.

## 8. Explicitly out of scope for this pass

- No WF-A to WF-E code, and nothing built or tested: the .NET 10 SDK is not yet installed on the
  primary host, so even the existing gates were not re-run here.
- No Pester or PowerShell tooling on either workstation, and no CI workflow run or read.
- No push, merge, tag, release or history rewrite. The four local commits stay local and the
  branch stays paused at `a23f216`.
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
