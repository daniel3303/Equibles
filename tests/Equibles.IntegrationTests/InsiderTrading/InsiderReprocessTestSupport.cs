using Equibles.Media.BusinessLogic;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.InsiderTrading;

internal static class InsiderReprocessTestSupport
{
    /// <summary>
    /// An <see cref="IFileManager"/> substitute whose <c>GetContent</c> mirrors the
    /// database storage provider (returns the file's seeded <c>FileContent</c> bytes).
    /// The reprocess manager reads its cached filing blob through the manager, so a bare
    /// substitute would intercept the read and return garbage — making every seeded cache
    /// look corrupt and silently changing the Processed/Fetched counts these tests pin.
    /// </summary>
    public static IFileManager NewFileManager()
    {
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .GetContent(Arg.Any<File>())
            .Returns(callInfo => ((File)callInfo[0]).FileContent?.Bytes);
        return fileManager;
    }
}
