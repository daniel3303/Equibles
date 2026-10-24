using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.Errors;

// Create caps Context, Message and RequestSummary to their columns without splitting a surrogate pair:
// exception text routinely carries emoji, and a lone surrogate fails the save of the very row that
// reports the failure. A null RequestSummary, the default, is stored as null.
public class ErrorManagerCreateCapsFieldsTests
{
    [Fact]
    public async Task Create_EmojiOnTheContextCap_DropsTheWholeCharacter()
    {
        var (sut, captured) = Build();

        await sut.Create(ErrorSource.McpTool, new string('a', 127) + "😀tail", "message", "stack");

        captured().Context.Should().Be(new string('a', 127));
    }

    [Fact]
    public async Task Create_LongMessageWithoutSurrogates_KeepsExactlyTheCap()
    {
        var (sut, captured) = Build();

        await sut.Create(ErrorSource.McpTool, "context", new string('m', 600), "stack");

        captured().Message.Should().HaveLength(512);
    }

    [Fact]
    public async Task Create_NoRequestSummary_StoresNull()
    {
        var (sut, captured) = Build();

        await sut.Create(ErrorSource.McpTool, "context", "message", "stack");

        captured().RequestSummary.Should().BeNull();
    }

    private static (ErrorManager Sut, Func<Error> Captured) Build()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new ErrorsModuleConfiguration() }
        );
        Error captured = null;
        var repo = Substitute.For<ErrorRepository>(db);
        repo.Add(Arg.Do<Error>(e => captured = e)).Returns(call => call.Arg<Error>());
        return (new ErrorManager(repo), () => captured);
    }
}
