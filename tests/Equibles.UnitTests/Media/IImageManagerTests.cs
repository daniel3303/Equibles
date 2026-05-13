using Equibles.Media.BusinessLogic;

namespace Equibles.UnitTests.Media;

public class IImageManagerTests {
    [Fact]
    public void AcceptedExtensionsString_JoinsExtensionsWithLeadingDotAndCommaSeparator() {
        // The interface's static helper exposes the upload-accept list in
        // the wire format browsers expect for <input type="file" accept="...">:
        // each extension prefixed with `.` and separated by `,`. Views and
        // validation messages render this string directly into HTML. A
        // refactor that drops the leading-dot prefix or swaps `,.` for a
        // plain comma would either break the accept filter (silently
        // letting non-image uploads through) or render confusing labels.
        // Pin the format so the regression surfaces in unit tests, not
        // user-reported "wrong file type accepted" tickets.
        var result = IImageManager.AcceptedExtensionsString();

        result.Should().Be(".png,.jpg,.jpeg,.gif,.bmp,.webp,.tiff,.svg");
    }
}
