#!/usr/bin/env python3
"""Validate a built Automation Monitor Windows distribution zip.

Runs anywhere Python does, which makes it usable both on a developer laptop and
in the Ubuntu CI job. It answers one question with a non-zero exit code: is this
archive complete, correctly laid out, self-consistent, and free of the
host-provided assemblies that break XrmToolBox plugin loading?

The archive's own manifest and SHA256SUMS.txt are treated as untrusted input and
recomputed from the bytes actually present.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import zipfile

from package_windows import (
    ASSEMBLY_ALLOW_LIST,
    CORE_ASSEMBLY,
    MANIFEST_NAME,
    MINIMUM_XRMTOOLBOX_VERSION,
    PLUGIN_ASSEMBLY,
    SCHEMA_VERSION,
    SUMS_NAME,
    TEXT_ASSETS,
    VERSION_RE,
)

REQUIRED_ENTRIES = ("Install.ps1", "Install.cmd", "README-WINDOWS.md", SUMS_NAME)

# Anything that the XrmToolBox host already owns, plus files that must never ship.
FORBIDDEN_NAME_PATTERNS = (
    re.compile(r"\.pdb$", re.I),
    re.compile(r"\.cs$", re.I),
    re.compile(r"\.csproj$", re.I),
    re.compile(r"\.props$", re.I),
    re.compile(r"\.targets$", re.I),
    re.compile(r"^runtimes/", re.I),
    re.compile(r"(^|/)(bin|obj|node_modules|\.git)/", re.I),
    re.compile(r"^(XrmToolBox|McTools|Microsoft|Newtonsoft|System|WeifenLuo|ScintillaNET)[./]", re.I),
    re.compile(r"WebView2Loader", re.I),
)
ZIP_NAME_RE = re.compile(r"^AutomationMonitor-v(.+)-win\.zip$")
EXPECTED_PLUGIN_DIR = "plugin/"


class Reporter:
    """Collects check results. Nothing is printed unless the caller asks, so the
    module stays usable as a library from the test suite."""

    def __init__(self) -> None:
        self.failures = []
        self.passes = 0
        self.results = []

    def check(self, ok: bool, code: str, detail: str = "") -> bool:
        if ok:
            self.passes += 1
            self.results.append("PASS %s" % code)
        else:
            self.failures.append(code)
            self.results.append("FAIL %s - %s" % (code, detail))
        return ok

    def render(self) -> str:
        return "\n".join(self.results)


def read_zip_bytes(archive: zipfile.ZipFile, name: str) -> bytes:
    with archive.open(name) as handle:
        return handle.read()


def read_optional(archive: zipfile.ZipFile, name: str):
    """None when the entry is absent; the layout checks already reported that."""
    if name not in archive.namelist():
        return None
    return read_zip_bytes(archive, name)


def check_layout(rep: Reporter, zip_path: str, archive: zipfile.ZipFile, expected_version: str) -> None:
    base = os.path.basename(zip_path)
    match = ZIP_NAME_RE.match(base)
    rep.check(bool(match), "zip-name", "got %s" % base)
    if match:
        rep.check(
            bool(VERSION_RE.match(match.group(1))),
            "zip-name-version",
            "version '%s' is not 0.1.0 or 0.1.0-label" % match.group(1),
        )
        if expected_version:
            wanted = VERSION_RE.match(expected_version.strip())
            wanted_full = wanted.group(0).lstrip("v") if wanted else expected_version
            rep.check(match.group(1) == wanted_full, "zip-version-matches-request", "%s != %s" % (match.group(1), wanted_full))

    names = [info.filename for info in archive.infolist()]
    rep.check(all(not n.startswith("/") and ".." not in n.split("/") for n in names), "zip-paths-safe", str(names))
    rep.check(all("\\" not in n for n in names), "zip-paths-no-backslash", str(names))
    rep.check(not any(n.endswith("/") for n in names), "zip-no-directory-entries", str(names))

    for required in REQUIRED_ENTRIES:
        rep.check(required in names, "required-file:%s" % required, "missing from archive")
    for name in ASSEMBLY_ALLOW_LIST:
        rep.check(EXPECTED_PLUGIN_DIR + name in names, "required-assembly:%s" % name, "missing from plugin/")
    rep.check(EXPECTED_PLUGIN_DIR + MANIFEST_NAME in names, "required-file:manifest", "missing")

    allowed = set(REQUIRED_ENTRIES) | {EXPECTED_PLUGIN_DIR + n for n in ASSEMBLY_ALLOW_LIST} | {EXPECTED_PLUGIN_DIR + MANIFEST_NAME}
    unexpected = sorted(set(names) - allowed)
    rep.check(not unexpected, "no-unexpected-entries", ", ".join(unexpected))

    # Patterns are matched against the whole entry and against the bare file name:
    # a host assembly smuggled into a subfolder is still a host assembly.
    offenders = [n for n in names if any(p.search(n) or p.search(os.path.basename(n)) for p in FORBIDDEN_NAME_PATTERNS)]
    rep.check(not offenders, "no-host-or-debug-files", ", ".join(offenders))


def check_manifest(rep: Reporter, archive: zipfile.ZipFile, zip_version: str) -> None:
    raw = read_optional(archive, EXPECTED_PLUGIN_DIR + MANIFEST_NAME)
    if raw is None:
        return  # required-file:manifest already reported the missing entry
    try:
        manifest = json.loads(raw.decode("utf-8"))
    except ValueError as error:
        rep.check(False, "manifest-parses", str(error))
        return
    rep.check(manifest.get("schemaVersion") == SCHEMA_VERSION, "manifest-schema-version", str(manifest.get("schemaVersion")))
    rep.check(manifest.get("version") == zip_version, "manifest-version-matches-zip", "%s vs %s" % (manifest.get("version"), zip_version))
    rep.check(manifest.get("minimumXrmToolBoxVersion") == MINIMUM_XRMTOOLBOX_VERSION, "manifest-minimum-host-version", str(manifest.get("minimumXrmToolBoxVersion")))

    listed = [item.get("name") for item in manifest.get("files", [])]
    rep.check(sorted(listed) == sorted(ASSEMBLY_ALLOW_LIST), "manifest-file-set", str(listed))
    for item in manifest.get("files", []):
        name = EXPECTED_PLUGIN_DIR + str(item.get("name"))
        if name not in archive.namelist():
            rep.check(False, "manifest-entry-present:%s" % name, "listed but not in archive")
            continue
        data = read_zip_bytes(archive, name)
        rep.check(item.get("sha256") == hashlib.sha256(data).hexdigest(), "manifest-hash:%s" % item.get("name"), "bytes do not match the manifest")
        rep.check(item.get("size") == len(data), "manifest-size:%s" % item.get("name"), "%s vs %d" % (item.get("size"), len(data)))
        rep.check(item.get("target") == "Plugins", "manifest-target:%s" % item.get("name"), str(item.get("target")))


def check_sums(rep: Reporter, archive: zipfile.ZipFile) -> None:
    names = set(archive.namelist())
    raw_sums = read_optional(archive, SUMS_NAME)
    if raw_sums is None:
        return  # required-file:SHA256SUMS.txt already reported the missing entry
    lines = [line for line in raw_sums.decode("utf-8").splitlines() if line.strip()]
    recorded = {}
    malformed = []
    for line in lines:
        parts = line.split("  ", 1)
        if len(parts) != 2 or len(parts[0]) != 64:
            malformed.append(line)
            continue
        recorded[parts[1]] = parts[0]
    rep.check(not malformed, "sums-well-formed", "; ".join(malformed))

    payload = {n for n in names if n.startswith(EXPECTED_PLUGIN_DIR)}
    rep.check(set(recorded) == payload, "sums-covers-payload", "listed=%s actual=%s" % (sorted(recorded), sorted(payload)))
    for name, digest in recorded.items():
        if name in names:
            rep.check(digest == hashlib.sha256(read_zip_bytes(archive, name)).hexdigest(), "sum-hash:%s" % name, "content does not match SHA256SUMS.txt")


def check_assets(rep: Reporter, archive: zipfile.ZipFile, zip_version: str) -> None:
    def text(name: str) -> str:
        raw = read_optional(archive, name)
        # An absent asset reads as empty, which makes every content check below fail
        # rather than crash the validator.
        return "" if raw is None else raw.decode("utf-8")

    ps1 = text("Install.ps1")
    rep.check("\r\n" in ps1, "install-ps1-crlf", "script must use CRLF line endings")
    rep.check(ps1.replace("\r\n", "\n").startswith("#Requires -Version 5"), "install-ps1-requires-5.1", "must target Windows PowerShell 5.1 explicitly")
    for token in ("param(", "StorageRoot", "Unblock-File", "[AutomationMonitor.Install]", "backup", "PACKAGE_HASH_MISMATCH"):
        rep.check(token.lower() in ps1.lower(), "install-ps1-has:%s" % token, "expected behaviour marker missing")
    # A brace imbalance means the shipped script was truncated or mis-edited; it would
    # fail to parse on the customer's machine, and no token search would notice.
    rep.check(ps1.count("{") == ps1.count("}"), "install-ps1-braces-balanced", "script body is truncated or malformed")
    rep.check(ps1.lower().count("function ") >= 6, "install-ps1-function-count", "installer functions are missing")
    for forbidden in ("pwsh", "Set-ExecutionPolicy"):
        rep.check(forbidden.lower() not in ps1.lower(), "install-ps1-no:%s" % forbidden, "must not appear")

    cmd = text("Install.cmd")
    rep.check("\r\n" in cmd, "install-cmd-crlf", "batch file must use CRLF line endings")
    for token in ("powershell.exe", "-NoProfile", "-ExecutionPolicy Bypass", "-File", "%~dp0Install.ps1", "AM_NO_PAUSE", "%*"):
        rep.check(token in cmd, "install-cmd-has:%s" % token, "expected marker missing")
    rep.check("pwsh" not in cmd.lower(), "install-cmd-no-pwsh", "targets Windows PowerShell 5.1 only")
    cmd_lf = cmd.replace("\r\n", "\n")
    bare_pause = [line for line in cmd_lf.splitlines() if line.strip().lower() == "pause"]
    rep.check(not bare_pause, "install-cmd-pause-guarded", "unconditional pause would block unattended runs")

    readme = text("README-WINDOWS.md")
    for token in ("XrmToolBox", "Visual Studio", ".NET SDK", "restart", "Unblock", "Uninstall", "Plugins", MINIMUM_XRMTOOLBOX_VERSION, zip_version):
        rep.check(token.lower() in readme.lower(), "readme-windows-has:%s" % token, "required guidance missing")


def validate(zip_path: str, expected_version: str = "") -> Reporter:
    rep = Reporter()
    if not os.path.isfile(zip_path):
        rep.check(False, "zip-exists", zip_path)
        return rep
    rep.check(True, "zip-exists")
    with zipfile.ZipFile(zip_path) as archive:
        rep.check(archive.testzip() is None, "zip-integrity", str(archive.testzip()))
        match = ZIP_NAME_RE.match(os.path.basename(zip_path))
        zip_version = match.group(1) if match else ""
        check_layout(rep, zip_path, archive, expected_version)
        if zip_version:
            check_manifest(rep, archive, zip_version)
            check_assets(rep, archive, zip_version)
        check_sums(rep, archive)
    return rep


def main(argv: list = None) -> int:
    parser = argparse.ArgumentParser(description="Validate an Automation Monitor Windows distribution zip.")
    parser.add_argument("zip_path")
    parser.add_argument("--version", default="", help="Fail unless the archive version matches this value")
    parser.add_argument("--quiet", action="store_true", help="Only print the summary and failures")
    args = parser.parse_args(argv)

    rep = validate(args.zip_path, args.version)
    if not args.quiet:
        print(rep.render())

    print("[AutomationMonitor.Package] %d checks passed, %d failed" % (rep.passes, len(rep.failures)))
    for code in rep.failures:
        print("[AutomationMonitor.Package] FAIL %s" % code, file=sys.stderr)
    return 1 if rep.failures else 0


if __name__ == "__main__":
    sys.exit(main())


