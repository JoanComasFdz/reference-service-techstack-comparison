namespace JoanComasFdz.Result;

public readonly record struct Unit
{
    public static readonly Unit Value = default;
    public override string ToString() => "()";
}
