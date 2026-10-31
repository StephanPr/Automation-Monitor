# Provider capabilities and verified facts

What each source really supports, how current its data is, and what still has to be confirmed
against a live environment. Every claim below names its evidence. Product behaviour lives in
`docs/specification.md`; packaging, installers, CI and releases in `docs/distribution.md`.

## Dataverse / Power Automate (solution-aware cloud flow discovery) — M1

| Fact | Value | Evidence | Status |
|---|---|---|---|
| `solutioncomponent.componenttype` for a Workflow component | `29` | Microsoft Learn, Solution component table reference | Verified against documentation; **not yet executed** against a tenant |
| `workflow.category` for a modern cloud flow | `5` | Microsoft Learn workflow entity reference + spec | Same: documented, spike pending |
| SDK enum for either value | none | Reflection over `Microsoft.Xrm.Sdk` 9.0.2.2 and `XrmToolBox.Extensibility` 1.2025.10.74 | They are named constants in `DataverseSchema`, not enums |
| `workflow.statecode` | `0` disabled, `1` enabled | Platform option-set metadata | Normalized to `WorkflowState`; raw code always retained |
| Environment id for identity scope | `ConnectionDetail.EnvironmentId` (string) | Reflection over `McTools.Xrm.Connection` 1.2025.9.64 | Used as the scope half of `WorkflowKey`; null scope stays a distinct key |
| Paging signal | `EntityCollection.MoreRecords` + `PagingCookie` | Reflection over `Microsoft.Xrm.Sdk` | Followed by `PageReader`; a stopped read is `Partial` |

### Query shape actually issued (read-only)

- Solutions: `QueryExpression("solution")` — no criteria (everything the user can read), ordered
  by `friendlyname`, paged at 250 rows.
- Flows of one solution: `QueryExpression("workflow")` with an inner `LinkEntity` to
  `solutioncomponent` on `workflow.workflowid = solutioncomponent.objectid`, filtered by
  `componenttype = 29` and `solutionid = <selected>`, `Distinct = true`, paged at 250 rows.
  `category` is deliberately **not** filtered so the excluded classic workflows can be counted
  and reported instead of disappearing.
- Only `IOrganizationService.RetrieveMultiple` is called anywhere in the plugin.

### Coverage limits this slice must keep admitting

- Solution scope only: cloud flows that belong to no solution are not discoverable through this
  path. A personal or solution-less flow is invisible here, and the empty grid must say so.
- A read that stops early (ceiling, stalled continuation, missing page) is `Partial` and stays
  labelled in the grid status line, the watchlist summary and any future export.
- `Unauthorized` and `Unsupported` are distinct from "no flows" and are never rendered as an
  empty list.
- Discovery is live and in-process: nothing is cached, so "checked at" is when the button was
  pressed, and source freshness is whatever Dataverse holds at that instant.

### Spike checklist for the first Windows session (M1 exit criteria)

1. Confirm the solution query returns the expected solutions (managed and unmanaged) and that
   `ismanaged`, `version` and `uniquename` are populated.
2. Confirm one known solution returns its cloud flows, that the count matches make.powerautomate.com,
   and that classic workflows / Business Process Flows in the same solution are reported as skipped
   rather than shown or hidden silently.
3. Confirm paging: a solution with more than 250 workflow-component rows advances via
   `PagingCookie` and never loops or duplicates rows.
4. Confirm a connection without `prvReadSolutionComponent` produces `Unauthorized` with the
   security-role advice, not an empty grid.
5. Record whether `ConnectionDetail.EnvironmentId` is populated for every connection type
   (Office 365, on-premises, ADX); when it is not, flows are keyed unscoped and that must be
   visible in the UI.

## Power Automate run history (`flowrun`) — M3

Run history for solution-aware cloud flows is replicated into the Dataverse `flowrun` table, and
`DataverseFlowRunSource` reads it. Every fact below comes from the Microsoft Learn FlowRun entity
reference and the run-history article, retrieved 2026-10-31. **None of it has been executed against
a tenant from this host**: the code and its tests are verified, the round trip is not.

| Fact | Value | Evidence | Status |
|---|---|---|---|
| Entity holding executions | `flowrun` | FlowRun entity reference | Documented; query not yet executed |
| Row id | `flowrunid`, **Uniqueidentifier** (primary id) | Same | kept as `WorkflowRun.RunRecordId` |
| Run identifier | `name`, String(100) — the Logic Apps run name of the execution | Same, plus the run-history article | Chosen as `WorkflowRun.RunId` because `parentrunid` points at this value |
| Parent link | `parentrunid`, String(100) | Same | the only parent/child evidence used |
| Tracking id | `clienttrackingid`, String(255) | Same | becomes `WorkflowRun.CorrelationId`; there is **no** `correlationid` column |
| Owning flow | `workflowid`, String(100); a `workflow` Lookup also exists | Same | server-side filter; a row naming another flow is refused, not merged |
| Flow display name | not a `flowrun` column (`flowname` does not exist) | Column-list scan of the entity reference | the name comes from the request scope; without it the row reads `(unnamed workflow)` |
| Duration | `duration`, **BigInt**, milliseconds (schema name `DurationInMs`) | Same | **not seconds**; read type-tolerantly as `long`, never rescaled |
| Status | `status`, String(100), RequiredLevel **None** | Same | free text; absent or unrecognised becomes `RunStatus.Unknown` and stays visible |
| Error | `errorcode` String(100); `errormessage` Memo(20000) | Same | error text is untrusted and is truncated to 2000 chars in memory |
| Primary-run marker | `isprimary`, **Picklist** (0 false, 1 true) — not a Boolean | Same | `DataverseRunNormalizer.NormalizePrimaryFlag`; an unrecognised code stays `null` |
| Trigger kind | `triggertype`, String(100) | Same | carried verbatim |
| Timestamps | `starttime` / `endtime`, DateTime with `DateTimeBehavior` **UserLocal** | Same | stored as UTC instants; see the window widening below |
| Retention | `organization.flowruntimetoliveinseconds`, Integer, default 2 419 200 s (28 days) | Organization entity reference | not yet surfaced in the UI; a window past retention simply returns fewer rows |
| Table type | **Elastic** table, partitioned per user (20 GB partitions), non-transactional ingestion | Run-history article | limits what "complete" can mean; see below |

### Query shape actually issued (read-only)

`QueryExpression("flowrun")` with `ColumnSet(DataverseSchema.RunAttributes)` — one declaration of the
column list, pinned by a test so the query and the normalizer cannot drift — filtered by
`workflowid = <flow>`, `starttime` inside the window, and optionally an `Or` filter of provider status
spellings translated from the requested outcome groups by `StatusNormalizer.ProviderValuesFor`.
Ordered by `starttime` descending, paged at 250 rows through the shared `PageReader`. Only
`IOrganizationService.RetrieveMultiple` is called.

Because `starttime` is declared `UserLocal`, a bound expressed in UTC may be interpreted in the
connecting user's zone. The gateway therefore sends the window widened by ±1 day
(`RunQueryCriteria.ServerFromUtc` / `ServerToUtc`) and `RunQueryService` re-applies the exact
half-open `[from, to)` window to every row. A widened read costs a few discarded rows; a narrowed one
would silently hide runs, which is the asymmetry that decision is based on.

### Coverage limits this milestone must keep admitting

- **Solution-aware flows only.** A flow outside a solution has no `flowrun` rows, and run history is
  not written for every flow regardless.
- Runs are ingested into the **current user's logical partition** and the stream is not
  transactional: skipped runs are reported in `FlowEvent`, not `FlowRun`. A shorter list than the
  portal's is expected behaviour, not a completeness claim this plugin can make.
- A run is **not stored at all** when the flow owner lacks `FlowRun` read permission, so a gap can be
  someone else's permission problem.
- Sorting and `PagingCookie` behaviour on an Elastic table are unverified against a tenant. The core
  re-orders rows itself and a stopped read is `Partial`, so a stalled continuation costs rows visibly
  instead of looping.
- One `IWorkflowRunSource` call is one workflow, and `RunQueryService` reads workflows
  **sequentially**: the connected `IOrganizationService` belongs to one host connection, so parallel
  fan-out over it is not a safe optimisation. Bounded concurrency arrives with the refresh
  coordinator and must reuse the merge rules here, never a second copy of them.
- `Unsupported`, `Unauthorized`, `Unavailable` and complete-but-empty are four different answers
  produced by four different paths in `DataverseFlowRunSource`.
- Correlation is **evidence only**: a link exists when a retrieved run's `name` equals another
  retrieved run's `parentrunid`. Similar names, near-identical start times and a shared
  `clienttrackingid` create no link. A parent id that resolves to nothing is reported unresolved; an
  id claimed by two retrieved runs is reported ambiguous and left unlinked; a parent cycle is
  reported and its members surface as roots. Nothing is walked recursively.
- `RunSummary` counts runs. A connector-action or transaction total cannot be derived from run rows
  and is never implied by `RunTree.DescendantCount` or `TotalDurationMs`.

### Spike checklist for the first Windows session (M3 exit criteria)

1. Pick one solution-aware cloud flow from discovery and confirm a known failed run appears with the
   same run id and outcome make.powerautomate.com shows. That is the POC gate's "a real failed run
   can be found and its correct workflow/ID displayed".
2. Confirm `duration` really is milliseconds by comparing one run against the portal's rendered
   duration. A factor-of-1000 disagreement means the schema note is wrong, not the test.
3. Confirm paging on the Elastic table: a flow with more than 250 runs in the window must advance by
   `PagingCookie` once per page, never loop and never duplicate a run id.
4. Confirm the `starttime` window behaves as expected when the connecting user's zone is not UTC: a
   run just outside the requested window must not appear and one just inside must not be missing.
   This is what the ±1 day widening plus core re-filtering exists to survive.
5. Confirm a connection without `FlowRun` read permission yields `Unauthorized` with the FlowRun
   advice rather than an empty grid, and record the exact message so `ProviderErrorClassifier` can be
   sharpened against real strings instead of guesses.
6. Record the environment's `flowruntimetoliveinseconds`. If it is 0, run ingestion is off and the
   honest answer for every flow is "no run history is stored here".
7. Compare one known interval against the portal and record the ingestion delay and any missing runs
   (M3.10). Once measured, that gap belongs in the table above.
8. Confirm a child-flow run carries `parentrunid` pointing at the parent run *name*. The whole
   correlation feature rests on that one fact.

### Power Platform Flow Runs API — assessed, deliberately not used (M3.2)

`GET {environment}/_apis/flowmanagement/flowRuns?workflowId={flow}&api-version=2024-10-01` on
`api.powerplatform.com` is documented, and its documented 404 ("no Dataverse database was found for
the environment") confirms it reads the same Dataverse-backed history rather than a second source. It
was not implemented, for reasons that are decisions rather than omissions:

- it requires its **own delegated token for the `https://api.powerplatform.com` audience and a
  registered app**; the XrmToolBox Dataverse connection does not grant it, and one credential is
  never assumed to cover the other;
- the documented request surface for this scope is `workflowId`, `api-version` and a correlation-id
  header. There is **no documented `$filter`, `$top` or `nextLink`** and no documented response body
  schema for run listing, so window filtering, paging and coverage could only be built on
  reverse-engineered portal behaviour - which `docs/specification.md` rules out;
- it would add a credential and an undocumented contract on top of a source this plugin can already
  reach with the connection the operator has.

The milestone plan makes this second provider conditional on the source decision justifying it, and
that justification never arrived.
The seam that would host it is `IWorkflowRunSource`, which is provider-agnostic, so M9/M10 can add an
authorised Azure or platform source without changing `RunQueryService`, the merge rules or the model a
future grid binds to. If a later spike documents the response shape and paging, that provider is an
addition, not a redesign.

## Logic Apps and APIM — not implemented

No Logic Apps or APIM endpoint is called yet. Their capability, freshness and retention questions must
each be answered by a spike recorded in this file before the corresponding milestone is implemented:
an unverified capability is disabled and labelled in the UI, never presented as an empty grid.
`ProviderKind.LogicAppsConsumption` and `LogicAppsStandard` already exist so run identity, keys and
merge rules fit those providers; `ProviderKinds.IsImplemented` reports them as unavailable, and
`RunQueryService` turns that into an `Unsupported` coverage row instead of asking a Power Automate
provider a question it cannot answer.


