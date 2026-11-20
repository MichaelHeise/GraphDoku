namespace GraphDoku.Core.Model;

/// <summary>Vergleichsoperator eines <see cref="GroupComparison"/>: Summe links OP Summe rechts.</summary>
public enum ComparisonOperator
{
    /// <summary>Beide Summen sind gleich (<c>=</c>).</summary>
    Equal,

    /// <summary>Die Summen sind verschieden (<c>≠</c>).</summary>
    NotEqual,

    /// <summary>Die linke Summe ist kleiner (<c>&lt;</c>).</summary>
    Less,

    /// <summary>Die linke Summe ist kleiner oder gleich (<c>≤</c>).</summary>
    LessOrEqual,

    /// <summary>Die linke Summe ist größer (<c>&gt;</c>).</summary>
    Greater,

    /// <summary>Die linke Summe ist größer oder gleich (<c>≥</c>).</summary>
    GreaterOrEqual,
}

/// <summary>
/// Namen im JSON-Format, Anzeigesymbole, deutsche Texte und Auswertung der <see cref="ComparisonOperator"/>-Werte.
/// </summary>
public static class ComparisonOperators
{
    public const string EqualName = "=";
    public const string NotEqualName = "!=";
    public const string LessName = "<";
    public const string LessOrEqualName = "<=";
    public const string GreaterName = ">";
    public const string GreaterOrEqualName = ">=";

    /// <summary>Alle Operatoren in der Reihenfolge der Aufzählung.</summary>
    public static IReadOnlyList<ComparisonOperator> All { get; } =
    [
        ComparisonOperator.Equal,
        ComparisonOperator.NotEqual,
        ComparisonOperator.Less,
        ComparisonOperator.LessOrEqual,
        ComparisonOperator.Greater,
        ComparisonOperator.GreaterOrEqual,
    ];

    /// <summary>Alle Namen im JSON-Format in der Reihenfolge der Aufzählung (ASCII, z. B. „&lt;=“).</summary>
    public static IReadOnlyList<string> Names { get; } =
        [EqualName, NotEqualName, LessName, LessOrEqualName, GreaterName, GreaterOrEqualName];

    /// <summary>Name im JSON-Format, z. B. „&lt;=“.</summary>
    public static string ToName(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => EqualName,
        ComparisonOperator.NotEqual => NotEqualName,
        ComparisonOperator.Less => LessName,
        ComparisonOperator.LessOrEqual => LessOrEqualName,
        ComparisonOperator.Greater => GreaterName,
        ComparisonOperator.GreaterOrEqual => GreaterOrEqualName,
        _ => throw Unknown(op),
    };

    /// <summary>Liest einen Namen im JSON-Format (genau, Groß-/Kleinschreibung zählt).</summary>
    public static bool TryParse(string? name, out ComparisonOperator op)
    {
        switch (name)
        {
            case EqualName:
                op = ComparisonOperator.Equal;
                return true;
            case NotEqualName:
                op = ComparisonOperator.NotEqual;
                return true;
            case LessName:
                op = ComparisonOperator.Less;
                return true;
            case LessOrEqualName:
                op = ComparisonOperator.LessOrEqual;
                return true;
            case GreaterName:
                op = ComparisonOperator.Greater;
                return true;
            case GreaterOrEqualName:
                op = ComparisonOperator.GreaterOrEqual;
                return true;
            default:
                op = default;
                return false;
        }
    }

    /// <summary>Anzeigesymbol: =, ≠, &lt;, ≤, &gt;, ≥.</summary>
    public static string ToSymbol(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "≠",
        ComparisonOperator.Less => "<",
        ComparisonOperator.LessOrEqual => "≤",
        ComparisonOperator.Greater => ">",
        ComparisonOperator.GreaterOrEqual => "≥",
        _ => throw Unknown(op),
    };

    /// <summary>Deutscher Text, z. B. „kleiner oder gleich“.</summary>
    public static string ToGermanText(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "gleich",
        ComparisonOperator.NotEqual => "ungleich",
        ComparisonOperator.Less => "kleiner als",
        ComparisonOperator.LessOrEqual => "kleiner oder gleich",
        ComparisonOperator.Greater => "größer als",
        ComparisonOperator.GreaterOrEqual => "größer oder gleich",
        _ => throw Unknown(op),
    };

    /// <summary>true, wenn <c>left OP right</c> gilt.</summary>
    public static bool Holds(ComparisonOperator op, long left, long right) => op switch
    {
        ComparisonOperator.Equal => left == right,
        ComparisonOperator.NotEqual => left != right,
        ComparisonOperator.Less => left < right,
        ComparisonOperator.LessOrEqual => left <= right,
        ComparisonOperator.Greater => left > right,
        ComparisonOperator.GreaterOrEqual => left >= right,
        _ => throw Unknown(op),
    };

    /// <summary>Symbol für Anzeigen, die nie scheitern dürfen: bei einem undefinierten Wert „?“.</summary>
    internal static string ToSymbolOrPlaceholder(ComparisonOperator op) => Enum.IsDefined(op) ? ToSymbol(op) : "?";

    private static ArgumentOutOfRangeException Unknown(ComparisonOperator op) =>
        new(nameof(op), op, "Unbekannter Vergleichsoperator.");
}
