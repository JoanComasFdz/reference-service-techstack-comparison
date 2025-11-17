using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PerformanceTester.EventConsuming;

/// <summary>
/// BackgroundService that reads EventThroughputSample data from Channel and stores in ConcurrentBag.
/// Implements IMetricsCollector interface for orchestrator to retrieve samples after test completion.
/// </summary>
internal sealed class MetricsCollectorService : BackgroundService, IMetricsCollector
{
    private readonly Channel<EventThroughputSample> _throughputChannel;
    private readonly ILogger<MetricsCollectorService> _logger;
    private readonly ConcurrentBag<EventThroughputSample> _samples = new();

    public MetricsCollectorService(
        Channel<EventThroughputSample> throughputChannel,
        ILogger<MetricsCollectorService> logger)
    {
        _throughputChannel = throughputChannel ?? throw new ArgumentNullException(nameof(throughputChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<EventThroughputSample> GetThroughputSamples()
    {
        return _samples.ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("MetricsCollector starting...");

            // Read from channel until it's completed
            await foreach (var sample in _throughputChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _samples.Add(sample);

                // Log every 10th sample for visibility
                if (_samples.Count % 10 == 0)
                {
                    _logger.LogDebug("Collected {Count} throughput samples", _samples.Count);
                }
            }

            _logger.LogInformation("✓ MetricsCollector completed: {Count} samples collected", _samples.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MetricsCollector stopping gracefully...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ MetricsCollector failed");
            throw;
        }
    }
}
