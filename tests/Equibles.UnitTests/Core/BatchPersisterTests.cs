using Equibles.Worker;

namespace Equibles.UnitTests.Core;

public class BatchPersisterTests {
    [Fact]
    public async Task Persist_ItemsNotDivisibleByBatchSize_FlushesEveryItemAndReturnsCorrectTotal() {
        var items = Enumerable.Range(1, 7).ToList();
        var flushed = new List<List<int>>();

        var total = await BatchPersister.Persist(items, batchSize: 3, flushBatch: batch => {
            flushed.Add([..batch]);
            return Task.CompletedTask;
        });

        total.Should().Be(7);
        flushed.Should().HaveCount(3);
        flushed[0].Should().Equal(1, 2, 3);
        flushed[1].Should().Equal(4, 5, 6);
        flushed[2].Should().Equal(7);
    }
}
