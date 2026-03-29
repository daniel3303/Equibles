using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.Tests.Errors;

public class ErrorReporterTests {
    [Fact]
    public async Task Report_DelegatesToErrorManager_ErrorPersisted() {
        var context = TestDbContextFactory.Create(new ErrorsModuleConfiguration());
        var repository = new ErrorRepository(context);
        var errorManager = new ErrorManager(repository);
        var scopeFactory = ServiceScopeSubstitute.Create((typeof(ErrorManager), errorManager));
        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        await sut.Report(ErrorSource.McpTool, "test-context", "test message", "stack");

        var errors = repository.GetAll().ToList();
        errors.Should().HaveCount(1);
        errors[0].Source.Should().Be(ErrorSource.McpTool);
        errors[0].Context.Should().Be("test-context");
    }

    [Fact]
    public async Task Report_ErrorManagerCannotBeResolved_ExceptionSuppressed() {
        var scopeFactory = ServiceScopeSubstitute.Create();
        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var act = () => sut.Report(ErrorSource.Other, "ctx", "msg", null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Report_ScopeCreationFails_ExceptionSuppressed() {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Throws(new ObjectDisposedException("disposed"));
        var sut = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());

        var act = () => sut.Report(ErrorSource.Other, "ctx", "msg", null);

        await act.Should().NotThrowAsync();
    }
}
