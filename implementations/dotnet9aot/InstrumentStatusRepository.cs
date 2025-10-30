using Npgsql;

namespace Dotnet9ReferenceServiceAoT;

// Using raw SQL with Npgsql instead of Entity Framework Core for Native AOT compatibility.
// EF Core's AOT support is limited and would require significant workarounds that negate
// the performance benefits of AOT compilation. Raw SQL provides full control and optimal AOT performance.
public class InstrumentStatusRepository : IInstrumentStatusRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public InstrumentStatusRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<InstrumentStatus?> GetLatestAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT id, device_id, previous_status, current_status, timestamp
            FROM dotnet9aot_instrument_status
            ORDER BY timestamp DESC
            LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new InstrumentStatus
            {
                Id = reader.GetInt32(0),
                DeviceId = reader.GetString(1),
                PreviousStatus = reader.GetString(2),
                CurrentStatus = reader.GetString(3),
                Timestamp = reader.GetDateTime(4)
            };
        }

        return null;
    }

    public async Task<int> AddAsync(InstrumentStatus status)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO dotnet9aot_instrument_status (device_id, previous_status, current_status, timestamp)
            VALUES ($1, $2, $3, $4)
            RETURNING id";

        command.Parameters.AddWithValue(status.DeviceId);
        command.Parameters.AddWithValue(status.PreviousStatus);
        command.Parameters.AddWithValue(status.CurrentStatus);
        command.Parameters.AddWithValue(status.Timestamp);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
