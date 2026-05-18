using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging;

namespace Equibles.IntegrationTests.Errors;

public class ErrorManagerCreateSurrogateTruncationTests
{
    private readonly ErrorManager _sut;
    private readonly ErrorRepository _repository;

    public ErrorManagerCreateSurrogateTruncationTests()
    {
        var context = TestDbContextFactory.Create(
            new ErrorsModuleConfiguration(),
            new MessagingModuleConfiguration()
        );
        _repository = new ErrorRepository(context);
        _sut = new ErrorManager(_repository);
    }

    // Contract: Create caps Message to fit storage; the stored value is later
    // persisted to text and JSON-serialized for the dashboard, so truncation
    // must yield a valid string. Index-slicing at 512 must not split a
    // surrogate pair (emoji/non-BMP chars are common in exception text) into a
    // dangling lone surrogate.
    [Fact]
    public async Task Create_MessageTruncationAtSurrogatePair_DoesNotLeaveLoneSurrogate()
    {
        // 511 'a' + "😀" (U+1F600, 2 UTF-16 units) => length 513; a raw [..512]
        // slice keeps the high surrogate at index 511 and drops its low pair.
        var message = new string('a', 511) + "\U0001F600";

        await _sut.Create(ErrorSource.McpTool, "ctx", message, "stack");

        var stored = _repository.GetAll().Single().Message;
        char.IsHighSurrogate(stored[^1]).Should().BeFalse();
    }
}
