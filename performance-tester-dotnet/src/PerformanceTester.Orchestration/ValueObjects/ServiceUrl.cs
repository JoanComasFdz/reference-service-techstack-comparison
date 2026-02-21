using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Orchestration.ValueObjects;

/// <summary>
/// Value object representing a service endpoint URL (e.g., "http://localhost:8094/kpi").
/// </summary>
public sealed record ServiceUrl : Url
{
    private ServiceUrl(string value) : base(value) { }

    public static Result<ServiceUrl, string> Create(string value) =>
        Create(value, "Service URL", v => new ServiceUrl(v));

    /// <summary>
    /// Bypass validation for trusted sources (e.g., computed from validated <see cref="Port"/>).
    /// </summary>
    public static ServiceUrl FromString(string value) => new(value);
}
