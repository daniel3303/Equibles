using System;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorProviderTimeoutTests
{
    private static SearchAggregator Build(params ISearchProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
        {
            services.AddScoped(typeof(ISearchProvider), _ => provider);
        }
        return new SearchAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchAggregator>.Instance
        );
    }

    [Fact]
    public async Task Search_ProviderIgnoresTokenAndStalls_GroupOmittedHealthyStillReturned()
    {
        // Contract (XML doc): "A provider that ... exceeds ProviderTimeout is logged and
        // dropped; one slow or broken module never breaks the results page." A provider
        // that ignores the cancellation token must still be backstopped by the timeout,
        // so the healthy group renders and the page does not stall on the slow one.
        var aggregator = Build(new StallingProvider("Slow", 0), new HealthyProvider("Healthy", 1));

        var result = await aggregator.Search("query", 5, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Category.Should().Be("Healthy");
    }

    [Fact]
    public async Task Search_AbandonedProviderStillRunning_ScopeReleasedOnlyAfterProviderSettles()
    {
        // A provider abandoned by the backstop may still be reading through its scope's
        // DbContext. Disposing the scope at that point hands a connection that is mid-read back
        // to the pool, so the scope must outlive the abandoned provider's work.
        var probe = new ScopeProbe();
        var services = new ServiceCollection();
        services.AddScoped<ISearchProvider>(_ => new ScopeHoldingProvider(probe));
        var aggregator = new SearchAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchAggregator>.Instance
        );
        using var requestAborted = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await aggregator.Search("query", 5, requestAborted.Token);

        result.Should().BeEmpty();
        probe.ScopeReleased.Task.IsCompleted.Should().BeFalse();

        probe.Release.SetResult();
        await probe.ScopeReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
        probe.ReleasedWhileRunning.Should().BeFalse();
    }

    // Ignores the cancellation token entirely and completes well past the 5s
    // ProviderTimeout — the exact "ignores the token" case the backstop guards.
    private sealed class StallingProvider : ISearchProvider
    {
        public StallingProvider(string category, int order)
        {
            Category = category;
            Order = order;
        }

        public string Category { get; }

        public int Order { get; }

        public async Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            return new SearchResultGroup
            {
                Category = Category,
                Order = Order,
                Hits = [new SearchHit { Title = "late" }],
            };
        }
    }

    private sealed class HealthyProvider : ISearchProvider
    {
        public HealthyProvider(string category, int order)
        {
            Category = category;
            Order = order;
        }

        public string Category { get; }

        public int Order { get; }

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new SearchResultGroup
                {
                    Category = Category,
                    Order = Order,
                    Hits = [new SearchHit { Title = "hit" }],
                }
            );
    }

    private sealed class ScopeProbe
    {
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ScopeReleased { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ReleasedWhileRunning { get; set; }
    }

    // Ignores its token and holds its scope until the test releases it, like a query whose
    // cancellation has not reached the server yet. Only the instance that searched reports.
    private sealed class ScopeHoldingProvider : ISearchProvider, IDisposable
    {
        private readonly ScopeProbe _probe;
        private bool _searched;
        private bool _running;

        public ScopeHoldingProvider(ScopeProbe probe) => _probe = probe;

        public string Category => "Holding";

        public int Order => 0;

        public async Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        )
        {
            _searched = true;
            _running = true;
            await _probe.Release.Task;
            _running = false;
            return new SearchResultGroup
            {
                Category = Category,
                Order = Order,
                Hits = [new SearchHit { Title = "late" }],
            };
        }

        public void Dispose()
        {
            if (!_searched)
                return;
            _probe.ReleasedWhileRunning |= _running;
            _probe.ScopeReleased.TrySetResult();
        }
    }
}
