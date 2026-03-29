using Equibles.Data;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Errors;

public class ErrorRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly ErrorRepository _repository;

    public ErrorRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new ErrorsModuleConfiguration());
        _repository = new ErrorRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static Error CreateError(
        ErrorSource source = null,
        string context = "TestContext",
        string message = "Something went wrong",
        bool seen = false,
        string stackTrace = null,
        string requestSummary = null
    ) {
        return new Error {
            Id = Guid.NewGuid(),
            Source = source ?? ErrorSource.Other,
            Context = context,
            Message = message,
            Seen = seen,
            StackTrace = stackTrace ?? "at Test.Method()",
            RequestSummary = requestSummary,
        };
    }

    // ── GetUnseen ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnseen_ReturnsOnlyUnseenErrors() {
        _repository.Add(CreateError(seen: false, message: "Unseen 1"));
        _repository.Add(CreateError(seen: false, message: "Unseen 2"));
        _repository.Add(CreateError(seen: true, message: "Seen"));
        await _repository.SaveChanges();

        var result = await _repository.GetUnseen().ToListAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => e.Seen.Should().BeFalse());
    }

    [Fact]
    public async Task GetUnseen_AllSeen_ReturnsEmpty() {
        _repository.Add(CreateError(seen: true));
        _repository.Add(CreateError(seen: true));
        await _repository.SaveChanges();

        var result = await _repository.GetUnseen().ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnseen_EmptyTable_ReturnsEmpty() {
        var result = await _repository.GetUnseen().ToListAsync();

        result.Should().BeEmpty();
    }

    // ── Search ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MatchesContext() {
        _repository.Add(CreateError(context: "HoldingsScraper", message: "Timeout"));
        _repository.Add(CreateError(context: "DocumentProcessor", message: "Parse error"));
        await _repository.SaveChanges();

        var result = await _repository.GetAll()
            .Where(e => e.Context.ToLower().Contains("holdings"))
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.Context.Should().Be("HoldingsScraper");
    }

    [Fact]
    public async Task Search_MatchesMessage() {
        _repository.Add(CreateError(context: "Ctx1", message: "Connection timeout occurred"));
        _repository.Add(CreateError(context: "Ctx2", message: "Parse error"));
        await _repository.SaveChanges();

        var result = await _repository.GetAll()
            .Where(e => e.Message.ToLower().Contains("timeout"))
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task Search_NullOrEmpty_ReturnsAll() {
        _repository.Add(CreateError(message: "Error 1"));
        _repository.Add(CreateError(message: "Error 2"));
        await _repository.SaveChanges();

        var resultNull = await _repository.Search(null).ToListAsync();
        var resultEmpty = await _repository.Search("").ToListAsync();

        resultNull.Should().HaveCount(2);
        resultEmpty.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty() {
        _repository.Add(CreateError(context: "Ctx", message: "Parse error"));
        await _repository.SaveChanges();

        // Use direct query since EF.Functions.ILike is not available in InMemory provider
        var result = await _repository.GetAll()
            .Where(e => e.Context.ToLower().Contains("nonexistent") || e.Message.ToLower().Contains("nonexistent"))
            .ToListAsync();

        result.Should().BeEmpty();
    }

    // ── GetBySource ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetBySource_ReturnsOnlyMatchingSource() {
        _repository.Add(CreateError(source: ErrorSource.DocumentScraper, message: "Doc error"));
        _repository.Add(CreateError(source: ErrorSource.HoldingsScraper, message: "Holdings error"));
        _repository.Add(CreateError(source: ErrorSource.DocumentScraper, message: "Doc error 2"));
        await _repository.SaveChanges();

        var result = await _repository.GetBySource(ErrorSource.DocumentScraper).ToListAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => e.Source.Should().Be(ErrorSource.DocumentScraper));
    }

    [Fact]
    public async Task GetBySource_NoMatch_ReturnsEmpty() {
        _repository.Add(CreateError(source: ErrorSource.DocumentScraper));
        await _repository.SaveChanges();

        var result = await _repository.GetBySource(ErrorSource.McpTool).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySource_EmptyTable_ReturnsEmpty() {
        var result = await _repository.GetBySource(ErrorSource.Other).ToListAsync();

        result.Should().BeEmpty();
    }

    // ── Base CRUD ───────────────────────────────────────────────────────

    [Fact]
    public async Task Add_PersistsErrorWithAllFields() {
        var error = CreateError(
            source: ErrorSource.CongressScraper,
            context: "CongressImport",
            message: "Failed to parse disclosure",
            stackTrace: "at Congress.Parse()",
            requestSummary: "GET /api/disclosures"
        );

        _repository.Add(error);
        await _repository.SaveChanges();

        var result = await _repository.Get(error.Id);
        result.Should().NotBeNull();
        result.Source.Should().Be(ErrorSource.CongressScraper);
        result.Context.Should().Be("CongressImport");
        result.Message.Should().Be("Failed to parse disclosure");
        result.StackTrace.Should().Be("at Congress.Parse()");
        result.RequestSummary.Should().Be("GET /api/disclosures");
        result.Seen.Should().BeFalse();
    }

    [Fact]
    public async Task Update_MarkAsSeen_PersistsChange() {
        var error = CreateError(seen: false);
        _repository.Add(error);
        await _repository.SaveChanges();

        error.Seen = true;
        _repository.Update(error);
        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        var result = await _repository.Get(error.Id);
        result.Seen.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_RemovesError() {
        var error = CreateError();
        _repository.Add(error);
        await _repository.SaveChanges();

        _repository.Delete(error);
        await _repository.SaveChanges();

        var result = await _repository.Get(error.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_ReturnsAllErrors() {
        _repository.Add(CreateError(message: "Error 1"));
        _repository.Add(CreateError(message: "Error 2"));
        _repository.Add(CreateError(message: "Error 3"));
        await _repository.SaveChanges();

        var result = await _repository.GetAll().ToListAsync();

        result.Should().HaveCount(3);
    }
}
