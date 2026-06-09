using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="DocumentRepository.GetWithContent"/>: it must eager-load the
/// Content and CommonStock navigations the document viewer dereferences while
/// rendering. Lazy-loading proxies are enabled process-wide, so a GetWithContent
/// that omits the Includes makes each render open a fresh per-navigation
/// connection (Castle proxy -> LazyLoader.Load), which under concurrent traffic
/// exhausts the Portal connection pool.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentRepositoryGetWithContentEagerLoadsTests : ParadeDbMcpTestBase
{
    public DocumentRepositoryGetWithContentEagerLoadsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetWithContent_LoadsContentAndCommonStock_WithoutLazyLoading()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var file = new File
        {
            Name = "10k",
            Extension = "htm",
            ContentType = "text/html",
            Size = 100,
            FileContent = new FileContent { Bytes = new byte[] { 0x01 } },
        };
        var document = new Document
        {
            CommonStock = stock,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 1, 15),
        };
        DbContext.Add(stock);
        DbContext.Add(file);
        DbContext.Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new DocumentRepository(verify);

        var loaded = await sut.GetWithContent(document.Id);

        loaded.Should().NotBeNull();
        // IsLoaded reports eager loading without dereferencing the navigation —
        // dereferencing would itself lazily load it and mask the regression.
        verify.Entry(loaded).Reference(d => d.Content).IsLoaded.Should().BeTrue();
        verify.Entry(loaded).Reference(d => d.CommonStock).IsLoaded.Should().BeTrue();
    }
}
