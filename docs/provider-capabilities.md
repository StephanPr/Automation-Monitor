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

## Power Automate run history, Logic Apps, APIM — not implemented

No run-history, Logic Apps or APIM endpoint is called yet. Their capability, freshness and
retention questions must each be answered by a spike recorded in this file before the
corresponding milestone is implemented: an unverified capability is disabled in the UI and
labelled, never presented as an empty grid.
