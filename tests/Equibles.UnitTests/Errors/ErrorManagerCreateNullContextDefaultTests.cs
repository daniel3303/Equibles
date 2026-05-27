using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.Errors;

public class ErrorManagerCreateNullContextDefaultTests
{
    // ErrorManager.Create's body opens with two null-coalescing defaults:
    //   context ??= "Unknown";
    //   message ??= "No message provided";
    // The literal default strings are then passed unchanged to Truncate
    // and persisted as Error.Context / Error.Message. Real callers across
    // the platform pass null when the upstream exception path has no
    // context info: McpToolExecutor's catch-all reports a raw exception
    // with no scope tag; ErrorReporter's catch-Arg-null branch propagates
    // null straight into Create; the worker-host crash reporter has no
    // context at the outermost handler.
    //
    // The risk this pin uniquely catches:
    //   • A "tidy the defaults" refactor that drops the null-coalesce
    //     entirely (under the assumption that callers never pass null)
    //     would NRE on Truncate(null, 128) — the downstream Truncate
    //     body dereferences value.Length unconditionally — and crash
    //     the very error-reporting path that's supposed to be the
    //     LAST defence against an unhandled exception. The platform
    //     would silently lose error visibility on every crash whose
    //     context was null.
    //   • A "harmonize the defaults" refactor that swaps the two
    //     literals (`context ??= "No message provided"`, `message ??=
    //     "Unknown"`) would compile, never crash, and silently render
    //     every null-context row as Context="No message provided" in
    //     the errors dashboard — wrong but plausible, until an
    //     operator notices the cross-wired strings.
    //   • A "consolidate to one literal" refactor (`context ??= "?";
    //     message ??= "?"`) would mangle the display strings without
    //     a crash. The dashboard column would lose its semantic
    //     distinction between unknown-context and missing-message.
    //
    // Strategy: substitute ErrorRepository (BaseRepository methods are
    // virtual, NSubstitute can intercept Add and SaveChanges) and
    // capture the Error passed to Add. Asserting Context="Unknown"
    // AND Message="No message provided" on a single null-context + null-
    // message call distinguishes the working defaults from every
    // regression class above. The literal strings must match exactly —
    // the dashboard groups errors by (Context, Message) for the
    // recurring-errors widget, so any change to the literal corrupts
    // historical grouping.
    [Fact]
    public async Task Create_NullContextAndMessage_PersistsDocumentedDefaultLiterals()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new ErrorsModuleConfiguration() }
        );
        Error captured = null;
        var repo = Substitute.For<ErrorRepository>(db);
        repo.Add(Arg.Do<Error>(e => captured = e)).Returns(call => call.Arg<Error>());
        var sut = new ErrorManager(repo);

        await sut.Create(ErrorSource.McpTool, context: null, message: null, stackTrace: "stack");

        captured.Should().NotBeNull();
        captured!.Context.Should().Be("Unknown");
        captured.Message.Should().Be("No message provided");
    }
}
