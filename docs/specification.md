# Automation Monitor for XrmToolBox

> **Product requirements and technical design — working specification**  
> **Version:** 0.1 (planning baseline)  
> **Date:** 29 October 2026  
> **Status:** Proposed; no implementation or API proof of concept has been completed  
> **Primary platform:** XrmToolBox for Microsoft Dataverse / Dynamics 365  
> **Initial operating model:** Standalone, read-only desktop plugin; no separately hosted components

---

## 1. Executive summary

Build an **Automation Monitor** plugin for **XrmToolBox** that provides one place to discover, group, monitor, investigate, filter, and report on executions of:

1. **Microsoft Power Automate cloud flows**, primarily solution-aware flows stored in Dataverse.
2. **Azure Logic Apps Consumption** workflows.
3. **Azure Logic Apps Standard** workflows (where run history is available).

It is particularly intended for **go-lives, deployment validation, integration testing, incident investigation, and daily support**. An operator should be able to select a collection of integrations (a **watchlist**) and view all their runs on a single chronological dashboard, without navigating between many flow pages and Azure resources.

A user should also be able to select a **Dataverse solution** to generate a watchlist of its cloud flows. For Azure, the tool should be able to discover Logic Apps and, in a later phase, suggest watchlist membership based on **Azure API Management (APIM)** APIs and their backends. APIM-based discovery must be reviewable because an APIM API does not necessarily map to exactly one Logic App.

The tool must support **date-and-time filtering, per-workflow filters and duration thresholds, parent/child run correlation, clickable links to workflows and individual runs, saved watchlists, and exports for multi-workflow reporting**.

### Non-negotiable first-release constraint

**The plugin must operate on its own while XrmToolBox is open.** It must not require an Azure Function, database server, web application, external monitoring agent, Log Analytics workspace, or other *deployed* monitoring infrastructure. It may call the existing Microsoft cloud service APIs needed to read workflow and run data. Azure sign-in and tenant-approved application registration/permissions may still be necessary for Azure features; those are authentication prerequisites, not a separately hosted application.

**Notifications when the application is closed are explicitly out of scope.** When the plugin or XrmToolBox is closed, its polling, monitoring, and local notifications stop. No background service is implied.

The first version is **read-only**. Disabling/enabling workflows, cancelling runs, resubmitting runs, and other write actions belong on the future roadmap only.

---

## 2. Problem statement and desired outcome

### Current problems

- A project can contain dozens of Power Automate flows and Logic Apps spread across solutions, environments, resource groups, and subscriptions.
- Operators must repeatedly open individual workflow pages to see failures or long-running executions.
- There is no convenient project-specific view of *all selected executions together* during a go-live.
- Reporting across several workflows for a precise timeframe requires manual collection and merging.
- Failures in child flows can be hard to trace from the parent execution.
- Teams often care more about a handful of critical integrations than every workflow in a tenant.
- Different monitoring data sources may have delays, missing records, or different permissions; a blank grid is not proof that everything succeeded.

### Desired operator experience

> Open XrmToolBox, connect to the relevant Dataverse environment (and optionally authenticate to Azure), select a saved go-live watchlist, choose a time window, and see all selected workflows' runs together. Filter failures, highlight unusually long executions, expand related child runs, open any workflow/run in its native portal, and export a combined report.

### Target users

- Dynamics 365 / Power Platform developers and consultants.
- Integration developers working with Power Automate, Logic Apps and APIM.
- Deployment and go-live support teams.
- Technical application support and operations teams.
- Administrators investigating cloud-flow execution history.

### Primary use cases

| ID | Use case | Expected outcome |
|---|---|---|
| UC-01 | Go-live watchlist | Monitor 10–50 selected business-critical integrations in one view. |
| UC-02 | Solution watchlist | Select a Dataverse solution and create a reusable list of its cloud flows. |
| UC-03 | Global failures | Show failures across all *discoverable and covered* flows in a connected environment, subject to source coverage. |
| UC-04 | Historical report | Select exact date/time boundaries and export combined run history. |
| UC-05 | Child-flow investigation | Expand a parent run to see related child runs when relationships are available. |
| UC-06 | Portal drill-down | Open the workflow home/details page or a specific run in Power Automate/Azure Portal. |
| UC-07 | Slow-run monitoring | See all failures, while highlighting runs exceeding global or per-workflow duration thresholds. |
| UC-08 | Logic Apps watchlist | Combine selected Logic Apps Consumption and Standard workflows with Power Automate flows. |
| UC-09 | APIM-assisted selection | Select an APIM service/API and propose a watchlist based on discoverable Logic App backends; confirm ambiguous mappings. |
| UC-10 | Support handover | Save a watchlist and filter preset, and share a report without sharing credentials. |

---

## 3. Product principles and constraints

1. **Local-first:** One XrmToolBox plugin, no required hosted backend or remote configuration database.
2. **Read-only by default:** Monitoring and reporting must never modify a production flow or Logic App.
3. **Provider-neutral UI:** Different platforms normalize into one workflow and run model.
4. **Truthful completeness:** Always show data source, last successful refresh, partial results, permission failures, and likely delays.
5. **Watchlist-centric:** A saved go-live/session view is the primary workflow; global monitoring is a complementary mode.
6. **Investigate in place, drill down natively:** Useful summary/details in the plugin, authoritative run details in the Microsoft portals.
7. **Distinguish *filtering* from *highlighting*:** A long successful run may be important without needing to hide all other rows.
8. **No forced tenant changes:** Basic monitoring should not require creating Dataverse tables, installing solutions, modifying flows, or enabling Azure diagnostics.
9. **Respect least privilege:** Use the signed-in user's allowed read access; expose permission limitations clearly.
10. **Build incrementally:** Validate APIs and run history before investing in sophisticated visualization.

### Explicit exclusions from initial releases

- Background or unattended monitoring when XrmToolBox is closed.
- Azure Functions, Logic Apps monitoring services, or a hosted central API as prerequisites.
- Cloud alert rules, email/Teams alerts when the app is closed, and shared incident state.
- A required server or embedded SQL database for watchlist persistence.
- Disabling/enabling flows, cancelling executions, resubmitting runs, or editing workflow definitions.
- Automatic guaranteed end-to-end correlation between unrelated integrations.
- Downloading and storing all action inputs/outputs by default.
- Claims of guaranteed real-time execution visibility.

---

## 4. Scope and planned releases

These releases represent a **suggested implementation sequence**, not promised dates.

| Capability | POC | Release 1: Power Automate | Release 2: Logic Apps | Release 3: APIM & polish | Future |
|---|:---:|:---:|:---:|:---:|:---:|
| XrmToolBox plugin host and Dataverse connection | ✓ | ✓ | ✓ | ✓ | |
| Solution-aware flow discovery and manual selection | ✓ | ✓ | ✓ | ✓ | |
| Solution-generated watchlists | ✓ | ✓ | ✓ | ✓ | |
| Combined run grid, status/date filters | ✓ | ✓ | ✓ | ✓ | |
| CSV export | ✓ | ✓ | ✓ | ✓ | |
| Saved watchlists and saved filter presets | | ✓ | ✓ | ✓ | |
| Auto-refresh while app is open | | ✓ | ✓ | ✓ | |
| Per-workflow duration/visibility rules | | ✓ | ✓ | ✓ | |
| Parent/child Power Automate correlation | | ✓ | ✓ | ✓ | |
| Workflow/run deep links | | ✓ | ✓ | ✓ | |
| Multi-sheet Excel reporting | | ✓ | ✓ | ✓ | |
| Logic Apps Consumption and Standard | | | ✓ | ✓ | |
| Combined Power Automate + Logic Apps reports | | | ✓ | ✓ | |
| Azure authentication and subscription/resource discovery | | | ✓ | ✓ | |
| APIM-derived watchlists and mapping review | | | | ✓ | |
| Cross-platform correlation by available tracking IDs | | | Candidate | ✓ | |
| Local in-app/desktop notifications **while open** | | Optional | Optional | ✓ | |
| Monitoring when the plugin is closed | | | | | Deferred / not currently planned |
| Workflow disable/enable, cancel, resubmit | | | | | Deferred |

**Priority rule:** The POC and Release 1 must be useful without Azure access. Azure functionality should never block Power Automate-only users.

---

## 5. Main navigation and user experience

The plugin will use **native WinForms** (the standard XrmToolBox approach), keeping the interface efficient and familiar. A future WebView-based UI is possible but not a first-release dependency.

### 5.1 Main pages

| Page | Role |
|---|---|
| **Monitor** | Active watchlist, combined run timeline, failures, counters, quick filters, auto-refresh and investigation. |
| **Watchlists** | Create/edit watchlists; select workflows manually; generate from Dataverse solutions and APIM; save presets. |
| **Reports** | Exact date/time queries, grouped summaries, comprehensive CSV/Excel export. |
| **Settings & Rules** | Default duration thresholds, per-workflow overrides, refresh settings, UI preferences, Azure connection settings. |

### 5.2 Example Monitor layout

```text
+---------------------------------------------------------------------------------+
| Automation Monitor             [Environment] [Azure: Connected] [Refresh] [Auto] |
+---------------------------------------------------------------------------------+
| Watchlist: CRM Production Go-Live  [Change] [Save]    Period: Last 60 min       |
| Search: [_______________] Status: [All / Failed / Running ...] [Advanced filters] |
+---------------------------------------------------------------------------------+
| Workflows: 24   Runs: 842   Failed: 7   Running: 12   Slow: 18   Stale: 1       |
+------------------------+--------------------------------------------------------+
| WATCHLIST / SOURCES    | UNIFIED EXECUTION GRID                                 |
| [x] Accounts Sync      | Time      Workflow           Status   Duration   Link  |
| [x] Invoice Handler    | 13:05:11  Accounts Sync      Failed   00:00:32   Run   |
| [x] ERP Outbound API   | 13:04:59  Invoice Handler    Running  00:05:20   Run   |
| [x] Customer LogicApp  | 13:04:36  Customer LogicApp  Success  00:00:14   Run   |
| ...                    | ...                                                    |
+------------------------+--------------------------------------------------------+
| RUN DETAILS / CORRELATION                                                      |
| Parent run -> Child: Validate Order (Success) -> Child: Send ERP (Failed)       |
| Error summary | Source freshness | Open workflow | Open run | Copy ID           |
+---------------------------------------------------------------------------------+
```

This sketch communicates structure, not final styling. Workflow/Run links are distinct actions, rather than ambiguous double-click targets.

### 5.3 Interaction rules

- Clicking a **workflow name** opens its native workflow details page (or a clearly labelled action does so).
- Clicking a **run ID / Open Run** opens that specific run, where supported by the native portal.
- Selecting a grid row shows local run details without leaving XrmToolBox.
- A context menu offers **Open workflow**, **Open run**, **Copy workflow ID**, **Copy run ID**, and **Copy tracking/correlation ID** when available.
- Sorting, resizing, showing/hiding columns, and remembering display preferences should be supported.
- Changing a watchlist, environment, or Azure subscription clearly identifies the current scope and avoids mixing results from the wrong connection.
- The header must show **Last checked**, **Source data timestamp/freshness where known**, and **Refresh errors**.
- Empty states distinguish **no matching runs**, **insufficient permissions**, **source unavailable**, **source not supported**, and **query still loading**.

---

## 6. Workflow discovery and selection

### 6.1 Power Automate catalogue

The plugin should discover visible solution-aware cloud flows in the connected Dataverse environment using the `workflow` table and relevant metadata. Identify modern/cloud flows correctly (commonly `workflow.category = 5`), and validate entity semantics against the installed environment.

Supported catalogue filters:

- Name or keyword.
- Solution.
- Enabled/disabled state, when available.
- Owner (when authorized and obtainable).
- Existing watchlist membership.
- Optional source/status metadata.

A global inventory must be described as **discoverable flows**, not necessarily every flow in the tenant. Flow visibility depends on permissions and the chosen API, and Dataverse run history is primarily for solution-aware flows.

### 6.2 Dataverse solution → watchlist

User flow:

1. Select a connected environment.
2. Select one or more Dataverse solutions.
3. Query `solutioncomponent` rows for `componenttype = 29` (**Workflow**), resolve each workflow, and keep qualifying cloud flows.
4. Preview matching flows and counts.
5. Create a watchlist (name defaults to solution name but can be changed).
6. Optionally include or exclude individual flows before saving.

**Two membership modes:**

- **Snapshot:** Current members remain fixed until manually edited or re-imported.
- **Dynamic:** Store the source solution IDs; on explicit refresh, reconcile added/removed components while honoring manually excluded flow IDs.

Dynamic membership must **not silently delete** a manually added workflow; show a reconciliation preview or preserve user additions separately.

If a flow is referenced by several selected solutions, it appears **once** in a watchlist. Use stable workflow IDs and environment identity, not names, for deduplication.

### 6.3 Manual watchlists

Users can:

- Select any subset of discoverable flows/Logic Apps.
- Add or remove integrations.
- Rename, duplicate, delete, import, and export watchlists.
- Save descriptions and tags such as *Go-Live*, *ERP*, *Production*, and *Integration Test*.
- Save preferred time range, column layout, filter preset, and per-flow overrides.
- Identify members missing from the current user's permissions or deleted in the source platform.

### 6.4 Azure Logic Apps discovery

After separate Azure sign-in, the user can browse authorized tenants, subscriptions, resource groups, and relevant Logic App resources:

- **Consumption:** `Microsoft.Logic/workflows` resources.
- **Standard:** `Microsoft.Web/sites` Logic App hosts and the workflows within each host.

The UI must distinguish **Logic App host** from **individual workflow** in Standard. Watchlist selection should be at the *workflow* level so individual runs can be queried consistently.

A Standard **stateless** workflow does not provide normal persistent run history. Its entry should explain the limitation rather than imply zero runs.

### 6.5 Azure API Management → watchlist (later phase)

User flow:

1. Select an Azure subscription, resource group, and APIM service.
2. Choose APIs, API groups/tags, or the entire service.
3. Read APIs, operations, configured backends, and applicable policies.
4. Propose candidate Logic App workflows or linked Azure resources.
5. Show mapping confidence: **Confirmed**, **Inferred**, or **Unresolved**.
6. Allow manual mapping and exclusions; preview before creating a watchlist.

**Important:** APIM can route through a direct service URL, backend entity, conditional policy, named values, or multiple backends. Some APIs do not call Logic Apps at all. A mapping is not guaranteed, and APIM requests should not be presented as Logic App runs.

Potential extension: show APIM API/operation as **context metadata** alongside a mapped Logic App. Full APIM gateway request analytics are not required for the first APIM discovery implementation.

---

## 7. Monitoring dashboard behavior

### 7.1 Monitoring modes

1. **Watchlist view (primary):** List executions from selected Power Automate and Logic App workflows together.
2. **Global failures (Power Automate):** Query available environment-level execution metadata for failures across all covered workflows, not only a watchlist.
3. **Single workflow view:** Focus on one workflow while preserving history, rules, and links.

The global view must display the **scope and coverage** of its source, especially when only solution-aware FlowRun data can be queried.

### 7.2 Unified run grid fields

| Field | Notes |
|---|---|
| Platform | Power Automate / Logic Apps Consumption / Logic Apps Standard. |
| Tenant/environment/subscription | Human-readable and stable identifying context. |
| Workflow name and ID | Name for display; ID for stable identity. |
| Workflow/host/resource group | Where applicable. |
| Run ID | Stable run-specific identity. |
| Start time | Stored in UTC; rendered in configured time zone. |
| End time | Nullable while running; stored in UTC. |
| Duration | Completed duration or continuously calculated elapsed duration for running executions. |
| Raw status | Original provider-specific status. |
| Normalized status | Shared category for filtering/counters. |
| Error code / summary | Show when available and authorized; sanitize/redact where necessary. |
| Parent run ID | When provided by source. |
| Correlation/tracking ID | When provided by source. |
| Monitoring provider | Dataverse FlowRun, Power Platform API, Logic Apps REST, etc. |
| Data freshness / last checked | Essential to avoid misleading all-green states. |
| Drill-down links | Workflow details and exact run. |

### 7.3 Status normalization

The tool should **preserve raw statuses** and map them to common groups:

| Common group | Typical raw values | Interpretation |
|---|---|---|
| Succeeded | Succeeded, Success | Completed successfully. |
| Failed | Failed, Faulted, TimedOut, Aborted | An unsuccessful outcome; raw cause remains visible. |
| Running | Running, Waiting, Suspended, Paused (context-dependent) | Not yet terminal; preserve the raw state. |
| Cancelled | Cancelled, Canceled | Terminal, distinguishable from failed runs. |
| Skipped | Skipped, Ignored | Separate from failures unless a rule explicitly includes them. |
| Unknown | Unrecognized/null | Do not silently label successful. |

These are proposed *UI groupings*, not claims that every status has identical meaning across products. Provide a configuration option for whether timeout/aborted/cancelled count toward **problem runs**, while keeping the raw status available.

### 7.4 Summary cards

- Selected workflows, visible workflows, and workflows with unavailable data.
- Total returned executions for the active period.
- Failed executions and failure rate (state denominator explicitly).
- Running executions.
- Slow executions above thresholds.
- Optional missing-expected-run warnings.
- Last successful refresh and number of errors/partial providers.

**Run counts are not necessarily transaction counts.** A parent execution plus two children may be three records representing one business transaction.

### 7.5 Refresh behavior

- Manual refresh is always available.
- Optional auto-refresh only while the plugin is open and active.
- Suggested configurable refresh values: **30 seconds, 1 minute, 2 minutes, 5 minutes**, plus Off; actual minimum may be constrained by rate limits.
- Refresh only the active watchlist/time window where possible.
- Use incremental lookback with overlap to catch late-arriving runs, deduplicating by provider + workflow + run ID.
- Query running executions again so their statuses and durations update.
- Bounded concurrency for per-workflow API calls; do not start unlimited parallel requests.
- Pause/cancel a refresh on connection change, watchlist change, or tool disposal.
- Separate *the API was just polled* from *the returned event data is fresh*.

The plugin may provide optional local notification sounds/toasts for newly observed failures **while it is running**. These are not part of the core POC and cannot be relied upon when XrmToolBox is closed.

---

## 8. Filters, thresholds, and customizable rules

### 8.1 Query and display filters

Support filtering by:

- Absolute **from/to date and time**, including hours and minutes.
- Quick periods: last 15 minutes, 1 hour, 4 hours, 24 hours, 7 days, and custom.
- Start time vs completion time (explicit selection).
- Run status and failure categories.
- Workflow, provider, environment, subscription, and watchlist.
- Duration minimum/maximum in seconds/minutes.
- Name and optionally error-code keyword.
- Parent vs child runs or correlated group.
- Running vs completed executions.

Use a **half-open time interval** convention where practical: `from <= timestamp < to` to prevent duplicate boundaries in adjacent reports. Persist UTC query boundaries but display and export the selected time zone, handling daylight-saving transitions correctly.

Some upstream APIs may not support every filter. The provider should push supported conditions server-side, retrieve all necessary pages, and then apply remaining filters locally. The UI must show when a report is incomplete because a provider limit was reached.

### 8.2 Rules are different from filters

A **filter** decides which rows appear. A **rule** evaluates rows for operational relevance and changes emphasis/notifications; it need not hide anything.

Examples:

- Show **all failed runs**, but highlight those taking **more than 120 seconds**.
- For **ERP Customer Sync**, use a **30-second** warning threshold rather than the global 120 seconds.
- Highlight **running longer than 5 minutes** even though the execution has not finished.
- Flag **3 failures in 10 minutes** for a selected workflow (future enhancement if appropriate).
- Flag **no successful execution in 20 minutes**, but *only for a workflow configured with an expected schedule/activity pattern*.

### 8.3 Settings inheritance

`Global defaults → Watchlist overrides → Workflow overrides`

Workflow overrides win. Each rule should have an **enabled** flag, a threshold, a severity, and actions such as highlight row, show local badge, and (optional, while-open) local notification.

Avoid a generic “flow did not run” alert for all event-driven flows; absence of new events is not automatically a failure.

### 8.4 Example rules configuration (illustrative)

```json
{
  "version": 1,
  "defaults": {
    "highlightFailed": true,
    "slowCompletedRunSeconds": 120,
    "slowRunningRunSeconds": 300
  },
  "workflowOverrides": [
    {
      "workflowKey": "powerautomate:ENVIRONMENT_ID:WORKFLOW_ID",
      "slowCompletedRunSeconds": 30,
      "slowRunningRunSeconds": 180
    }
  ]
}
```

The actual persisted schema will be versioned and validated; this is not a final contract.

---

## 9. Parent/child correlation

### 9.1 Power Automate parent and child runs

Where available, Dataverse FlowRun records expose `parentrunid`. The plugin should:

1. Index normalized executions by provider, environment, workflow, and run ID.
2. Resolve referenced parent IDs from the current result set.
3. Optionally retrieve additional parent/child runs on demand, subject to the user's permissions and API coverage.
4. Show a tree or expandable hierarchy beneath the selected run.
5. Mark missing or inaccessible parent/child records as **unresolved** rather than fabricating a relationship.

The system must handle nested children, duplicate references, absent parents, circular/corrupt metadata safely, and cases where the child is outside the current watchlist.

### 9.2 Cross-platform correlation (later enhancement)

Power Automate-to-Logic Apps relationships may be supported when an integration carries a **shared tracking/correlation ID** that is exposed by both APIs or diagnostics. Such matching must be evidence-based.

Correlation levels:

- **Explicit:** Provider returns direct parent/child ID or another authoritative relationship.
- **Tracking match:** Shared, known correlation ID deliberately propagated between workflows.
- **Suggested:** Weak heuristic (e.g., timestamp proximity); **not** automatically merged into one execution chain.
- **Unresolved:** No reliable relationship available.

The tool should never treat “runs started at similar times” as proof that one triggered another.

### 9.3 Reporting consequences

Expose separately:

- **Total run records**.
- **Failed run records**.
- **Correlated execution groups**, where determinable.
- **Failed correlated groups**, where determinable.

If grouping is incomplete, label group totals as partial/estimated or omit them. Avoid double-counting a failed business transaction in executive summaries.

---

## 10. Historical reports and export

### 10.1 Reporting experience

A user should be able to:

1. Select one or several workflows, a watchlist, or the global-covered scope.
2. Choose an exact **from** and **to** date/time and time zone.
3. Select whether filtering uses execution **start** or **completion** timestamps.
4. Apply status, duration, source, and correlation filters.
5. Preview totals and a limited run sample.
6. Export the **entire matching dataset** (not just the visible grid page).
7. Save the report filter definition for reuse.

Query preparation should estimate scope and provide progress/cancel controls for large reports. All server pages must be retrieved before the exporter may label the report **complete**.

### 10.2 Export formats

**CSV — first proof of concept**

- Flat run-level rows; UTF-8 encoded with appropriate spreadsheet compatibility.
- Configurable delimiter if useful for European regional settings.
- Stable column names, UTC timestamps plus optional display-zone columns.
- Proper escaping of delimiters, quotes, line breaks, and spreadsheet formula injection.

**XLSX — Release 1**

| Worksheet | Data |
|---|---|
| `Summary` | Workflow-level counts, failure rates, average/median/max duration and problem counts. |
| `Run History` | All matching execution records with IDs, links, timestamps, status, duration, source. |
| `Failures` | Failed/problem executions, error code and permitted error summary. |
| `Correlation` | Parent-child relationships and tracking IDs with confidence/source. |
| `Report Metadata` | Scope, watchlist, filters, generation time, time zone, source coverage, incomplete queries, warnings. |

Formatting: frozen headers, filters, readable date/time formats, duration as numeric seconds and a human-readable representation, link columns where supported. Export is a **snapshot** of information available at generation time, not an immutable audit source.

### 10.3 Privacy and reliability

- Default exports should contain metadata and error summaries, **not raw action payloads**.
- Avoid saving secrets, HTTP authorization headers, signed SAS URLs, tokens, or sensitive input/output content.
- Mark partial exports prominently, including timeouts/429s, unsupported sources, permissions, and data-retention limits.
- Include report generation timestamp and source freshness so users can interpret apparent gaps.
- Large Excel exports should use a memory-aware writing strategy and clearly report limits.

---

## 11. Microsoft data sources and API strategy

### 11.1 Power Automate: Dataverse metadata and run records

**Discovery:** Dataverse `workflow`, `solution`, `solutioncomponent` and related metadata through the XrmToolBox-provided organization service. `solutioncomponent.componenttype = 29` refers to Workflow. Cloud flows are identified using the workflow category/metadata as applicable.

**Global run view:** Dataverse `flowrun` table (`flowruns` Web API entity set where applicable), scoped to the connected environment and authorization.

**Known caveats:**

- Dataverse FlowRun history is for **solution-aware cloud flows**; it is not a universal tenant-wide stream.
- FlowRun ingestion can be disabled, incomplete, or delayed. Default retention is **28 days** but configurable.
- Access to workflow/run metadata and action-level details depends on permissions.
- An environment may not have the data or it may not cover the requested dates.

The UI must present coverage and latency limits instead of assuming absence of rows means no failures.

### 11.2 Power Automate: documented Power Platform API

Microsoft documents a per-workflow run listing endpoint:

```http
GET https://api.powerplatform.com/powerautomate/environments/{environmentId}/flowRuns?workflowId={workflowId}&api-version=2024-10-01
```

**Implementation rule:** Do not assume this endpoint provides a fully independent or more real-time dataset than Dataverse FlowRun; its documented `404` response includes environments without a Dataverse database. A proof of concept must compare freshness, coverage, fields, authentication requirements, pagination behavior, and cost against direct Dataverse retrieval.

Use the documented and supported API, not private browser network endpoints.

**Recorded outcome of that comparison (M3):** direct Dataverse `flowrun` retrieval is the implemented
run source, because it needs no credential beyond the connection the operator already has. The
endpoint above requires its own delegated token for the Power Platform audience plus an app
registration, and its documented surface for run listing exposes no filter, page-size or continuation
parameter and no response body schema — so coverage and paging could only be built on
reverse-engineered behaviour, which this document rules out. The seam that would host a second
provider is the same `IWorkflowRunSource` the Dataverse provider implements. `docs/provider-capabilities.md`
holds the verified field-by-field evidence for the chosen source.

### 11.3 Logic Apps Consumption

Microsoft's Azure management REST API provides per-workflow run listing:

```http
GET https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Logic/workflows/{workflowName}/runs?api-version=2019-05-01
```

It supports server-side filters such as status, start time, and client tracking ID, plus pagination (`nextLink`). Enumerate resources only where Azure RBAC permits.

### 11.4 Logic Apps Standard

The run API is under the App Service / `Microsoft.Web/sites` host path:

```http
GET https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/sites/{siteName}/hostruntime/runtime/webhooks/workflow/api/management/workflows/{workflowName}/runs?api-version=2025-05-01
```

Confirm the selected supported API version at implementation time. Standard workflows must first be discovered within their host. For **stateless workflows**, normal durable run history is not available; debugging-mode history is not a production monitoring strategy.

### 11.5 Azure API Management discovery

Use Azure Resource Manager APIs to enumerate APIM APIs, backends, and policies. Illustrative read-only endpoints:

```http
GET https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.ApiManagement/service/{serviceName}/apis?api-version=2024-05-01

GET https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.ApiManagement/service/{serviceName}/backends?api-version=2024-05-01
```

The mapper may also need API-level/operation-level policies and effective inherited policy configuration. Inspect what can be confidently resolved, disclose ambiguity, and allow user confirmation. Never execute an APIM operation just to determine its backend.

### 11.6 Provider capability and completeness contract

Every data provider should publish:

- Which discovery/run/parent-link/date-filter capabilities it supports.
- Whether it returned a **complete, partial, stale, unsupported or unauthorized** result.
- The source timestamp / collection timestamp and the provider's last successful refresh.
- Known time-range and retention limitations.
- Whether deep-link generation is available for workflows and individual runs.

A single global **healthy** status must not override stale or failing source providers.

---

## 12. Authentication, authorization, and security

### 12.1 Dataverse

- Reuse the active XrmToolBox Dataverse connection via `PluginControlBase` and its provided service.
- Do not demand separate Dataverse credentials for the normal workflow inventory and run queries.
- Handle connection changes and disconnects; invalidate outdated query scopes and caches.
- Use the current identity's permissions; explain missing entities/rows or denied actions.

### 12.2 Azure

- XrmToolBox's Dataverse connection is **not automatically an Azure Resource Manager token**.
- Add a delegated, interactive Microsoft Entra ID authentication flow for Azure APIs; support tenant/subscription selection and sign-out.
- Depending on tenant policy and client configuration, an application registration, consent, and read permissions may be required. The plugin must document these prerequisites.
- Avoid shipping a client secret with the plugin. A desktop public-client pattern or another approved delegated approach is preferred.
- Use appropriate Azure RBAC permissions with the least privilege necessary for discovery and run history.
- Keep authentication token caches secure using supported mechanisms; never serialize access tokens into watchlist files or logs.

### 12.3 Data handling

- Store watchlists, rule configurations and UI preferences **locally** in the XrmToolBox user context.
- Do not create required Dataverse configuration tables or hosted stores.
- Keep run data primarily in memory; optionally cache non-sensitive metadata for the active session.
- Avoid raw payload persistence by default. Redact secrets from diagnostics and logs.
- Validate outbound portal links and constrain them to known Microsoft portal hosts/path structures.
- Normalize IDs using source + tenant/environment/subscription + resource/workflow + run, avoiding cross-tenant collisions.
- Provide clear consent/warning when exporting potentially sensitive error messages or identifiers.

### 12.4 Dependency interpretation

“Standalone” means **no additional runtime service that the developer deploys or operates**. The tool inevitably depends on XrmToolBox, supported .NET libraries, Microsoft services being available, and user permissions. Libraries can be bundled in the plugin package; their licensing and XrmToolBox assembly-isolation rules must be respected.

---

## 13. Proposed software architecture

### 13.1 High-level flow

```text
XrmToolBox (desktop host, Dataverse connection)
  |
  +-- Automation Monitor (WinForms UI)
        |
        +-- Monitor / Watchlists / Reports / Settings
        |
        +-- Application services
        |     +-- WatchlistManager
        |     +-- RunQueryCoordinator
        |     +-- RunNormalizer / StatusMapper
        |     +-- CorrelationResolver
        |     +-- RuleEvaluator
        |     +-- ReportGenerator
        |     +-- RefreshScheduler (only while open)
        |
        +-- Providers (read-only)
        |     +-- DataverseFlowProvider
        |     +-- PowerPlatformRunProvider (validate usefulness)
        |     +-- LogicAppsConsumptionProvider
        |     +-- LogicAppsStandardProvider
        |     +-- ApiManagementDiscoveryProvider
        |
        +-- Local configuration (versioned JSON/XrmToolBox settings)
        +-- In-memory run cache
```

### 13.2 Recommended implementation stack

- **Language:** C#.
- **Host/UI:** XrmToolBox plugin + Windows Forms.
- **Framework:** Target a .NET Framework version compatible with the installed XrmToolBox/selected template (proposed **.NET Framework 4.8**, to verify against the chosen SDK package).
- **Dataverse:** XrmToolBox-provided `IOrganizationService`/Dataverse SDK, using late-bound `Entity` / `QueryExpression` (avoid introducing conflicting early-bound SDK assemblies into the plugin host).
- **Azure REST:** `HttpClient` and documented REST APIs, with a tested Entra ID/MSAL-compatible auth strategy.
- **JSON:** A single compatible JSON serializer; store schema versions and migrate safely.
- **Excel:** A .NET Framework-compatible, redistributable XLSX library (evaluate packaging, license, memory usage); CSV can use standard .NET.
- **Testing:** Pure unit-testable application services; integration tests against a non-production environment and a test Azure subscription.

A modular architecture **inside one deployed plugin** is preferable to a microservice architecture. Additional local DLLs are permissible if correctly packaged/isolated.

### 13.3 Initial folder layout

```text
AutomationMonitor/
  Plugin/
    AutomationMonitorPlugin.cs
    AutomationMonitorControl.cs
  UI/
    MonitorView.cs
    WatchlistsView.cs
    ReportsView.cs
    RulesView.cs
    RunDetailsControl.cs
  Models/
    WorkflowReference.cs
    WorkflowRun.cs
    MonitoringQuery.cs
    Watchlist.cs
    MonitorRule.cs
    ProviderResult.cs
  Providers/
    IWorkflowProvider.cs
    DataverseFlowProvider.cs
    PowerPlatformRunProvider.cs
    LogicAppsConsumptionProvider.cs
    LogicAppsStandardProvider.cs
    ApiManagementDiscoveryProvider.cs
  Services/
    WatchlistManager.cs
    RunQueryCoordinator.cs
    CorrelationResolver.cs
    RuleEvaluator.cs
    PortalLinkService.cs
  Export/
    CsvExporter.cs
    XlsxExporter.cs
  Persistence/
    LocalSettingsRepository.cs
    WatchlistRepository.cs
  Tests/
    ...
```

The final project/file layout can evolve. Dependencies should flow from UI → application services → provider interfaces; providers must not directly manipulate WinForms controls.

---

## 14. Normalized data model (conceptual)

### 14.1 Workflow reference

| Field | Meaning |
|---|---|
| `ProviderKind` | PowerAutomate / LogicAppsConsumption / LogicAppsStandard. |
| `TenantId` | May be unknown for some Dataverse-only contexts. |
| `EnvironmentId` | Power Platform environment, where applicable. |
| `SubscriptionId` | Azure subscription, where applicable. |
| `ResourceGroup` | Azure resource group, where applicable. |
| `HostResourceId` | Azure ARM resource ID for a Standard host, where applicable. |
| `WorkflowId` | Source-specific stable workflow identifier. |
| `Name` | Display name. |
| `DisplayScope` | Solution, application, resource group, watchlist metadata. |
| `IsRunHistorySupported` | Indicates a known limitation such as stateless Standard. |

The **canonical workflow key** includes its provider and scope, not just its name.

### 14.2 Run record

| Field | Meaning |
|---|---|
| `RunKey` | Provider + canonical workflow key + native run ID. |
| `WorkflowKey` | Reference to normalized workflow. |
| `StartUtc`, `EndUtc` | UTC timestamps; end nullable. |
| `Duration` | Completed duration; running duration computed when displayed. |
| `RawStatus`, `NormalizedStatus` | Both preserved. |
| `ErrorCode`, `ErrorSummary` | Optional/permission-dependent. |
| `ParentRunId` | Optional provider-reported relationship. |
| `TrackingId` | Optional client/correlation tracking value. |
| `ProviderName` | Which API supplied the record. |
| `ObservedAtUtc` | When the plugin last received this observation. |
| `DataQuality` | Complete/partial/stale or flags explaining source limitations. |

Implemented as `WorkflowRun` + `RunKey` in the core: `Duration` is reported with its basis
(`Measured` from the stored instants, `ProviderReported` from the source's own column, or
`NotAvailable` while running) and a finished run with no end instant is a reported timing gap rather
than a zero; `TrackingId` is `CorrelationId`; `ProviderName` is `DataSource`; `ObservedAtUtc` is
`CheckedAtUtc`; `DataQuality` is `CoverageState`, carried on every row so an aggregate cannot wash the
qualifier out. See `docs/provider-capabilities.md` for what the current source can and cannot fill.

### 14.3 Watchlist

```json
{
  "schemaVersion": 1,
  "id": "WATCHLIST_ID",
  "name": "CRM Production Go-Live",
  "description": "Critical integration workflows for cutover monitoring",
  "membershipMode": "snapshot",
  "sourceSolutions": ["SOLUTION_ID"],
  "workflows": [
    "powerautomate:ENVIRONMENT_ID:WORKFLOW_ID",
    "logicapps-consumption:SUBSCRIPTION_ID:RESOURCE_ID"
  ],
  "excludedWorkflowKeys": [],
  "filterPresetId": "PRESET_ID"
}
```

IDs are placeholders. Actual persisted configuration should support per-workflow overrides, source scope identity, schema migration, and safe sharing without credentials.

### 14.4 Suggested interfaces (illustrative)

```csharp
public interface IWorkflowProvider
{
    Task<ProviderResult<IReadOnlyList<WorkflowReference>>>
        DiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken);

    Task<ProviderResult<IReadOnlyList<WorkflowRun>>>
        QueryRunsAsync(MonitoringQuery query, CancellationToken cancellationToken);

    Uri GetWorkflowPortalUri(WorkflowReference workflow);

    Uri GetRunPortalUri(WorkflowRun run);
}
```

This is **architecture pseudocode**, not compile-ready code. Real pagination should use a page/result stream or a separate paging contract; links must allow “not supported” rather than throwing for missing features. `ProviderResult` must carry errors, coverage, continuation, and freshness metadata.

---

## 15. Query engine, performance, and correctness

### 15.1 Collection strategy

- Prefer **server-side** time/status filtering for large datasets where supported.
- Use **paging/continuation** until all required results are obtained or a visible user-defined limit is reached.
- Bound concurrency for per-workflow calls and provide cancellation.
- Respect HTTP `429`, `Retry-After`, network timeouts, and transient errors; avoid immediate retry storms.
- Cache workflow metadata for the active connection; do not assume a cached flow catalogue is current forever.
- Refresh a bounded trailing time window and recheck previously running runs.
- Deduplicate using canonical keys while merging newer observations of the same run.
- Preserve provider-specific error and completeness information through to reports.

### 15.2 Report correctness

- A report is **complete only if every requested provider/scope was queried successfully and all required pages were consumed**.
- A cancelled or throttled export should be saved only with explicit partial status and warnings (or not saved at all, depending on user choice).
- Report totals must be computed from the **exported normalized dataset**, not from the current grid's first page.
- A “no failures” indicator must be qualified when source data is stale, unavailable, or not known to cover all selected workflows.
- Parent and child entries are distinct runs; group aggregates use explicit deduplication semantics.
- Missing expected runs only trigger rules when the workflow has a defined expected activity pattern.

### 15.3 Responsiveness goals (to benchmark, not guarantees)

- The UI remains responsive while retrieving and exporting data.
- Large queries show progress, cancellation, and per-provider completion.
- The default monitoring view fetches an appropriately bounded recent window, not unlimited history.
- Auto-refresh does not create overlapping full reloads or unlimited network work.
- Initial test target: a watchlist of **20–50 workflows** with an hour-long time window and mixed run volumes; tune concurrency and refresh interval based on actual API behaviour.

---

## 16. Portal deep links

Every provider owns two distinct link generators:

1. `GetWorkflowPortalUri(workflow)` — opens the relevant Power Automate flow or Azure Logic App workflow/resource page.
2. `GetRunPortalUri(run)` — opens the **specific execution**.

Requirements:

- Prefer documented/validated portal routing patterns and verify them in each platform.
- Use the selected environment, tenant, subscription, resource group, and native workflow/run ID.
- Handle different Logic Apps Standard vs Consumption routes.
- Open with the user's default browser and existing Microsoft authentication.
- Display a disabled action and helpful explanation when a direct run link cannot be produced.
- Do **not** try to extract credentials, signed callback URLs, or secret parameters to construct links.

Browser access continues to be subject to the user's permissions.

---

## 17. Local settings and deployment

### Persistence

Store locally:

- Watchlists and membership source definitions.
- Saved filter presets and recent date-range preferences.
- Global, watchlist, and workflow threshold rules.
- Column/order/layout settings.
- Refresh interval and optional local notification preferences.
- Non-secret Azure subscription/resource selection preferences.

Use a versioned JSON schema and/or XrmToolBox settings helpers. Scope settings by environment and tenant where appropriate, and support safe export/import. Never persist access tokens or client secrets inside these settings.

### Packaging

- Deliver as an **XrmToolBox plugin**, not as a Dataverse server plugin or installed Dataverse solution.
- Develop in Visual Studio using XrmToolBox's supported plugin template and `PluginControlBase`.
- Start using one Dataverse connection; consider `MultipleConnectionsPluginControlBase` only if cross-environment monitoring becomes a supported use case.
- Use `WorkAsync` or equivalent managed background operations to avoid blocking the WinForms UI; marshal updates to the UI thread.
- Package third-party dependencies according to XrmToolBox assembly isolation and distribution rules.
- First distribute for internal/local testing; later consider the XrmToolBox Tool Library.

### What happens when closed

When the plugin or application closes, **refreshing and notification stop**. On reopening, reload configuration and query the requested recent period. There is **no daemon, scheduled job, or cloud monitoring component**.

---

## 18. Development workflow and implementation plan

### Milestone 0 — Technical proof of concept

**Goal:** Prove data access and completeness before polishing the dashboard.

1. Create XrmToolBox WinForms plugin project; run and debug inside XrmToolBox.
2. Reuse an active Dataverse connection.
3. List solutions and resolve cloud flows via `solutioncomponent`.
4. Query recent FlowRun metadata for those workflows.
5. Populate a simple combined grid with status/start/duration/run identifiers.
6. Filter failures and an exact time range.
7. Export the returned full set to CSV.
8. Record observed run history latency/coverage and permission failures.
9. Test the documented Power Platform run-list API separately and compare results/freshness.

**Exit criteria:** A user can select a solution, view its visible flows and matching runs, filter failures, and export accurate CSV from a real test environment. Any coverage limitations are visible.

### Milestone 1 — Functional Power Automate release

- Save and reload snapshot/dynamic watchlists.
- Add manual selection and global-covered failure view.
- Add auto-refresh and polling health.
- Implement advanced date/time/status/duration filters.
- Add per-workflow rules with highlight-only mode.
- Implement parent/child navigation for available relationships.
- Add workflow and run deep links.
- Add CSV/XLSX reports with provenance/partial warning pages.
- Handle disconnected/unauthorized/stale states cleanly.

**Exit criteria:** Go-live monitoring of selected Power Automate flows is useful without Azure connections or additional infrastructure.

### Milestone 2 — Logic Apps support

- Implement delegated Azure sign-in and tenant/subscription choice.
- Discover Consumption workflows.
- Discover Standard hosts and child workflows.
- Query run history through supported Azure REST APIs.
- Normalize statuses, run links, error fields, and tracking IDs.
- Merge Power Automate and Logic Apps into one grid/report.
- Show stateless Standard limitations accurately.

**Exit criteria:** One watchlist can combine supported Power Automate and Logic Apps workflows and produce a combined report.

### Milestone 3 — APIM-assisted discovery and usability polish

- List APIM APIs/backends/policies and propose Logic App mappings.
- Show confirmed/inferred/unresolved mapping levels.
- Support review and manual corrections.
- Generate watchlists from selected APIs/operations where mappings are confirmed.
- Improve correlation where tracking IDs are available.
- Add additional usability features such as reusable report presets and local, while-open failure notifications if desired.

**Exit criteria:** Users can form an APIM-related monitoring watchlist without assuming ambiguous gateway routes are authoritative.

### Deferred write/operations release

Only after explicit redesign and risk review: enable/disable workflows, cancel runs, resubmit executions. Require permission checks, confirmation dialogs, auditability, and safeguards against non-idempotent reruns. **Not included in first release.**

---

## 19. Acceptance criteria (testable)

### Discovery/watchlists

- **AC-01:** Given a solution containing cloud flows, selecting that solution lists qualifying flows, including flow ID and name.
- **AC-02:** Creating a watchlist saves membership locally and restores it after restarting XrmToolBox.
- **AC-03:** The same flow referenced by multiple solutions appears once in a watchlist.
- **AC-04:** A dynamic solution watchlist can detect changed membership without losing manual exclusions.

### Monitoring

- **AC-05:** The Monitor grid combines runs from at least two selected workflows and sorts them chronologically.
- **AC-06:** Filtering by a specific status and exact start/end date/time returns the corresponding run subset, with time-zone semantics documented.
- **AC-07:** A duration threshold can **highlight** a run without hiding other failed runs.
- **AC-08:** Workflow-level thresholds override global defaults.
- **AC-09:** A failed/unauthorized/stale source is visibly distinguished from a successful query returning zero failures.
- **AC-10:** Manual refresh and auto-refresh do not freeze the UI; a refresh can be cancelled safely.

### Correlation and navigation

- **AC-11:** When a source supplies a parent run ID and both runs are accessible, the UI shows the relationship.
- **AC-12:** When a parent or child cannot be resolved, the UI shows **unresolved** instead of inventing a link.
- **AC-13:** Open Workflow opens that workflow's main portal page; Open Run opens that specific execution when supported.

### Reports

- **AC-14:** CSV export contains all matching fetched pages, not just grid-visible rows.
- **AC-15:** XLSX includes summary, run-level history, failures, correlation, and metadata sheets.
- **AC-16:** UTC handling and a selected display time zone are consistent across the UI and export, including daylight-saving boundaries.
- **AC-17:** Partial exports clearly identify failed sources, missing pages, and report generation time.
- **AC-18:** Exported watchlists and reports contain no authentication secrets.

### Azure and independence

- **AC-19:** Power Automate-only features work without any Azure login.
- **AC-20:** Azure auth and Logic Apps retrieval use the user's approved delegated access and respect RBAC.
- **AC-21:** Standard stateless workflows do not appear falsely healthy due to missing run history.
- **AC-22:** No server, hosted API, database, Azure Function, or cloud monitoring worker must be deployed to run the product.
- **AC-23:** Closing XrmToolBox stops all polling/local notifications; the app makes no unattended monitoring claim.
- **AC-24:** The plugin performs no workflow modification, cancellation, or resubmission in the initial releases.

---

## 20. Risks, limitations, and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| FlowRun ingestion delay or gaps | Missed or late go-live failures | Freshness indicators; validate supported alternative APIs; never equate blank results with health. |
| Non-solution Power Automate flows | Incomplete global coverage | Explicit scope labelling; additional supported discovery sources only if justified and authorized. |
| Inconsistent Azure/Power Platform permissions | Missing workflows/runs | Connection diagnostics, provider-level errors, clear permission guidance. |
| Many workflows/high run volume | Throttling, slow UI and incomplete export | Server-side filters, pagination, concurrency limits, cancellation, `Retry-After`, incremental refresh. |
| Logic Apps Standard stateless | No standard persistent run history | Show unsupported/limited state; do not silently report zero runs. |
| APIM dynamic backend routing | Incorrect Logic App mappings | Confidence levels, review UI, manual mapping, policy-aware resolver. |
| Child flows outside watched scope | Broken hierarchy | On-demand lookup where authorized; unresolved placeholders and relationship provenance. |
| Cross-platform correlation absent | False association | Match only verified IDs; mark heuristic suggestions separately. |
| API/version changes | Broken provider | Encapsulate and test each provider; use documented public APIs and pinned versions. |
| Ambiguous portal URL schemes | Wrong workflow/run opened | Provider-specific validated link templates; fallback to workflow page. |
| Date/time and DST | Incorrect historical totals | Store UTC and explicit time zone, half-open intervals, DST tests. |
| Sensitive error content in exports | Privacy or security exposure | Metadata-first export, opt-in details, redaction, no tokens/payloads by default. |
| Library conflicts in XrmToolBox | Plugin load errors | Bundle/resolve DLLs per XrmToolBox guidance; test against current host. |

---

## 21. Testing strategy

### Unit tests

- Status normalization for all known provider statuses.
- Duration calculations for running and completed runs.
- UTC-to-local conversion, exact boundaries, and DST cases.
- Global/watchlist/workflow rule inheritance.
- Watchlist deduplication, dynamic reconciliation and JSON schema migration.
- Parent/child hierarchy with missing parents and accidental cycles.
- Export correctness, proper CSV quoting, Excel formula-injection safety.
- Provider result merging and freshness/partial status propagation.

### Integration tests

- Dataverse environment with sample solution-aware cloud flows and known run outcomes.
- Multiple solutions referencing the same flow.
- Real FlowRun paging and permission differences.
- Power Platform API response comparison with Dataverse FlowRun.
- Logic Apps Consumption and Standard stateful workflows, including running/failed/successful runs.
- Standard stateless workflow explicitly missing historical runs.
- Azure subscription with limited RBAC and another with authorized read access.
- APIM API with a clear backend, one with conditional policies, and one with no Logic App backend.

### Failure and scale tests

- HTTP 429 / `Retry-After`, transient 5xx, token expiration, cancellation during pagination.
- Connection changes mid-refresh and loss of network access.
- 20–50 workflows, differing run volumes, large historical intervals and large exports.
- A report spanning multiple providers where one provider fails; verify visible partial status.
- Valid workflow/run deep links in every supported portal surface.

---

## 22. Decisions made and questions to validate

### Decisions already made

- **Product format:** XrmToolBox desktop plugin.
- **Initial architecture:** Standalone/local only; no hosted worker, shared cloud database, or server component.
- **Initial permissions:** Read-only.
- **No closed-app monitoring or notifications.**
- **Primary value:** Saved watchlists and unified runs, especially during go-lives.
- **Platforms:** Power Automate first, then Logic Apps Consumption and Standard; APIM-assisted discovery later.
- **Reporting:** Exact date/time filters and CSV/XLSX exports across selected integrations.
- **Correlation:** Parent/child runs and evidence-based cross-platform tracking.
- **Navigation:** Direct workflow and run links.
- **Rules:** Global and per-workflow duration/status controls; filters separate from highlighting.
- **Future operations only:** Disable/enable and cancel/resubmit.

### Technical questions to answer during POC (not user blockers)

1. How complete and fresh are Dataverse FlowRun records in representative customer environments?
2. Does the documented Power Platform run-list endpoint add useful coverage/freshness or merely a different interface to substantially the same history?
3. Which permission levels yield reliable run metadata for ordinary support users without giving edit rights?
4. Which current portal deep links work reliably for Power Automate and both Logic Apps models?
5. Which OAuth desktop flow and client-registration model best supports Azure across different organizations?
6. What practical refresh intervals and concurrency limits avoid API throttling for 20–50 monitored workflows?
7. Which APIM policy patterns are feasible to resolve without executing or evaluating dynamic policy expressions?
8. What report sizes require streaming/memory-conscious XLSX generation?

These are **implementation experiments**, not reasons to delay the first proof of concept.

---

## 23. Short product definition (for a README or issue tracker)

**Automation Monitor for XrmToolBox** is a read-only desktop monitoring and reporting tool for Power Automate and Azure Logic Apps. It lets developers and go-live operators create watchlists manually or from Dataverse solutions, combine run histories on one dashboard, filter by precise date/time and execution characteristics, highlight long-running or failed runs, investigate parent-child relationships, jump directly to workflows or executions in Microsoft portals, and export consolidated CSV/Excel reports. A later phase adds APIM-assisted watchlist discovery. The initial product is entirely local to XrmToolBox: it uses Microsoft APIs under the user's permissions and requires no hosted monitoring service. It only monitors while the application is open; unattended notifications and workflow-management write operations are intentionally deferred.

---

## 24. Documentation and implementation references

These references describe the Microsoft/XrmToolBox capabilities that informed the design. API versions and permission details should be rechecked during implementation.

### XrmToolBox

- [XrmToolBox — developer documentation](https://www.xrmtoolbox.com/documentation/for-developers/)
- [PluginControlBase and WorkAsync](https://www.xrmtoolbox.com/documentation/for-developers/plugincontrolbase-base-class/)
- [MultipleConnectionsPluginControlBase](https://www.xrmtoolbox.com/documentation/for-developers/multipleconnectionsplugincontrolbase-base-class/)
- [Developing an XrmToolBox plugin](https://xrmtoolbox.microsoftcrmportals.com/documentation/for-developers/create-your-own-plugin-for-xrmtoolbox/)
- [Debugging an XrmToolBox plugin](https://www.xrmtoolbox.com/documentation/for-developers/debug/)

### Power Automate / Dataverse

- [Manage cloud flow run history in Dataverse](https://learn.microsoft.com/en-us/power-automate/dataverse/cloud-flow-run-metadata)
- [FlowRun Web API entity reference](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/flowrun?view=dataverse-latest)
- [Solution Component entity reference](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/solutioncomponent)
- [Solution-aware cloud flows](https://learn.microsoft.com/en-us/power-automate/create-flow-solution)
- [Power Platform API — List Flow Runs](https://learn.microsoft.com/en-us/rest/api/power-platform/powerautomate/flow-runs/list-flow-runs)
- [Power Automate monitoring and alerting guidance](https://learn.microsoft.com/power-automate/guidance/coding-guidelines/monitoring-and-alerting)

### Azure Logic Apps / API Management

- [Logic Apps Consumption — List workflow runs](https://learn.microsoft.com/en-us/rest/api/logic/workflow-runs/list?view=rest-logic-2019-05-01)
- [Logic Apps Standard / App Service — List workflow runs](https://learn.microsoft.com/en-us/rest/api/appservice/workflow-runs/list?view=rest-appservice-2025-05-01)
- [View Logic App run history and status](https://learn.microsoft.com/en-us/azure/logic-apps/view-workflow-status-run-history)
- [Standard stateless run-history limitations](https://learn.microsoft.com/en-us/azure/logic-apps/create-single-tenant-workflows-azure-portal?tabs=standard)
- [API Management backends](https://learn.microsoft.com/en-us/azure/api-management/backends)
- [API Management — List APIs](https://learn.microsoft.com/en-us/rest/api/apimanagement/apis/list-by-service?view=rest-apimanagement-2024-05-01)
- [API Management — List backends](https://learn.microsoft.com/en-us/rest/api/apimanagement/backend/list-by-service?view=rest-apimanagement-2024-05-01)
- [API Management — Get API policy](https://learn.microsoft.com/en-us/rest/api/apimanagement/api-policy/get?view=rest-apimanagement-2024-05-01)

---

**Document status:** This is the initial shared specification. It captures the requested feature set, architectural constraints, phased implementation strategy, and key limitations. It is intended to be directly usable as the foundation for an engineering backlog and implementation plan, without assuming that every proposed API capability has already been tested in a real tenant.
