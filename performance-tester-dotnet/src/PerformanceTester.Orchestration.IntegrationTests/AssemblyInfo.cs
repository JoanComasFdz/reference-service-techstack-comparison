using Xunit;

// MANDATORY: Disable test parallelization
// These tests share critical resources that cannot be isolated:
// - RabbitMQ queues (all tests publish to same queue)
// - Database (all tests use dotnet9aot_perftest_integrationtest_db)
// - Port 8093 (only one .NET AOT service instance can bind)
// - Service state (some tests require service running, one requires it NOT running)
[assembly: CollectionBehavior(DisableTestParallelization = true)]
