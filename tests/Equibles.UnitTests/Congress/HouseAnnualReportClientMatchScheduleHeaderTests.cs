using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Contract (HouseAnnualReportClient.cs:502-514): a small-caps "Schedule A"
/// header reduces to a 2-token line of `("S", _)` and `("A:", _)` after the
/// NUL-strip / Trim; MatchScheduleHeader must recognise it as the start of
/// the asset schedule and return 'A'. A regression that widens the letter
/// range, drops the colon check, or requires additional tokens would surface
/// as a wrong return value on this minimal 2-token input.
/// </summary>
public class HouseAnnualReportClientMatchScheduleHeaderTests
{
    [Fact]
    public void MatchScheduleHeader_SmallCapsScheduleAHeader_ReturnsA()
    {
        var method = typeof(HouseAnnualReportClient).GetMethod(
            "MatchScheduleHeader",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();

        var tokenType = typeof(HouseAnnualReportClient).GetNestedType(
            "ScheduleToken",
            BindingFlags.NonPublic
        );
        tokenType.Should().NotBeNull();
        var tokens = (System.Collections.IList)
            Activator.CreateInstance(typeof(List<>).MakeGenericType(tokenType))!;
        tokens.Add(Activator.CreateInstance(tokenType, "S", 0.0));
        tokens.Add(Activator.CreateInstance(tokenType, "A:", 50.0));

        var line = (System.Collections.IEnumerable)tokens;

        var result = (char)method!.Invoke(null, [line])!;

        result.Should().Be('A');
    }
}
