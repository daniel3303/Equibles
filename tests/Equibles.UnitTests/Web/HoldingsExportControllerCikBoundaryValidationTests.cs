using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerCikBoundaryValidationTests
{
    private static HoldingsExportController Controller()
    {
        var constructor = typeof(HoldingsExportController).GetConstructors().Single();
        return (HoldingsExportController)
            constructor.Invoke(constructor.GetParameters().Select(_ => (object)null).ToArray());
    }

    [Theory]
    [InlineData("ſ234")]
    [InlineData("١٢٣")]
    [InlineData("12345678901234567")]
    [InlineData("0000")]
    public async Task InstitutionRejectsInvalidCikBeforeRepositoryAccess(string cik)
    {
        (await Controller().Institution(cik, null)).Should().BeOfType<NotFoundResult>();
    }
}
