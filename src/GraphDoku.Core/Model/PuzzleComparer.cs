namespace GraphDoku.Core.Model;

/// <summary>Strukturvergleich zweier Rätsel (Reihenfolge der Listen ist bedeutsam).</summary>
public static class PuzzleComparer
{
    public static bool AreEqual(Puzzle a, Puzzle b) => Differences(a, b).Count == 0;

    /// <summary>Liefert lesbare Beschreibungen aller Unterschiede (leer = gleich).</summary>
    public static IReadOnlyList<string> Differences(Puzzle a, Puzzle b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var diffs = new List<string>();
        Compare(diffs, "name", a.Name, b.Name);
        Compare(diffs, "grid", a.Grid, b.Grid);
        Compare(diffs, "domain", a.Domain, b.Domain);

        CompareLists(diffs, "nodes", a.Nodes, b.Nodes, (path, x, y) =>
        {
            Compare(diffs, path + ".id", x.Id, y.Id);
            Compare(diffs, path + ".x", x.X, y.X);
            Compare(diffs, path + ".y", x.Y, y.Y);
            Compare(diffs, path + ".domain", x.Domain, y.Domain);
            Compare(diffs, path + ".given", x.Given, y.Given);
        });

        CompareLists(diffs, "edges", a.Edges, b.Edges, (path, x, y) =>
        {
            Compare(diffs, path + ".id", x.Id, y.Id);
            Compare(diffs, path + ".from", x.From, y.From);
            Compare(diffs, path + ".to", x.To, y.To);
            Compare(diffs, path + ".rule", x.Rule, y.Rule);
            CompareParams(diffs, path + ".params", x.Params, y.Params);
        });

        CompareLists(diffs, "groups", a.Groups, b.Groups, (path, x, y) =>
        {
            Compare(diffs, path + ".id", x.Id, y.Id);
            if (!x.Nodes.SequenceEqual(y.Nodes))
            {
                diffs.Add($"{path}.nodes: [{string.Join(", ", x.Nodes)}] ≠ [{string.Join(", ", y.Nodes)}]");
            }

            Compare(diffs, path + ".rule", x.Rule, y.Rule);
            CompareParams(diffs, path + ".params", x.Params, y.Params);
            Compare(diffs, path + ".color", x.Color, y.Color);
            Compare(diffs, path + ".label", x.Label, y.Label);
        });

        return diffs;
    }

    private static void Compare<T>(List<string> diffs, string path, T x, T y)
    {
        if (!EqualityComparer<T>.Default.Equals(x, y))
        {
            diffs.Add($"{path}: {x?.ToString() ?? "null"} ≠ {y?.ToString() ?? "null"}");
        }
    }

    private static void CompareParams(List<string> diffs, string path, RuleParams x, RuleParams y)
    {
        if (!x.StructurallyEquals(y))
        {
            diffs.Add($"{path}: {x} ≠ {y}");
        }
    }

    private static void CompareLists<T>(List<string> diffs, string path, IList<T> x, IList<T> y, Action<string, T, T> compareItem)
    {
        if (x.Count != y.Count)
        {
            diffs.Add($"{path}: {x.Count} Einträge ≠ {y.Count} Einträge");
            return;
        }

        for (var i = 0; i < x.Count; i++)
        {
            compareItem($"{path}[{i}]", x[i], y[i]);
        }
    }
}
