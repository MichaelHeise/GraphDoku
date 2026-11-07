using GraphDoku.Core.Model;

namespace GraphDoku.Solver.Tests.Helpers;

/// <summary>
/// Unabhängige Prüfung einer Belegung gegen ein Rätsel: Wertebereiche, Givens und die Bedeutung aller acht Regeln,
/// direkt aus dem Regelkatalog der Spezifikation nachprogrammiert (ohne Solver- oder Propagationscode).
/// </summary>
internal static class SolutionVerifier
{
    /// <summary>Liefert alle Verstöße (leer = gültige Lösung).</summary>
    public static List<string> Violations(Puzzle puzzle, IReadOnlyDictionary<int, int> values)
    {
        var errors = new List<string>();
        foreach (var node in puzzle.Nodes)
        {
            if (!values.TryGetValue(node.Id, out var value))
            {
                errors.Add($"Knoten {node.Id} hat keinen Wert.");
                continue;
            }

            var domain = node.Domain ?? puzzle.Domain;
            if (value < domain.Min || value > domain.Max)
            {
                errors.Add($"Knoten {node.Id} = {value} liegt außerhalb von {domain}.");
            }

            if (node.Given is int given && given != value)
            {
                errors.Add($"Knoten {node.Id} = {value}, Given ist {given}.");
            }
        }

        if (values.Count != puzzle.Nodes.Count)
        {
            errors.Add($"{values.Count} Werte für {puzzle.Nodes.Count} Knoten.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        foreach (var edge in puzzle.Edges)
        {
            long a = values[edge.From];
            long b = values[edge.To];
            var ok = edge.Rule switch
            {
                "NotEqual" => a != b,
                "Difference" => edge.Params.GetString("mode", "exact") == "atLeast"
                    ? Math.Abs(a - b) >= edge.Params.GetInt("k", 0)
                    : Math.Abs(a - b) == edge.Params.GetInt("k", 0),
                "Ratio" => a == edge.Params.GetInt("factor", 2) * b || b == edge.Params.GetInt("factor", 2) * a,
                "LessThan" => a < b,
                _ => throw new NotSupportedException($"Kantenregel {edge.Rule} ist im Prüfer nicht bekannt."),
            };
            if (!ok)
            {
                errors.Add($"Kante {edge.Id} ({edge.Rule}) verletzt: {a}, {b}.");
            }
        }

        foreach (var group in puzzle.Groups)
        {
            var groupValues = group.Nodes.Select(id => values[id]).ToList();
            var distinct = groupValues.Distinct().Count() == groupValues.Count;
            var ok = group.Rule switch
            {
                "AllDifferent" => distinct,
                "CoverRange" => groupValues.Order().SequenceEqual(Enumerable.Range(
                    group.Params.GetInt("min", puzzle.Domain.Min),
                    group.Params.GetInt("max", puzzle.Domain.Max) - group.Params.GetInt("min", puzzle.Domain.Min) + 1)),
                "Sum" => groupValues.Sum(v => (long)v) == group.Params.GetInt("target", int.MinValue)
                    && (group.Params.GetBool("allowRepeats", false) || distinct),
                "Thermometer" => groupValues.Zip(groupValues.Skip(1)).All(p => p.First < p.Second),
                _ => throw new NotSupportedException($"Gruppenregel {group.Rule} ist im Prüfer nicht bekannt."),
            };
            if (!ok)
            {
                errors.Add($"Gruppe {group.Id} ({group.Rule}) verletzt: {string.Join(", ", groupValues)}.");
            }
        }

        return errors;
    }

    public static bool IsSolution(Puzzle puzzle, IReadOnlyDictionary<int, int> values) => Violations(puzzle, values).Count == 0;

    /// <summary>Zählt alle Lösungen eines kleinen Rätsels durch vollständiges Durchprobieren (nur für Tests mit wenigen Knoten).</summary>
    public static int BruteForceCount(Puzzle puzzle)
    {
        var nodes = puzzle.Nodes.ToList();
        var domains = nodes.Select(n => n.Given is int g ? [g] : (n.Domain ?? puzzle.Domain).Values.ToArray()).ToList();
        var total = domains.Aggregate(1L, (product, d) => product * d.Length);
        if (total > 2_000_000)
        {
            throw new InvalidOperationException($"Zu viele Belegungen ({total}) zum Durchprobieren.");
        }

        var values = new Dictionary<int, int>();
        var count = 0;
        Enumerate(0);
        return count;

        void Enumerate(int index)
        {
            if (index == nodes.Count)
            {
                if (IsSolution(puzzle, values))
                {
                    count++;
                }

                return;
            }

            foreach (var value in domains[index])
            {
                values[nodes[index].Id] = value;
                Enumerate(index + 1);
            }
        }
    }
}
