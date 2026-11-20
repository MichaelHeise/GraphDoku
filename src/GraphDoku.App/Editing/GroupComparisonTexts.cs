using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Anzeigetexte zu Gruppenvergleichen für Liste, Panel und Statusmeldungen. Fehlende Gruppen (Datei ohne inhaltliche
/// Prüfung geöffnet, Nr. 57) erscheinen mit „(fehlt)“, ein undefinierter Operator als „?“ – die Texte scheitern nie.
/// </summary>
public static class GroupComparisonTexts
{
    /// <summary>Symbol des Operators (=, ≠, &lt;, ≤, &gt;, ≥), bei einem undefinierten Wert „?“.</summary>
    public static string Symbol(ComparisonOperator op) => Enum.IsDefined(op) ? ComparisonOperators.ToSymbol(op) : "?";

    /// <summary>Gruppe wie in der Gruppenliste: „Käfig (2)“ bzw. „Gruppe 2“; fehlt sie, „Gruppe 2 (fehlt)“.</summary>
    public static string GroupTitle(Puzzle puzzle, int groupId)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        return puzzle.FindGroup(groupId) switch
        {
            null => $"Gruppe {groupId} (fehlt)",
            { Label: { } label } when !string.IsNullOrWhiteSpace(label) => $"{label} ({groupId})",
            _ => $"Gruppe {groupId}",
        };
    }

    /// <summary>Gruppe im Satz: „Gruppe 2 „Käfig““ bzw. „Gruppe 2“; fehlt sie, „Gruppe 2 (fehlt)“.</summary>
    public static string GroupName(Puzzle puzzle, int groupId)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        return puzzle.FindGroup(groupId) switch
        {
            null => $"Gruppe {groupId} (fehlt)",
            { Label: { } label } when !string.IsNullOrWhiteSpace(label) => $"Gruppe {groupId} „{label}“",
            _ => $"Gruppe {groupId}",
        };
    }

    /// <summary>Titel in der Liste der Gruppenvergleiche, z. B. „Käfig (2) ≤ Gruppe 5“.</summary>
    public static string ListTitle(Puzzle puzzle, GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return $"{GroupTitle(puzzle, comparison.Left)} {Symbol(comparison.Operator)} {GroupTitle(puzzle, comparison.Right)}";
    }

    /// <summary>Bedeutung, z. B. „Summe von Gruppe 2 „Käfig“ ≤ Summe von Gruppe 5“.</summary>
    public static string Meaning(Puzzle puzzle, GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return $"Summe von {GroupName(puzzle, comparison.Left)} {Symbol(comparison.Operator)} Summe von {GroupName(puzzle, comparison.Right)}";
    }

    /// <summary>„1 Gruppenvergleich“ bzw. „3 Gruppenvergleiche“.</summary>
    public static string Count(int count) => count == 1 ? "1 Gruppenvergleich" : $"{count} Gruppenvergleiche";

    /// <summary>
    /// Umfang des Rätsels für Status und Statistik: „81 Knoten, 0 Kanten, 27 Gruppen“, mit Gruppenvergleichen
    /// „…, 27 Gruppen, 2 Gruppenvergleiche“. Ohne Vergleiche genau der bisherige Text.
    /// </summary>
    public static string PuzzleCounts(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var text = $"{puzzle.Nodes.Count} Knoten, {puzzle.Edges.Count} Kanten, {puzzle.Groups.Count} Gruppen";
        return puzzle.GroupComparisons.Count == 0 ? text : $"{text}, {Count(puzzle.GroupComparisons.Count)}";
    }
}
