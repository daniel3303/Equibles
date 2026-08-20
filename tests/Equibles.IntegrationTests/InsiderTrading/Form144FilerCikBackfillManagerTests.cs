using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.InsiderTrading;

public class Form144FilerCikBackfillManagerTests : IDisposable
{
    private const string SuccessfulAccession = "0000000001-26-000065";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly Form144FilingRepository _repository;

    public Form144FilerCikBackfillManagerTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration()
        );
        _repository = new Form144FilingRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Run_PermanentFailureBatch_RetriesAcrossManagersAndReachesLaterNotice()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TEST",
            Name = "Test Issuer",
            Cik = "0000000001",
        };
        _dbContext.Set<CommonStock>().Add(stock);

        for (var index = 1; index <= 64; index++)
        {
            _repository.Add(
                Filing(stock, $"0000000001-26-{index:000000}", new DateOnly(2026, 8, 1))
            );
        }
        _repository.Add(Filing(stock, SuccessfulAccession, new DateOnly(2026, 8, 2)));
        await _repository.SaveChanges();

        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(Arg.Any<string>(), stock.Cik)
            .Returns(call =>
                call.ArgAt<string>(0) == SuccessfulAccession
                    ? "<edgarSubmission><headerData><filerInfo><filer><filerCredentials><cik>0000000065</cik></filerCredentials></filer></filerInfo></headerData></edgarSubmission>"
                    : throw new HttpRequestException("permanent test failure")
            );
        var now = new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(_ => now);

        var firstResolved = await Manager().Run();

        firstResolved.Should().Be(1);
        await edgar.Received(65).GetDocumentContent(Arg.Any<string>(), stock.Cik);
        _dbContext.ChangeTracker.Entries().Should().BeEmpty();

        now = now.AddHours(6);
        var secondResolved = await Manager().Run();

        secondResolved.Should().Be(0);
        await edgar.Received(129).GetDocumentContent(Arg.Any<string>(), stock.Cik);

        now = now.AddHours(6);
        var thirdResolved = await Manager().Run();

        thirdResolved.Should().Be(0);
        var filings = await _repository
            .GetAll()
            .AsNoTracking()
            .OrderBy(f => f.FilingDate)
            .ToListAsync();
        filings
            .Take(64)
            .Should()
            .OnlyContain(f => f.FilerCik == Form144FilerCikBackfillManager.UnavailableMarker);
        filings[^1].FilerCik.Should().Be("0000000065");
        filings.Take(64).Should().OnlyContain(f => f.FilerCikBackfillAttempts == 3);
        await edgar.Received(193).GetDocumentContent(Arg.Any<string>(), stock.Cik);

        Form144FilerCikBackfillManager Manager() =>
            new(
                _repository,
                edgar,
                NullLogger<Form144FilerCikBackfillManager>.Instance,
                timeProvider
            );
    }

    [Fact]
    public async Task Run_CancelledMidBatch_PersistsEarlierSuccessAndCurrentAttempt()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TEST",
            Name = "Test Issuer",
            Cik = "0000000001",
        };
        _dbContext.Set<CommonStock>().Add(stock);
        var first = Filing(stock, "0000000001-26-000001", new DateOnly(2026, 8, 1));
        var second = Filing(stock, "0000000001-26-000002", new DateOnly(2026, 8, 2));
        second.FilerCikBackfillAttempts = 2;
        _repository.Add(first);
        _repository.Add(second);
        await _repository.SaveChanges();

        using var cancellation = new CancellationTokenSource();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(first.AccessionNumber, stock.Cik)
            .Returns(
                "<edgarSubmission><headerData><filerInfo><filer><filerCredentials><cik>0000000002</cik></filerCredentials></filer></filerInfo></headerData></edgarSubmission>"
            );
        edgar
            .GetDocumentContent(second.AccessionNumber, stock.Cik)
            .Returns(
                (Func<NSubstitute.Core.CallInfo, Task<string>>)(
                    _ =>
                    {
                        cancellation.Cancel();
                        return Task.FromCanceled<string>(cancellation.Token);
                    }
                )
            );
        var now = new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(_ => now);
        var manager = new Form144FilerCikBackfillManager(
            _repository,
            edgar,
            NullLogger<Form144FilerCikBackfillManager>.Instance,
            timeProvider
        );

        var run = () => manager.Run(cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        var filings = await _repository
            .GetAll()
            .AsNoTracking()
            .OrderBy(f => f.AccessionNumber)
            .ToListAsync();
        filings[0].FilerCik.Should().Be("0000000002");
        filings[0].FilerCikBackfillAttempts.Should().Be(1);
        filings[1].FilerCik.Should().BeNull();
        filings[1].FilerCikBackfillAttempts.Should().Be(3);
        filings[1].FilerCikBackfillAttemptedAt.Should().NotBeNull();
        _dbContext.ChangeTracker.Entries().Should().BeEmpty();

        edgar
            .GetDocumentContent(second.AccessionNumber, stock.Cik)
            .Returns(
                "<edgarSubmission><headerData><filerInfo><filer><filerCredentials><cik>0000000003</cik></filerCredentials></filer></filerInfo></headerData></edgarSubmission>"
            );
        now = now.AddHours(6);
        var resumedManager = new Form144FilerCikBackfillManager(
            _repository,
            edgar,
            NullLogger<Form144FilerCikBackfillManager>.Instance,
            timeProvider
        );

        (await resumedManager.Run()).Should().Be(0);
        var resumed = await _repository.GetAll().AsNoTracking().SingleAsync(f => f.Id == second.Id);
        resumed.FilerCik.Should().Be(Form144FilerCikBackfillManager.UnavailableMarker);
        resumed.FilerCikBackfillAttempts.Should().Be(3);
        await edgar.Received(1).GetDocumentContent(second.AccessionNumber, stock.Cik);
    }

    [Fact]
    public async Task Run_PreCancelled_ThrowsWithoutFetching()
    {
        var edgar = Substitute.For<ISecEdgarClient>();
        var manager = new Form144FilerCikBackfillManager(
            _repository,
            edgar,
            NullLogger<Form144FilerCikBackfillManager>.Instance,
            TimeProvider.System
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var run = () => manager.Run(cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());
    }

    private static Form144Filing Filing(
        CommonStock stock,
        string accessionNumber,
        DateOnly filingDate
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            AccessionNumber = accessionNumber,
            FilingDate = filingDate,
            SellerName = "Test Seller",
            RelationshipToIssuer = "Officer",
            SecurityClassTitle = "Common Stock",
        };
}
