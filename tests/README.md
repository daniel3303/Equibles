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
├── Core/                    # Extension methods, shared utilities
├── Data/                    # EquiblesDbContext, module builder
├── Errors/                  # ErrorSource value object
├── Holdings/                # Value normalizers, TsvParser, import helpers
├── Integrations/            # RateLimiter
├── Models/                  # Entity models, enum Display attributes
└── Sec/                     # Document processing, normalizers, filing processors
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

### Holdings Module
- `ValueNormalizerTests` — Passthrough and Thousands normalizer behavior
- `TsvParserTests` — Tab-delimited parsing from zip archives, edge cases
- `HoldingsImportServiceTests` — Static parsing helpers (dates, enums, lookups, deduplication)

### SEC Module
- `ChunkingStrategyTests` — Document segmentation into token-limited chunks
- `TokenCounterTests` — Tokenization utility
- `SecDocumentHtmlNormalizerTests` — SGML parsing, document type filtering
- `SecDocumentHtmlToMarkdownConverterTests` — HTML-to-Markdown conversion
- `InsiderTradingFilingProcessorTests` — XML sanitization, transaction code/bool/numeric parsing
- `FtdImportServiceTests` — FTD file name generation, recent-file detection
- **Normalizer pipeline** (6 test classes): XBRL stripping, table normalization, heading conversion, list conversion, pagination removal, currency consolidation

### Integrations
- `RateLimiterTests` — Async rate limiting, pause behavior, concurrency

## What Could Be Tested Next

### High Value (complex logic, currently private or requires mocking)

| Component | What to Test | Blocker |
|-----------|-------------|---------|
| `HoldingsImportService.SelectNormalizer` | Value normalization decision tree (pre-2023 vs post-cutoff, amendment consensus) | Requires DB mock for `GetConsensusPrice` |
| `CommonStockManager.ValidateCommonStock` | Multi-field validation with uniqueness checks | Requires `CommonStockRepository` mock |
| `ErrorManager.Create` | String truncation at 128/512 char boundaries | Requires `ErrorRepository` mock |
| `CompanySyncService` | Ticker conflict resolution, obsolete stock replacement | Heavy service scope dependencies |
| `InsiderTradingFilingProcessor.Process` | Full XML parsing pipeline with owner/transaction upsert | Requires SEC client + repo mocks |

### Medium Value (less complex but useful coverage)

| Component | What to Test |
|-----------|-------------|
| `ImageManager.SaveImage` | Extension validation, MIME resolution, resize logic |
| `FileManager.SaveFile` | Extension parsing, MIME fallback |
| `ErrorReporter.Report` | Exception suppression behavior |
| `EquiblesMcpBuilder` | Middleware registration (already has module tests) |

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
