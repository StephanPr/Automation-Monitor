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
| Environment id for identity scope | `ConnectionDetail.EnvironmentId` (string) | Reflection over `McTools.Xrm.Connection` 1.2025.9.64; **measured live 2026-11-15** on an Office 365 online connection in XrmToolBox `1.2026.9.78` | Used as the scope half of `WorkflowKey`; null scope stays a distinct key. **Measured: empty on that connection.** The plugin's own `watchlists.json` proved it - the saved watchlist, all 59 of its members and the environment profile stored an empty scope - so flows were keyed unscoped and the Environment column read the unverified label (`no verified environment` since AM-44.1), correctly. AM-40 was the defect that made this legitimate state invisible rather than merely unscoped. **Superseded for the scope source by AM-44.3a (2026-11-15): this property is no longer what keys a `WorkflowKey`.** The scope now comes from `WhoAmIResponse.OrganizationId` through the read-only identity seam, because this property is empty here while a completed read names an organization - keying off the property would keep storing an empty scope while looking verified |
| Paging signal | `EntityCollection.MoreRecords` + `PagingCookie` | Reflection over `Microsoft.Xrm.Sdk` | Followed by `PageReader`; a stopped read is `Partial` |
| Reading an attribute that is **not** an `AliasedValue` as one | `GetAttributeValue<AliasedValue>` **throws** `InvalidCastException`; `entity.Attributes[x] as AliasedValue` returns null instead | net48 harness loading the `Microsoft.Xrm.Sdk.dll` this XrmToolBox install ships (assembly 9.0.0.0, file 9.2.49.14828), 2026-11-09 | **Verified against the assembly the host loads**; still not a tenant observation |
| Absent attribute, and attribute present with a null value | both read as null; neither throws | Same harness | Verified client-side |
| A value in a shape the reader does not accept | refused: `DataverseMappingException` naming the column and the CLR type, never the value | `DataverseValue` xUnit cases + harness case E | Verified |
| A projected column the table does not have | Dataverse refuses the **whole** `RetrieveMultiple`; not one row comes back | First live session, 2026-11-09: the flow query asked for `primarycategory` and the platform answered `The 'workflow' entity doesn't contain attribute with Name = 'primarycategory' and NameMapping = 'Logical'` | **Observed live** (maintainer-reported). That column is no longer projected, and `DataverseSchemaTests` keeps it out of the list |
| The solution read against a real tenant | `complete - 975 row(s) over 4 page(s)` | First live session, 2026-11-09, after the AM-32 type-read repair | **Observed live**: the AM-32 cast defect did not return |
| `WorkflowActivity` (the table M7 needs for classic workflows) | absent in every environment tried | First live session, 2026-11-09 | **Observed live**: M7 stays blocked on an alternative source, not on our query |
| The previous released build (commit `be8ea8b`, the last commit before AM-44.1) reads and re-saves a version-1 file whose members carry `dataverse-organization:<guid>`, an unknown-kind scope and a damaged qualified scope | 6 of 6 members read, 6 written back, every scope value byte-for-byte the same, `schemaVersion` still 1, `kind` still `watchlist-store`, inclusion flags and member provenance unchanged, no refusal raised | `dotnet run -c Release --project %TEMP%\am441-compat\Am441Compat.csproj` on Windows 11, 2026-11-15 — a harness outside the repository that generates the fixtures with this build's `WatchlistManager` and opens them with the worktree build's own `SettingsFileStore` + `JsonWatchlistStore` + `DataContractJsonSettingsSerializer` | **Verified on Windows 11**: `COMPATIBILITY: PASS` over three fixtures (qualified scopes, the real 59-member unscoped shape, mixed GUID spellings). AC-30's condition is met, so `schemaVersion` stays 1. Re-run before the first production write of a qualified scope (AM-44.2). **Re-run on 2026-11-15 for AM-44.3a against the build that actually shipped it (see the next row) and passed** |
| The same round trip against commit `b318416` - the AM-44.2a build deployed on the primary host, and therefore the build a user still has installed when AM-44.3a writes its first qualified scope - using the shape AM-44.3a writes: a list whose `environmentId` is the canonical qualified scope, members whose `scopeId` names that organization, one unknown-kind scope, one damaged scope, one empty scope, the 59-member unscoped file, and three spellings of one organization | 6 of 6 and 59 of 59 members read and re-saved; every scope value verbatim; `schemaVersion` still 1; `kind` still `watchlist-store`; inclusion, provenance, active selection and environment profile intact; no empty scope filled in. On the mixed-spelling fixture the previous build **normalised** the braced upper-case GUID to the canonical spelling (two canonical, none braced, no member lost) - a change of spelling that keeps the same organization, not a loss of scope | `dotnet run -c Release --project %TEMP%\am443-compat\Am443Compat.csproj` on Windows 11, 2026-11-15 - the same harness re-pointed at a scratch `git worktree` at `b318416` | **Verified on Windows 11**: `COMPATIBILITY: PASS`, exit 0, before AM-44.3a's first qualified write. AM-44.3b must re-run it against whatever commit is then previous before re-keying a stored list |

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
   visible in the UI. **Partly answered on 2026-11-15, and the answer is narrower than it first looked:** an
   Office 365 online connection in XrmToolBox `1.2026.9.78` reported **no** environment id, and the plugin's
   own file showed it (the saved watchlist, all of its members and the environment profile carried an empty
   scope). Unscoped keying is therefore the ordinary state on at least this connection type, not an edge
   case: the saved-watchlist line now says the lists are not environment-scoped, and
   `WatchlistCatalog.ForEnvironment` shows unscoped lists everywhere so a list built in that state cannot
   vanish. **Amended 2026-11-15 (AM-44.3a):** unscoped keying stopped being the ordinary state here - the scope
   now comes from the organization a completed `WhoAmI` read named, so discovery and a new save carry
   `dataverse-organization:<canonical-guid>`; rows stay unscoped, and the status line says so, only when that read
   names no organization, and a list already saved unscoped keeps no scope until the operator confirms an association.
   **This is not proof that no environment id is reachable.** The empty value was read from the
   plugin's own *saved* `ConnectionDetail`, and the connection library assigns `EnvironmentId` from the
   connector client it built rather than storing a value the environment reported, so a missing element in the
   saved record says nothing about `CrmServiceClient.EnvironmentId` on the live client. On-premises and ADX
   remain unrecorded; the live-client question was answered by AM-42's maintainer run on 2026-11-15 (below). An earlier conclusion here that no stable
   environment identity is available to this build was drawn from the saved file alone and is withdrawn; see
   the environment-identity section below.

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
question and the `category = 5` question from one diagnostic instead of another guess.
**Conditional, measured 2026-11-11 (AM-36):** that line is written at Information, and XrmToolBox's
`LogManager` skips entries below its log level, which is `Warning` by default and `Warning` on the primary host.
So the line exists but no file holds it unless the operator raises Settings → log level first. The probe result
no longer depends on that (it is shown in its own window); the discovery diagnostic does, and closing that gap is
roadmap item WF-A.9 rather than a reason to widen the status line.

## Environment identity — which value may name an environment (AM-41, measured 2026-11-15)

A watchlist is opened through one connection, so a saved list must say which environment its members live
in: a flow stored under one environment id cannot be queried through a connection to another, and the
operator would read the resulting empty grid as "no runs" rather than "wrong environment". That is what makes
an *empty* scope dangerous rather than merely untidy — two connections that both report no environment id look
identical while they may be two different environments. An empty scope is therefore recorded as
**unverified identity**, never as a scope that can be matched against, merged with, or filled in from
whatever environment happens to be connected.

### What the host and the SDK expose (every row measured on this host)

Property presence was established by reading assembly metadata (the accessor name in the metadata string
heap); versions by reading the deployed and restored binaries themselves.

| Fact | Evidence |
|---|---|
| The host's application directory is `C:\Users\Stephan\Desktop\XrmToolbox` (`XrmToolBox.exe` ProductVersion `1.2026.9.78`). `%APPDATA%\MscrmTools\XrmToolBox\1.2026.9.78` is the **storage** root and holds no host binaries, so host versions cannot be inspected there | Directory reads 2026-11-15 |
| `McTools.Xrm.Connection.dll` deployed by the host is ProductVersion and FileVersion `1.2025.9.64` — the version the plugin compiles against | Deployed binary `VersionInfo`, versus the plugin's `PackageReference` |
| `ConnectionDetail` exposes `EnvironmentId`, `EnvironmentText`, `TenantId`, `ConnectionId`, `Organization`, `OrganizationUrlName`, `OrganizationFriendlyName`, `OrganizationServiceUrl`, `OrganizationDataServiceUrl`, `OrganizationVersion`, `ServerName`, `OriginalUrl`, `WebApplicationUrl` and `ServiceClient` | Accessors `get_EnvironmentId`, `get_TenantId`, `get_ConnectionId`, `get_OrganizationUrlName`, `get_ServiceClient` … present in both the restored and the deployed assembly |
| `ConnectionDetail` exposes **no organization id**, so the organization the connection talks to is not derivable from the connection object alone | No `get_OrganizationId` accessor in that assembly's metadata |
| The connector documents `EnvironmentId` as the Power Platform environment id: *"Gets the PowerPlatform Environment Id of the environment that is hosting this instance of CDS"* | Microsoft Learn, `Microsoft.Xrm.Tooling.Connector.CrmServiceClient.EnvironmentId` (package `Microsoft.CrmSdk.XrmTooling.CoreAssembly` v9.1.1.65) |
| `WhoAmIResponse` returns `OrganizationId`, `BusinessUnitId` and `UserId` — and **no** environment id | Microsoft Learn, `WhoAmIResponse` properties; these types ship in `Microsoft.Crm.Sdk.Proxy.dll` |
| The deployed `Microsoft.Xrm.Tooling.Connector.dll` is ProductVersion `4.0.0.188+gf6ab2d7` and carries `get_EnvironmentId`; the restored `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 9.1.1.65 is the **same** ProductVersion, and it reaches the plugin transitively through `XrmToolBoxPackage` 1.2025.10.74 — so the property is compilable against and present at runtime with no new dependency | Binary `VersionInfo` and metadata scan of both copies; `obj/project.assets.json` |
| The deployed `Microsoft.Crm.Sdk.Proxy.dll` is ProductVersion `9.2.49.14828` | Binary `VersionInfo` |
| **`ConnectionDetail.ServiceClient` is not a safe read.** Its getter is `GetCrmServiceClient()`: it returns the cached client *only* when one already exists, and otherwise constructs a `CrmServiceClient` from the stored connection string or runs the online / IFD / on-premises / OAuth / client-secret / key-vault connect path — and it throws outright for connections made through the SDK login control. Reading a property can therefore start a sign-in, open a second connection, or raise | `McTools.Xrm.Connection` `ConnectionDetail.cs` (master, read in full 2026-11-15): property L450-452; method L560-633, cached-client fast path L562 and L576, throw L583, construction L588-626, `IsReady` check L628-633 |
| The connector's environment id is therefore read from the service **the host already connected**, by type test (`Service as CrmServiceClient`), never from `ConnectionDetail.ServiceClient`. When that service is not a connector client, AM-42 reports the source as *not read by this build* rather than as blank | `XrmToolBox.Extensibility` `PluginControlBase.cs`: `public IOrganizationService Service { get; private set; }`, assigned by `UpdateConnection(IOrganizationService newService, …)`; `DataverseConnectionIdentityProbe` |
| `ConnectionDetail.EnvironmentId` is a plain auto-property (`public string EnvironmentId { get; set; }`) and safe to read, but the connection library fills it only on the code path that builds a client — not on the paths that hand back an already-cached one. That is one reason a *live* read can legitimately differ from what the saved record holds | `ConnectionDetail.cs` L243 (declaration), L644 (`EnvironmentId = crmSvc.EnvironmentId;`), contrasted with the early returns at L562-566 and L574-581 |
| The operator's saved connection record carries **no `EnvironmentId` element at all**, while `TenantId`, `Organization`, `OrganizationFriendlyName`, `OrganizationUrlName`, `OrganizationServiceUrl`, `OrganizationDataServiceUrl`, `OrganizationVersion`, `OriginalUrl`, `WebApplicationUrl`, `ServerName` and `ConnectionId` are present | Element names of `Connections\ConnectionsList.Default.xml`, read for names and emptiness only. No connection value was read, copied or recorded, and the file was not modified |
| **AM-42 live read, reported by the maintainer 2026-11-15 at 11:06:15 UTC** on build `0.1.0+729f485…`: the connection record's `EnvironmentId` and the already-connected client's `EnvironmentId` both stated **nothing**; `WhoAmIRequest` **succeeded**; `WhoAmIResponse.OrganizationId` was **present, valid GUID shape**; the two environment-id sources were **not comparable** because neither supplied an identifier | The plugin's own copyable identity window, which is built from types that cannot hold a value, pasted back by the maintainer. One Office 365 online connection to a Dataverse environment — on-premises and ADX stay unmeasured |
| That one empty value drives the whole identity chain, which is why discovery shows `no verified environment` in its Environment column: `ConnectionDetail.EnvironmentId` → `DataverseGateway.EnvironmentId` → `DataverseSolutionFlowSource` → `DataverseCloudFlowSelector.Select(…, environmentId)` → `WorkflowKey.TryCreate(PowerAutomate, environmentId, id)` → `WorkflowReference.EnvironmentId`, which **is** `Key.ScopeId` | Read of the call path; AM-40's file audit proved the stored consequence |

### The identifiers, and why they are never interchangeable

| Identity kind | Where it comes from | What it names |
|---|---|---|
| Power Platform environment id | `ConnectionDetail.EnvironmentId`, which the connection library assigns from `CrmServiceClient.EnvironmentId` on the client it built | The admin-plane environment hosting this Dataverse environment |
| Dataverse organization id | `WhoAmIRequest` → `WhoAmIResponse.OrganizationId`, executed on the `IOrganizationService` the host already owns | The Dataverse organization the connection is talking to. Authoritative for *organization* identity, and **not** a Power Platform environment id |
| Context, never identity | `TenantId`, `OrganizationUrlName`, `OrganizationFriendlyName`, `OrganizationServiceUrl`, `OriginalUrl`, `WebApplicationUrl`, `ServerName` | Display, provenance and mismatch detection. None is unique or stable enough to key a resource on |
| Connection identity | `ConnectionId` | One saved connection record on one workstation. Two records can point at one environment and re-creating a record produces a new value, so it must never enter stored workflow identity |

Both of the first two are preserved when both are known, each tagged with its own identity kind; one is never
written into the other's field. The second is the documented candidate for **authoritative Dataverse
organization identity**, and the only route to it that needs nothing beyond the connection the host already
owns.

### What AM-41 concludes, and what it refuses to conclude

It concludes a narrow fact: the saved connection record for this connection type carries no environment id,
and unscoped keying is the ordinary state there. It does **not** conclude that no identity is reachable — the
live client was never queried, and reading a saved record cannot answer a question about a running client.
(The state it describes held until AM-44.3a: the plugin now keys discovery and new saves from the organization
a completed `WhoAmI` read returns rather than from this property, so the running client is asked. That change
is recorded in the AM-44.3a section below and retroactively narrows nothing here - the observation about the
saved record stands.)

Substituting any identity into scope derivation is not a display change. `WorkflowKey.ScopeId` participates in
every stored member key and in `RunKey`, so a fresh identity that disagrees with the 59 stored empty-scope
members would silently orphan a list already proven to load correctly. The operator's existing watchlist
therefore stays byte-for-byte as it is, and its empty scope stays *unverified* rather than being taken as
globally valid or as a match for the currently connected environment.

AM-42 answers the open questions read-only (see `docs/roadmap.md` section 5.7): what each candidate reports on
the **active** connection, whether `CrmServiceClient.EnvironmentId` is populated at runtime, and whether the two
environment-id sources agree with each other. It was implemented on 2026-11-15 as one labelled diagnostic action
that reports presence, identifier shape and that one comparison — and *nothing else*, because every type it
returns is incapable of holding a value. **The maintainer ran it the same day (reported 11:06:15 UTC, build
`0.1.0+729f485…`) and the three answers are recorded in the table below.** No agent connects to an environment; the
run is the maintainer's and only shapes came back. Both environment-id sources stated nothing on this connection —
the connector client's own value is empty as well as the saved record's — so the live client does not carry a Power
Platform environment id here either, and the two are *not comparable* because neither supplied an identifier.
`WhoAmIRequest` succeeded and `WhoAmIResponse.OrganizationId` was present with a valid GUID shape, which makes the
organization id the only resource identifier this connection supplies: the kind the scope contract below is written
against. Multi-environment watchlists are a required later capability and are specified in `docs/specification.md`;
their storage format, schema version and migration shape are **still** not decided — those belong to the
multi-environment checkpoint. What has since been decided is the single-environment scope contract below (AM-43,
design only, implemented by AM-44.1); AM-42 itself changes no scope, no stored key and no file.

The organization id and the environment id are deliberately **not** compared with each other. They are different
identity kinds, and a line reading "these two differ" would look like a defect instead of a definition.

### Not verified by AM-41

- ~~Whether `ConnectionDetail.ServiceClient` hands back an already-connected client or builds one.~~ **Answered
  at source level on 2026-11-15: it builds one when no client is cached, which can authenticate or throw. The
  probe therefore never reads that property; see the recorded-facts table above.**
- ~~Whether `CrmServiceClient.EnvironmentId` is populated for this connection type, or whether it agrees with the
  connection record's own value.~~ **Answered by the maintainer's AM-42 run on 2026-11-15: neither source stated an
  identifier on this connection, so they are not comparable and the live client carries no Power Platform environment
  id here.** No identifier value was observed, recorded or compared by any investigation on this host, and none will
  be: the AM-42 window reports shape only.
- Whether the Dataverse `organization` table exposes an environment id column. Learn's `organization`
  reference exceeds the fetch limit (287 KB, truncated at 50 KB) so it was not verified — and a wrong column
  name makes `RetrieveMultiple` refuse the **whole** query (the `primarycategory` class), so it may only be
  measured one candidate column per isolated query.
- On-premises and ADX connections remain unrecorded for every identity field above.

## The resource scope contract (AM-43, design decision recorded 2026-11-15)

This section is a **decision and its evidence**, not a measurement. No build has written anything it describes:
`schemaVersion` is still 1 and the operator's `watchlists.json` still holds one list of 59 members whose `scopeId`
is empty. It also does not depend on AM-42's outstanding live read — the contract holds whichever identifiers that
probe reports, and the gate for its run stays open in `docs/roadmap.md` section 3.

### Four states, and what each may claim

| State | Established by | What it licenses | What it must never be called |
|---|---|---|---|
| **Unverified** | The scope field is empty — the ordinary state on a connection that reports no environment id | Reading and listing the watchlist on any connection, labelled as not environment-scoped | "this environment"; a value that matches a scope |
| **Recognized** | The scope text parses as a known kind token plus a well-formed id | Equality between two scopes; naming the kind; offering to verify it | **verified.** That a string is well formed says nothing about whether it names the operator's environment, or where it came from |
| **Connected** | One read-only `WhoAmIRequest` on the service the host already connected answers with that same organization id, in this session | Querying that organization, and saying "this connection is that organization" | Proof that a stored list belongs to it |
| **Associated** | A **completed** verification read of the stored member ids, a preview the operator can read, and an explicit confirmation | Writing a qualified scope into that list's stored keys | Automatic, inferred, or derived from whatever is connected |

Only **associated** authorises a change to stored identity, and only **connected** authorises a query. A file can
hold a recognized scope that was never associated — hand-edited, or carried in from another workstation — so the
plugin presents such a list as recognized and lets the next read report what it actually found. It does not claim a
confirmation it does not hold. Remembering *when* a list was associated would need a new stored field, which the
version-1 boundary below forbids; the durable fact is the scope itself, and membership truth comes from the read
that reports it.

### The token

The Dataverse resource scope is a kind-qualified token stored **in the fields that already exist** (`scopeId` per
member, `environmentId` per list and per environment profile), not a new field:

```
dataverse-organization:<canonical-guid>
```

| Rule | Decision |
|---|---|
| Why the kind is in the text | A Power Platform environment id and a Dataverse organization id are both GUIDs under the same `ProviderKind`. A bare GUID cannot say which kind it is, so an environment-scoped list from one workstation would silently disagree — or wrongly agree — with an organization-scoped one. Naming the kind inside the value makes the "never one into the other's field" rule structural |
| Implemented kind | `dataverse-organization`, from `WhoAmIResponse.OrganizationId` — the maintainer-approved Dataverse identity source, and the only one reachable through the connection the host already owns |
| Reserved kinds | `powerplatform-environment` (no investigation approved to obtain it on this connection type; nothing in this contract needs it); `azure-subscription`, `azure-resource-group` and `logicapps-standard-host` for AM-45 |
| Grammar | `<kind-token>:<id>`, split on the **first** colon only, so a future ARM resource id containing `/` or `:` survives whole. Tokens are lower-case, matching `ProviderKinds.Prefix` |
| Id form | Dashed lower-case GUID. `CanonicalId.Text`, which `WorkflowKey` applies to the scope, only trims and lower-cases — unlike `CanonicalId.Identifier` it does **not** collapse `{…}` or undashed spellings. `ResourceScope` closes that gap for the one kind it understands: a qualified scope is parsed with `Guid.TryParse` and re-emitted through `CanonicalId.Identifier`, so braced, undashed and upper-case spellings of one organization canonicalise to the same `dataverse-organization:<guid>` text and therefore the same key, while an unqualified stored value keeps the old `CanonicalId.Text` behaviour so no member already on disk is re-keyed. AM-44.1 implemented this and tested all four spellings |
| Unverified | The empty string, unchanged, never equal to a qualified scope and never completed from the active connection |
| Unknown kind token | Preserved, never dropped — the next section |
| Canonical key text | `provider:scope:workflow` stays an **opaque** key. Once the scope holds a colon it can no longer be split into parts, and nothing may try: no code in `src/` parses `WorkflowKey.Canonical` back, member identity is persisted as three separate fields, and exclusion state persists per member (`Watchlist.ExcludedWorkflowKeys` is derived, not stored) |

A recognized scope never makes a list invisible or unopenable. It makes the *difference* visible: today two lists
that both report no environment id are indistinguishable, which is the dangerous case AM-40 exposed, and a named
kind at least lets a list say which environment it believes it belongs to.

### A scope this build cannot interpret is preserved, never dropped

A member whose scope text this build does not recognise must either survive that build's next save **unchanged in
the same field**, or the build must refuse to modify that watchlist at all. Dropping it is not an option: a member
is operator-owned content, and a plugin that quietly deletes a membership it cannot understand has destroyed data it
had no right to change. Preserving a scope costs nothing, because the field is plain pass-through text — the writer
copies `Key.ScopeId` and the reader feeds `stored.ScopeId` straight into `WorkflowKey.Create`, which only trims and
lower-cases it.

The same hazard **was** unresolved for the `provider` field, and it is the sharper case: `SettingsMap` skipped a
member whose `provider` token it did not know, because no `WorkflowKey` can be built without a `ProviderKind`, so a
build that predates a future provider would have dropped those members the next time it saved that list. AM-44.1
closed it with a refusal rather than a skip: every member the reader cannot rebuild (unknown provider, or the identity
fields missing) is tallied on the catalog as unrepresented content, which makes the store refuse an ordinary save,
makes `WatchlistManager` refuse every mutation of every list in that file, and surfaces one operator-facing reason —
*"N saved member(s) are written in a form this version cannot read, so the saved file is not rewritten from what is in
memory. Nothing has been lost: the file still holds them."* followed by which list and which kind of value. A scope of
an unknown kind is **not** in that class: the member is held in memory with its scope text, so the rewrite is lossless,
which is what the round trip below proves.

### `schemaVersion` 1 is approved conditionally, and the condition is a test

Storing a qualified scope is a change of **value**, not of format, so it needs no version bump: `scopeId` and
`environmentId` have been untyped text since the first committed version of this file (`a23f216`), no committed
build has ever parsed their contents, and this build's reader and writer round-trip an arbitrary trimmed
lower-case string through both fields. A length cap is not in the way either — `SettingsFormat.Text` caps names and
notes, but the scope field is passed to `WorkflowKey.Create` uncapped.

A **new field** is a different matter. `DataContractJsonSettingsSerializer` is the framework's
`DataContractJsonSerializer`, which has nowhere to hold a member its type does not declare. The suite already
relies on that: `AFieldThisBuildDoesNotKnowIsIgnoredRatherThanFatal` proves a build reads past a field it does not
know — and proves nothing about what it writes back, because the unknown value cannot survive a save by a build
that has no member for it. That asymmetry is the boundary:

- **Permitted under version 1:** new *values* in the existing scope fields.
- **Not permitted under version 1:** any new field — `scopeKind`, `verifiedAt`, an environment array. A build that
  met one would silently discard it for anyone still running the previous version.

Before the first write of a qualified scope, AM-44.1 had to prove the round trip **with the previous build**, not by
reasoning about it: in a scratch `git worktree` at the commit before it, read a fixture whose members carry
`dataverse-organization:<guid>` scopes, and assert that the members come back with that exact scope text and that a
save from that build writes the same text. If the older build parses the value but corrupts, rewrites or discards it,
that is the stop condition: revisit versioning or add a compatibility barrier, rather than shipping a file the
previous release damages.

**That proof was run and passed on 2026-11-15** (row in the verified-fact table above). A harness outside the
repository generated three populated version-1 fixtures with this build and opened them with the `be8ea8b` worktree's
own persistence code: the qualified/unknown-kind/damaged fixture came back with all six scope values verbatim and was
written back verbatim; the real 59-member unscoped shape kept 59 members, 45 included and 14 excluded, 8 hand-added
provenances, its active selection, its environment profile and its preferences, and its empty scopes stayed empty; the
mixed-spelling fixture kept its one canonical value. Nothing bumped `schemaVersion`, refused the file, or moved a
member. The stop condition did not trigger, so version 1 stands and no compatibility barrier is needed. AM-44.2 shipped
without writing any qualified scope from the running plugin - applying an association is deferred (see below) - so no
re-run was owed for it. **AM-44.3a did owe one, and ran it on 2026-11-15**: its shipped path writes qualified scope text
the first time it saves a list discovered against a connected organization, so the harness was re-pointed at
`b318416`, the build the maintainer actually has installed, and answered `COMPATIBILITY: PASS` (row in the table above).
The one expectation restated in that harness is not a loosened guard: the mixed-spelling fixture's braced upper-case
GUID comes back from the previous build's save in canonical form, which is the canonicalisation AM-44.1 chose for
values this build can interpret - the member, its identity and every other scope value survive, and AM-44.3a never
writes that spelling. **AM-44.3b re-runs the same harness against whatever commit is then the previous release before
it re-keys a stored list**, because the build to compare against moves each time.

### Display identity is separate from key identity

The stored token is for matching, never for reading. What an operator sees comes from one Core renderer,
`ResourceScopeLabel` (AM-44.1), and never from the stored text: a recognized organization is
`Dataverse organization <first eight characters>`, an empty scope is `no verified environment`, an unqualified stored
value is `unqualified environment id <fingerprint>` or `unqualified environment scope`, and a kind this build does not
know or a qualified value with no readable identifier is named by its kind token as unreadable. Supplied with a scope
the current connection actually read, the same renderer may add `(this connection)` or `(not this connection)`;
without that evidence a recognized token never reads as connected. The label never participates in a key, a dictionary
or a comparison, and no friendly name, url or display name is ever used in it — those are mutable and collide, which is
why they are context and not identity in the table above. Raw identifiers stay out of status lines, logs and exports:
`DiscoveryRows` used to print the scope verbatim, which is the shape this rule forbids, and AM-44.1 routed that column
through the label. `ResourceScopeLabelTests` asserts that no label carries a whole identifier in any state.

### Verifying a stored list against the connected organization (AM-44.2, built 2026-11-15)

The identity source is the one AM-42 proved reachable: one `WhoAmIRequest` on the service the host already connected,
read by `DataverseOrganizationIdentity`. Unlike AM-42's probe - whose result types hold shapes and cannot carry a value -
this read must return the `OrganizationId`, because a qualified scope can only be authored from a real identifier. The
value exists in exactly one place, is never logged, never shown, and never stored outside `ResourceScope`; every report
renders `ResourceScopeLabel`'s fingerprint instead. It is compared with no other identifier: the organization id and
`ConnectionDetail.EnvironmentId` / `CrmServiceClient.EnvironmentId` are different kinds (section above), and AM-42
measured both of those as *not stated* on the maintainer's connection, which is why the organization id is the only
route to a qualified scope here.

The verification read asks one question - does this organization hold the workflow ids this saved list carries - and it
is bounded because it runs against a live tenant on an operator's click:

| Bound | Value | Whose decision it is |
|---|---|---|
| Ids per request | 20 | this tool's, not a platform limit |
| Requests per verification | 12 (so 240 ids maximum) | this tool's; a bigger list is refused before any request |
| Pages per request | 4, 50 rows per page, 500 rows in total | this tool's; a source that keeps promising pages stops the read and it reports `Incomplete` |
| Projection | `workflowid` only, `Distinct = false`, ordered by `workflowid` | the platform's rule that a projected column must exist (`primarycategory`) and that a distinct query returns no primary keys (AM-34) |
| Filter | `ConditionOperator.In` over individually boxed `Guid` values | documented operator ("the value exists in a list of values"); no undocumented endpoint. The boxed-per-id shape is a platform contract, measured live - see below |
| Messages | `RetrieveMultiple` plus the one already-approved `WhoAmIRequest` | read-only by construction; no other `Execute` message is approved |

Rows become identity through the same `WorkflowIdentity.TryResolve` rule discovery uses, so a row whose projected column
and row id disagree is counted as unusable rather than guessed, and any row that arrives with an id the read never asked
about makes the whole answer `Incomplete`: a response that contradicts its own filter is not evidence.

**The `In` filter's argument shape is a platform contract, and the first live run broke it (2026-11-15).** The maintainer's
first press of *Verify saved list (diagnostic)* returned, for a 59-member list:

> Condition for attribute 'workflow.workflowid': expected argument(s) of type 'System.Guid' but received 'System.Guid[]'.

Measured against the assembly the plugin restores (`Microsoft.Xrm.Sdk` 9.0.0.0, file version 9.0.2.59, `lib/net462`):
`FilterExpression.AddCondition(String, ConditionOperator, Object[])` is the `params` overload, and a `Guid[]` is **not** an
`object[]` because value-type arrays are not covariant - so passing the array binds it as *one* value. Reflecting the built
`ConditionExpression.Values` showed a count of 1 holding a `System.Guid[]` of 2, where the boxed-per-id call yields a count
of 2 holding two `System.Guid` values. The platform's answer for the nested form is a refusal of the whole query, so no id
was read and nothing about membership was established - the same failure class as projecting a column the table does not
have: the environment is answering, and it is answering this build's request. `DataverseWorkflowIdentityQuerySdkTests`
now asserts the constructed `QueryExpression` itself - operator, one `Guid` per value, no nested collection, the 20/20/19
split of 59 ids, no query for an empty batch, cancellation, and the page ceiling - against the real SDK types, because no
core fake can see this mistake.

**What a read that never answered may report.** That first run also mis-reported itself: 59 members were counted as "not
found" by a query that was refused, and the advice line said the environment had not answered. Both are now prevented by
construction, because they were the same confusion written twice:

| Claim | What may establish it |
|---|---|
| A workflow id exists in this organization | A row carrying it, returned by a completed read that asked for it |
| A workflow id is **absent** from this organization | A **completed, successful** read that asked for it and did not return it - and nothing else |
| A workflow id is **unresolved** | Anything that left it unanswered: a refused or failed read, a read that stopped at a ceiling, an id never reached |
| This list belongs to this organization | Every stored id confirmed by one completed read, plus the operator's confirmation - and applying it is deferred to AM-44.3 |

The counts therefore name five different things rather than two: members considered, ids this read could express as a
query, ids actually placed in front of the platform, ids a completed read answered for, and ids it confirmed. A refused
query is classified as a local defect (`DefectKind.QueryConstruction`), which keeps it out of the `Unavailable` wording
that tells an operator to check a working connection, and the copyable report's "What this proves" line is produced from
the verdict: a read that never answered states that it proved nothing, and one that stopped part-way claims only the ids
it actually found.

Ten evidence classes come out of the read, and only `Verified` - a completed read that found every stored member id -
supports attaching an identity. Partially resolved, unresolved, nothing to verify, incomplete, unauthorized, unavailable,
unsupported and no-connected-organization each refuse, each with its own wording, and each leaves the saved file
byte-identical. **What a hit does not prove is stated in the report itself:** the organization holds those ids, which is
not the same as this list having come from there - importing a solution preserves workflow ids - so the operator
confirms and the tool never infers.

**Applying the association is still deferred, for a different reason than it was.** When AM-44.2 shipped,
`ScopeResolutionSupport.Shipped` answered "can this build resolve a qualified connected scope when it loads, lists and
matches saved watchlists?" with no, and that answer was measured rather than assumed: `AutomationMonitorControl.EnvironmentScope`
derived the connected scope from `ConnectionDetail.EnvironmentId` (empty on this connection) and discovery produced
unscoped flow identities, so re-keying a member then would have broken `Watchlist.FindMember`, the builder's
`Key.Canonical` index, reconciliation, include/exclude/remove by key and the add-a-flow scope check - the list would
still be on disk and would have stopped working. **AM-44.3a removed that obstacle and no other.** The connected scope
now comes from the organization a completed `WhoAmI` read named, discovery keys every row with it, a newly saved list is
created under the scope the save resolved, and a stored qualified scope is resolved against the connected one wherever a
list is listed, opened, matched or reconciled. The association action is still refused, and refused by name: re-keying a
list the operator saved earlier waits until that resolution has been observed running inside XrmToolBox
(`docs/roadmap.md`, section 3, item 10), and will then happen only on their confirmation. Sideways association is closed
too, and is now tested in both directions: adding a flow whose scope is a qualified organization, or importing such
flows, into a list whose environment was never verified is refused (`AssociationRequired`), and a flow that names one
organization is never added to a list that names another - such a member stays visible and unresolved rather than being
removed. Adopting a *legacy unqualified* scope still behaves as before. What a new save does write is the scope that
save resolved; what an existing list keeps is exactly what it had, including nothing.

**Not verified by AM-44.2.** The read has run exactly once against a tenant, on the maintainer's connection, and that run
found the argument-shape defect recorded above; the corrected build has not been observed yet. No agent session connects,
so whether the boxed `In` filter is accepted and whether the projected column arrives as a `Guid` on the real platform
remain first-hand-untested until the maintainer presses the read-only action again and pastes back the copyable block
(`docs/roadmap.md` section 3, item 9). What *is* proved: the Core logic, the ceilings, the classifications and the file
behaviour by the unit suite on both hosts' automation; the SDK query shape by tests that execute the real
`Microsoft.Xrm.Sdk` types; the plugin compile by `net48` Release. What is not: loading the button, and the answer this
environment gives back.

### Resolving and using the connected organization (AM-44.3a, built 2026-11-15)

The scope contract above is only honest once the running plugin can name the organization it is inside. AM-44.3a adds
that, and nothing else:

- **One identity source.** Core's `ConnectedOrganizationResolver` asks an `IOrganizationIdentitySource` once per
  connection and caches the answer; the plugin implements the seam as `DataverseOrganizationIdentitySource` over the
  single `WhoAmIRequest` AM-42 proved reachable. `ConnectionDetail.EnvironmentId` is not consulted - it is measured
  empty on this connection, and keying off it would store no scope while looking like a verified one.
- **Three states, because "not asked yet" and "asked and refused" license different sentences.**
  `ConnectedOrganizationState.NotAttempted / Resolved / Failed`. A failed read comes back unverified *and* carries the
  provider result state and failure kind, so `DescribeEnvironment` can say this connection did not name an organization
  instead of showing a blank scope as though it were the answer.
- **One connection and one scope per action.** `ConnectedDataverseEnvironment` turns the service and connection detail
  the host hands the control into a `DataverseOperation`: the bound service, the resolved organization, and a
  `ConnectionTicket` from `ConnectionContext`. `AutomationMonitorControl` captures one operation per asynchronous action
  and every callback passes `IsStale(ticket)` before it touches the screen, the catalogue or the file, so a read that
  finishes after a switch or a disconnect is discarded with a sentence saying so. The gateway is built from the
  operation with the operation's scope, so no provider call can carry a scope its action did not verify.
- **Qualified identity where it is used.** Discovery keys each row `dataverse-organization:<canonical-guid>` when the
  read named an organization, and stays unscoped - visibly, in the status line - when it did not. A save creates its
  list under the scope that save resolved, not under whatever connection is open when the bytes are written. Run
  history still filters the text `workflowid` column with the provider's own id, which is what AM-35 measured; the
  scope qualifies the plugin-side key, never the value sent to Dataverse.
- **Matching on what a scope names.** `WatchlistCatalog.ForEnvironment` / `ActiveWatchlistFor` and
  `MembershipReconciler` compare `ResourceScope` values, so a stored qualified scope matches the connected one whatever
  the file's spelling, while textual equality is kept as an extra arm for a stored value this build cannot canonicalise
  (a bare legacy environment id, an unknown kind, a damaged identifier), which must keep matching exactly what it says.
  Member identity needs no such arm: `WorkflowKey` already stores `ResourceScope.NormalizeStored` output, so a member's
  scope is canonical before it takes part in equality, a hash or the builder's index. A member whose identity names a
  *different* organization - or none - is kept and reported unresolved, never removed as "no longer in the environment";
  `MembershipReconciler.DescribeVocabularyMismatch` states which vocabulary the read and the store are written in.
- **Display stays display.** `EnvironmentDisplay` renders a scope for the Environment column, the saved-list labels and
  the status line: the identity always, plus the host-supplied friendly name only for the organization this session
  actually read. That name is sanitized (trimmed, control characters replaced, clamped to 80 characters) and never
  enters a key, a filter, a merge, a cache key or a path. An unverified scope renders as `Unverified environment` and
  never borrows the connected name.

What AM-44.3a does not do: re-key a list that is already stored, adopt a qualified scope from an "add a flow" click, or
offer the association action. `ScopeResolutionSupport` still ships `Shipped`, and its deferral reason now names the
unobserved runtime gate (`docs/roadmap.md`, section 3, item 10) rather than an inability this build has already fixed.

**Not verified by AM-44.3a.** No agent session connected to an environment, so none of this has been observed against a
tenant: whether `WhoAmIResponse.OrganizationId` arrives as a `Guid` through the host's own assembly (AM-42's shape
observation says it does; this build has not read one), whether a qualified row still resolves its run history on the
real platform, whether a discarded in-flight result behaves as described when a connection changes mid-read, and how
the new environment wording sits on screen at 100 / 150 / 200 percent scaling with keyboard navigation. That is item
10's checklist, and it is the maintainer's run.

### Decided here, and deliberately not

Decided: the four states; `dataverse-organization:<canonical-guid>` as the Dataverse scope token in the existing
fields; the empty string as unverified; unknown scopes preserved or refused; `schemaVersion` 1 with the round-trip
condition above; the organization id as the identity source, with no further environment-id hunt.

Not decided, and owned by the later multi-environment checkpoint: whether one watchlist may hold members from
several organizations at once, how the Saved list filters when it does, how such a list is serialized, and what
becomes of today's one-environment-per-list rule. The capability stays mandatory
(`docs/specification.md` section 6.6) and the token above is what makes it expressible; none of it is approved by
this section.

## Power Automate run history (`flowrun`) — M3

Run history for solution-aware cloud flows is replicated into the Dataverse `flowrun` table, and
`DataverseFlowRunSource` reads it. Every fact below comes from the Microsoft Learn FlowRun entity
reference and the run-history article, retrieved 2026-10-31. **The column shapes below have since been seen on
live rows**: the AM-35 diagnostic probe read this table on 2026-11-12 and reported the CLR type of every column
it projected (see "The first live answer"). That is not the monitoring path having run.
`DataverseFlowRunSource`, `RunQueryService`, `DataverseRunNormalizer`, the correlation resolver and any grid
**have still never been executed against a tenant from this host**, and there is still no run grid: the code and
its tests are verified, the production round trip is not.

| Fact | Value | Evidence | Status |
|---|---|---|---|
| Entity holding executions | `flowrun` | FlowRun entity reference | Documented; query not yet executed |
| Row id | `flowrunid`, **Uniqueidentifier** (primary id) | Same | kept as `WorkflowRun.RunRecordId` |
| Run identifier | `name`, String(100) — the Logic Apps run name of the execution | Same, plus the run-history article | Chosen as `WorkflowRun.RunId` because `parentrunid` points at this value |
| Parent link | `parentrunid`, String(100) | Same | the only parent/child evidence used |
| Tracking id | `clienttrackingid`, String(255) | Same | becomes `WorkflowRun.CorrelationId`; there is **no** `correlationid` column |
| Owning flow, as text | `workflowid`, String(100), documented as "unique identifier of the workflow associated with this run" | FlowRun entity reference, re-read 2026-11-10, cross-checked against the local schema validation of the same table | The production run query filters on this column. Which of the workflow table's identifiers the text actually stores is **not documented**, and the table exposes three - so it was measured, not assumed; see "The AM-35 identity probe" below. **Measured 2026-11-12:** on the probed flow every sampled run row's `workflowid` equalled the identity discovery produces, so the production filter is on the right column with the right value spelling. A row naming another flow is refused, not merged |
| Owning flow, as a lookup | `workflow`, Lookup to `workflow`, RequiredLevel **None** | Same | The join the platform documents: the referenced primary key is `workflow.workflowid`, which is exactly the value discovery uses as workflow identity. **Not** projected by the monitoring query - only by the probe (`DataverseSchema.RunProbeAttributes`) - because a projected column is a contract with the platform and the production projection is already proven live. **Observed 2026-11-12:** the probe's filter on this column returned no rows for an identity that its text filter matched, while the column itself arrived as an `EntityReference` on sampled rows. That is **unresolved filter behaviour, not a disproof of the documented relationship** - see "The first live answer" |
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

### The AM-35 identity probe (bounded diagnostic; answered for one flow)

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
  line. **AM-36 changed where that answer appears:** after every run the plugin shows the whole block in its own
  copyable window - window, both counts, the overlap, the verdict, what it means and any query failure - while
  the status line keeps one short verdict sentence. The host log still receives the same text, at Information,
  Warning or Error depending on what the environment did, but it is no longer the path anyone has to follow to
  read the result; see "Reading the result out of the plugin" below.

What each verdict means - and does not:

| Verdict | Observation | Next step |
|---|---|---|
| Both filters return the same runs | the text column stores the primary id discovery uses; for this table the two are interchangeable | moving the production filter to the documented lookup is a separate change with its own tests |
| Only the text filter returns rows | the documented join did not match this identity in this environment; the column may or may not be populated, which this verdict alone cannot tell | keep the text filter and record the lookup's filter behaviour as unresolved - a zero answer is not evidence that the relationship is invalid |
| Only the lookup filter returns rows | the text column holds something else - another workflow identifier, or nothing | the monitoring filter is wrong for this environment; open the fix as its own task |
| Rows both ways, none in common | something other than identity separated the two reads | investigate with the reported shapes; the probe does not explain it |
| No rows either way | **inconclusive** | consistent with run-history ingestion off, retention shorter than a week, no `FlowRun` read permission, or a flow that genuinely did not run. The probe lists these and picks none |
| A query refused | not an empty query | settle the coverage state first; `Unsupported` / `Unauthorized` wording already exists |

### Reading the result out of the plugin (AM-36)

The first live probe run answered `0 rows / 0 rows`, and the word that would have explained it travelled only in
an Information-level log entry the host never wrote. These are the measured facts about where the answer lives
now, so no future session has to rediscover them:

| Fact | Value | Evidence |
|---|---|---|
| Where the answer appears | a modal "Run-history identity probe - result" window inside the tool, with a **Copy all** button (text pre-selected, so Ctrl+C works too), plus one verdict sentence on the status line | `FlowRunProbeReport.Build` in core; `ProbeReportForm` and `DiscoveryView.ShowProbeReport` in the plugin |
| What the host log holds | the same block, written at Information, Warning or Error chosen from what the environment did: a clean empty read is Information; a query that could not be sent or two reads that contradict each other is Warning; a lost-access refusal is Error | `FlowRunProbeOutcome.Severity` feeding `LogProbeResult` |
| Host logging default, measured | plugin entries below `Warning` are **not written at all**: `Settings\XrmToolBox.Settings.xml` carries `<LogLevel>Warning</LogLevel>`, and no `Logs\AutomationMonitor.Plugin.log` existed after the probe runs that "produced nothing" | read on the primary Windows host, 2026-11-11, against `XrmToolBox.Extensibility.LogManager` |
| XrmToolBox build in use here | `1.2026.9.78`, storage root `%APPDATA%\MscrmTools\XrmToolBox` | read from disk 2026-11-11; still named per session, because an `/overridepath` install moves it |
| What the block never contains | a flow id, a run id, an environment name or url, or provider message text - only counts, column names, CLR type names, state tokens and UTC timestamps | `FlowRunProbeReportTests` privacy cases |

**What the runs so far say, and what they do not.** The maintainer pressed the probe on several flows. Most
answered `no rows either way`; on 2026-11-12 one flow returned rows on one side only - `TextOnly`, recorded in full
in "The first live answer" below. That is the evidence the identity question needed, and for the flow and
environment observed it settles the filter choice: the text column is the one that works, and production keeps it.
`BothEmpty` on its own proves nothing about either column: it is equally consistent with a flow that did not run
inside the window, a retention window shorter than seven days, run history not being stored in this environment, or
a read this build cannot see - so the `no rows either way` population remains unexplained, which is what roadmap
WF-A.10 exists for.

The spike settles the identity question only. `parentrunid`'s parent/child semantics (checklist item 8) remain
a separate task, and a five-row ceiling says nothing about whether any window is complete.

### The first live answer (2026-11-12): the text filter finds runs

The maintainer probed one solution-aware cloud flow in the test environment (XrmToolBox `1.2026.9.78`, build
`0.1.0+f07ee70`, source revision `f07ee704dbe2`) and pasted the AM-36 result window back. The scope is narrow and
is not extended by inference: **one flow, one environment, one seven-day window, a five-row ceiling per query.**

| Reported field | Value | What it establishes |
|---|---|---|
| Verdict | `TextOnly` - only the text-column filter returned runs | The spike question is answered for this flow: production filters on the column that works. |
| Query A (`flowrun.workflowid` text) | 5 rows, 5 distinct run ids, ceiling reached so labelled a sample | The monitoring filter returns rows for a flow that has runs. |
| `text id vs requested` | `all values are the requested identity` | The text column stores the same value `workflow.workflowid` discovery uses - the question the entity reference cannot settle. |
| Query B (`flowrun.workflow` lookup) | 0 rows, answered, no failure | A zero, not a refusal and not a paging fault: the two reads differ only by the column they filtered on. |
| `lookup column arrived` | `yes`, with `workflow=EntityReference` among the shapes | The lookup arrived on rows the text filter returned, so B's zero is **not** "the column is empty". |
| `Failures` | `none - both queries were sent and the environment answered` | Neither zero is a coverage artefact. |

**The decision recorded here.** The `flowrun` monitoring filter stays on the text `workflowid` column, and no
production change task is opened by this result: the two verdicts that would have forced one - runs only through
the lookup, or two different sets of runs - did not occur.

**What stays unresolved, deliberately.** Why the lookup filter matched nothing is not established, and none of
these is ruled in or out: the reference may carry an identifier other than the one requested; it may target a
table other than `workflow`; or an `Equal` filter on a lookup column of an elastic, non-transactional table may
simply not behave as the schema reading implies. **The documented join stands** - what was measured is that this
build's lookup filter did not match this identity in this environment. Re-measuring it is a legitimate later task
with its own evidence; nothing here forbids it.

**One limit of the shape evidence.** The probe tallies one CLR type per column from the last row that carried it,
so `workflow=EntityReference` means *at least one* of the five sampled rows held a value, not that all five did.
That is precisely why the zero cannot be read as an unpopulated column.

**Column shapes seen on live rows**, every one matching the declared type above: `flowrunid=Guid`, `name=String`,
`workflowid=String`, `status=String`, `starttime=DateTime`, `endtime=DateTime`, `duration=Int64`,
`triggertype=String`, `isprimary=Int32`, `clienttrackingid=String`, `partitionid=String`, `versionnumber=Int64`,
`workflow=EntityReference`. This retires the "this build guessed a column type wrong" risk class on the projected
set - the class that cost every flow row in the 2026-11-09 session - without validating anything downstream of
the read.

**What this read is not.** These are the diagnostic probe's own rows, projected by
`DataverseSchema.RunProbeAttributes` and reduced by the gateway's unwrapping rule. Production run history -
`DataverseFlowRunSource` into `DataverseRunNormalizer`, `RunQueryService`, correlation, and any grid - has still
never run against a tenant, and there is no run grid to run it in. Observed shapes are not pipeline validation;
that is what the M3 UI pass exists to prove.

**Also implied, and still owed.** The probe acts only on a flow selected in the discovery grid, so a flow row did
reach the screen and its identity reached the probe - the first live sign the AM-34 identity fix works. Row
counts, the identity-source tally and layout at 100/150/200 percent scaling were not reported, so roadmap §3 item
4 and the direct WF-A.7 discovery report stay open.

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
   and copy the result window the plugin opens afterwards (AM-36). Do not look for it in the log panel or in
   `Logs\`: the host leaves an Information-level plugin entry unwritten at its default level. Both filters
   returning the same runs settles both halves at once (the text stores
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


