using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.InsiderTrading;

public class InsiderTradingModuleReprocessIndexTests
{
    [Fact]
    public void ParserVersionAccessionIndex_SupportsConcurrentOrderedReprocessSelection()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new InsiderTradingModuleConfiguration(),
                new MediaModuleConfiguration(),
            }
        );

        var entity = db.Model.FindEntityType(typeof(InsiderTransaction));
        entity.Should().NotBeNull();

        var index = entity
            .GetIndexes()
            .Single(i =>
                i.Properties.Select(p => p.Name)
                    .SequenceEqual(
                        new[]
                        {
                            nameof(InsiderTransaction.ParserVersion),
                            nameof(InsiderTransaction.AccessionNumber),
                        }
                    )
            );

        index
            .IsCreatedConcurrently()
            .Should()
            .BeTrue("the live insider table must remain writable while the index builds");
    }
}
