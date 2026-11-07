namespace GraphDoku.Core.Model;

/// <summary>Geschlossener ganzzahliger Wertebereich [Min, Max].</summary>
public sealed record ValueDomain(int Min, int Max)
{
    /// <summary>Größte erlaubte Anzahl an Werten in einem Wertebereich.</summary>
    public const int MaxSize = 4096;

    /// <summary>Anzahl der Werte (0, wenn Min größer als Max ist).</summary>
    public long Size => Max < Min ? 0 : (long)Max - Min + 1;

    public bool IsValid => Min <= Max;

    public bool Contains(int value) => value >= Min && value <= Max;

    public IEnumerable<int> Values
    {
        get
        {
            for (long v = Min; v <= Max; v++)
            {
                yield return (int)v;
            }
        }
    }

    public override string ToString() => $"{Min}..{Max}";
}
