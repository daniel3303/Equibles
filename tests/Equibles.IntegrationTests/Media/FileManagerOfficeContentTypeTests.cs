using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Media;

/// <summary>
/// The allowlisted Office extensions have MIME types longer than 64 characters (xlsx 65,
/// docx 71). Only Postgres enforces the column length, so this round-trips
/// against the migrated fixture.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FileManagerOfficeContentTypeTests : ParadeDbMcpTestBase
{
    public FileManagerOfficeContentTypeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Theory]
    [InlineData("report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(
        "letter.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    )]
    public async Task SaveFile_OfficeDocument_PersistsItsFullContentType(
        string name,
        string contentType
    )
    {
        var file = await FileManagerTestFactory
            .Create(new FileRepository(DbContext))
            .SaveFile([0x50, 0x4b, 0x03, 0x04], name);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var stored = await new FileRepository(verify)
            .GetAll()
            .Where(row => row.Id == file.Id)
            .Select(row => row.ContentType)
            .SingleAsync();

        stored.Should().Be(contentType);
    }
}
