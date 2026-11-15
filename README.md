# Automation Monitor

A **read-only** [XrmToolBox](https://www.xrmtoolbox.com/) plugin for discovering, organizing and
monitoring Power Automate cloud flows in the Dataverse environment you are already connected to.

> **Status: pre-MVP, under active development. MVP target: 31 December 2026.**
> There is no released download yet — no GitHub Releases, no tags, no Tool Library listing.

It runs on your machine, on the connection XrmToolBox already opened with your own credentials: no
hosted service, no database, no telemetry, and nothing that can change a flow.

## 1. What is Automation Monitor?

Cloud flows behind a Dataverse solution are easy to build and hard to keep an eye on: their runs sit on
per-flow pages in the maker portal, there is no combined view of the flows you care about, and nothing
says whether what you are looking at is the whole picture. The plugin answers three questions from inside
the admin tool you already use:

- **What automation is there?** The flows reachable through the solutions the connected user can read.
- **Which ones matter?** Watchlists you save locally and can hand to a colleague.
- **Did they run?** Status, timing, duration and failure detail — coverage stated rather than assumed, so
  "partial" never reads as "complete" and "unauthorized" never as "nothing happened".

Read-only is enforced rather than intended: no flow can be enabled, disabled, cancelled, resubmitted or
reconfigured here, and every Dataverse call the plugin makes is a read.

## 2. What it aims to do

One trustworthy view of an automation estate, held locally: browse what exists, keep the set that matters,
watch how it runs, and export what was seen — with every answer carrying how complete and how current it is.

## 3. Current development status

**Working today**, inside XrmToolBox on Windows:

- **Solution-based discovery** — the solutions of the connected environment, and the cloud flows inside a
  chosen one.
- **Persistent watchlists** — save the discovered set locally and reopen it later. A file this build
  cannot fully interpret is refused rather than rewritten.
- **Organization-qualified identity** — a list saved against a resolved environment records which
  Dataverse organization it belongs to, so two environments are never silently treated as one.
- **Explicit association of older lists** — a watchlist saved before that identity existed can be
  verified against the connected environment and re-keyed, but only from a preview the operator confirms.

**Built and tested, not on screen yet:** the run-history engine — a flow's runs, normalized statuses kept
alongside the raw ones, duplicates collapsed, coverage worked out. **No view shows run history today.**

**Not built:** solution-independent browsing, selecting individual flows into a list, reports, exports,
monitoring rules, alerts, Logic Apps, APIM, multi-environment lists.

## 4. MVP scope and roadmap — target 31 December 2026

The MVP is read-only monitoring of Power Automate cloud flows in one Dataverse environment:

1. Browse cloud flows without needing a solution as the entry point.
2. Select individual flows into a saved watchlist.
3. Monitor their run history — status, timing, duration, failure detail — behind refresh, cancel and an
   on-screen statement of coverage and staleness.
4. A usable interface: one view per job, readable at high display scaling, operable from the keyboard.

The rest of 2026 brings solution-independent browsing with flow selection, the run-history view, and a
stabilization pass ending in a packaged prerelease candidate; the roadmap states what must be observed.

## 5. Long-term vision

- **Multi-environment watchlists** — flows from DEV, TEST, UAT and PROD in one list, each member qualified
  by the organization it really belongs to, so a flow in two environments is never merged with itself.
- **More sources** — Azure Logic Apps (Consumption and Standard, with their real differences) and Azure
  API Management, each behind the same normalized model.
- **Reports and exports** that state their own period, watchlist, providers covered, and which sources
  were partial or failed.
- **Correlation where the data supports it** — runs linked by a recorded reference, never inferred from
  similar names or close timestamps.
- **Local by design** — monitoring happens while XrmToolBox is open and stops when it closes: no
  background worker, no queue, no cloud component.

## 6. Limitations

- Windows only, and only as an XrmToolBox plugin; there is no standalone or command-line mode.
- One environment per watchlist today; multi-environment lists are planned, not built.
- Discovery covers what the connected user can read, and run history is bounded by the provider's
  retention window and ingestion delay. The plugin reports both; it cannot remove them.
- No run history on screen yet, and no reports, exports, monitoring rules or alerts.
- Nothing is released: the code moves between builds and there is no supported upgrade path yet.

## 7. Documentation and license

| Document | What it owns |
|---|---|
| [`docs/specification.md`](docs/specification.md) | Product behaviour and its acceptance criteria |
| [`docs/roadmap.md`](docs/roadmap.md) | Current state, the sequence of work, what is still unproven |
| [`docs/provider-capabilities.md`](docs/provider-capabilities.md) | What each source supports: scope, paging, retention, freshness |
| [`docs/distribution.md`](docs/distribution.md) | How the Windows package is built, validated and released |

Automation Monitor is licensed under the **Apache License, Version 2.0**: [`LICENSE`](LICENSE) is the full
text, [`NOTICE`](NOTICE) the copyright statement and third-party record, and
[`CONTRIBUTORS.md`](CONTRIBUTORS.md) the authorship the published history can prove. Submitting a
contribution licenses it under Apache 2.0, as section 5 of the license provides; there is no contributor
license agreement and no copyright assignment.

