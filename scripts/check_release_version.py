#!/usr/bin/env python3
"""Gate a release tag against the source before any Windows build is spent.

Used by .github/workflows/release.yml. It prints ``key=value`` lines for
``$GITHUB_OUTPUT`` and fails with a plain-language reason when the tag may not be
released. Keeping the rule in a script rather than in shell inside the workflow
means it can be tested on any platform, which matters because the workflow itself
only ever runs on GitHub.
"""

from __future__ import annotations

import argparse
import os
import re
import sys

from package_windows import PackageError, normalise_version

CS_PROJ_VERSION_RE = re.compile(r"<Version>\s*(\d+\.\d+\.\d+)\s*</Version>")


def read_project_version(project_path: str) -> str:
    """Return the <Version>x.y.z</Version> declared by a project file."""
    if not os.path.isfile(project_path):
        raise PackageError("project file not found: %s" % project_path)
    with open(project_path, encoding="utf-8") as handle:
        text = handle.read()
    match = CS_PROJ_VERSION_RE.search(text)
    if not match:
        raise PackageError("no <Version>x.y.z</Version> found in %s" % project_path)
    return match.group(1)


def evaluate(tag: str, project_version: str, require_prerelease: bool) -> tuple:
    """Return (fields, problems). Problems empty means the tag may be released."""
    problems = []
    tag = (tag or "").strip()
    fields = {"tag": tag, "version": "", "numeric": "", "prerelease": ""}
    if not tag:
        problems.append("no tag was given")
        return fields, problems
    if not tag.startswith("v"):
        problems.append("tag '%s' must start with 'v', e.g. v0.1.0-alpha.1" % tag)
        return fields, problems
    if tag == "v" or "//" in tag:
        problems.append("tag '%s' is not a version" % tag)
        return fields, problems

    try:
        parts = normalise_version(tag)
    except PackageError as error:
        problems.append(str(error))
        return fields, problems

    fields["version"] = parts["full"]
    fields["numeric"] = parts["assembly_version"]
    fields["prerelease"] = parts["prerelease"]

    if project_version and parts["assembly_version"] != project_version:
        problems.append(
            "tag '%s' declares %s but the project declares <Version>%s</Version>; "
            "bump the project version in the same commit that cuts the release"
            % (tag, parts["assembly_version"], project_version)
        )
    if require_prerelease and not parts["prerelease"]:
        problems.append(
            "runtime behaviour inside XrmToolBox has not been signed off yet, so releases "
            "must carry a prerelease label, e.g. v%s-alpha.1" % parts["assembly_version"]
        )
    return fields, problems


def main(argv: list = None) -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description="Validate a release tag against the plugin project version.")
    parser.add_argument("--tag", required=True, help="Tag to publish, e.g. v0.1.0-alpha.1")
    parser.add_argument(
        "--project",
        default=os.path.join(root, "src", "AutomationMonitor.Plugin", "AutomationMonitor.Plugin.csproj"),
        help="Project file whose <Version> the tag must agree with",
    )
    parser.add_argument("--require-prerelease", default="true", help="'true' while runtime testing is outstanding")
    args = parser.parse_args(argv)

    require = str(args.require_prerelease).strip().lower() in ("1", "true", "yes")
    try:
        project_version = read_project_version(args.project)
    except PackageError as error:
        print("[AutomationMonitor.Release] ERROR: %s" % error, file=sys.stderr)
        return 1

    fields, problems = evaluate(args.tag, project_version, require)
    if problems:
        for problem in problems:
            print("[AutomationMonitor.Release] ERROR: %s" % problem, file=sys.stderr)
        return 1

    for key in ("tag", "version", "numeric", "prerelease"):
        print("%s=%s" % (key, fields[key]))
    print(
        "[AutomationMonitor.Release] tag %s is release-ready (project version %s%s)"
        % (fields["tag"], project_version, ", prerelease" if fields["prerelease"] else ""),
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
