using Equibles.Media.BusinessLogic;

namespace Equibles.UnitTests.Media;

public class FileManagerStaticHelperTests {
    [Fact]
    public void AcceptedExtensionsString_JoinsAllExtensionsWithLeadingDotAndCommaSeparator() {
        // Mirror of the IImageManager.AcceptedExtensionsString helper test but
        // for the FileManager (generic-file) accept list. The exact output is
        // wired straight into HTML <input type="file" accept="..."> attrs on
        // upload forms — a refactor that drops the leading-dot prefix or swaps
        // \",.\" for a plain comma would either silently let non-document
        // uploads through OR render confusing labels. Pin the format so the
        // regression surfaces in unit tests rather than user-reported "wrong
        // file type accepted" tickets.
        var result = FileManager.AcceptedExtensionsString();

        result.Should().Be(".pdf,.png,.jpg,.jpeg,.xls,.xlsx,.doc,.docx,.txt,.psd");
    }
}
