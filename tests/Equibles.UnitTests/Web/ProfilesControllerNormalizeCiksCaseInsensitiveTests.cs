using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class ProfilesControllerNormalizeCiksValidationTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(ProfilesController).GetMethod(
        "NormalizeCiks",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Theory]
    [InlineData("12345678901234567")]
    [InlineData("１２３")]
    [InlineData("123x")]
    [InlineData("0000")]
    public void NormalizeCiks_InvalidMember_RejectsWholeBatch(string invalidCik)
    {
        var result = (List<string>)NormalizeMethod.Invoke(null, [new[] { "1234567", invalidCik }]);

        result.Should().BeNull();
    }
}
