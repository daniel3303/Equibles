using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The MaxFileNameLength cap exists so the File insert can never overflow — i.e. the capped
/// name must be DB-safe. An orphan surrogate corrupts the PostgreSQL UTF-8 round-trip (the
/// GH-3408 failure mode, guarded in EdgarXmlSubmissionParser.Truncate and the Congress
/// sibling), so a cut landing inside a surrogate pair must not keep the lone high surrogate.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentPersistenceServiceUpdateXbrlSurrogateFileNameTests : ParadeDbMcpTestBase
{
    public DocumentPersistenceServiceUpdateXbrlSurrogateFileNameTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task UpdateXbrl_FileNameCapSplitsSurrogatePair_PassesNoOrphanSurrogateToFileManager()
    {
        // 255 ASCII chars + "🏛" (U+1F3DB, two UTF-16 units) = 257 units: the 256-unit cap
        // lands exactly between the high and low surrogate.
        var fileName = new string('a', 255) + "🏛";
        string capturedName = null;
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .SaveInternalFile(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(ci =>
            {
                capturedName = ci.ArgAt<string>(1);
                return (Equibles.Media.Data.Models.File)null;
            });

        await using var ctx = Fixture.CreateDbContext();
        var service = new DocumentPersistenceService(
            new DocumentRepository(ctx),
            new ChunkRepository(ctx),
            fileManager,
            new DocumentImageService(
                new DocumentImageRepository(ctx),
                new Equibles.Media.Repositories.FileRepository(ctx),
                fileManager
            ),
            Substitute.For<IBus>()
        );

        await service.UpdateXbrl(
            new Document(),
            XbrlCaptureResult.Captured(XbrlType.InlineIxbrl, fileName, [1, 2, 3])
        );

        capturedName.Should().NotBeNull();
        capturedName.Length.Should().BeLessThanOrEqualTo(256);
        char.IsHighSurrogate(capturedName[^1])
            .Should()
            .BeFalse("a capped file name must not end in an orphan high surrogate");
    }
}
