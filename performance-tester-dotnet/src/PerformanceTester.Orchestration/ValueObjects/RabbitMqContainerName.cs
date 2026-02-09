using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record RabbitMqContainerName : ContainerName
{
    private RabbitMqContainerName(string value) : base(value) { }

    public static Result<RabbitMqContainerName, string> Create(string? value) =>
        Create(value, "RabbitMQ container name cannot be empty", v => new RabbitMqContainerName(v));

    public static RabbitMqContainerName FromString(string value) => new(value);
}
