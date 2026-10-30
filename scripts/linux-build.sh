#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

echo "[1/3] Run platform-neutral core unit tests"
dotnet test tests/AutomationMonitor.Core.Tests/AutomationMonitor.Core.Tests.csproj -c Debug

echo "[2/3] Build and run platform-neutral smoke test"
dotnet run --project tests/AutomationMonitor.Core.Smoke/AutomationMonitor.Core.Smoke.csproj -c Debug

echo "[3/3] Attempt .NET Framework 4.8 cross-compilation"
echo "If the XrmToolBox dependencies require Windows-specific build tasks, use GitHub Actions (windows-2022) for the plugin build."
dotnet build src/AutomationMonitor.Plugin/AutomationMonitor.Plugin.csproj -c Debug
