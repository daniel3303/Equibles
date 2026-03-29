using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Tests.Helpers;

namespace Equibles.Tests.Errors;

public class ErrorManagerTests {
    private readonly ErrorManager _sut;
    private readonly ErrorRepository _repository;

    public ErrorManagerTests() {
        var context = TestDbContextFactory.Create(new ErrorsModuleConfiguration());
        _repository = new ErrorRepository(context);
        _sut = new ErrorManager(_repository);
    }

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_NormalStrings_PersistsAllFields() {
        await _sut.Create(ErrorSource.McpTool, "TestContext", "Test message", "stack trace", "request");

        var errors = _repository.GetAll().ToList();
        errors.Should().HaveCount(1);
        var error = errors[0];
        error.Source.Should().Be(ErrorSource.McpTool);
        error.Context.Should().Be("TestContext");
        error.Message.Should().Be("Test message");
        error.StackTrace.Should().Be("stack trace");
        error.RequestSummary.Should().Be("request");
    }

    [Fact]
    public async Task Create_ContextExceeds128Chars_TruncatesTo128() {
        var longContext = new string('x', 200);

        await _sut.Create(ErrorSource.Other, longContext, "msg", null);

        var error = _repository.GetAll().Single();
        error.Context.Should().HaveLength(128);
    }

    [Fact]
    public async Task Create_ContextExactly128Chars_NotTruncated() {
        var context = new string('x', 128);

        await _sut.Create(ErrorSource.Other, context, "msg", null);

        var error = _repository.GetAll().Single();
        error.Context.Should().HaveLength(128);
        error.Context.Should().Be(context);
    }

    [Fact]
    public async Task Create_NullContext_DefaultsToUnknown() {
        await _sut.Create(ErrorSource.Other, null, "msg", null);

        var error = _repository.GetAll().Single();
        error.Context.Should().Be("Unknown");
    }

    [Fact]
    public async Task Create_EmptyContext_StoresEmptyString() {
        await _sut.Create(ErrorSource.Other, "", "msg", null);

        var error = _repository.GetAll().Single();
        error.Context.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_EmptyMessage_StoresEmptyString() {
        await _sut.Create(ErrorSource.Other, "ctx", "", null);

        var error = _repository.GetAll().Single();
        error.Message.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_MessageExceeds512Chars_TruncatesTo512() {
        var longMessage = new string('m', 600);

        await _sut.Create(ErrorSource.Other, "ctx", longMessage, null);

        var error = _repository.GetAll().Single();
        error.Message.Should().HaveLength(512);
    }

    [Fact]
    public async Task Create_MessageExactly512Chars_NotTruncated() {
        var message = new string('m', 512);

        await _sut.Create(ErrorSource.Other, "ctx", message, null);

        var error = _repository.GetAll().Single();
        error.Message.Should().HaveLength(512);
        error.Message.Should().Be(message);
    }

    [Fact]
    public async Task Create_NullMessage_DefaultsToNoMessageProvided() {
        await _sut.Create(ErrorSource.Other, "ctx", null, null);

        var error = _repository.GetAll().Single();
        error.Message.Should().Be("No message provided");
    }

    [Fact]
    public async Task Create_RequestSummaryExceeds512Chars_TruncatesTo512() {
        var longSummary = new string('r', 600);

        await _sut.Create(ErrorSource.Other, "ctx", "msg", null, longSummary);

        var error = _repository.GetAll().Single();
        error.RequestSummary.Should().HaveLength(512);
    }

    [Fact]
    public async Task Create_NullRequestSummary_RemainsNull() {
        await _sut.Create(ErrorSource.Other, "ctx", "msg", null);

        var error = _repository.GetAll().Single();
        error.RequestSummary.Should().BeNull();
    }

    [Fact]
    public async Task Create_StackTracePassesThrough_NoTruncation() {
        var longStack = new string('s', 5000);

        await _sut.Create(ErrorSource.Other, "ctx", "msg", longStack);

        var error = _repository.GetAll().Single();
        error.StackTrace.Should().Be(longStack);
    }

    // ── MarkAsSeen ──────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsSeen_SetsSeenToTrue() {
        await _sut.Create(ErrorSource.Other, "ctx", "msg", null);
        var error = _repository.GetAll().Single();
        error.Seen.Should().BeFalse();

        await _sut.MarkAsSeen(error);

        error.Seen.Should().BeTrue();
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesEntity() {
        await _sut.Create(ErrorSource.Other, "ctx", "msg", null);
        var error = _repository.GetAll().Single();

        await _sut.Delete(error);

        _repository.GetAll().Should().BeEmpty();
    }
}
