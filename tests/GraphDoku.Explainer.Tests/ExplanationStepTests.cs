using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Explainer.Tests.Helpers;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.Explainer.Tests;

/// <summary>Aufbau der Schritte (Zusammenfassung, Begründung, Hervorhebung) und Navigation über Zustände.</summary>
public class ExplanationStepTests
{
    private static readonly OrToolsSolver Solver = new();

    private static ExplanationResult Easy() => new ExplanationEngine(Solver).Explain(ExplainerPuzzles.Load("klassisch-leicht.json"));

    [Fact]
    public void Placed_value_eliminations_of_a_group_form_one_step()
    {
        var first = Easy().Steps[0];

        // Zeile 1 des leichten Sudokus: 5 3 . . 7 . . . .
        first.Kind.Should().Be(StepKind.EliminateCandidates);
        first.Technique.Should().Be(ExplanationTechnique.FixedValueElimination);
        first.Constraint.Should().Be(ConstraintRef.ForGroup(1));
        first.NodeIds.Should().Equal(3, 4, 6, 7, 8, 9);
        first.Changes.Should().OnlyContain(c => c.Values.SequenceEqual(new[] { 5 }));
        first.InvolvedNodeIds.Should().BeEquivalentTo([1, 3, 4, 6, 7, 8, 9]);
        first.Text.Should().Be("Gruppe 1 „Zeile 1“ enthält bereits die 5 (Knoten 1). Alle Werte sind verschieden. Also ist die 5 in den übrigen Knoten der Gruppe nicht mehr möglich.");
        first.Summary.Should().Be("Knoten 3, 4, 6, 7, 8, 9: streiche 5");
        first.NodeId.Should().BeNull();
        first.Value.Should().BeNull();
        first.ToString().Should().StartWith("Schritt 1 (gestrichene Kandidaten): Knoten 3, 4, 6, 7, 8, 9: streiche 5. Gruppe 1");
    }

    [Fact]
    public void Naked_and_hidden_single_steps_have_reasons()
    {
        var naked = Easy().Steps.First(s => s.Technique == ExplanationTechnique.NakedSingle);
        naked.Kind.Should().Be(StepKind.SetValue);
        naked.Constraint.Should().BeNull();
        naked.InvolvedNodeIds.Should().Equal(naked.NodeId!.Value);
        naked.Text.Should().Be($"Knoten {naked.NodeId} hat nur noch den Kandidaten {naked.Value}.");
        naked.Summary.Should().Be($"Knoten {naked.NodeId} = {naked.Value}");

        // Das leichte Sudoku kommt ganz mit einzigen Kandidaten aus; hidden singles zeigt „mittel“.
        var medium = new ExplanationEngine(Solver).Explain(ExplainerPuzzles.Load("klassisch-mittel.json"));
        var hidden = medium.Steps.First(s => s.Technique == ExplanationTechnique.HiddenSingle);
        hidden.Kind.Should().Be(StepKind.SetValue);
        hidden.Constraint.Should().NotBeNull();
        hidden.InvolvedNodeIds[0].Should().Be(hidden.NodeId!.Value);
        hidden.InvolvedNodeIds.Should().HaveCount(9, "die ganze Gruppe ist am Schluss beteiligt");
        var group = medium.Puzzle.FindConstraint(hidden.Constraint!.Value) as Group;
        hidden.Text.Should().Be(
            $"In Gruppe {group!.Id} „{group.Label}“ muss jede Zahl von 1 bis 9 genau einmal vorkommen. Die {hidden.Value} ist nur noch in Knoten {hidden.NodeId} möglich.");
        hidden.Changes.Should().ContainSingle().Which.Kind.Should().Be(CandidateChangeKind.Assign);
    }

    [Fact]
    public void Hidden_single_names_the_pigeonhole_argument_when_the_domains_are_larger_than_the_group()
    {
        // Gruppe aus drei Knoten mit Werten 1..4; Knoten 1 und 2 nur 1..2, Knoten 3 verliert die 4 über die Kante zu
        // Knoten 4 (= 4). Dann kommen nur noch 1, 2, 3 in Frage, und die 3 passt nur in Knoten 3.
        var puzzle = ExplainerPuzzles.WithNodes(4, 1, 4).Given(4, 4)
            .Group(AllDifferentRule.Name, [1, 2, 3], label: "Trio")
            .Edge(4, 3, NotEqualRule.Name);
        puzzle.Nodes[0].Domain = new ValueDomain(1, 2);
        puzzle.Nodes[1].Domain = new ValueDomain(1, 2);

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        var hidden = result.Steps.Should().Contain(s => s.Technique == ExplanationTechnique.HiddenSingle).Subject;
        hidden.NodeId.Should().Be(3);
        hidden.Value.Should().Be(3);
        hidden.Text.Should().Be(
            "Gruppe 1 „Trio“: Alle 3 Knoten der Gruppe haben verschiedene Werte, und es kommen nur noch die Werte 1, 2, 3 in Frage, genauso viele wie Knoten. Also muss jede Zahl von 1 bis 3 genau einmal vorkommen. Die 3 ist nur noch in Knoten 3 möglich.");
    }

    [Fact]
    public void Snapshots_match_forward_replay_of_all_steps()
    {
        var result = Easy();
        var puzzle = result.Puzzle;
        var state = CandidateState.FromPuzzle(puzzle);
        var set = puzzle.Nodes.Where(n => n.Given is not null).Select(n => n.Id).ToHashSet();

        result.GetInitialSnapshot().SetNodeIds.Should().BeEquivalentTo(set);
        result.GetInitialSnapshot().Candidates.HasSameCandidates(state).Should().BeTrue();
        for (var i = 0; i < result.Steps.Count; i++)
        {
            var step = result.Steps[i];
            foreach (var change in step.Changes)
            {
                state.Apply(change).Should().Be(change.Kind == CandidateChangeKind.Eliminate || step.Technique != ExplanationTechnique.NakedSingle);
            }

            if (step.Kind != StepKind.EliminateCandidates)
            {
                set.Add(step.NodeId!.Value).Should().BeTrue("ein Knoten wird nur einmal gesetzt");
            }

            var snapshot = result.GetSnapshot(i + 1);
            snapshot.AppliedSteps.Should().Be(i + 1);
            snapshot.Candidates.HasSameCandidates(state).Should().BeTrue($"Zustand nach Schritt {i + 1}");
            snapshot.SetNodeIds.Should().BeEquivalentTo(set);
        }

        result.GetFinalSnapshot().Candidates.IsSolved.Should().BeTrue();
    }

    [Fact]
    public void Snapshots_are_independent_copies_and_range_checked()
    {
        var result = Easy();
        var snapshot = result.GetSnapshot(5);
        var nodeId = snapshot.Candidates.NodeIds.First(id => !snapshot.IsSet(id));
        var before = snapshot.Candidates.GetCandidates(nodeId);

        snapshot.Candidates.Remove(nodeId, before.MinValue);

        result.GetSnapshot(5).Candidates.GetCandidates(nodeId).Should().Equal(before);
        snapshot.GetSetValue(nodeId).Should().BeNull();
        result.Invoking(r => r.GetSnapshot(-1)).Should().Throw<ArgumentOutOfRangeException>();
        result.Invoking(r => r.GetSnapshot(result.Steps.Count + 1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Step_constructor_checks_kind_and_changes()
    {
        var reason = new Reason(null, [1], "Test");
        var assign = CandidateChange.Assign(1, 2, reason);
        var eliminate = CandidateChange.Eliminate(1, [3], reason);

        var act1 = () => new ExplanationStep(0, StepKind.SetValue, ExplanationTechnique.NakedSingle, [eliminate], reason);
        var act2 = () => new ExplanationStep(0, StepKind.EliminateCandidates, ExplanationTechnique.RulePropagation, [assign], reason);
        var act3 = () => new ExplanationStep(0, StepKind.Branch, ExplanationTechnique.Branch, [], reason);

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
        new ExplanationStep(0, StepKind.Branch, ExplanationTechnique.Branch, [assign], reason).Summary.Should().Be("Knoten 1 = 2");
    }

    [Fact]
    public void Kinds_and_techniques_have_german_names()
    {
        StepKind.SetValue.ToGermanText().Should().Be("gesetzter Wert");
        StepKind.EliminateCandidates.ToGermanText().Should().Be("gestrichene Kandidaten");
        StepKind.Branch.ToGermanText().Should().Be("Verzweigung");
        ExplanationTechnique.FixedValueElimination.ToGermanText().Should().Be("Feststehenden Wert streichen");
        ExplanationTechnique.NakedSingle.ToGermanText().Should().Be("Einziger Kandidat");
        ExplanationTechnique.HiddenSingle.ToGermanText().Should().Be("Einziger Platz in der Gruppe");
        ExplanationEngine.GetRuleRank("AllDifferent").Should().BeLessThan(ExplanationEngine.GetRuleRank("LessThan"));
        ExplanationEngine.GetRuleRank("LessThan").Should().BeLessThan(ExplanationEngine.GetRuleRank("Sum"));
        ExplanationEngine.GetRuleRank("Eigene").Should().Be(3);
    }
}
