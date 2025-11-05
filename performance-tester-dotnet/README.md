# PerformanceTester - .NET Integration Testing Infrastructure

This directory contains the .NET rewrite of the Performance Tester, including shared integration testing infrastructure.

## Projects

### PerformanceTester.IntegrationTesting (Phase 0)

Shared integration testing infrastructure project that provides foundational components for all integration test projects:

- **ContainerManager**: Singleton managing PostgreSQL and RabbitMQ Testcontainers lifecycle
- **IntegrationTestBase**: Abstract base class providing container initialization and logging capture
- **Logging Infrastructure**: xUnit test output integration (XunitLogger, XunitLoggerProvider, LoggingTestExtensions)

**Status**: ✅ Complete - All 8 verification tests passing

### PerformanceTester.IntegrationTesting.Tests (Phase 0 Verification)

Verification test project that validates the Phase 0 shared infrastructure works correctly:

1. ✅ ContainerManager starts PostgreSQL container successfully
2. ✅ ContainerManager starts RabbitMQ container successfully
3. ✅ ContainerManager is singleton across multiple calls
4. ✅ EnsureStartedAsync is idempotent when called multiple times
5. ✅ XunitLogger writes to test output successfully
6. ✅ LoggingTestExtensions adds xUnit output to logging builder
7. ✅ IntegrationTestBase.InitializeAsync ensures containers started
8. ✅ ContainerManager health checks prevent connection failures

**Test Results**: All 8 tests passing in ~1.2 minutes (includes container startup)

## Building

```bash
# Build entire solution
dotnet build

# Build specific project
cd PerformanceTester.IntegrationTesting
dotnet build
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run tests from specific project
cd PerformanceTester.IntegrationTesting.Tests
dotnet test
```

## Prerequisites

- .NET 9.0 SDK
- Docker (for Testcontainers)
- Docker must be running before executing tests

## Key Design Decisions

### Container Lifecycle
- **Singleton pattern**: One ContainerManager instance per test run
- **Lazy initialization**: Containers start on first `EnsureStartedAsync()` call
- **Health checks**: Polls PostgreSQL and RabbitMQ until ready (30s timeout)
- **Never stopped**: Containers remain running for entire test session

### Test Isolation
- **Per-test instances**: Each test gets a new test class instance (xUnit default)
- **Shared containers**: ContainerManager singleton reused across all tests
- **Fresh DI containers**: Each SystemUnderTest instance creates new DI container

### Performance Expectations
- **First test**: ~20-25 seconds (container startup + health checks)
- **Subsequent tests**: ~100ms overhead (per-test DI container)
- **Total test suite**: ~1.2 minutes for 8 Phase 0 verification tests

## Architecture

See [docs/05.PHASE_0_INTEGRATION_TESTING_DESIGN.md](docs/05.PHASE_0_INTEGRATION_TESTING_DESIGN.md) for complete architecture and design decisions.

## Next Steps

Phase 0 is complete. Future phases will:
- Phase 1: Infrastructure integration tests (using Phase 0 as foundation)
- Phase 2+: Additional slice integration tests (EventPublishing, EventConsuming, etc.)

Each test project will:
1. Reference PerformanceTester.IntegrationTesting
2. Create project-specific `IntegrationTest` base class
3. Implement `System` class providing access to SUT and test helpers
4. Implement `SystemUnderTest` class (named after project being tested)
