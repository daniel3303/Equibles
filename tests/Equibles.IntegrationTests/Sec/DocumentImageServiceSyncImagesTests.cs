using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// A filing's image set is written buffered and made durable with one store sync before the
/// caller commits the rows that point at the blobs; an empty set syncs nothing.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentImageServiceSyncImagesTests : ParadeDbMcpTestBase
{
    private readonly IBufferedFileWriter _files = Substitute.For<IBufferedFileWriter>();

    public DocumentImageServiceSyncImagesTests(ParadeDbFixture fixture)
        : base(fixture)
    {
        _files
            .SaveInternalFileBuffered(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(_ =>
                Task.FromResult(
                    new File
                    {
                        Name = "image",
                        Extension = "png",
                        ContentType = "image/png",
                    }
                )
            );
    }

    private DocumentImageService Sut() =>
        new(new DocumentImageRepository(DbContext), new FileRepository(DbContext), _files);

    [Fact]
    public async Task SyncImages_WritesEveryImageBufferedAndSyncsTheStoreOnce()
    {
        var document = new Document { Id = Guid.NewGuid() };

        await Sut()
            .SyncImages(
                document,
                [
                    new CapturedImage("deck001.jpg", [1, 2, 3], "image/jpeg"),
                    new CapturedImage("logo.png", [4, 5], "image/png"),
                ]
            );

        await _files
            .Received(2)
            .SaveInternalFileBuffered(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
        _files.Received(1).SyncStore();
    }

    [Fact]
    public async Task SyncImages_NoImages_DoesNotSyncTheStore()
    {
        await Sut().SyncImages(new Document { Id = Guid.NewGuid() }, []);

        _files.DidNotReceive().SyncStore();
    }
}
