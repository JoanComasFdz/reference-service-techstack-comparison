namespace PerformanceTester.SystemMonitoring.Tests;

/// <summary>
/// Unit tests for PowerShellHelper logging behavior.
/// </summary>
public sealed class PowerShellHelperTests
{
    [Fact]
    public void LogPathResolution_WhenCalled_ShouldInvokeLogAction()
    {
        // Arrange
        var logMessages = new List<string>();

        // Act
        PowerShellHelper.LogPathResolution(msg => logMessages.Add(msg));

        // Assert - Should have logged something (either found or not found)
        Assert.NotEmpty(logMessages);
        Assert.Contains(logMessages, m => m.Contains("PowerShell"));
    }

    [Fact]
    public void LogPathResolution_WhenPowerShellAvailable_ShouldLogPath()
    {
        // Arrange
        var logMessages = new List<string>();

        // Act
        PowerShellHelper.LogPathResolution(msg => logMessages.Add(msg));

        // Assert - If available, should mention the path
        if (PowerShellHelper.IsAvailable)
        {
            Assert.Contains(logMessages, m => m.Contains("found at") || m.Contains("located"));
        }
        else
        {
            Assert.Contains(logMessages, m => m.Contains("not available") || m.Contains("not found"));
        }
    }
}
