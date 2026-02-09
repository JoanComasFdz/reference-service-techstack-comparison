using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.RabbitMqContainerName, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record RabbitMqContainerName : ContainerName
{
    private RabbitMqContainerName(string value) : base(value) { }

    public static Result<RabbitMqContainerName, string> Create(string? value) =>
        IsValid(value)
            ? new Success(new RabbitMqContainerName(Trimmed(value!)))
            : new Failure("RabbitMQ container name cannot be empty");

    public static RabbitMqContainerName FromString(string value) => new(value);
}
