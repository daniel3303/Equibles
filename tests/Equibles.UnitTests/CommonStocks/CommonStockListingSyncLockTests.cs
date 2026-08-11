using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.UnitTests.CommonStocks;

public class CommonStockListingSyncLockTests
{
    [Fact]
    public async Task Acquire_HoldsTheSecondWriterUntilTheFirstReleases()
    {
        var first = await CommonStockListingSyncLock.Acquire();
        try
        {
            var secondTask = CommonStockListingSyncLock.Acquire();
            await Task.Yield();

            secondTask.IsCompleted.Should().BeFalse();

            first.Dispose();
            using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            first.Dispose();
        }
    }
}
