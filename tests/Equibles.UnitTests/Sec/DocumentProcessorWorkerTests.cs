using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class DocumentProcessorWorkerTests
{
    [Fact]
    public void SleepInterval_IsFifteenSeconds()
    {
        // DocumentProcessorWorker is the worker that orchestrates Phase 1 (chunking)
        // and Phase 2 (embedding generation) over every pending SEC document. The
        // 15-second `SleepInterval` is the gap between full passes of both phases —
        // each pass can chain many `DocumentManager.GenerateEmbeddingBatch` calls,
        // and each batch fans out to the Ollama / hosted embedding endpoint. If a
        // refactor tightened the interval (a plausible copy-paste of
        // `TimeSpan.FromSeconds(1)` from a test helper) and there were no pending
        // documents, the worker would still spin a no-op loop every second — under
        // load with thousands of pending chunks, even a few seconds of interval
        // savings would translate into burst traffic that exhausts a hosted
        // provider's per-minute embedding quota (most providers cap requests at
        // a few hundred per minute for inexpensive models). The failure mode is
        // silent: HTTP 429s get logged inside the embedding client and the
        // batch retries forever, leaving the worker stuck. Pin the literal value
        // so any future change has to update this test deliberately.
        var sut = new TestableDocumentProcessorWorker(
            Substitute.For<ILogger<DocumentProcessorWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        sut.InvokeSleepInterval().Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ErrorSource_IsDocumentProcessor()
    {
        // DocumentProcessorWorker is the post-fetch pipeline (chunking + embedding) that
        // sits downstream of SecScraperWorker. When BaseScraperWorker's catch-all reports
        // a failure, it tags the error with this enum value as the routing key for the
        // issue-tracker queue. The Sec.HostedService assembly hosts BOTH this worker
        // (`ErrorSource.DocumentProcessor`) and SecScraperWorker
        // (`ErrorSource.DocumentScraper`) — two intentionally distinct enum members
        // chosen so on-call can tell "the SEC website returned junk" (DocumentScraper)
        // from "Ollama choked while embedding a chunk" (DocumentProcessor). Those have
        // wildly different remediations: one points at SEC outages, the other at the
        // embedding stack. A regression that copy-pasted `DocumentScraper` here (the
        // two are visually similar; the source files even live in the same folder)
        // would silently merge the two operational queues — embedding failures would
        // start surfacing as "SEC scraper down" and trigger the wrong runbook. Pin the
        // literal value so the reorder is loud.
        var sut = new TestableDocumentProcessorWorker(
            Substitute.For<ILogger<DocumentProcessorWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        sut.InvokeErrorSource().Should().Be(ErrorSource.DocumentProcessor);
    }

    [Fact]
    public void WorkerName_IsDocumentProcessor()
    {
        // Fifth WorkerName pin, completing the SleepInterval/ErrorSource/WorkerName
        // triple for DocumentProcessorWorker. WorkerName flows into BaseScraperWorker's
        // structured log scope and shows up in every Serilog line the worker emits —
        // it's the string on-call greps for in `data/worker/logs/log<date>.txt` when
        // chasing "is the embedding pipeline running?". DocumentProcessorWorker shares
        // the SEC worker assembly with SecScraperWorker ("SEC filing scraper") and
        // FtdScraperWorker ("FTD scraper") — three workers, three distinct names so a
        // tail -f can tell them apart at a glance. A copy-paste regression that gave
        // this worker "SEC filing scraper" would silently merge two operational
        // streams: the embedding pipeline's HTTP 429 churn against Ollama would start
        // appearing as filing-scraper noise, and runbook authors would point on-call
        // at the SEC website during what's actually an embedding-stack outage. Pin
        // the literal "Document processor" so the rename is loud, not silent.
        var sut = new TestableDocumentProcessorWorker(
            Substitute.For<ILogger<DocumentProcessorWorker>>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        sut.InvokeWorkerName().Should().Be("Document processor");
    }

    private sealed class TestableDocumentProcessorWorker : DocumentProcessorWorker
    {
        public TestableDocumentProcessorWorker(
            ILogger<DocumentProcessorWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter
        )
            : base(logger, scopeFactory, errorReporter) { }

        public TimeSpan InvokeSleepInterval() => SleepInterval;

        public ErrorSource InvokeErrorSource() => ErrorSource;

        public string InvokeWorkerName() => WorkerName;
    }
}
