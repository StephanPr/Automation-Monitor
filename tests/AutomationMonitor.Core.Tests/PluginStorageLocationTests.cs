using System;
using System.IO;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Plugin.Storage;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// How the plugin decides where its local files live. The core refuses to be handed a guessed directory,
/// so this is the one place a guess could hide. These cases pin that only a root the host or the operator
/// controls is ever used, that having no root is a refusal rather than a plausible default, and that
/// watchlists can never be pointed inside the folder XrmToolBox loads plugins from.
/// </summary>
public class PluginStorageLocationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "AutomationMonitor-LocationTests");

    [Fact]
    public void TheSettingsDirectoryTheHostReportsWins()
    {
        string host = Path.Combine(Root, "host-settings");
        string overrideRoot = Path.Combine(Root, "operator-root");

        bool resolved = PluginStorageLocation.TryResolve(host, overrideRoot, null, out PluginStorageLocation? location, out string? reason);

        Assert.True(resolved);
        Assert.Null(reason);
        Assert.NotNull(location);
        Assert.Equal(Path.Combine(host, SettingsLayout.DirectoryName), location!.DirectoryPath);
        Assert.Contains("XrmToolBox", location.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheOperatorOverrideIsUsedOnlyWhenTheHostHasNoAnswer(string? hostSettingsPath)
    {
        string overrideRoot = Path.Combine(Root, "operator-root");

        bool resolved = PluginStorageLocation.TryResolve(hostSettingsPath, overrideRoot, null, out PluginStorageLocation? location, out string? reason);

        Assert.True(resolved);
        Assert.Null(reason);
        Assert.NotNull(location);
        Assert.Equal(
            Path.Combine(overrideRoot, PluginStorageLocation.SettingsFolderName, SettingsLayout.DirectoryName),
            location!.DirectoryPath);
        Assert.Contains(PluginStorageLocation.RootOverrideVariable, location.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "  ")]
    public void WithNoHostPathAndNoOverrideThereIsNoLocationAndAReason(string? hostSettingsPath, string? overrideRoot)
    {
        bool resolved = PluginStorageLocation.TryResolve(hostSettingsPath, overrideRoot, null, out PluginStorageLocation? location, out string? reason);

        Assert.False(resolved);
        Assert.Null(location);
        Assert.False(string.IsNullOrWhiteSpace(reason));

        // The refusal has to name the one thing the operator can still set, or it is just a dead end.
        Assert.Contains(PluginStorageLocation.RootOverrideVariable, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SurroundingSpacesAreNotPartOfTheDirectory()
    {
        string host = Path.Combine(Root, "padded-settings");

        Assert.True(PluginStorageLocation.TryResolve("  " + host + "  ", null, null, out PluginStorageLocation? location, out _));
        Assert.Equal(Path.Combine(host, SettingsLayout.DirectoryName), location!.DirectoryPath);
    }

    [Fact]
    public void ASettingsDirectoryInsideThePluginsFolderIsRefused()
    {
        string plugins = Path.Combine(Root, "install", "Plugins");
        string insidePlugins = Path.Combine(plugins, "Settings");

        bool resolved = PluginStorageLocation.TryResolve(insidePlugins, null, plugins, out PluginStorageLocation? location, out string? reason);

        Assert.False(resolved);
        Assert.Null(location);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("plugins", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFolderNamedLikeThePluginsFolderButBesideItIsStillUsable()
    {
        // A prefix test that forgot the separator would refuse this, and the real install would lose its
        // settings directory. The check is about containment, not about the letters a folder starts with.
        string plugins = Path.Combine(Root, "install", "Plugins");
        string sibling = Path.Combine(Root, "install", "PluginsExtra");

        Assert.True(PluginStorageLocation.TryResolve(sibling, null, plugins, out PluginStorageLocation? location, out string? reason));
        Assert.NotNull(location);
        Assert.Null(reason);
    }

    [Fact]
    public void APathNoFolderCanHoldIsRefusedWithAReason()
    {
        // Exactly what a host that pasted something unexpected into its own path would hand over: the
        // composition must refuse, because SettingsLayout would throw when the first save was attempted.
        Assert.False(PluginStorageLocation.TryResolve("broken\0path", null, null, out PluginStorageLocation? location, out string? reason));
        Assert.Null(location);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TheDirectoryAlwaysEndsInThisPluginsOwnFolder()
    {
        Assert.True(PluginStorageLocation.TryResolve(Path.Combine(Root, "host-settings"), null, null, out PluginStorageLocation? location, out _));
        Assert.EndsWith(Path.DirectorySeparatorChar + SettingsLayout.DirectoryName, location!.DirectoryPath, StringComparison.Ordinal);
        Assert.Contains(SettingsLayout.DirectoryName, location.Description, StringComparison.Ordinal);
    }
}
