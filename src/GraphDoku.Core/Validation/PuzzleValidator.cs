using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Validation;

/// <summary>Prüft ein Rätsel auf strukturelle und regelbezogene Fehler. Alle Meldungen sind deutsch.</summary>
public static class PuzzleValidator
{
    /// <summary>Liefert alle gefundenen Fehler (leer = gültig).</summary>
    public static IReadOnlyList<string> Validate(Puzzle puzzle, RuleRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        registry ??= RuleRegistry.Default;
        var errors = new List<string>();

        ValidateDomain(puzzle.Domain, "Der globale Wertebereich", errors);
        var nodeIds = ValidateNodes(puzzle, errors);
        ValidateEdges(puzzle, registry, nodeIds, errors);
        ValidateGroups(puzzle, registry, nodeIds, errors);
        return errors;
    }

    /// <exception cref="PuzzleValidationException">Das Rätsel enthält Fehler.</exception>
    public static void EnsureValid(Puzzle puzzle, RuleRegistry? registry = null)
    {
        var errors = Validate(puzzle, registry);
        if (errors.Count > 0)
        {
            throw new PuzzleValidationException(errors);
        }
    }

    private static void ValidateDomain(ValueDomain domain, string subject, List<string> errors)
    {
        if (!domain.IsValid)
        {
            errors.Add($"{subject} ist ungültig: min ({domain.Min}) ist größer als max ({domain.Max}).");
        }
        else if (domain.Size > ValueDomain.MaxSize)
        {
            errors.Add($"{subject} {domain} ist zu groß: höchstens {ValueDomain.MaxSize} Werte sind erlaubt.");
        }
    }

    private static HashSet<int> ValidateNodes(Puzzle puzzle, List<string> errors)
    {
        var ids = new HashSet<int>();
        foreach (var node in puzzle.Nodes)
        {
            if (node.Id <= 0)
            {
                errors.Add($"Knoten-Id {node.Id} ist ungültig: Ids müssen positive Ganzzahlen sein.");
            }

            if (!ids.Add(node.Id))
            {
                errors.Add($"Die Knoten-Id {node.Id} ist mehrfach vergeben.");
            }

            if (node.Domain is { } own)
            {
                ValidateDomain(own, $"Knoten {node.Id}: Der eigene Wertebereich", errors);
            }

            var domain = puzzle.GetEffectiveDomain(node);
            if (node.Given is int given && domain.IsValid && !domain.Contains(given))
            {
                errors.Add($"Knoten {node.Id}: Der vorgegebene Wert {given} liegt außerhalb des Wertebereichs {domain}.");
            }
        }

        return ids;
    }

    private static void ValidateEdges(Puzzle puzzle, RuleRegistry registry, HashSet<int> nodeIds, List<string> errors)
    {
        var ids = new HashSet<int>();
        foreach (var edge in puzzle.Edges)
        {
            var subject = $"Kante {edge.Id}";
            if (edge.Id <= 0)
            {
                errors.Add($"Kanten-Id {edge.Id} ist ungültig: Ids müssen positive Ganzzahlen sein.");
            }

            if (!ids.Add(edge.Id))
            {
                errors.Add($"Die Kanten-Id {edge.Id} ist mehrfach vergeben.");
            }

            var structureOk = true;
            foreach (var (role, nodeId) in new[] { ("Startknoten", edge.From), ("Zielknoten", edge.To) })
            {
                if (!nodeIds.Contains(nodeId))
                {
                    errors.Add($"{subject}: Der {role} {nodeId} existiert nicht.");
                    structureOk = false;
                }
            }

            if (edge.From == edge.To)
            {
                errors.Add($"{subject}: Start- und Zielknoten sind identisch (Knoten {edge.From}); eine Kante muss zwei verschiedene Knoten verbinden.");
                structureOk = false;
            }

            if (!registry.TryGet(edge.Rule, out var rule))
            {
                errors.Add($"{subject}: {registry.UnknownRuleMessage(edge.Rule)}");
                continue;
            }

            if (!rule.Kind.AppliesToEdges())
            {
                errors.Add($"{subject}: Die Regel „{rule.TypeName}“ ({rule.DisplayName}) gilt für {rule.Kind.ToGermanText()}, nicht für Kanten.");
                continue;
            }

            if (structureOk)
            {
                AddRuleErrors(rule, edge, puzzle, $"{subject} ({rule.TypeName})", errors);
            }
        }
    }

    private static void ValidateGroups(Puzzle puzzle, RuleRegistry registry, HashSet<int> nodeIds, List<string> errors)
    {
        var ids = new HashSet<int>();
        foreach (var group in puzzle.Groups)
        {
            var subject = group.Label is { Length: > 0 } label ? $"Gruppe {group.Id} „{label}“" : $"Gruppe {group.Id}";
            if (group.Id <= 0)
            {
                errors.Add($"Gruppen-Id {group.Id} ist ungültig: Ids müssen positive Ganzzahlen sein.");
            }

            if (!ids.Add(group.Id))
            {
                errors.Add($"Die Gruppen-Id {group.Id} ist mehrfach vergeben.");
            }

            var structureOk = true;
            var seen = new HashSet<int>();
            foreach (var nodeId in group.Nodes)
            {
                if (!nodeIds.Contains(nodeId))
                {
                    errors.Add($"{subject}: Der Knoten {nodeId} existiert nicht.");
                    structureOk = false;
                }

                if (!seen.Add(nodeId))
                {
                    errors.Add($"{subject}: Der Knoten {nodeId} ist mehrfach enthalten.");
                    structureOk = false;
                }
            }

            if (!registry.TryGet(group.Rule, out var rule))
            {
                errors.Add($"{subject}: {registry.UnknownRuleMessage(group.Rule)}");
                continue;
            }

            if (!rule.Kind.AppliesToGroups())
            {
                errors.Add($"{subject}: Die Regel „{rule.TypeName}“ ({rule.DisplayName}) gilt für {rule.Kind.ToGermanText()}, nicht für Gruppen.");
                continue;
            }

            if (structureOk)
            {
                AddRuleErrors(rule, group, puzzle, $"{subject} ({rule.TypeName})", errors);
            }
        }
    }

    private static void AddRuleErrors(IRule rule, IConstraint constraint, Puzzle puzzle, string subject, List<string> errors)
    {
        foreach (var error in rule.Validate(constraint, puzzle))
        {
            errors.Add($"{subject}: {error}");
        }
    }
}

/// <summary>Das Rätsel ist ungültig; <see cref="Errors"/> enthält alle deutschen Fehlermeldungen.</summary>
public sealed class PuzzleValidationException : Exception
{
    public PuzzleValidationException()
        : this([])
    {
    }

    public PuzzleValidationException(string message)
        : this([message])
    {
    }

    public PuzzleValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public PuzzleValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors) => errors.Count switch
    {
        0 => "Das Rätsel ist ungültig.",
        1 => $"Das Rätsel ist ungültig: {errors[0]}",
        _ => $"Das Rätsel ist ungültig ({errors.Count} Fehler):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}",
    };
}
