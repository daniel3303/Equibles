using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="DocumentTextToolsSearchKeywordErrorTests"/>. Pins
/// <c>ReadDocumentLines</c>'s catch arm: a repository failure (here, a disposed
/// DbContext) must be logged, persisted via the error manager, and returned as
/// the safe fallback message — never propagated to the MCP caller.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsReadLinesErrorTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsReadLinesErrorTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task ReadDocumentLines_RepositoryThrows_LogsReportsAndReturnsFallback()
    {
        // A repository over a disposed context throws on first access, driving
        // the catch arm. The error manager uses the live base context.
        var disposedContext = Fixture.CreateDbContext();
        disposedContext.Dispose();

        var sut = new DocumentTextTools(
            new DocumentRepository(disposedContext),
            ErrorManager,
            Substitute.For<ILogger<DocumentTextTools>>()
        );

        var output = await sut.ReadDocumentLines(Guid.NewGuid(), startLine: 1, endLine: 10);

        output.Should().Be("An error occurred while reading document lines. Please try again.");

        await using var verify = Fixture.CreateDbContext();
        var reported = await verify
            .Set<Error>()
            .AsNoTracking()
            .AnyAsync(e => e.Context == "ReadDocumentLines");
        reported.Should().BeTrue("the catch arm must persist the failure via the error manager");
    }
}
