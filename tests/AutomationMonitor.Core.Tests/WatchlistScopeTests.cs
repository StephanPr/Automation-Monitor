using AutomationMonitor.Core.Models;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The rule that decides whether the saved-watchlist catalogue has to be read again. AM-40: a connection
/// that reports no environment id uses the empty string as its scope, and a "have I read this yet" value
/// initialised to the empty string answered that question with "yes" forever - so a valid file sat on disk
/// while the drop-down stayed empty. These cases keep the two states, "never read" and "read for the
/// unscoped environment", from collapsing into each other again.
/// </summary>
public class WatchlistScopeTests
{
    private const string EnvironmentA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string EnvironmentB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    [Fact]
    public void ANeverReadCatalogueMustBeReadEvenOnAnUnscopedConnection()
    {
        // The AM-40 regression: this is the state a fresh session on a connection without an environment
        // id is in, and it is indistinguishable from "already read" unless the read state is nullable.
        Assert.True(WatchlistScope.NeedsRead(null, string.Empty, blocksSave: false));
        Assert.True(WatchlistScope.NeedsRead(null, EnvironmentA, blocksSave: false));
    }

    [Fact]
    public void AReadAlreadyTakenForAnUnscopedConnectionIsNotRepeated()
    {
        Assert.False(WatchlistScope.NeedsRead(string.Empty, string.Empty, blocksSave: false));
        Assert.False(WatchlistScope.NeedsRead(string.Empty, null, blocksSave: false));
        Assert.False(WatchlistScope.NeedsRead("   ", "  ", blocksSave: false));
    }

    [Fact]
    public void AConnectionThatNamesADifferentEnvironmentInvalidatesTheLastRead()
    {
        Assert.True(WatchlistScope.NeedsRead(EnvironmentA, EnvironmentB, blocksSave: false));
        Assert.True(WatchlistScope.NeedsRead(EnvironmentA, string.Empty, blocksSave: false));
        Assert.True(WatchlistScope.NeedsRead(string.Empty, EnvironmentA, blocksSave: false));
        Assert.False(WatchlistScope.NeedsRead(EnvironmentA, " " + EnvironmentA + " ", blocksSave: false));
    }

    [Fact]
    public void ARefusedReadIsAlwaysTriedAgain()
    {
        // The operator may have repaired or replaced the file by hand; a refusal must never be cached.
        Assert.True(WatchlistScope.NeedsRead(string.Empty, string.Empty, blocksSave: true));
        Assert.True(WatchlistScope.NeedsRead(EnvironmentA, EnvironmentA, blocksSave: true));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa  ", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public void NormalizeTreatsAMissingIdAsTheUnscopedScope(string? environmentId, string expected)
    {
        Assert.Equal(expected, WatchlistScope.Normalize(environmentId));
        Assert.Equal(expected.Length == 0, WatchlistScope.IsUnscoped(environmentId));
    }
}
