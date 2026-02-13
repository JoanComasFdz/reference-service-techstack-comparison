namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Detects if running inside a container (Docker, Podman, Kubernetes, etc.)
/// </summary>
internal static class ContainerDetector
{
    private static readonly Lazy<bool> _isContainer = new(DetectContainer);

    /// <summary>
    /// Gets whether the current process is running inside a container.
    /// </summary>
    /// <remarks>
    /// Detection methods (in order):
    /// 1. /.dockerenv file exists (Docker-specific marker)
    /// 2. DEVCONTAINER environment variable (VS Code devcontainers)
    /// 3. /proc/1/cgroup contains container runtime markers
    /// </remarks>
    public static bool IsContainer => _isContainer.Value;

    private static bool DetectContainer()
    {
        // Method 1: /.dockerenv file (Docker-specific)
        if (File.Exists("/.dockerenv"))
        {
            return true;
        }

        // Method 2: DEVCONTAINER environment variable (VS Code devcontainers)
        // This is set in devcontainer.json containerEnv
        if (Environment.GetEnvironmentVariable("DEVCONTAINER")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        // Method 3: Check /proc/1/cgroup for container runtime markers
        // This works for Docker, containerd, Kubernetes, and LXC
        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                var cgroup = File.ReadAllText("/proc/1/cgroup");
                if (cgroup.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("containerd", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("kubepods", StringComparison.OrdinalIgnoreCase) ||
                    cgroup.Contains("lxc", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore errors reading cgroup - not fatal
        }

        return false;
    }
}
