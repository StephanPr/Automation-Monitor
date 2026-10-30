#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

echo "[1/4] Run platform-neutral core unit tests"
dotnet test tests/AutomationMonitor.Core.Tests/AutomationMonitor.Core.Tests.csproj -c Debug

echo "[2/4] Build and run platform-neutral smoke test"
dotnet run --project tests/AutomationMonitor.Core.Smoke/AutomationMonitor.Core.Smoke.csproj -c Debug

echo "[3/4] Attempt .NET Framework 4.8 cross-compilation"
echo "If the XrmToolBox dependencies require Windows-specific build tasks, use GitHub Actions (windows-2022) for the plugin build."
dotnet build src/AutomationMonitor.Plugin/AutomationMonitor.Plugin.csproj -c Debug

echo "[4/4] Build and validate the Windows distribution package"
# The zip is platform-neutral work: it is built and checked here, on Linux, and
# only then exercised by the installer tests on the Windows runner.
dotnet build src/AutomationMonitor.Plugin/AutomationMonitor.Plugin.csproj -c Release -v q --nologo
python3 -m unittest discover -s tests/packaging
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/AutomationMonitor.Plugin/AutomationMonitor.Plugin.csproj | head -1)
python3 scripts/package_windows.py --version "$version" --output dist --quiet
python3 scripts/validate_windows_package.py "dist/AutomationMonitor-v$version-win.zip" --version "$version" --quiet

echo "Note: this does not load the plugin in XrmToolBox. That still needs Windows; see docs/distribution.md."

