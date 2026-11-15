# Contributors

The authorship record for Automation Monitor, as far as this repository's published history
can prove it. `NOTICE` credits "The Automation Monitor authors" collectively; this file says
what that means and where the evidence stops.

## Maintainer

| Person | Role | Evidence in this repository | Public identification |
|---|---|---|---|
| The repository owner | Creator and sole maintainer; author of every published commit to date | Commit author name `Stephan` on 44 of the 45 commits | GitHub [`StephanPr`](https://github.com/StephanPr) |

No legal entity is named anywhere in this repository's records - not in the commit metadata,
not in the plugin's `<Company>` / `<AssemblyTitle>` assembly identity (which carries the
product name, "Automation Monitor") - which is why `NOTICE` uses the collective form instead
of asserting a holder this repository cannot evidence. The maintainer can replace that line
with a personal or company name at any time; nothing else about the licensing changes.

## Commit identities as published

| Author name | Identity | Commits | Note |
|---|---|---|---|
| `Stephan` | a personal mailbox address | 44 | the development-machine identity for all feature and documentation work |
| `StephanPr` | GitHub's noreply identity for the account above | 1 | `Initial commit` (2026-10-29), created through GitHub's web interface |

Both identities belong to the same maintainer; the second is what GitHub writes when a commit
is made in the browser. The counts are those of the history as it stood when this file was added
and are not refreshed on every commit. The exact email addresses live in the commit metadata and
are not repeated here: this file is documentation, not a directory. Reproduce the record with

```text
git log --format='%an <%ae>' | sort | uniq -c
```

If you contribute from more than one address, add a `.mailmap` entry rather than asking for a
history rewrite.

## What this file does not claim

- **No inherited history.** Automation Monitor was written in this repository. It carries no
  attribution, no relicensing statement and no dependency list from any other project, and its
  published history begins with its own `Initial commit`.
- **No copyright transfer.** There is no contributor license agreement, no DCO and no
  assignment. Contributors keep their copyright and grant the Apache License, Version 2.0 to
  everyone by submitting code, as section 5 of that license provides.
- **No per-file claim.** Commit counts are not a measure of contribution, and no authorship is
  asserted here for an individual file. For a specific file, run
  `git log --follow --format='%h %an %ad' --date=short -- <path>`.
- **No employer position.** Nothing recorded here establishes whether an employer or client of
  any contributor has a claim on a commit. If one ever does, `NOTICE` has to name that holder
  too, and that change needs the evidence behind it.
- **Attribution, not endorsement.** Naming a maintainer records who wrote the code. It does not
  make them responsible for a fork or a derivative, and the license itself disclaims warranty
  and liability (sections 7 and 8).

## Adding yourself

Open a pull request. Submitting a contribution licenses it to everyone under the Apache
License, Version 2.0 - the license this project already carries - so nothing has to be signed
separately. Add a row to the Maintainer table with the name you want published, the GitHub
account you contribute from, and what you worked on. Do not edit another contributor's row: a
correction there needs the evidence that supports it.
