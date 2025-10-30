using Microsoft.EntityFrameworkCore;
using Dotnet9ReferenceService;

var builder = WebApplication.CreateBuilder(args);

// Build connection string from configuration (supports environment variable overrides)
var postgresHost = builder.Configuration.GetValue<string>("Postgres:Host") ?? "localhost";
var postgresPort = builder.Configuration.GetValue<int>("Postgres:Port", 5432);
var postgresDb = builder.Configuration.GetValue<string>("Postgres:Database") ?? "dotnet9_db";
var postgresUser = builder.Configuration.GetValue<string>("Postgres:Username") ?? "admin";
var postgresPassword = builder.Configuration.GetValue<string>("Postgres:Password") ?? "admin";

var connectionString = $"Host={postgresHost};Port={postgresPort};Database={postgresDb};Username={postgresUser};Password={postgresPassword}";

// Configure database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Add RabbitMQ background service
builder.Services.AddHostedService<RabbitMqService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// KPI endpoint
app.MapGet("/kpi", async (AppDbContext db) =>
{
    var status = await db.InstrumentStatuses
        .OrderByDescending(s => s.Timestamp)
        .FirstOrDefaultAsync();
    return status != null ? Results.Ok(status) : Results.NotFound();
}).WithName("GetLatestInstrumentStatus");

app.Run();
