"""Tests for the Windows distribution packager and validator.

These run with plain ``python3 -m unittest`` on any platform: the packager is the
part of this milestone that Linux can verify completely, so it is covered here
rather than deferred to CI. The PowerShell installer is covered separately by
Pester, which runs only on the Windows CI runner.
"""

from __future__ import annotations

import json
import os
import shutil
import sys
import tempfile
import unittest
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "scripts"))

import package_windows  # noqa: E402
import validate_windows_package  # noqa: E402

ASSETS_DIR = os.path.join(ROOT, "packaging")
EXPECTED_ZIP = "AutomationMonitor-v0.1.0-win.zip"
EXPECTED_ENTRIES = [
    "Install.cmd",
    "Install.ps1",
    "README-WINDOWS.md",
    "SHA256SUMS.txt",
    "plugin/AutomationMonitor.Core.dll",
    "plugin/AutomationMonitor.Plugin.dll",
    "plugin/plugin.manifest.json",
]


def make_build_output(directory):
    """A build output directory shaped like the real one, minus the noise."""
    os.makedirs(directory, exist_ok=True)
    for name in package_windows.ASSEMBLY_ALLOW_LIST:
        with open(os.path.join(directory, name), "wb") as handle:
            handle.write(b"MZ\x90\x00 fake assembly payload for " + name.encode("utf-8"))
    return directory


def add_build_noise(directory):
    """Files MSBuild leaves next to the plugin: host assemblies, pdb, RID natives, sources."""
    noise = {
        "AutomationMonitor.Plugin.pdb": b"pdb",
        "AutomationMonitor.Core.pdb": b"pdb",
        "XrmToolBox.exe": b"MZ host executable",
        "XrmToolBox.Extensibility.dll": b"MZ host assembly",
        "Microsoft.Xrm.Sdk.dll": b"MZ sdk assembly",
        "Newtonsoft.Json.dll": b"MZ json",
        "McTools.Xrm.Connection.dll": b"MZ connection",
        "runtimes/win-x64/native/WebView2Loader.dll": b"MZ native",
        "obj/project.assets.json": b"{}",
        "Install.ps1": b"# a stale copy must never win over packaging/Install.ps1",
    }
    for name, data in noise.items():
        target = os.path.join(directory, name)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "wb") as handle:
            handle.write(data)


def entries_of(zip_path):
    with zipfile.ZipFile(zip_path) as archive:
        return sorted(archive.namelist())


def read_entry(zip_path, name):
    with zipfile.ZipFile(zip_path) as archive:
        return archive.read(name)


def rewrite_zip(zip_path, add=None, remove=None, replace=None):
    """Modify a built archive in place, so the validator can be shown noticing."""
    add = add or {}
    replace = replace or {}
    drop = set(remove or [])
    keep = {}
    with zipfile.ZipFile(zip_path) as archive:
        for name in archive.namelist():
            if name not in drop:
                keep[name] = replace.get(name, archive.read(name))
    keep.update(add)
    os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w") as archive:
        for name, data in keep.items():
            archive.writestr(name, data)


class PackageTests(unittest.TestCase):
    def setUp(self):
        # A parent path with spaces and parentheses mirrors real Windows folders.
        self.tmp = tempfile.mkdtemp(prefix="Automation Monitor Packaging (test) ")
        self.build = make_build_output(os.path.join(self.tmp, "bin", "Release", "net48"))
        self.out = os.path.join(self.tmp, "dist")

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def build_zip(self, version="0.1.0", emit=None, assets=ASSETS_DIR):
        return package_windows.package(self.build, assets, version, self.out, emit)

    def test_happy_path_creates_expected_name_and_entries(self):
        zip_path = self.build_zip()
        self.assertEqual(os.path.join(self.tmp, "dist", EXPECTED_ZIP), zip_path)
        self.assertEqual(EXPECTED_ENTRIES, entries_of(zip_path))
        self.assertTrue(os.path.isfile(zip_path + ".sha256"))

    def test_build_output_noise_is_never_packaged(self):
        add_build_noise(self.build)
        zip_path = self.build_zip()
        names = entries_of(zip_path)
        for forbidden in (
            "AutomationMonitor.Plugin.pdb",
            "XrmToolBox.exe",
            "XrmToolBox.Extensibility.dll",
            "Microsoft.Xrm.Sdk.dll",
            "Newtonsoft.Json.dll",
            "McTools.Xrm.Connection.dll",
            "runtimes/win-x64/native/WebView2Loader.dll",
            "obj/project.assets.json",
        ):
            self.assertNotIn(forbidden, names)
            self.assertNotIn("plugin/" + os.path.basename(forbidden), names)
        self.assertNotIn(b"stale copy", read_entry(zip_path, "Install.ps1"))

    def test_missing_assembly_fails_and_writes_nothing(self):
        os.remove(os.path.join(self.build, "AutomationMonitor.Core.dll"))
        with self.assertRaises(package_windows.PackageError):
            self.build_zip()
        self.assertFalse(os.path.isdir(self.out) and os.listdir(self.out))

    def test_missing_asset_fails(self):
        partial_assets = os.path.join(self.tmp, "assets")
        os.makedirs(partial_assets)
        shutil.copy(os.path.join(ASSETS_DIR, "Install.ps1"), partial_assets)
        with self.assertRaises(package_windows.PackageError):
            package_windows.package(self.build, partial_assets, "0.1.0", self.out)

    def test_version_forms_and_assembly_version(self):
        zip_path = self.build_zip("v0.1.0-alpha.1")
        self.assertTrue(zip_path.endswith("AutomationMonitor-v0.1.0-alpha.1-win.zip"))
        manifest = json.loads(read_entry(zip_path, "plugin/plugin.manifest.json"))
        self.assertEqual("0.1.0-alpha.1", manifest["version"])
        self.assertEqual("0.1.0", manifest["assemblyVersion"])
        self.assertEqual(1, manifest["schemaVersion"])
        self.assertEqual("Plugins", manifest["files"][0]["target"])

    def test_unparsable_version_is_rejected(self):
        for bad in ("", "0.1", "v1", "1.0.0.0", "release-1"):
            with self.assertRaises(package_windows.PackageError):
                package_windows.normalise_version(bad)

    def test_manifest_hashes_match_packaged_bytes(self):
        zip_path = self.build_zip()
        manifest = json.loads(read_entry(zip_path, "plugin/plugin.manifest.json"))
        for item in manifest["files"]:
            data = read_entry(zip_path, "plugin/" + item["name"])
            self.assertEqual(item["size"], len(data))
            self.assertEqual(package_windows.sha256_bytes(data), item["sha256"])

    def test_scripts_are_crlf_and_tokens_replaced(self):
        zip_path = self.build_zip()
        for name in ("Install.ps1", "Install.cmd"):
            self.assertIn(b"\r\n", read_entry(zip_path, name), name)
        readme = read_entry(zip_path, "README-WINDOWS.md").decode("utf-8")
        self.assertIn("0.1.0", readme)
        self.assertNotIn("@@", readme)

    def test_versioned_readme_carries_the_package_name(self):
        zip_path = self.build_zip("1.2.3-beta.4")
        self.assertIn("AutomationMonitor-v1.2.3-beta.4-win.zip", read_entry(zip_path, "README-WINDOWS.md").decode("utf-8"))

    def test_emit_manifest_supports_the_developer_deploy_flow(self):
        emit = os.path.join(self.tmp, "side-by-side")
        self.build_zip(emit=emit)
        self.assertTrue(os.path.isfile(os.path.join(emit, "plugin.manifest.json")))

    def test_paths_with_spaces_and_parentheses_work(self):
        self.assertIn("(", self.tmp)
        self.assertTrue(os.path.isfile(self.build_zip()))


class ValidatorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="Automation Monitor Validate (test) ")
        self.build = make_build_output(os.path.join(self.tmp, "net48"))
        self.zip_path = package_windows.package(self.build, ASSETS_DIR, "0.1.0", os.path.join(self.tmp, "dist"))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def failures(self, zip_path=None, version=""):
        return validate_windows_package.validate(zip_path or self.zip_path, version).failures

    def test_clean_package_passes_every_check(self):
        self.assertEqual([], self.failures(version="0.1.0"))

    def test_tampered_assembly_is_caught(self):
        rewrite_zip(self.zip_path, replace={"plugin/AutomationMonitor.Core.dll": b"MZ tampered bytes"})
        codes = self.failures()
        self.assertTrue(any(code.startswith("manifest-hash:") for code in codes), codes)
        self.assertTrue(any(code.startswith("sum-hash:") for code in codes), codes)

    def test_missing_installer_file_is_caught(self):
        rewrite_zip(self.zip_path, remove=["Install.cmd"])
        self.assertIn("required-file:Install.cmd", self.failures())

    def test_missing_assembly_is_caught(self):
        rewrite_zip(self.zip_path, remove=["plugin/AutomationMonitor.Plugin.dll"])
        self.assertIn("required-assembly:AutomationMonitor.Plugin.dll", self.failures())

    def test_extra_host_assembly_in_the_package_is_caught(self):
        rewrite_zip(self.zip_path, add={"plugin/Newtonsoft.Json.dll": b"MZ not ours"})
        codes = self.failures()
        self.assertIn("no-unexpected-entries", codes)
        self.assertIn("no-host-or-debug-files", codes)

    def test_debug_and_native_leftovers_are_caught(self):
        rewrite_zip(
            self.zip_path,
            add={"plugin/AutomationMonitor.Plugin.pdb": b"x", "runtimes/win-x64/native/WebView2Loader.dll": b"x"},
        )
        codes = self.failures()
        self.assertIn("no-unexpected-entries", codes)
        self.assertIn("no-host-or-debug-files", codes)

    def test_zip_name_must_match_the_request(self):
        self.assertIn("zip-version-matches-request", self.failures(version="9.9.9"))

    def test_wrong_zip_name_is_caught(self):
        renamed = os.path.join(self.tmp, "plugin.zip")
        shutil.move(self.zip_path, renamed)
        self.assertIn("zip-name", self.failures(renamed))

    def test_lf_only_batch_file_is_caught(self):
        data = read_entry(self.zip_path, "Install.cmd").replace(b"\r\n", b"\n")
        rewrite_zip(self.zip_path, replace={"Install.cmd": data})
        self.assertIn("install-cmd-crlf", self.failures())

    def test_unconditional_pause_is_caught(self):
        data = read_entry(self.zip_path, "Install.cmd").decode("utf-8")
        stripped = [line for line in data.splitlines() if "AM_NO_PAUSE" not in line]
        rewrite_zip(self.zip_path, replace={"Install.cmd": ("\r\n".join(stripped) + "\r\npause\r\n").encode("utf-8")})
        self.assertIn("install-cmd-pause-guarded", self.failures())

    def test_pwsh_fallback_is_caught(self):
        data = read_entry(self.zip_path, "Install.cmd").decode("utf-8")
        rewrite_zip(self.zip_path, replace={"Install.cmd": (data + "\r\nrem pwsh fallback\r\n").encode("utf-8")})
        self.assertIn("install-cmd-no-pwsh", self.failures())

    def test_installer_targeting_another_shell_is_caught(self):
        data = read_entry(self.zip_path, "Install.ps1").decode("utf-8")
        rewritten = "#Requires -Version 7" + data.split("#Requires -Version 5", 1)[1]
        rewrite_zip(self.zip_path, replace={"Install.ps1": rewritten.encode("utf-8")})
        self.assertIn("install-ps1-requires-5.1", self.failures())

    def test_missing_host_version_guidance_in_readme_is_caught(self):
        data = read_entry(self.zip_path, "README-WINDOWS.md").decode("utf-8").replace("1.2025.10.74", "an old version")
        rewrite_zip(self.zip_path, replace={"README-WINDOWS.md": data.encode("utf-8")})
        self.assertIn("readme-windows-has:1.2025.10.74", self.failures())

    def test_manifest_version_drift_from_archive_name_is_caught(self):
        manifest = json.loads(read_entry(self.zip_path, "plugin/plugin.manifest.json"))
        manifest["version"] = "0.2.0"
        payload = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")
        rewrite_zip(self.zip_path, replace={"plugin/plugin.manifest.json": payload})
        self.assertIn("manifest-version-matches-zip", self.failures())

    def test_sums_file_must_cover_the_payload(self):
        rewrite_zip(self.zip_path, replace={"SHA256SUMS.txt": b""})
        self.assertIn("sums-covers-payload", self.failures())

    def test_truncated_installer_script_is_caught(self):
        """A mis-edited Install.ps1 must not reach a customer's machine."""
        data = read_entry(self.zip_path, "Install.ps1").decode("utf-8")
        damaged = data.replace("PACKAGE_HASH_MISMATCH", "DROPPED-BRANCH")
        damaged = damaged[: damaged.rindex("}")]
        rewrite_zip(self.zip_path, replace={"Install.ps1": damaged.encode("utf-8")})
        codes = self.failures()
        self.assertIn("install-ps1-braces-balanced", codes)
        self.assertTrue(
            any(code.startswith("install-ps1-has:PACKAGE_HASH_MISMATCH") for code in codes),
            codes,
        )



if __name__ == "__main__":
    unittest.main()


