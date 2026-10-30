#!/usr/bin/env python3
"""Build the Windows distribution package for Automation Monitor.

Runs on any platform with Python 3 - it needs no Windows, PowerShell or .NET. It
assembles ``AutomationMonitor-v<VERSION>-win.zip`` from a plugin build output
directory plus the packaging assets in ``packaging/``.

Two rules matter more than convenience here:

1. The payload is an explicit allow-list. The plugin build output also contains
   host-provided assemblies and RID-native files. Copying those into XrmToolBox'
   Plugins directory is the classic cause of plugin load failures, so nothing
   outside the allow-list is ever staged, whatever is present in the output.
2. Debug symbols, sources and build intermediates are never shipped.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
import zipfile

PRODUCT_NAME = "Automation Monitor"
PLUGIN_ASSEMBLY = "AutomationMonitor.Plugin.dll"
CORE_ASSEMBLY = "AutomationMonitor.Core.dll"
MANIFEST_NAME = "plugin.manifest.json"
SUMS_NAME = "SHA256SUMS.txt"
SCHEMA_VERSION = 1
TARGET_FRAMEWORK = "net48"
# Verified from host assembly metadata: XrmToolBox.exe 1.2025.10.74 binds to
# McTools.Xrm.Connection 1.2025.9.64, which this plugin also binds to by strong name.
MINIMUM_XRMTOOLBOX_VERSION = "1.2025.10.74"

# Files copied from packaging/ into the zip root.
TEXT_ASSETS = ("Install.ps1", "Install.cmd", "README-WINDOWS.md")
# Install.cmd is parsed by cmd.exe and LF-only batch files misbehave, so script
# assets are normalised to CRLF on the way into the archive.
CRLF_ASSETS = ("Install.ps1", "Install.cmd")

ASSEMBLY_ALLOW_LIST = (PLUGIN_ASSEMBLY, CORE_ASSEMBLY)
VERSION_RE = re.compile(
    r"^v?(?P<major>\d+)\.(?P<minor>\d+)\.(?P<patch>\d+)(?:-(?P<prerelease>[0-9A-Za-z.-]+))?$"
)


class PackageError(Exception):
    """A packaging precondition failed. Message text is user-facing."""


def normalise_version(raw: str) -> dict:
    """Split a tag or plain version into its parts, rejecting anything unversioned."""
    match = VERSION_RE.match(raw.strip())
    if not match:
        raise PackageError(
            "version '%s' is not of the form 0.1.0 or 0.1.0-label "
            "(an optional leading 'v' is allowed)" % raw
        )
    numeric = "%d.%d.%d" % (
        int(match.group("major")),
        int(match.group("minor")),
        int(match.group("patch")),
    )
    prerelease = match.group("prerelease") or ""
    return {
        "full": numeric if not prerelease else "%s-%s" % (numeric, prerelease),
        "assembly_version": numeric,
        "file_version": "%s.0" % numeric,
        "prerelease": prerelease,
    }


def sha256_of(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(256 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()



def build_manifest(version: dict, plugin_dir: str) -> dict:
    """Describe the shipped assemblies; the installer trusts this over file names it sees."""
    files = []
    for name in ASSEMBLY_ALLOW_LIST:
        path = os.path.join(plugin_dir, name)
        if not os.path.isfile(path):
            raise PackageError("required assembly is missing from the build output: %s" % path)
        files.append(
            {
                "name": name,
                "sha256": sha256_of(path),
                "size": os.path.getsize(path),
                "target": "Plugins",
            }
        )
    return {
        "schemaVersion": SCHEMA_VERSION,
        "product": PRODUCT_NAME,
        "component": "AutomationMonitor.Plugin",
        "version": version["full"],
        "assemblyVersion": version["assembly_version"],
        "targetFramework": TARGET_FRAMEWORK,
        "minimumXrmToolBoxVersion": MINIMUM_XRMTOOLBOX_VERSION,
        "files": sorted(files, key=lambda item: item["name"]),
    }


def render_text_asset(assets_dir: str, name: str, replacements: dict) -> bytes:
    """Read a packaging asset, substitute @@TOKENS@@, and force CRLF for script files."""
    source = os.path.join(assets_dir, name)
    if not os.path.isfile(source):
        raise PackageError("required packaging asset is missing: %s" % source)
    with open(source, "rb") as handle:
        text = handle.read().decode("utf-8").replace("\r\n", "\n")
    for token, value in replacements.items():
        text = text.replace(token, value)
    if name in CRLF_ASSETS:
        text = text.replace("\n", "\r\n")
    return text.encode("utf-8")


def package(plugin_dir: str, assets_dir: str, version_raw: str, output_dir: str, emit_manifest_dir: str = None) -> str:
    """Create the distribution zip and return its path."""
    version = normalise_version(version_raw)
    if not os.path.isdir(plugin_dir):
        raise PackageError("plugin build output directory does not exist: %s" % plugin_dir)
    if not os.path.isdir(assets_dir):
        raise PackageError("packaging assets directory does not exist: %s" % assets_dir)

    manifest = build_manifest(version, plugin_dir)
    zip_name = "AutomationMonitor-v%s-win.zip" % version["full"]
    zip_path = os.path.join(output_dir, zip_name)
    os.makedirs(output_dir, exist_ok=True)

    replacements = {
        "@@VERSION@@": version["full"],
        "@@ASSEMBLY_VERSION@@": version["assembly_version"],
        "@@MINIMUM_XRMTOOLBOX_VERSION@@": MINIMUM_XRMTOOLBOX_VERSION,
        "@@PRODUCT@@": PRODUCT_NAME,
        "@@ZIP_NAME@@": zip_name,
    }

    # Payload first: everything under plugin/ plus the manifest that describes it.
    payload = {}
    for item in manifest["files"]:
        with open(os.path.join(plugin_dir, item["name"]), "rb") as handle:
            payload["plugin/" + item["name"]] = handle.read()
    payload["plugin/" + MANIFEST_NAME] = (
        json.dumps(manifest, indent=2, sort_keys=True) + "\n"
    ).encode("utf-8")

    sums = ["%s  %s" % (sha256_bytes(data), name) for name, data in sorted(payload.items())]
    entries = [(name, payload[name]) for name in sorted(payload)]
    for name in TEXT_ASSETS:
        entries.append((name, render_text_asset(assets_dir, name, replacements)))
    entries.append((SUMS_NAME, ("\n".join(sums) + "\n").encode("utf-8")))

    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in entries:
            info = zipfile.ZipInfo(name, date_time=(2000, 1, 1, 0, 0, 0))
            # Neutral mode: nothing in the package is executable by permission bit.
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)

    if emit_manifest_dir is not None:
        os.makedirs(emit_manifest_dir, exist_ok=True)
        with open(os.path.join(emit_manifest_dir, MANIFEST_NAME), "w", encoding="utf-8") as handle:
            handle.write(json.dumps(manifest, indent=2, sort_keys=True) + "\n")

    with open(zip_path + ".sha256", "w", encoding="utf-8") as handle:
        handle.write("%s  %s\n" % (sha256_of(zip_path), zip_name))
    return zip_path


def main(argv: list = None) -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description="Create the Automation Monitor Windows distribution zip.")
    parser.add_argument("--version", required=True, help="Release version, e.g. 0.1.0-alpha.1 (a leading 'v' is accepted)")
    parser.add_argument(
        "--plugin-output-dir",
        default=os.path.join(root, "src", "AutomationMonitor.Plugin", "bin", "Release", "net48"),
        help="Directory containing the built plugin assemblies",
    )
    parser.add_argument("--assets-dir", default=os.path.join(root, "packaging"), help="Directory holding the installer assets")
    parser.add_argument("--output", default=os.path.join(root, "dist"), help="Where to write the zip")
    parser.add_argument("--emit-manifest", default=None, help="Also write plugin.manifest.json here (developer deploy flow)")
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args(argv)

    try:
        zip_path = package(args.plugin_output_dir, args.assets_dir, args.version, args.output, args.emit_manifest)
    except PackageError as error:
        print("[AutomationMonitor.Package] ERROR: %s" % error, file=sys.stderr)
        return 1

    if not args.quiet:
        print("[AutomationMonitor.Package] created %s" % zip_path)
        print("[AutomationMonitor.Package] checksum %s.sha256" % zip_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
