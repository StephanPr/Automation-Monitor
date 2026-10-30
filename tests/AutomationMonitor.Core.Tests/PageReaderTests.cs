using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The shared paging rule. A provider that stops early must never look like a complete read,
/// and the continuation token must travel back to the source on every request.
/// </summary>
public class PageReaderTests
{
    private sealed class FakeSource
    {
        private readonly List<Page<int>> _pages;
        private readonly List<PageRequest> _requests = new List<PageRequest>();

        public FakeSource(params Page<int>[] pages)
        {
            _pages = new List<Page<int>>(pages);
        }

        public IReadOnlyList<PageRequest> Requests => _requests;

        public Page<int>? Next(PageRequest request)
        {
            _requests.Add(request);
            if (request.PageNumber < 1 || request.PageNumber > _pages.Count)
            {
                throw new InvalidOperationException("The reader asked for a page beyond the end of the source: " + request.PageNumber);
            }

            return _pages[request.PageNumber - 1];
        }
    }

    private static Page<int> PageOf(int count, int start, bool moreRecords, string? cookie = null)
    {
        List<int> items = new List<int>();
        for (int i = 0; i < count; i++)
        {
            items.Add(start + i);
        }

        return new Page<int>(items, moreRecords, cookie);
    }

    [Fact]
    public void SinglePage_WithNoMoreRecords_IsComplete()
    {
        FakeSource source = new FakeSource(PageOf(3, 0, false));

        PagedReadResult<int> result = PageReader.Read(source.Next);

        Assert.True(result.IsComplete);
        Assert.False(result.IsTruncated);
        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, result.PagesRead);
        Assert.Null(result.StopReason);
    }

    [Fact]
    public void EmptyFirstPage_IsACompleteEmptyResult()
    {
        FakeSource source = new FakeSource(PageOf(0, 0, false));

        PagedReadResult<int> result = PageReader.Read(source.Next);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.PagesRead);
    }

    [Fact]
    public void MultiplePages_AreAllConsumedAndCookieIsForwarded()
    {
        FakeSource source = new FakeSource(
            PageOf(2, 0, true, "cookie-1"),
            PageOf(2, 2, true, "cookie-2"),
            PageOf(1, 4, false));

        PagedReadResult<int> result = PageReader.Read(source.Next, pageSize: 2);

        Assert.True(result.IsComplete);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(3, result.PagesRead);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, result.Items);
        Assert.Equal(1, source.Requests[0].PageNumber);
        Assert.Null(source.Requests[0].Cookie);
        Assert.Equal("cookie-1", source.Requests[1].Cookie);
        Assert.Equal("cookie-2", source.Requests[2].Cookie);
    }

    [Fact]
    public void RowCeiling_StopsEarlyAndReportsPartial()
    {
        FakeSource source = new FakeSource(
            PageOf(4, 0, true, "c1"),
            PageOf(4, 4, true, "c2"),
            PageOf(4, 8, false));

        PagedReadResult<int> result = PageReader.Read(source.Next, pageSize: 4, maxItems: 8);

        Assert.False(result.IsComplete);
        Assert.True(result.IsTruncated);
        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.Equal(8, result.Items.Count);
        Assert.Equal(2, result.PagesRead);
        Assert.Contains("ceiling", result.StopReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PageCeiling_StopsEvenWhenSourceKeepsPromisingMore()
    {
        FakeSource source = new FakeSource(
            PageOf(1, 0, true, "c1"),
            PageOf(1, 1, true, "c2"),
            PageOf(1, 2, true, "c3"));

        PagedReadResult<int> result = PageReader.Read(source.Next, pageSize: 1, maxItems: 1000, maxPages: 3);

        Assert.False(result.IsComplete);
        Assert.Equal(3, result.PagesRead);
        Assert.Contains("pages", result.StopReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceThatNeverProgresses_StopsInsteadOfLooping()
    {
        FakeSource source = new FakeSource(PageOf(0, 0, true, "stuck"));

        PagedReadResult<int> result = PageReader.Read(source.Next);

        Assert.False(result.IsComplete);
        Assert.True(result.IsTruncated);
        Assert.Empty(result.Items);
        Assert.Contains("empty page", result.StopReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullPage_IsTreatedAsIncompleteNotAsFailure()
    {
        PagedReadResult<int> result = PageReader.Read(request => (Page<int>?)null);

        Assert.False(result.IsComplete);
        Assert.Equal(ProviderResultState.Partial, result.State);
    }

    [Fact]
    public void Cancellation_StopsBeforeTheNextRoundTrip()
    {
        using CancellationTokenSource source = new CancellationTokenSource();
        source.Cancel();
        int roundTrips = 0;

        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() =>
            PageReader.Read(
                request =>
                {
                    roundTrips++;
                    return PageOf(1, 0, true, "x");
                },
                cancellationToken: source.Token));

        Assert.Equal(0, roundTrips);
        Assert.Equal(source.Token, error.CancellationToken);
    }

    [Fact]
    public void InvalidArguments_ThrowInsteadOfSilentlyReadingNothing()
    {
        Assert.Throws<ArgumentNullException>(() => PageReader.Read<int>(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageReader.Read(request => PageOf(1, 0, false), pageSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageReader.Read(request => PageOf(1, 0, false), maxItems: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageReader.Read(request => PageOf(1, 0, false), maxPages: 0));
    }
}
