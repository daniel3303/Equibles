using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class RawFilingArtifactRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly RawFilingArtifactRepository _repository;

    public RawFilingArtifactRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _repository = new RawFilingArtifactRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "AAPL", string cik = "0000320193")
    {
        return new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = cik,
        };
    }

    private static RawFilingArtifact CreateArtifact(
        Guid commonStockId,
        string accessionNumber = "0000320193-18-000145",
        RawFilingArtifactType artifactType = RawFilingArtifactType.InlineIxbrl,
        string sourceFileName = "aapl-20180929.htm",
        byte[] content = null
    )
    {
        content ??= [1, 2, 3, 4, 5];
        return new RawFilingArtifact
        {
            Id = Guid.NewGuid(),
            CommonStockId = commonStockId,
            AccessionNumber = accessionNumber,
            ArtifactType = artifactType,
            SourceFileName = sourceFileName,
            Content = content,
            UncompressedSize = 1000,
            CompressedSize = content.Length,
        };
    }

    [Fact]
    public async Task GetByStock_ReturnsOnlyArtifactsForThatStock()
    {
        var apple = CreateStock("AAPL", "0000320193");
        var microsoft = CreateStock("MSFT", "0000789019");
        _dbContext.Set<CommonStock>().AddRange(apple, microsoft);
        await _dbContext.SaveChangesAsync();

        _repository.Add(CreateArtifact(apple.Id, "0000320193-18-000145"));
        _repository.Add(
            CreateArtifact(
                apple.Id,
                "0000320193-18-000145",
                RawFilingArtifactType.StandaloneXbrl,
                "aapl-20180929.xml"
            )
        );
        _repository.Add(CreateArtifact(microsoft.Id, "0000789019-18-000123"));
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(apple).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.CommonStockId == apple.Id);
    }

    [Fact]
    public async Task GetByAccessionNumber_ExistingAccession_ReturnsArtifact()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateArtifact(stock.Id, "0000320193-18-000145"));
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0000320193-18-000145")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result.SourceFileName.Should().Be("aapl-20180929.htm");
    }

    [Fact]
    public async Task GetByAccessionNumber_NonExistentAccession_ReturnsEmpty()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(CreateArtifact(stock.Id, "0000320193-18-000145"));
        await _repository.SaveChanges();

        var any = await _repository.GetByAccessionNumber("9999999999-99-999999").AnyAsync();

        any.Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_PreservesCompressedContentBytes()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var bytes = Encoding.UTF8.GetBytes("the gzip-compressed envelope stand-in");
        _repository.Add(CreateArtifact(stock.Id, "0000320193-18-000145", content: bytes));
        await _repository.SaveChanges();

        var loaded = await _repository.GetByAccessionNumber("0000320193-18-000145").FirstAsync();

        loaded.Content.Should().Equal(bytes);
        loaded.CompressedSize.Should().Be(bytes.Length);
        loaded.UncompressedSize.Should().Be(1000);
    }

    [Fact]
    public async Task Exists_MatchesOnAccessionAndArtifactType()
    {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
        _repository.Add(
            CreateArtifact(stock.Id, "0000320193-18-000145", RawFilingArtifactType.InlineIxbrl)
        );
        await _repository.SaveChanges();

        var inlineExists = await _repository.Exists(
            "0000320193-18-000145",
            RawFilingArtifactType.InlineIxbrl
        );
        var standaloneExists = await _repository.Exists(
            "0000320193-18-000145",
            RawFilingArtifactType.StandaloneXbrl
        );
        var otherAccession = await _repository.Exists(
            "9999999999-99-999999",
            RawFilingArtifactType.InlineIxbrl
        );

        inlineExists.Should().BeTrue();
        standaloneExists.Should().BeFalse();
        otherAccession.Should().BeFalse();
    }
}
