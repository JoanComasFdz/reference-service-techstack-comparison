namespace Dotnet9ReferenceServiceAoT;

/// <summary>
/// Repository interface for managing instrument status data.
/// </summary>
public interface IInstrumentStatusRepository
{
    /// <summary>
    /// Retrieves the latest instrument status record from the database.
    /// </summary>
    /// <returns>The most recent instrument status, or null if no records exist.</returns>
    Task<InstrumentStatus?> GetLatestAsync();

    /// <summary>
    /// Adds a new instrument status record to the database.
    /// </summary>
    /// <param name="status">The instrument status to add.</param>
    /// <returns>The ID of the newly created record.</returns>
    Task<int> AddAsync(InstrumentStatus status);
}
