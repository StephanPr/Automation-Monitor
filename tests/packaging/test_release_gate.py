"""Tests for the release gate that .github/workflows/release.yml relies on.

The workflow itself can only run on GitHub, but the rule it enforces - a tag may
only be published when its numeric core equals the project version and, while
runtime testing is outstanding, when it carries a prerelease label - is plain
Python and is fully covered here.
"""

from __future__ import annotations

import contextlib
import io
import os
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "scripts"))

import check_release_version  # noqa: E402

PLUGIN_PROJECT = os.path.join(ROOT, "src", "AutomationMonitor.Plugin", "AutomationMonitor.Plugin.csproj")


class ReleaseGateTests(unittest.TestCase):
    def write_project(self, version):
        handle, path = tempfile.mkstemp(suffix=".csproj")
        os.close(handle)
        self.addCleanup(os.remove, path)
        body = "<Project><PropertyGroup>"
        if version is not None:
            body += "<Version>%s</Version>" % version
        body += "</PropertyGroup></Project>"
        with open(path, "w", encoding="utf-8") as stream:
            stream.write(body)
        return path

    def test_matching_prerelease_tag_passes(self):
        fields, problems = check_release_version.evaluate("v0.1.0-alpha.1", "0.1.0", True)
        self.assertEqual([], problems)
        self.assertEqual("0.1.0", fields["numeric"])
        self.assertEqual("alpha.1", fields["prerelease"])
        self.assertEqual("0.1.0-alpha.1", fields["version"])

    def test_plain_release_tag_is_blocked_while_runtime_testing_is_outstanding(self):
        _fields, problems = check_release_version.evaluate("v0.1.0", "0.1.0", True)
        self.assertEqual(1, len(problems))
        self.assertIn("prerelease", problems[0])

    def test_plain_release_tag_is_allowed_once_the_requirement_is_lifted(self):
        _fields, problems = check_release_version.evaluate("v0.1.0", "0.1.0", False)
        self.assertEqual([], problems)

    def test_tag_that_disagrees_with_the_project_is_blocked(self):
        _fields, problems = check_release_version.evaluate("v0.2.0-alpha.1", "0.1.0", True)
        self.assertEqual(1, len(problems))
        self.assertIn("0.2.0", problems[0])
        self.assertIn("<Version>0.1.0</Version>", problems[0])

    def test_tag_without_the_v_prefix_is_blocked(self):
        _fields, problems = check_release_version.evaluate("0.1.0-alpha.1", "0.1.0", True)
        self.assertIn("must start with 'v'", problems[0])

    def test_junk_tags_are_blocked(self):
        for bad in ("", "v", "v1", "v1.0", "v0.1.0.0", "v0.1.0-", "release-0.1.0"):
            _fields, problems = check_release_version.evaluate(bad, "0.1.0", True)
            self.assertTrue(problems, "tag %r should have been rejected" % bad)

    def test_project_without_a_version_is_reported(self):
        path = self.write_project(None)
        with self.assertRaises(check_release_version.PackageError):
            check_release_version.read_project_version(path)

    def test_project_version_is_read(self):
        self.assertEqual("0.1.0", check_release_version.read_project_version(self.write_project("0.1.0")))

    def test_the_repository_can_be_released_as_a_prerelease_today(self):
        project_version = check_release_version.read_project_version(PLUGIN_PROJECT)
        tag = "v%s-alpha.1" % project_version
        fields, problems = check_release_version.evaluate(tag, project_version, True)
        self.assertEqual([], problems, "the repo's own version must be releasable as a prerelease")
        self.assertEqual(project_version, fields["numeric"])

    def test_cli_emits_github_output_lines(self):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = check_release_version.main(["--tag", "v0.1.0-alpha.1", "--project", PLUGIN_PROJECT])
        self.assertEqual(0, code)
        lines = dict(line.split("=", 1) for line in out.getvalue().splitlines() if "=" in line)
        self.assertEqual("v0.1.0-alpha.1", lines["tag"])
        self.assertEqual("0.1.0", lines["numeric"])
        self.assertEqual("alpha.1", lines["prerelease"])

    def test_cli_fails_a_mismatched_tag(self):
        project = self.write_project("0.1.0")
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = check_release_version.main(["--tag", "v9.9.9-alpha.1", "--project", project])
        self.assertEqual(1, code)
        self.assertIn("ERROR", err.getvalue())
        self.assertEqual("", out.getvalue())

    def test_cli_honours_the_require_prerelease_switch(self):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            blocked = check_release_version.main(["--tag", "v0.1.0", "--project", PLUGIN_PROJECT, "--require-prerelease", "true"])
            allowed = check_release_version.main(["--tag", "v0.1.0", "--project", PLUGIN_PROJECT, "--require-prerelease", "false"])
        self.assertEqual(1, blocked)
        self.assertEqual(0, allowed)


if __name__ == "__main__":
    unittest.main()
