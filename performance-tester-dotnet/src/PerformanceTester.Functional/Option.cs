using Dunet;

namespace PerformanceTester.Functional;

/// <summary>
/// A discriminated union representing either a value (<see cref="Some"/>) or no value (<see cref="None"/>).
/// Forces callers to explicitly handle both cases — no forgotten null checks.
/// <para>
/// <b>Example — returning an Option:</b>
/// <code>
/// using static Option&lt;User&gt;;
///
/// public static Option&lt;User&gt; FindUser(int id)
/// {
///     var user = db.Users.Find(id);
///     return user is not null
///         ? new Some(user)
///         : new None();
/// }
/// </code>
/// </para>
/// <para>
/// <b>Example — consuming an Option with Match():</b>
/// <code>
/// var option = FindUser(42);
/// option.Match(
///     some: s => Console.WriteLine($"Found: {s.Value.Name}"),
///     none: _ => Console.WriteLine("User not found")
/// );
/// </code>
/// </para>
/// </summary>
/// <typeparam name="T">The type of the value when present.</typeparam>
[Union]
public partial record Option<T>
{
    /// <summary>Represents a present value.</summary>
    partial record Some(T Value);

    /// <summary>Represents the absence of a value.</summary>
    partial record None;

    /// <summary>Returns true if this option contains a value.</summary>
    public bool IsSome => this is Some;

    /// <summary>Returns true if this option contains no value.</summary>
    public bool IsNone => this is None;

    /// <summary>Gets the value. Throws <see cref="InvalidCastException"/> if this is None.</summary>
    public T SomeValue => ((Some)this).Value;
}
