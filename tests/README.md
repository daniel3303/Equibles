# Equibles Test Suite

## Overview

Unit tests for the Equibles platform, covering core logic, data models, document processing, and service helpers. All tests are pure unit tests with no database or network dependencies.

## Stack

| Tool              | Purpose                              |
|-------------------|--------------------------------------|
| xUnit             | Test framework                       |
| FluentAssertions  | Readable assertion syntax            |
| NSubstitute       | Mocking (for interface dependencies) |

## Running Tests

```bash
dotnet test tests/Equibles.Tests
```

## Test Organization

Tests are organized by feature area, mirroring the `src/` project structure:

```
tests/Equibles.Tests/
├── Helpers/                 # TestDbContextFactory, ServiceScopeSubstitute
├── CommonStocks/            # CommonStockManager validation
├── Congress/                # DisclosureParsingHelper (static parsing methods)
├── Core/                    # Extension methods, shared utilities
├── Data/                    # EquiblesDbContext, module builder
├── Errors/                  # ErrorSource, ErrorManager, ErrorReporter
├── Holdings/                # Value normalizers, TsvParser, import helpers, SelectNormalizer
├── Integrations/            # RateLimiter
├── Mcp/                     # ApiKeyMiddleware, EquiblesMcpBuilder
├── Media/                   # FileManager (save, MIME detection)
├── Models/                  # Entity models, enum Display attributes
└── Sec/                     # Document processing, normalizers, filing processors, RagManager
    └── Normalizers/         # HTML normalization pipeline steps
```

## Conventions

- **Class naming**: `{SubjectClass}Tests`
- **Method naming**: `MethodName_Condition_ExpectedResult`
- **Attributes**: `[Fact]` for single scenarios, `[Theory]` with `[InlineData]` for parametrized
- **SUT naming**: `_sut` or a descriptive name like `_strategy`
- **Assertions**: Always use FluentAssertions (`Should().Be(...)`)

## What Is Tested

### Core & Infrastructure
- `EnumExtensionsTests` — `NameForHumans()` extension across multiple enum types
- `EquiblesModuleBuilderTests` — Module registration, deduplication, fluent API

### Data Models & Enums
- `CommonStockModelTests` — Entity defaults, property initialization
- `HoldingsEnumTests` — `[Display]` attributes on ShareType, OptionType, InvestmentDiscretion
- `InsiderTradingEnumTests` — `[Display]` on TransactionCode, AcquiredDisposed, OwnershipNature
- `CongressFredEnumTests` — `[Display]` on CongressPosition, CongressTransactionType, FredSeriesCategory
- `FileModelTests` — `NameWithExtension` computed property, GUID uniqueness
- `ErrorSourceTests` — Value object equality, hashing, `GetAll()`
- `DocumentTypeTests` — Case-insensitive parsing, display names, custom registration

### Business Logic
- `CommonStockManagerTests` — Create/Update validation (required fields, uniqueness, secondary ticker conflicts)
- `ErrorManagerTests` — Create with truncation boundaries, null defaults, MarkAsSeen, Delete
- `ErrorReporterTests` — Delegation to ErrorManager, exception suppression on failure
- `FileManagerTests` — SaveFile with MIME type detection, extension parsing, size validation

### Holdings Module
- `ValueNormalizerTests` — Passthrough and Thousands normalizer behavior
- `TsvParserTests` — Tab-delimited parsing from zip archives, edge cases
- `HoldingsImportServiceTests` — Static parsing helpers (dates, enums, lookups, deduplication), SelectNormalizer decision tree (pre/post-2023, amendment consensus)

### SEC Module
- `ChunkingStrategyTests` — Document segmentation into token-limited chunks
- `TokenCounterTests` — Tokenization utility
- `SecDocumentHtmlNormalizerTests` — SGML parsing, document type filtering
- `SecDocumentHtmlToMarkdownConverterTests` — HTML-to-Markdown conversion
- `InsiderTradingFilingProcessorTests` — XML sanitization, transaction code/bool/numeric parsing, full Process pipeline (Form 3/4, amendments, deduplication)
- `FtdImportServiceTests` — FTD file name generation, recent-file detection
- `RagManagerTests` — BuildContext formatting (grouping, ordering, whitespace filtering)
- **Normalizer pipeline** (6 test classes): XBRL stripping, table normalization, heading conversion, list conversion, pagination removal, currency consolidation

### Congress Module
- `DisclosureParsingHelperTests` — ParseTransactionType, ParseAmountRange, ParseDate, ExtractTickerFromAssetName, GetCell, CleanSentinel, Truncate, IsValidDisclosureUrl, ParseTransactionsFromHtml (end-to-end)

### MCP Infrastructure
- `ApiKeyMiddlewareTests` — Valid/invalid Bearer tokens, disabled validator, missing headers, case-insensitive prefix
- `EquiblesMcpBuilderTests` — AddModule registration/deduplication, UseMiddleware DI registration, fluent API

### Integrations
- `RateLimiterTests` — Async rate limiting, pause behavior, concurrency

## What Could Be Tested Next

### High Value

| Component | What to Test | Notes |
|-----------|-------------|-------|
| `CompanySyncService` | Ticker conflict resolution, obsolete stock replacement | Heavy service scope dependencies |
| `SecDocumentService` | Paginated document listing with filters | Requires in-memory DB |
| `ImageManager.SaveImage` | Extension validation, MIME resolution, resize logic | Requires ImageSharp test images |

### Medium Value

| Component | What to Test |
|-----------|-------------|
| `FredImportService.ImportSeries` | Series creation, observation import, deduplication | Requires making method internal |
| `ShortInterestImportService` | Date filtering, ticker mapping, duplicate detection |
| `YahooPriceImportService` | Date range calculation, price record mapping |
| Integration clients (`SecEdgarClient`, `FredClient`, `FinraClient`) | Response parsing with `HttpMessageHandler` mocking |

### Lower Priority (simple pass-through or IO-bound)

| Component | Why Lower Priority |
|-----------|-------------------|
| Repository classes | Thin wrappers over EF Core; integration tests needed |
| Scraper workers | Orchestration logic; would need full integration setup |
| MCP tool classes | Mostly delegates to repositories/managers |
| Web controllers | Thin controllers per architecture; integration test territory |

## Adding New Tests

1. Create a test class in the appropriate feature folder
2. Follow the `MethodName_Condition_ExpectedResult` naming convention
3. Use `[InternalsVisibleTo("Equibles.Tests")]` in the source project's `.csproj` when testing `internal` members
4. Prefer `[Theory]` with `[InlineData]` for methods with multiple input variations
5. Keep tests focused on one behavior per test method
