using Dunet;

namespace JoanComasFdz.Result;

/// <summary>
/// A discriminated union representing either a successful value or a typed failure.
/// Forces callers to explicitly handle both outcomes — no unchecked exceptions, no forgotten null checks.
/// <para>
/// <b>Type parameter guidelines:</b><br/>
/// - Use <c>string</c> as <typeparamref name="TFailure"/> for simple failure messages.<br/>
/// - Use a custom type (ideally a dunet <c>[Union]</c>) as <typeparamref name="TFailure"/> for structured errors.<br/>
/// - Use <see cref="Unit"/> as <typeparamref name="TSuccess"/> for operations that don't produce a meaningful value.
/// </para>
/// <para>
/// <b>Example — returning a Result:</b>
/// <code>
/// // Define a failure type with dunet for exhaustive matching:
/// [Union]
/// public partial record DivisionError
/// {
///     partial record DivideByZero;
///     partial record Overflow;
/// }
///
/// // Return Result instead of throwing:
/// using static Result<double, DivisionError>;
/// public static Result&lt;double, DivisionError&gt; Divide(double numerator, double denominator)
/// {
///     if (denominator == 0)
///         return new Failure(new DivisionError.DivideByZero());
///
///     var result = numerator / denominator;
///     if (double.IsInfinity(result))
///         ? return new Failure(new DivisionError.Overflow());
///         : return new Success(result);
/// }
/// </code>
/// </para>
/// <para>
/// <b>Example — consuming a Result with Dunet's Match() method:</b>
/// <code>
/// var result = Divide(10, 0);
///
/// // The compiler ensures you handle all cases:
/// result.Match(
///     success: s => Console.WriteLine($"Got {s.Value}"),
///     failure: f => f.Error.Match(
///         divideByZero: _ => Console.WriteLine("Cannot divide by zero"),
///         overflow: _ => Console.WriteLine("Overflow")
///      )
/// );
/// </code>
/// </para>
/// </summary>
/// <typeparam name="TSuccess">The type of the value on success.</typeparam>
/// <typeparam name="TFailure">The type of the error on failure.</typeparam>
[Union]
public partial record Result<TSuccess, TFailure>
{
    /// <summary>Represents a successful outcome containing a <see cref="Value"/>.</summary>
    partial record Success(TSuccess Value);

    /// <summary>Represents a failed outcome containing an <see cref="Error"/>.</summary>
    partial record Failure(TFailure Error);
}
