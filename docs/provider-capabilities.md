# Provider capabilities and verified facts

What each source really supports, how current its data is, and what still has to be confirmed
against a live environment. Every claim below names its evidence. Product behaviour lives in
`docs/specification.md`; packaging, installers, CI and releases in `docs/distribution.md`.

## Dataverse / Power Automate (solution-aware cloud flow discovery) — M1

| Fact | Value | Evidence | Status |
|---|---|---|---|
| `solutioncomponent.componenttype` for a Workflow component | `29` | Microsoft Learn, Solution component table reference | Verified against documentation; **not yet executed** against a tenant |
| `workflow.category` for a modern cloud flow | `5` | Microsoft Learn workflow entity reference + spec | Still documented-only: no workflow row has ever been returned, so the selector value is unconfirmed. `CloudFlowSelectionResult.DescribeDiagnostics()` now logs the category values a real environment does send, which is what will settle it |
| SDK enum for either value | none | Reflection over `Microsoft.Xrm.Sdk` 9.0.2.2 and `XrmToolBox.Extensibility` 1.2025.10.74 | They are named constants in `DataverseSchema`, not enums |
| `workflow.statecode` | `0` disabled, `1` enabled | Platform option-set metadata | Normalized to `WorkflowState`; raw code always retained |
| Environment id for identity scope | `ConnectionDetail.EnvironmentId` (string) | Reflection over `McTools.Xrm.Connection` 1.2025.9.64 | Used as the scope half of `WorkflowKey`; null scope stays a distinct key |
| Paging signal | `EntityCollection.MoreRecords` + `PagingCookie` | Reflection over `Microsoft.Xrm.Sdk` | Followed by `PageReader`; a stopped read is `Partial` |
| Reading an attribute that is **not** an `AliasedValue` as one | `GetAttributeValue<AliasedValue>` **throws** `InvalidCastException`; `entity.Attributes[x] as AliasedValue` returns null instead | net48 harness loading the `Microsoft.Xrm.Sdk.dll` this XrmToolBox install ships (assembly 9.0.0.0, file 9.2.49.14828), 2026-11-09 | **Verified against the assembly the host loads**; still not a tenant observation |
| Absent attribute, and attribute present with a null value | both read as null; neither throws | Same harness | Verified client-side |
| A value in a shape the reader does not accept | refused: `DataverseMappingException` naming the column and the CLR type, never the value | `DataverseValue` xUnit cases + harness case E | Verified |
| A projected column the table does not have | Dataverse refuses the **whole** `RetrieveMultiple`; not one row comes back | First live session, 2026-11-09: the flow query asked for `primarycategory` and the platform answered `The 'workflow' entity doesn't contain attribute with Name = 'primarycategory' and NameMapping = 'Logical'` | **Observed live** (maintainer-reported). That column is no longer projected, and `DataverseSchemaTests` keeps it out of the list |
| The solution read against a real tenant | `complete - 975 row(s) over 4 page(s)` | First live session, 2026-11-09, after the AM-32 type-read repair | **Observed live**: the AM-32 cast defect did not return |
| `WorkflowActivity` (the table M7 needs for classic workflows) | absent in every environment tried | First live session, 2026-11-09 | **Observed live**: M7 stays blocked on an alternative source, not on our query |

### Query shape actually issued (read-only)

- Solutions: `QueryExpression("solution")` — no criteria (everything the user can read), ordered
  by `friendlyname`, paged at 250 rows.
- Flows of one solution: `QueryExpression("workflow")` with an inner `LinkEntity` to
  `solutioncomponent` on `workflow.workflowid = solutioncomponent.objectid`, filtered by
  `componenttype = 29` and `solutionid = <selected>`, paged at 250 rows. `Distinct` is **false**, and it
  has to stay that way — see "Identity" below, which is this provider's second live defect.
  `category` is deliberately **not** filtered so the excluded classic workflows can be counted
  and reported instead of disappearing.
- The flow projection is exactly `DataverseSchema.WorkflowAttributes`: eight logical names, each one read
  by a decision or by the grid. The first is `workflowid` — the table's documented primary id attribute and
  the identity of every flow — because a projection that leaves the id out makes the read depend on a
  query-shape side effect. Nothing speculative is added: an unknown column is not a missing cell, it is a
  rejected query and an empty list, as the first live session proved.
- Only `IOrganizationService.RetrieveMultiple` is called anywhere in the plugin.

### How a returned row becomes model data (measured client-side 2026-11-09)

Two stages, and the split is deliberate because the second one is what unit tests can reach:

1. **The gateway reduces SDK shapes to a plain CLR value.** `DataverseGateway.Unwrap` reads
   `entity.Attributes[attribute]` and type-tests it: a genuine `AliasedValue` yields its inner value,
   anything else is already the value. It never asks the SDK for an attribute *as* a type, because
   that accessor casts rather than tests (see the table above). `OptionSetValue` is reduced to its
   option value, which is what an option-set column states.
2. **The core decides what that value means.** `DataverseValue.AsText` / `AsInt32` / `AsBoolean` /
   `AsUtc` treat absent, null and blank as "the source did not say", convert only the shapes named by
   the schema, and raise `DataverseMappingException` for anything else. A refusal is never a blank
   cell: the read fails, the failure is classified as a defect in this build (`Unsupported`, with
   advice that says the environment *did* answer), and no partially understood row reaches the grid.

Consequence for `flowrun`: `isprimary` is a Picklist, so the numeric option is the evidence and the
boolean field stays null unless the column really arrives as a Boolean. Turning an unrecognised option
value into `true` would invent a fact the source never stated.

### Identity: which value is a flow's id (second live defect, 2026-11-09)

The rerun after the projection fix answered the query and returned rows — 59 of them for one solution —
and the grid still said `0 cloud flow(s) - 59 row(s) without an id skipped`. Nothing was wrong with the
data; the query was throwing the identity away twice over:

| Fact | Source |
|---|---|
| `workflowid` is the primary id attribute of the `workflow` table. | Microsoft Learn, workflow entity reference (`PrimaryIdAttribute`). |
| A distinct `QueryExpression` returns **no primary key values** for each record. | Microsoft Learn, `QueryExpression.Distinct` remarks: "When the Distinct property is true, the results returned don't include primary key values for each record because they represent an aggregation of all the distinct values." |
| The discovery query set `Distinct = true` **and** did not project `workflowid`; its link-entity asked for `ColumnSet(false)`, so `solutioncomponent.objectid` was not returned either. | `DataverseGateway` before this change, confirmed by reading the query side by side with the observed `Entity.Id == Guid.Empty` symptom. |

So every row arrived with an empty row id and no id column to read, `WorkflowKey.TryCreate` refused all
59, and the coverage line reported it honestly while the read itself still claimed `Complete`. The fix is
the pair: project `workflowid` (the documented primary id) and stop asking for distinct rows
(`DataverseSchema.WorkflowQueryDistinct = false`), with `WorkflowIdentity` in the core deciding the value
and `DataverseCloudFlowSelector` collapsing duplicates where the count is visible.

Three ids on this path that look interchangeable and are not:

- `workflow.workflowid` — the table's primary id, and the identity this tool keys on
  (`WorkflowKey` = provider + environment + this value).
- `workflow.workflowidunique` — a different column on the same table. Neither projected nor read, and no
  claim is made here about what it holds; it is named only so nobody substitutes it for the primary id.
- `solutioncomponent.objectid` — the component's reference to the workflow. The join
  `workflow.workflowid = solutioncomponent.objectid` matched 59 rows on a real tenant, which is the
  observed evidence that it carries the same primary id for `componenttype = 29`. It is still never used
  as identity: it is the join key, not the read.

The general rule taken from this: **never let identity depend on a query flag.** Project the documented id
column, and refuse a row rather than fall back to anything name-like or adjacent.

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

Every item below is executed by the maintainer against an environment they choose. An agent records the
answer as a fact with its evidence; it does not connect to an environment itself, and the recorded fact
names column behaviour and coverage - not tenant content.

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

#### Recorded answers — first live session, 2026-11-09 (maintainer-reported)

The session ran on Windows against an environment of the maintainer's choosing. No tenant name, url, ids
or payload were recorded; only column behaviour and coverage, which is what this file is for.

| Item | Outcome |
|---|---|
| 1 | Partly answered. The solution read succeeded end to end: `complete - 975 row(s) over 4 page(s)`, proving `solution.name`/`friendlyname` and the paging signal on a real tenant. Whether `ismanaged`, `version` and `uniquename` carry values is not yet visible in the UI (discovery shows the friendly name only). |
| 2 | **Blocked, and the cause was ours.** The flow read was answered with `The 'workflow' entity doesn't contain attribute with Name = 'primarycategory'`, so Dataverse rejected the query and no workflow row reached the model. `primarycategory` was projected but never read; it is removed, and the session must be repeated. Nothing was learned about the count matching the portal or about classic-flow exclusion. |
| 2 — rerun after that fix | The read succeeded and 59 workflow rows arrived for one solution, and **all 59 were skipped as "without an id"** — a second defect of ours, explained in "Identity" above: the query was `Distinct = true`, which Learn documents as returning no primary key values, and `workflowid` was not projected. Identity is now a projected column and the query is not distinct. **Whether those 59 rows are cloud flows, and how many are classic workflows, is still unanswered** — that needs the next session. |
| 3 | Not reached for workflows. The solution side did page (4 pages, 975 rows) without looping or duplicating. |
| 4 | Not exercised — the connection used had read access. |
| 5 | Not answered: no flow row was returned, so no scoped key was built. |

Three facts the sessions produced that no checklist item asked for: `WorkflowActivity` is absent from every
environment tried (M7's blocker); an unknown projected column fails the entire read rather than returning a
blank cell; and a distinct query silently removes the primary key, which can empty a grid while the read
still reports `Complete`. The second is why the projection is a single named list with a guard test. The
third is why identity is a projected column *and* a per-row record of where the value came from: an empty or
degraded flow list writes `DescribeDiagnostics()` to the XrmToolBox log — the projected columns, the row
count, the selector value, whether `workflowid` was projected, whether the query was distinct, where each
row's identity came from, and the distinct `category` values seen — so one session settles the identity
question and the `category = 5` question from a single log line instead of another guess.

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
| Owning flow, as text | `workflowid`, String(100), documented as "unique identifier of the workflow associated with this run" | FlowRun entity reference, re-read 2026-11-10, cross-checked against the local schema validation of the same table | The production run query filters on this column. Which of the workflow table's identifiers the text actually stores is **not documented**, and the table exposes three - so it is measured, not assumed; see "The AM-35 identity probe" below. A row naming another flow is refused, not merged |
| Owning flow, as a lookup | `workflow`, Lookup to `workflow`, RequiredLevel **None** | Same | The join the platform documents: the referenced primary key is `workflow.workflowid`, which is exactly the value discovery uses as workflow identity. **Not** projected by the monitoring query - only by the probe (`DataverseSchema.RunProbeAttributes`) - because a projected column is a contract with the platform and the production projection is already proven live |
| Flow platform kind | `modernflowtype`, Picklist `flowrun_modernflowtype`: `0` PowerAutomateFlow, `1` CopilotStudioFlow, `2` M365CopilotAgentFlow | Same, re-read 2026-11-10 | **Not projected and not filtered on.** It would not narrow this read: a flow's platform kind is a property of the flow, and discovery already selects `workflow.category = 5`. Recorded so that adding it later is a decision rather than a guess |
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

### The AM-35 identity probe (bounded diagnostic; deployed, answer owed)

`flowrun` documents two columns that each claim to name the owning flow, and only one of them can hold the
identity discovery produces. That is not a detail: the monitoring query filters on the text column, so if that
text stores a different workflow identifier than the one discovery reads, every flow shows an empty run list
while the portal shows runs - a scope failure no filtering logic could fix. The schema does not settle it, so
the spike asks the environment instead. `DataverseFlowRunProbe` (plugin side) is the whole implementation;
`FlowRunProbeDiagnostics` (core side) is the comparison and the wording.

What it does, and what bounds it:

- **Two isolated reads** of the selected flow, a seven-day window, a hard ceiling of five rows each, both
  ordered `starttime` descending and both widened by the same ±1 day the monitoring path uses, so the two
  answers differ only by the column they filtered on:
  - **A** - `flowrun.workflowid = <identity as a string>` in the canonical dashed spelling, i.e. what
    production does;
  - **B** - `flowrun.workflow = <identity as a GUID>` lookup value, i.e. the documented join.
- **Compared in memory by `flowrunid`**: rows per filter, distinct run ids per filter, and how many run ids
  came back through both. Canonicalisation is the shared `CanonicalId` rule, so one run spelled three ways is
  one run and a repeated page cannot inflate the overlap.
- **Bounded by construction.** Paging goes through the shared `PageReader` with page size = row ceiling = 5 and
  a two-page limit, deliberately **without** `TopCount`: `TopCount` makes the platform report that there are no
  more records, which would let a five-row sample claim a complete window. A read that reaches the ceiling is
  reported as a sample with more rows pending.
- **Read-only**: `RetrieveMultiple` only, one connection, no new package, no write, no second query path that
  the monitoring pipeline could accidentally share - the production criteria, normalizer and correlation
  resolver are untouched.
- **Structural output.** The report is counts, column names, CLR type names and coverage-state tokens. The
  requested identity, the run ids and the provider's error text are compared in memory and cannot reach the
  rendered line; the message is dropped where the probe catches it, keeping only the exception type name and
  the classified state. A unit test asserts that a fixture id, run name and tenant url do not survive into the
  line. The screen shows two counts; the structural detail goes to the XrmToolBox log.

What each verdict means - and does not:

| Verdict | Observation | Next step |
|---|---|---|
| Both filters return the same runs | the text column stores the primary id discovery uses; for this table the two are interchangeable | moving the production filter to the documented lookup is a separate change with its own tests |
| Only the text filter returns rows | the lookup column is not populated, or not queryable, in this environment | keep the text filter and record that the documented join is unusable here |
| Only the lookup filter returns rows | the text column holds something else - another workflow identifier, or nothing | the monitoring filter is wrong for this environment; open the fix as its own task |
| Rows both ways, none in common | something other than identity separated the two reads | investigate with the reported shapes; the probe does not explain it |
| No rows either way | **inconclusive** | consistent with run-history ingestion off, retention shorter than a week, no `FlowRun` read permission, or a flow that genuinely did not run. The probe lists these and picks none |
| A query refused | not an empty query | settle the coverage state first; `Unsupported` / `Unauthorized` wording already exists |

The spike settles the identity question only. `parentrunid`'s parent/child semantics (checklist item 8) remain
a separate task, and a five-row ceiling says nothing about whether any window is complete.

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

Maintainer-run, as in the M1 checklist above: an agent records the reported answer and never connects
to an environment itself.

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
9. Confirm which workflow value `flowrun.workflowid` really holds: the `workflow` table's primary id
   (`workflow.workflowid`) — which is what this tool uses as workflow identity and what the discovery join
   matched — or the table's separate `workflowidunique`. The run query filters
   `flowrun.workflowid = <the identity discovery produced>`, so if those two columns differ the symptom is a
   run list that is empty for a flow the portal shows as having runs, which is a scope failure and not a
   filtering bug. No answer is assumed in code: the filter value is exactly the discovery identity. This is
   the first thing to settle before M3 is claimed against a tenant.
   **Amended 2026-11-10 (AM-35).** The re-read of the entity reference found a second candidate the wording
   above did not know about: `flowrun.workflow`, a Lookup to `workflow` whose referenced key is exactly the
   identity discovery uses, and the local schema validation of the same table confirmed both columns exist.
   The question is now measured rather than reasoned about - run the identity probe above on one selected flow
   and report its log line. Both filters returning the same runs settles both halves at once (the text stores
   the primary id, not `workflowidunique`); the verdict table in "The AM-35 identity probe" says what every
   other answer means, and no answer licenses a change to the monitoring query by itself.

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


