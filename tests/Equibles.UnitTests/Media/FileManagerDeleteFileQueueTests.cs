using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Microsoft.Extensions.Options;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Media;

public class FileManagerDeleteFileQueueTests
{
    private static (
        FileManager Manager,
        FileRepository Files,
        PendingBlobDeletionRepository Queue
    ) CreateSut()
    {
        var files = Substitute.For<FileRepository>((EquiblesFinancialDbContext)null);
        var queue = Substitute.For<PendingBlobDeletionRepository>((EquiblesFinancialDbContext)null);
        var options = Options.Create(new FileStorageOptions());
        var manager = new FileManager(
            files,
            queue,
            new DatabaseFileStorageProvider(),
            new FileSystemFileStorageProvider(options),
            options
        );
        return (manager, files, queue);
    }

    // A filesystem-stored file's blob can't be unlinked inline (shared by dedup, racy),
    // so the delete must leave a mark the sweep can act on — carrying both the hash for
    // the reference re-check and the path to locate the blob.
    [Fact]
    public void DeleteFile_FileSystemFile_QueuesBlobDeletionAndDeletesRow()
    {
        var (manager, files, queue) = CreateSut();
        var file = new File
        {
            StorageProvider = StorageProvider.FileSystem,
            ContentHash = "sha256:abc123",
            RelativePath = "blob/sha256/ab/c1/abc123",
        };

        manager.DeleteFile(file);

        files.Received(1).Delete(file);
        queue
            .Received(1)
            .Add(
                Arg.Is<PendingBlobDeletion>(p =>
                    p.ContentHash == "sha256:abc123" && p.RelativePath == "blob/sha256/ab/c1/abc123"
                )
            );
    }

    // Database-stored files have no blob on disk; their FileContent row cascades away
    // with the delete, so nothing must be queued.
    [Fact]
    public void DeleteFile_DatabaseFile_DoesNotQueue()
    {
        var (manager, files, queue) = CreateSut();
        var file = new File { StorageProvider = StorageProvider.Database };

        manager.DeleteFile(file);

        files.Received(1).Delete(file);
        queue.DidNotReceive().Add(Arg.Any<PendingBlobDeletion>());
    }
}
