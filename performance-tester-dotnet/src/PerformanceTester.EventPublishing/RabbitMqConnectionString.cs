namespace PerformanceTester.EventPublishing;

/// <summary>
/// Value object for a RabbitMQ AMQP connection string.
/// Validates format on construction and provides credential masking for logging.
/// </summary>
internal readonly record struct RabbitMqConnectionString
{
    public string Value { get; }

    public RabbitMqConnectionString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Connection string must be a valid URI", nameof(value));
        }

        Value = value;
    }

    public Uri ToUri() => new(Value);

    /// <summary>
    /// Returns the connection string with credentials replaced by ***.
    /// Safe for logging.
    /// </summary>
    public string ToMaskedString()
    {
        var uri = new Uri(Value);
        var userInfo = !string.IsNullOrEmpty(uri.UserInfo) ? "***:***@" : "";
        return $"{uri.Scheme}://{userInfo}{uri.Host}:{uri.Port}";
    }

    public override string ToString() => ToMaskedString();
}
