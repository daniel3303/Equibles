using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsRealtimeReplaySignalTests
{
    [Fact]
    public async Task BulkReplay_WakesSleepingRealtimeWorkerImmediately()
    {
        var signal = new HoldingsRealtimeReplaySignal();
        var services = new ServiceCollection().AddSingleton(signal).BuildServiceProvider();
        using var worker = new Holdings13FRealtimeWorker(
            Substitute.For<ILogger<Holdings13FRealtimeWorker>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            new ConfigurationBuilder().Build()
        );
        var method = typeof(Holdings13FRealtimeWorker).GetMethod(
            "WaitForNextCycle",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var sleeping = (Task)
            method.Invoke(worker, [TimeSpan.FromHours(6), CancellationToken.None])!;
        sleeping.IsCompleted.Should().BeFalse();

        signal.RequestReplay();

        await sleeping.WaitAsync(TimeSpan.FromSeconds(5));
        await services.DisposeAsync();
    }

    [Fact]
    public async Task RequestsCoalesceAndCancelledWaitDoesNotConsumeTheNextReplay()
    {
        var signal = new HoldingsRealtimeReplaySignal();
        signal.RequestReplay();
        signal.RequestReplay();
        await signal.WaitAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = signal.WaitAsync(cancellation.Token);
        pending.IsCompleted.Should().BeFalse();
        await cancellation.CancelAsync();
        var wait = () => pending;
        await wait.Should().ThrowAsync<OperationCanceledException>();
        signal.RequestReplay();
        await signal.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
