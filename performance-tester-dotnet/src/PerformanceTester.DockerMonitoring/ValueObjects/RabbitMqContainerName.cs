using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

public sealed record RabbitMqContainerName : NonEmptyString
{
    private RabbitMqContainerName(string value) : base(value) { }

    public static Result<RabbitMqContainerName, string> Create(string value) => Create(value, "RabbitMQ container name", v => new RabbitMqContainerName(v));

    public static RabbitMqContainerName FromString(string value) => new(value);
}
