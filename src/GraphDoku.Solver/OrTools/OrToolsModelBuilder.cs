using Google.OrTools.Sat;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Validation;

namespace GraphDoku.Solver.OrTools;

/// <summary>Ein fertig übersetztes CP-SAT-Modell mit einer Variablen pro Knoten.</summary>
public sealed class OrToolsModel
{
    internal OrToolsModel(CpModel model, IReadOnlyList<(int NodeId, IntVar Variable)> variables)
    {
        Model = model;
        NodeVariables = variables;
    }

    public CpModel Model { get; }

    /// <summary>Knoten-Id und zugehörige Variable in der Reihenfolge der Knotenliste.</summary>
    public IReadOnlyList<(int NodeId, IntVar Variable)> NodeVariables { get; }
}

/// <summary>Ergebnis von <see cref="OrToolsModelBuilder.Build"/>: Modell oder deutsche Fehlermeldungen.</summary>
public sealed class OrToolsModelBuildResult
{
    internal OrToolsModelBuildResult(OrToolsModel? model, IReadOnlyList<string> errors)
    {
        Model = model;
        Errors = errors;
    }

    public OrToolsModel? Model { get; }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Übersetzt ein Rätsel in ein CP-SAT-Modell: eine Integer-Variable pro Knoten mit ihrem (ggf. eigenen) Wertebereich,
/// Givens fixiert, jede Kante und Gruppe über den Adapter ihres Regeltyps.
/// </summary>
public sealed class OrToolsModelBuilder
{
    private readonly RuleRegistry _rules;
    private readonly OrToolsAdapterRegistry _adapters;

    public OrToolsModelBuilder(RuleRegistry? rules = null, OrToolsAdapterRegistry? adapters = null)
    {
        _rules = rules ?? RuleRegistry.Default;
        _adapters = adapters ?? OrToolsAdapterRegistry.Default;
    }

    /// <summary>Prüft das Rätsel und baut das Modell. Bei Fehlern ist <see cref="OrToolsModelBuildResult.Model"/> <c>null</c>.</summary>
    public OrToolsModelBuildResult Build(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var errors = PuzzleValidator.Validate(puzzle, _rules).ToList();
        foreach (var constraint in puzzle.Constraints)
        {
            if (_rules.Contains(constraint.Rule) && !_adapters.Contains(constraint.Rule))
            {
                errors.Add($"{constraint.Ref}: Der Solver kann die Regel „{constraint.Rule}“ nicht übersetzen, weil für sie kein OR-Tools-Adapter registriert ist.");
            }
        }

        if (errors.Count > 0)
        {
            return new OrToolsModelBuildResult(null, errors);
        }

        var model = new CpModel();
        var variables = new Dictionary<int, IntVar>();
        var domains = new Dictionary<int, ValueDomain>();
        var ordered = new List<(int, IntVar)>(puzzle.Nodes.Count);
        foreach (var node in puzzle.Nodes)
        {
            var domain = node.Given is int given ? new ValueDomain(given, given) : puzzle.GetEffectiveDomain(node);
            var variable = model.NewIntVar(domain.Min, domain.Max, $"n{node.Id}");
            variables.Add(node.Id, variable);
            domains.Add(node.Id, domain);
            ordered.Add((node.Id, variable));
        }

        var context = new OrToolsModelContext(puzzle, model, variables, domains);
        foreach (var constraint in puzzle.Constraints)
        {
            try
            {
                _adapters.TryGet(constraint.Rule, out var adapter);
                adapter!.AddToModel(context, constraint);
            }
            catch (RuleParamsException exception)
            {
                errors.Add($"{constraint.Ref} ({constraint.Rule}): {exception.Message}");
            }
        }

        var modelError = model.Validate();
        if (!string.IsNullOrEmpty(modelError))
        {
            errors.Add($"OR-Tools hat das erzeugte Modell abgelehnt: {modelError}");
        }

        return errors.Count > 0
            ? new OrToolsModelBuildResult(null, errors)
            : new OrToolsModelBuildResult(new OrToolsModel(model, ordered), []);
    }
}
