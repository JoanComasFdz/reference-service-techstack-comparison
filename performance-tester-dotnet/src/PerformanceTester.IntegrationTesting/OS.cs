using System.Net;
using System.Net.Sockets;

namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Provides OS-level process and port management for integration testing.
/// Allows tests to simulate processes listening on specific ports.
/// </summary>
public sealed class OS : IDisposable
{
    private readonly Dictionary<int, TcpListener> _activeListeners = [];
    private readonly object _lock = new();

    /// <summary>
    /// Starts a TCP listener on the specified port to simulate a process.
    /// Binds to all interfaces (0.0.0.0) to ensure OS tools like lsof can discover it.
    /// </summary>
    /// <param name="port">The port to listen on. Use 0 to let the OS assign an available port.</param>
    /// <returns>The actual port number the listener is bound to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a listener is already running on the specified port.</exception>
    public int StartProcessOnPort(int port = 0)
    {
        lock (_lock)
        {
            // Prevent starting multiple listeners on the same port
            if (port != 0 && _activeListeners.ContainsKey(port))
            {
                throw new InvalidOperationException($"A process is already running on port {port}. Stop it first before starting a new one.");
            }

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            // If port was 0 (auto-assigned), check if we somehow got a collision (unlikely but defensive)
            if (_activeListeners.ContainsKey(actualPort))
            {
                listener.Stop();
                throw new InvalidOperationException($"OS assigned port {actualPort} which is already in use internally. This should not happen.");
            }

            _activeListeners[actualPort] = listener;
            return actualPort;
        }
    }

    /// <summary>
    /// Stops the TCP listener on the specified port.
    /// </summary>
    /// <param name="port">The port number to stop listening on.</param>
    /// <exception cref="InvalidOperationException">Thrown when no listener is running on the specified port.</exception>
    public void StopProcessOnPort(int port)
    {
        lock (_lock)
        {
            if (!_activeListeners.TryGetValue(port, out var listener))
            {
                throw new InvalidOperationException($"No process is running on port {port}. Cannot stop a non-existent process.");
            }

            listener.Stop();
            _activeListeners.Remove(port);
        }
    }

    /// <summary>
    /// Checks if a process (TCP listener) is currently running on the specified port.
    /// </summary>
    /// <param name="port">The port to check.</param>
    /// <returns>True if a listener is active on the port, false otherwise.</returns>
    public bool IsProcessRunningOnPort(int port)
    {
        lock (_lock)
        {
            return _activeListeners.ContainsKey(port);
        }
    }

    /// <summary>
    /// Gets the count of active listeners (simulated processes).
    /// </summary>
    public int ActiveProcessCount
    {
        get
        {
            lock (_lock)
            {
                return _activeListeners.Count;
            }
        }
    }

    /// <summary>
    /// Stops all active TCP listeners and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var listener in _activeListeners.Values)
            {
                try
                {
                    listener.Stop();
                }
                catch
                {
                    // Suppress exceptions during cleanup
                    // Listener might already be stopped or in invalid state
                }
            }

            _activeListeners.Clear();
        }
    }
}
