using Equibles.Worker;

namespace Equibles.UnitTests.Core;

public class BatchPersisterTests
{
    [Fact]
    public async Task Persist_ItemsNotDivisibleByBatchSize_FlushesEveryItemAndReturnsCorrectTotal()
    {
        var items = Enumerable.Range(1, 7).ToList();
        var flushed = new List<List<int>>();

        var total = await BatchPersister.Persist(
            items,
            batchSize: 3,
            flushBatch: batch =>
            {
                flushed.Add([.. batch]);
                return Task.CompletedTask;
            }
        );

        total.Should().Be(7);
        flushed.Should().HaveCount(3);
        flushed[0].Should().Equal(1, 2, 3);
        flushed[1].Should().Equal(4, 5, 6);
        flushed[2].Should().Equal(7);
    }

    [Fact]
    public async Task Persist_EmptyItems_DoesNotCallFlushBatchAndReturnsZero()
    {
        // Sibling to the divisible/non-divisible pins. Both existing tests
        // use non-empty inputs. The third structural shape — an empty
        // enumerable — exercises a distinct path: the foreach body never
        // runs AND the trailing `if (batch.Count > 0)` guard correctly
        // skips the would-be tail flush.
        //
        // The risk this catches: a refactor that drops the trailing
        // `if (batch.Count > 0)` guard (under the false intuition that
        // "the foreach already drained everything, the final flush is
        // dead code") would still pass both existing pins — both have
        // a leftover-but-non-empty final batch — and silently introduce
        // an empty-list flush call on every empty payload. The flush
        // callback is the EF-Core `repo.AddRange + SaveChanges` path
        // for FINRA / FRED / CFTC imports; an empty SaveChanges is a
        // wasted DB roundtrip per empty-input invocation. More subtly,
        // some callers' flush callbacks assume `batch.Count > 0` and
        // dereference `batch[0]` for logging/metadata — a regression
        // that drops the guard would IndexOutOfRange on empty input.
        //
        // The complementary risk: a refactor that swaps the foreach
        // body's flush-then-clear order (clear first, then flush a
        // now-empty list) would also pass the divisible pin's exact-
        // count assertion accidentally (because the flushed list would
        // still see the right invocation count), but would NOT pass
        // this empty-input pin's `Receive(0)` assertion — the
        // would-be empty flushes from the divisible path could
        // collide with the test setup if the implementation
        // miscounts on empty input.
        //
        // Pin: empty input. Assert total=0 AND no flush calls. The
        // dual assertion proves (a) no work happened and (b) the
        // final-flush guard fired correctly.
        var flushed = new List<List<int>>();

        var total = await BatchPersister.Persist(
            Enumerable.Empty<int>(),
            batchSize: 3,
            flushBatch: batch =>
            {
                flushed.Add([.. batch]);
                return Task.CompletedTask;
            }
        );

        total.Should().Be(0);
        flushed.Should().BeEmpty();
    }

    [Fact]
    public async Task Persist_ItemCountExactlyDivisibleByBatchSize_FlushesEachBatchOnceAndDoesNotDoubleFlush()
    {
        // The non-divisible sibling pins the "leftover partial batch" path. This pin
        // covers the boundary case the sibling can NOT catch: when the last full
        // batch fills the list (count % batchSize == 0), `batch.Clear()` MUST run
        // before the trailing `if (batch.Count > 0)` check — otherwise that trailing
        // block re-flushes the same items the foreach already flushed, doubling
        // every "exactly divisible" import.
        //
        // The production risk is concrete: Persist is the workhorse for FINRA short
        // volume, FINRA short interest, FRED series imports, and CFTC position
        // reports — all bulk inserts where `flushBatch` is `repo.AddRange + SaveChanges`.
        // A double-flush regression means the repo would re-AddRange entities EF
        // Core is still tracking from the previous flush, throwing
        // InvalidOperationException ("The instance of entity type ... cannot be
        // tracked because another instance with the same key value is already
        // being tracked") on every divisible-sized payload. CI without this pin
        // would only catch the bug if the test data happened to be non-divisible —
        // which is exactly the case the existing sibling uses. Pick 6 items at
        // batchSize=3 so the final flush is the boundary case (2 batches × 3,
        // nothing left over), with strict equality assertions on each batch and
        // an exact-count assertion on the flush list to detect the duplicate flush.
        var items = Enumerable.Range(1, 6).ToList();
        var flushed = new List<List<int>>();

        var total = await BatchPersister.Persist(
            items,
            batchSize: 3,
            flushBatch: batch =>
            {
                flushed.Add([.. batch]);
                return Task.CompletedTask;
            }
        );

        total.Should().Be(6);
        flushed.Should().HaveCount(2);
        flushed[0].Should().Equal(1, 2, 3);
        flushed[1].Should().Equal(4, 5, 6);
    }
}
