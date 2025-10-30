using Npgsql;
using Dotnet9ReferenceServiceAoT;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON options for AOT compatibility
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Build connection string from configuration (supports environment variable overrides)
var postgresHost = builder.Configuration.GetValue<string>("Postgres:Host") ?? "localhost";
var postgresPort = builder.Configuration.GetValue<int>("Postgres:Port", 5432);
var postgresDb = builder.Configuration.GetValue<string>("Postgres:Database") ?? "dotnet9aot_db";
var postgresUser = builder.Configuration.GetValue<string>("Postgres:Username") ?? "admin";
var postgresPassword = builder.Configuration.GetValue<string>("Postgres:Password") ?? "admin";

var connectionString = $"Host={postgresHost};Port={postgresPort};Database={postgresDb};Username={postgresUser};Password={postgresPassword}";

// Configure Npgsql DataSource for AOT compatibility
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddScoped<IInstrumentStatusRepository, InstrumentStatusRepository>();

// Add RabbitMQ background service
builder.Services.AddHostedService<RabbitMqService>();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("DatabaseInitializer");

    await using var connection = await dataSource.OpenConnectionAsync();
    await using var command = connection.CreateCommand();

    // NOTE: Using raw SQL instead of Entity Framework Core for AOT compatibility.
    // EF Core has limited Native AOT support and requires:
    // - Compile-time model building with source generators
    // - No runtime migrations or model discovery
    // - Severely limited LINQ query support (many queries fail at runtime)
    // - Complex configuration and workarounds
    // Raw SQL with Npgsql provides better performance, smaller binary size,
    // and full AOT compatibility without limitations.
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS dotnet9aot_instrument_status (
            id SERIAL PRIMARY KEY,
            device_id VARCHAR(255) NOT NULL,
            previous_status VARCHAR(255) NOT NULL,
            current_status VARCHAR(255) NOT NULL,
            timestamp TIMESTAMP WITH TIME ZONE NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_dotnet9aot_instrument_status_timestamp ON dotnet9aot_instrument_status(timestamp DESC);
    ";

    await command.ExecuteNonQueryAsync();
    logger.LogInformation("Database initialized successfully");
}

// KPI endpoint
app.MapGet("/kpi", async (IInstrumentStatusRepository repository) =>
{
    var status = await repository.GetLatestAsync();
    return Results.Ok(status);
});

app.Run();
