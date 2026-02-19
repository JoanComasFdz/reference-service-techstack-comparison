using System;

namespace PerformanceTester.Functional;

/// <summary>
/// LINQ query syntax support for <see cref="Option{T}"/>.
/// Enables chaining via <c>from...in...select</c> comprehension syntax.
/// <para>
/// <b>Example:</b>
/// <code>
/// var result =
///     from user in FindUser(42)
///     from email in GetEmail(user)
///     select (user, email);
/// </code>
/// The chain short-circuits on the first None.
/// </para>
/// </summary>
public static class OptionLinqExtensions
{
    /// <summary>
    /// Projects the value if present. Enables the <c>select</c> keyword in LINQ queries.
    /// </summary>
    public static Option<U> Select<T, U>(
        this Option<T> option,
        Func<T, U> selector)
    {
        if (option is Option<T>.Some s)
        {
            return new Option<U>.Some(selector(s.Value));
        }

        return new Option<U>.None();
    }

    /// <summary>
    /// Chains a dependent operation. Enables multiple <c>from</c> clauses in LINQ queries.
    /// </summary>
    public static Option<V> SelectMany<T, U, V>(
        this Option<T> option,
        Func<T, Option<U>> bind,
        Func<T, U, V> project)
    {
        if (option is not Option<T>.Some s)
        {
            return new Option<V>.None();
        }

        var bound = bind(s.Value);
        if (bound is Option<U>.Some next)
        {
            return new Option<V>.Some(project(s.Value, next.Value));
        }

        return new Option<V>.None();
    }
}
