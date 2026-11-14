using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The one shared wording for "what is in this watchlist". C6 put the discovery UI onto a persisted
/// watchlist with no run-history query behind it, so "N flow(s) monitored" became a claim this build
/// cannot honour - a saved list is a selection, not a running monitor. These tests pin the honest
/// "selected" wording and the corrected empty-state line, so a later edit cannot quietly restore the
/// misleading text (the count and mode are still asserted because they carry the member count and the
/// snapshot/dynamic label the C6 state line depends on).
/// </summary>
public class WatchlistSummaryTests
{
    private static readonly DateTimeOffset SeenAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static Watchlist TwoFlowSnapshot()
    {
        WatchlistBuilder builder = new WatchlistBuilder(
            "list-1", "Go-live", WatchlistMembershipMode.Snapshot, null, FlowRef.Environment);
        builder.AddManualFlow(FlowRef.Create("11111111-1111-1111-1111-1111111111aa", "Approvals"), SeenAt);
        builder.AddManualFlow(FlowRef.Create("22222222-2222-2222-2222-2222222222bb", "Nightly sync"), SeenAt);
        return builder.Build();
    }

    [Fact]
    public void DescribeSaysSelectedNotMonitoredWhenNoRunHistoryRan()
    {
        string line = WatchlistSummary.Describe(TwoFlowSnapshot());

        Assert.Contains("2 flow(s) selected of 2 member(s)", line, StringComparison.Ordinal);
        Assert.DoesNotContain("monitored", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeStillNamesMembershipModeAndCheckTime()
    {
        string line = WatchlistSummary.Describe(TwoFlowSnapshot());

        Assert.Contains("snapshot", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("membership checked", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyLineStopsClaimingNothingIsEverSaved()
    {
        Assert.Equal("No watchlist selected. Nothing is being monitored yet.", WatchlistSummary.Empty);
        Assert.DoesNotContain("saved", WatchlistSummary.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
