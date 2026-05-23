using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// AvgPositionsPerFiler divides PositionCount by FilerCount. When FilerCount
/// is 0 (an empty report-date bucket or a newly-seeded database), the
/// property must return 0 rather than throwing DivideByZeroException. A
/// refactor that drops the FilerCount > 0 guard — e.g. simplifying to
/// (double)PositionCount / FilerCount — would crash every page that reads
/// the AUM trend data for an empty quarter.
/// </summary>
public class AumSnapshotAvgPositionsPerFilerZeroFilersTests
{
    [Fact]
    public void AvgPositionsPerFiler_ZeroFilerCount_ReturnsZero()
    {
        var snapshot = new AumSnapshot
        {
            ReportDate = new DateOnly(2024, 12, 31),
            TotalValue = 0,
            FilerCount = 0,
            PositionCount = 0,
        };

        snapshot.AvgPositionsPerFiler.Should().Be(0);
    }
}
