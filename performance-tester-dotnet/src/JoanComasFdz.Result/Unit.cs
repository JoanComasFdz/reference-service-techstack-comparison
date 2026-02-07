namespace JoanComasFdz.Result;

/// <summary>
/// A type with exactly one possible value, representing "no meaningful data."
/// Used as a generic type parameter where <c>void</c> cannot be used (e.g. <c>Result&lt;Unit, TFailure&gt;</c>).
/// <para>
/// <b>Why "Unit" and not "Void"?</b><br/>
/// In type theory, <b>Unit</b> is a type with exactly one value — because there is only one possible
/// value, it carries zero information. A function returning Unit <i>does</i> return something; it is
/// just trivially predictable. This is the correct type for "this operation succeeds without producing
/// a meaningful value."<br/>
/// <b>Void</b> in type theory is a type with <i>zero</i> values — no inhabitants at all. A function
/// returning Void can never return (it must throw, loop forever, or abort). Haskell calls this
/// <c>Void</c>, Rust calls it <c>!</c> (the "never" type).
/// </para>
/// <para>
/// C used <c>void</c> to mean "no return value," which is really the Unit concept. This naming
/// mistake propagated through C++, Java, and C#. So when you see <c>void</c> in C#, that is
/// semantically Unit — but because <c>void</c> is not a real type in C# (you cannot write
/// <c>Task&lt;void&gt;</c>), an explicit Unit struct is needed to fill that gap in generic contexts.
/// </para>
/// <para>
/// <b>Usage:</b><br/>
/// <c>Result&lt;Unit, ClearDatabaseError&gt;</c> means "succeeds with nothing meaningful, or fails
/// with a ClearDatabaseError." If it were <c>Result&lt;Void, ClearDatabaseError&gt;</c>, that would
/// mean "the success case can never happen" — the opposite of what you want.
/// </para>
/// </summary>
public readonly record struct Unit
{
    /// <summary>
    /// The single value of the Unit type.
    /// </summary>
    public static readonly Unit Value = default;

    public override string ToString() => "()";
}
