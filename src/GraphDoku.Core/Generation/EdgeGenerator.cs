using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Generation;

/// <summary>Wie From und To einer erzeugten Kante festgelegt werden.</summary>
public enum EdgeDirectionPolicy
{
    /// <summary>
    /// From ist der Knoten, der in Leserichtung zuerst kommt: kleinere Gitterzeile (y), dann weiter links
    /// (waagerechte Weltkoordinate), dann kleinere Id. Bei „Kleiner als“ steigen die Werte also nach rechts bzw. unten.
    /// </summary>
    ReadingOrder,

    /// <summary>Umgekehrt: From ist der Knoten, der in Leserichtung später kommt.</summary>
    ReverseReadingOrder,
}

/// <summary>Auftrag an den <see cref="EdgeGenerator"/>.</summary>
/// <param name="Distance">Abstandsregel: welche Knotenpaare eine Kante bekommen.</param>
/// <param name="Rule">Typname der Kantenregel (z. B. "NotEqual").</param>
public sealed record EdgeGenerationRequest(DistanceRule Distance, string Rule)
{
    /// <summary>Parameter der neuen Kanten; <c>null</c> = Standardparameter der Regel (<see cref="IRule.CreateDefaultParams"/>).</summary>
    public RuleParams? Params { get; init; }

    /// <summary>
    /// Knoten, auf die der Generator angewendet wird (z. B. die Auswahl); <c>null</c> = alle Knoten. Beide Endknoten
    /// einer Kante müssen in dieser Menge liegen.
    /// </summary>
    public IReadOnlyCollection<int>? NodeIds { get; init; }

    /// <summary>Richtung der Kanten (bedeutsam nur bei gerichteten Regeln wie „Kleiner als“).</summary>
    public EdgeDirectionPolicy Direction { get; init; } = EdgeDirectionPolicy.ReadingOrder;
}

/// <summary>Ergebnis des <see cref="EdgeGenerator"/>. Das Rätsel wurde nicht verändert.</summary>
public sealed class EdgeGenerationResult
{
    internal EdgeGenerationResult(IReadOnlyList<Edge> edges, int matchingPairs, int skippedExisting, IReadOnlyList<string> errors, string message)
    {
        Edges = edges;
        MatchingPairs = matchingPairs;
        SkippedExisting = skippedExisting;
        Errors = errors;
        Message = message;
    }

    /// <summary>
    /// Die neuen Kanten mit fortlaufenden Ids ab <see cref="Puzzle.NextEdgeId"/>, mindestens aber ab 1 (leer bei Fehlern).
    /// </summary>
    public IReadOnlyList<Edge> Edges { get; }

    /// <summary>Anzahl der Knotenpaare, die die Abstandsregel erfüllen (mit und ohne bestehende Kante).</summary>
    public int MatchingPairs { get; }

    /// <summary>Anzahl der Paare, die schon eine Kante mit derselben Regel haben und deshalb übersprungen wurden.</summary>
    public int SkippedExisting { get; }

    /// <summary>Deutsche Fehlermeldungen (leer = Erfolg).</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    /// <summary>Deutsche Zusammenfassung für die Oberfläche.</summary>
    public string Message { get; }

    /// <summary>
    /// Fügt die erzeugten Kanten (als Kopien) dem Rätsel hinzu. Rückgängig machen = die Kanten mit den Ids aus
    /// <see cref="Edges"/> wieder entfernen.
    /// </summary>
    /// <exception cref="InvalidOperationException">Das Ergebnis enthält Fehler, oder eine Kanten-Id ist im Rätsel schon vergeben
    /// (das Rätsel wurde seit dem Erzeugen verändert).</exception>
    public void ApplyTo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        if (!IsSuccess)
        {
            throw new InvalidOperationException("Ein fehlerhaftes Generator-Ergebnis kann nicht angewendet werden.");
        }

        var used = puzzle.Edges.Select(e => e.Id).ToHashSet();
        if (Edges.Any(e => used.Contains(e.Id)))
        {
            throw new InvalidOperationException("Die Kanten-Ids des Generators sind im Rätsel bereits vergeben; bitte neu erzeugen.");
        }

        foreach (var edge in Edges)
        {
            puzzle.Edges.Add(edge.Clone());
        }
    }

    public override string ToString() => Message;
}

/// <summary>
/// Kanten-Generator (Paket 5): erzeugt für alle Knotenpaare einer Knotenmenge, die eine <see cref="DistanceRule"/>
/// erfüllen, Kanten eines gewählten Regeltyps. Verändert das Rätsel nicht, sondern liefert die neuen Kanten; der Editor
/// fügt sie in einem einzigen Undo-Schritt ein (<see cref="EdgeGenerationResult.ApplyTo"/>).
/// <list type="bullet">
/// <item>Doppelte Kanten werden vermieden: Ein Paar wird übersprungen, wenn zwischen beiden Knoten schon eine Kante mit
/// demselben Regeltyp besteht – ungerichtet verglichen, auch bei „Kleiner als“ (eine Gegenkante würde dort sofort einen
/// Widerspruch erzeugen) und unabhängig von den Parametern.</item>
/// <item>Knoten an derselben Gitterposition werden nie verbunden.</item>
/// <item>Reihenfolge der Kanten: nach dem Knoten des Paars, der in Leserichtung zuerst kommt, dann nach dem zweiten –
/// unabhängig von der <see cref="EdgeDirectionPolicy"/> (bei <see cref="EdgeDirectionPolicy.ReverseReadingOrder"/> also
/// nach To, dann From).</item>
/// <item>Kanten-Ids beginnen bei <see cref="Puzzle.NextEdgeId"/>, mindestens bei 1: Auch wenn eine ungeprüft geöffnete
/// Datei Kanten mit Ids ≤ 0 enthält, erzeugt der Generator keine weitere ungültige Id. Reichen die Ids bis
/// <see cref="int.MaxValue"/> nicht für alle neuen Kanten (größte vorhandene Id zu nahe daran), ist das ein Fehler im
/// Ergebnis statt eines Überlaufs.</item>
/// </list>
/// </summary>
public sealed class EdgeGenerator
{
    public EdgeGenerator(RuleRegistry? registry = null)
    {
        Registry = registry ?? RuleRegistry.Default;
    }

    public RuleRegistry Registry { get; }

    /// <summary>Erzeugt die Kanten. Wirft nur bei Programmierfehlern (<c>null</c>); Eingabefehler stehen in <see cref="EdgeGenerationResult.Errors"/>.</summary>
    public EdgeGenerationResult Generate(Puzzle puzzle, EdgeGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Distance);

        var errors = new List<string>();
        errors.AddRange(request.Distance.Validate(puzzle.Grid));
        var parameters = ValidateRule(puzzle, request, errors, out var rule);
        var nodes = SelectNodes(puzzle, request.NodeIds, errors);
        if (errors.Count > 0 || rule is null || parameters is null || nodes is null)
        {
            return Failure(errors);
        }

        var pairs = FindPairs(puzzle.Grid, nodes, request.Distance);
        var existing = puzzle.Edges
            .Where(e => e.Rule == rule.TypeName)
            .Select(e => PairKey(e.From, e.To))
            .ToHashSet();

        var created = new List<(Node From, Node To)>();
        var skipped = 0;
        foreach (var (first, second) in pairs)
        {
            if (!existing.Add(PairKey(first.Id, second.Id)))
            {
                skipped++;
                continue;
            }

            created.Add(request.Direction == EdgeDirectionPolicy.ReadingOrder ? (first, second) : (second, first));
        }

        // Ids ≤ 0 sind ungültig (PuzzleValidator), daher mindestens ab 1. In long gerechnet: Bei einer vorhandenen Id
        // nahe int.MaxValue liefe NextEdgeId() bzw. das Hochzählen sonst über (negative oder doppelte Ids).
        var firstId = Math.Max(1L, puzzle.Edges.Count == 0 ? 1L : puzzle.Edges.Max(e => (long)e.Id) + 1);
        if (created.Count > 0 && firstId + created.Count - 1 > int.MaxValue)
        {
            return Failure([$"Für {created.Count} neue Kanten sind keine freien Kanten-Ids mehr übrig (die größte vorhandene Id ist {firstId - 1}); bitte die Kanten-Ids in der Datei verkleinern."]);
        }

        var edges = created
            .Select((pair, i) => new Edge((int)(firstId + i), pair.From.Id, pair.To.Id, rule.TypeName, parameters.Clone()))
            .ToList();

        return new EdgeGenerationResult(edges, pairs.Count, skipped, [], Describe(rule, request.Distance, edges.Count, pairs.Count, skipped));
    }

    private RuleParams? ValidateRule(Puzzle puzzle, EdgeGenerationRequest request, List<string> errors, out IRule? rule)
    {
        if (!Registry.TryGet(request.Rule ?? string.Empty, out rule))
        {
            errors.Add(Registry.UnknownRuleMessage(request.Rule ?? string.Empty));
            return null;
        }

        if (!rule.Kind.AppliesToEdges())
        {
            errors.Add($"Die Regel „{rule.DisplayName}“ ({rule.TypeName}) gilt für {rule.Kind.ToGermanText()}, nicht für Kanten. Der Generator erzeugt nur Kanten.");
            return null;
        }

        var parameters = request.Params?.Clone() ?? rule.CreateDefaultParams();

        // Die Parameter werden an einer Probekante geprüft (die Knotenanzahl ist bei Kanten immer 2).
        var probe = new Edge(0, 0, 0, rule.TypeName, parameters);
        foreach (var error in rule.Validate(probe, puzzle))
        {
            errors.Add($"Ungültige Parameter für „{rule.DisplayName}“: {error}");
        }

        return parameters;
    }

    private static List<Node>? SelectNodes(Puzzle puzzle, IReadOnlyCollection<int>? nodeIds, List<string> errors)
    {
        var duplicates = puzzle.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key).Order().ToList();
        if (duplicates.Count > 0)
        {
            errors.Add($"Die Knoten-Ids {string.Join(", ", duplicates)} sind mehrfach vergeben; bitte zuerst die Prüfmeldungen beheben.");
            return null;
        }

        if (nodeIds is null)
        {
            return [.. puzzle.Nodes];
        }

        var byId = puzzle.Nodes.ToDictionary(n => n.Id);
        var missing = nodeIds.Where(id => !byId.ContainsKey(id)).Distinct().Order().ToList();
        if (missing.Count > 0)
        {
            errors.Add(missing.Count == 1
                ? $"Der Knoten {missing[0]} existiert nicht."
                : $"Die Knoten {string.Join(", ", missing)} existieren nicht.");
            return null;
        }

        return [.. nodeIds.Distinct().Select(id => byId[id])];
    }

    /// <summary>Alle Paare (erster, zweiter in Leserichtung), geordnet nach dem ersten, dann dem zweiten Knoten.</summary>
    private static List<(Node First, Node Second)> FindPairs(GridType grid, List<Node> nodes, DistanceRule distance)
    {
        var ordered = nodes
            .Select(n => (Node: n, Point: new GridPoint(n.X, n.Y), World: GridGeometry.ToWorld(grid, n.X, n.Y)))
            .OrderBy(n => n.Node.Y)
            .ThenBy(n => n.World.X)
            .ThenBy(n => n.Node.Id)
            .ToList();
        var rank = new Dictionary<int, int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            rank[ordered[i].Node.Id] = i;
        }

        var pairs = new List<(Node First, Node Second)>();
        if (distance.Kind == DistanceKind.Free)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    if (ordered[i].Point != ordered[j].Point && distance.IsInRange(ordered[i].World.DistanceTo(ordered[j].World)))
                    {
                        pairs.Add((ordered[i].Node, ordered[j].Node));
                    }
                }
            }

            return pairs;
        }

        var byPoint = ordered.ToLookup(n => n.Point, n => n.Node);
        foreach (var (node, point, _) in ordered)
        {
            var partners = distance.GetPartners(grid, point)
                .Distinct()
                .SelectMany(p => byPoint[p])
                .Where(p => rank[p.Id] > rank[node.Id])
                .OrderBy(p => rank[p.Id]);
            foreach (var partner in partners)
            {
                pairs.Add((node, partner));
            }
        }

        return pairs;
    }

    private static (int, int) PairKey(int a, int b) => a <= b ? (a, b) : (b, a);

    private static EdgeGenerationResult Failure(List<string> errors) => new(
        [],
        0,
        0,
        errors,
        errors.Count == 1
            ? $"Es wurden keine Kanten erzeugt: {errors[0]}"
            : $"Es wurden keine Kanten erzeugt ({errors.Count} Fehler):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}");

    private static string Describe(IRule rule, DistanceRule distance, int created, int matching, int skipped)
    {
        if (matching == 0)
        {
            return $"Keine Kanten erzeugt: Kein Knotenpaar erfüllt die Abstandsregel „{distance.Description}“.";
        }

        if (created == 0)
        {
            return matching == 1
                ? $"Keine Kanten erzeugt: Das passende Knotenpaar hat bereits eine Kante „{rule.DisplayName}“."
                : $"Keine Kanten erzeugt: Alle {matching} passenden Knotenpaare haben bereits eine Kante „{rule.DisplayName}“.";
        }

        var text = created == 1
            ? $"1 Kante „{rule.DisplayName}“ erzeugt ({distance.Description})."
            : $"{created} Kanten „{rule.DisplayName}“ erzeugt ({distance.Description}).";
        return skipped switch
        {
            0 => text,
            1 => text + " 1 Knotenpaar hatte bereits eine solche Kante.",
            _ => text + $" {skipped} Knotenpaare hatten bereits eine solche Kante.",
        };
    }
}
