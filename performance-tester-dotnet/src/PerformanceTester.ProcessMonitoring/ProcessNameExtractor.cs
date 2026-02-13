namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Extracts meaningful service names from process command lines.
/// Handles Java JARs, .NET DLLs, native executables, and interpreted languages.
/// </summary>
public static class ProcessNameExtractor
{
    /// <summary>
    /// Extracts a meaningful service name from a process's command line arguments.
    /// Falls back to the base process name if no meaningful name can be extracted.
    /// </summary>
    /// <param name="baseProcessName">The OS-reported process name (e.g., "java", "python3")</param>
    /// <param name="commandLine">The full command line arguments, or null if unavailable</param>
    /// <returns>A meaningful service name for display and reporting</returns>
    public static string ExtractMeaningfulName(string baseProcessName, string[]? commandLine)
    {
        if (commandLine is null || commandLine.Length == 0)
        {
            return baseProcessName;
        }

        // Java processes: look for JAR file or main class
        if (baseProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractJavaServiceName(commandLine) ?? baseProcessName;
        }

        // .NET processes: look for DLL file (dotnet myapp.dll)
        if (baseProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDotNetServiceName(commandLine) ?? baseProcessName;
        }

        // Python processes: look for script name
        if (baseProcessName.StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPythonServiceName(commandLine) ?? baseProcessName;
        }

        // Node/Bun: look for script name
        if (baseProcessName.Equals("node", StringComparison.OrdinalIgnoreCase) ||
            baseProcessName.Equals("bun", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractNodeServiceName(commandLine) ?? baseProcessName;
        }

        // For native executables, use the executable name from command line if available
        var executableName = Path.GetFileNameWithoutExtension(commandLine[0]);
        return !string.IsNullOrEmpty(executableName) ? executableName : baseProcessName;
    }

    private static string? ExtractJavaServiceName(string[] commandLine)
    {
        for (var i = 0; i < commandLine.Length; i++)
        {
            var arg = commandLine[i];

            // Check for -jar flag followed by JAR path
            if (arg.Equals("-jar", StringComparison.OrdinalIgnoreCase) && i + 1 < commandLine.Length)
            {
                var jarPath = commandLine[i + 1];
                return Path.GetFileNameWithoutExtension(jarPath);
            }

            // Check for JAR file as direct argument (e.g., java myapp.jar)
            if (arg.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !arg.StartsWith("-"))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        // Look for main class (last non-option argument that looks like a class name)
        for (var i = commandLine.Length - 1; i >= 0; i--)
        {
            var arg = commandLine[i];
            if (!arg.StartsWith("-") && !arg.Contains('=') && arg.Contains('.'))
            {
                // Likely a fully qualified class name like com.example.MainClass
                var className = arg.Split('.')[^1]; // Get last segment
                return className;
            }
        }

        return null;
    }

    private static string? ExtractDotNetServiceName(string[] commandLine)
    {
        foreach (var arg in commandLine)
        {
            // Skip flags
            if (arg.StartsWith("-"))
            {
                continue;
            }

            // Found a DLL file
            if (arg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        return null;
    }

    private static string? ExtractPythonServiceName(string[] commandLine)
    {
        foreach (var arg in commandLine)
        {
            // Skip python executable and flags
            if (arg.StartsWith("-") || arg.Contains("python"))
            {
                continue;
            }

            // Found a script file
            if (arg.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        return null;
    }

    private static string? ExtractNodeServiceName(string[] commandLine)
    {
        foreach (var arg in commandLine)
        {
            // Skip node/bun executable and flags
            if (arg.StartsWith("-"))
            {
                continue;
            }

            // Found a script file
            if (arg.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        return null;
    }
}
