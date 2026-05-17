using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;

namespace Equibles.IntegrationTests.Media;

public class FileManagerSaveFileExtensionAllowlistTests
{
    private readonly FileManager _sut;

    public FileManagerSaveFileExtensionAllowlistTests()
    {
        var context = TestDbContextFactory.Create(new MediaModuleConfiguration());
        _sut = new FileManager(new FileRepository(context));
    }

    // Contract: FileManager publishes a curated AcceptedExtensions allowlist
    // (+ AcceptedExtensionsString()). A caller relies on SaveFile enforcing it
    // — otherwise an executable/HTML/SVG payload is persisted and later served,
    // and the allowlist is decorative. SaveFile already throws ArgumentException
    // for a missing extension; a disallowed one must be rejected the same way.
    [Fact(Skip = "GH-766 — FileManager.SaveFile does not enforce AcceptedExtensions")]
    public async Task SaveFile_ExtensionNotInAcceptedAllowlist_IsRejected()
    {
        var act = async () => await _sut.SaveFile([0x4d, 0x5a], "malware.exe");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
