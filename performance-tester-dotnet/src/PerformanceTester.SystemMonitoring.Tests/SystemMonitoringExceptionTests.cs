namespace PerformanceTester.SystemMonitoring.Tests;

/// <summary>
/// Unit tests for SystemMonitoringException.
/// </summary>
public sealed class SystemMonitoringExceptionTests
{
    [Fact]
    public void ParameterlessConstructor_ShouldCreateExceptionWithDefaults()
    {
        // Act
        var exception = new SystemMonitoringException();

        // Assert
        Assert.NotNull(exception);
        Assert.Equal("Unknown", exception.Platform);
        Assert.Equal("Unknown", exception.MetricType);
        Assert.Equal("A system monitoring error occurred.", exception.Message);
    }

    [Fact]
    public void MessageOnlyConstructor_ShouldCreateExceptionWithMessage()
    {
        // Arrange
        const string message = "Custom error message";

        // Act
        var exception = new SystemMonitoringException(message);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal("Unknown", exception.Platform);
        Assert.Equal("Unknown", exception.MetricType);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void MessageAndInnerExceptionConstructor_ShouldCreateExceptionWithBoth()
    {
        // Arrange
        const string message = "Outer error";
        var inner = new InvalidOperationException("Inner error");

        // Act
        var exception = new SystemMonitoringException(message, inner);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal("Unknown", exception.Platform);
        Assert.Equal("Unknown", exception.MetricType);
        Assert.Equal(message, exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void FullConstructor_ShouldCreateExceptionWithAllProperties()
    {
        // Arrange
        const string platform = "Linux";
        const string metricType = "CPU";
        const string message = "Failed to read CPU";

        // Act
        var exception = new SystemMonitoringException(platform, metricType, message);

        // Assert
        Assert.Equal(platform, exception.Platform);
        Assert.Equal(metricType, exception.MetricType);
        Assert.Equal(message, exception.Message);
    }
}
