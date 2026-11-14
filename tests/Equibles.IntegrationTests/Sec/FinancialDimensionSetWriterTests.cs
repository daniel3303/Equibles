using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

// The conflict-ignoring inserts only run on real Postgres.
[Collection(ParadeDbCollection.Name)]
public class FinancialDimensionSetWriterTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly ParsedXbrlDimension IPhone = new()
    {
        Axis = "srt:ProductOrServiceAxis",
        Member = "aapl:IPhoneMember",
    };
    private static readonly ParsedXbrlDimension Americas = new()
    {
        Axis = "srt:StatementGeographicalAxis",
        Member = "aapl:AmericasSegmentMember",
    };

    private FinancialDimensionSetWriter Writer() =>
        new(new FinancialDimensionSetRepository(DbContext));

    private static Dictionary<string, List<ParsedXbrlDimension>> ByKey(
        params List<ParsedXbrlDimension>[] sets
    ) => sets.ToDictionary(set => XbrlDimensionsKey.Compute(set));

    private async Task<List<FinancialDimensionSet>> StoredSets()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext
            .Set<FinancialDimensionSet>()
            .Include(set => set.Members)
            .AsNoTracking()
            .ToListAsync();
    }

    [Fact]
    public async Task AddMissing_SetsSharingAPair_StoresEveryPairOfEachOnce()
    {
        var sets = ByKey([], [IPhone], [IPhone, Americas]);

        (await Writer().AddMissing(sets, CancellationToken.None)).Should().Be(2);

        var stored = await StoredSets();
        stored
            .Select(set => set.Key)
            .Should()
            .BeEquivalentTo(
                XbrlDimensionsKey.Compute([IPhone]),
                XbrlDimensionsKey.Compute([IPhone, Americas])
            );
        stored
            .Single(set => set.Key == XbrlDimensionsKey.Compute([IPhone, Americas]))
            .Members.Select(member => (member.Axis, member.Member))
            .Should()
            .BeEquivalentTo([(IPhone.Axis, IPhone.Member), (Americas.Axis, Americas.Member)]);
        stored
            .Single(set => set.Key == XbrlDimensionsKey.Compute([IPhone]))
            .Members.Should()
            .ContainSingle();

        (await Writer().AddMissing(sets, CancellationToken.None)).Should().Be(0);
        (await StoredSets()).Should().HaveCount(2);
    }

    [Fact]
    public async Task AddMissing_PairsThatDoNotHashToTheirKey_AreRefused()
    {
        var sets = new Dictionary<string, List<ParsedXbrlDimension>>
        {
            [XbrlDimensionsKey.Compute([IPhone, Americas])] = [IPhone],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Writer().AddMissing(sets, CancellationToken.None)
        );

        (await StoredSets()).Should().BeEmpty();
    }
}
