namespace GraphDoku.App.ViewModels;

/// <summary>Angaben zu einer inhaltlichen Änderung des Rätsels (Bearbeitung, Rückgängig/Wiederholen oder neues Dokument).</summary>
public sealed class PuzzleContentChangedEventArgs(bool documentReplaced, bool solutionWasShown, bool explanationWasShown = false) : EventArgs
{
    /// <summary>true bei Neu, Öffnen oder Vorlage; false bei einer Bearbeitung.</summary>
    public bool DocumentReplaced { get; } = documentReplaced;

    /// <summary>true, wenn dadurch eine angezeigte Lösung ausgeblendet wurde.</summary>
    public bool SolutionWasShown { get; } = solutionWasShown;

    /// <summary>true, wenn dadurch der Erklär-Modus beendet wurde (siehe <c>EditorViewModel.Explanation.cs</c>).</summary>
    public bool ExplanationWasShown { get; } = explanationWasShown;
}

/// <summary>
/// Anzeige einer Solver-Lösung in den Knoten. Die Lösung ist reiner Anzeigezustand: Sie steht nicht im Rätsel, ist kein
/// Undo-Schritt, macht das Dokument nicht „ungespeichert“ und wird nie gespeichert. Jede inhaltliche Änderung des
/// Rätsels blendet sie aus, damit nie eine veraltete Lösung zu sehen ist.
/// </summary>
public sealed partial class EditorViewModel
{
    private static readonly IReadOnlySet<int> NoNodes = new HashSet<int>();

    private long _observedRevision;
    private long _contentVersion;

    /// <summary>Nach jeder inhaltlichen Änderung des Rätsels (nicht nach Speichern, Auswahl oder Ansicht).</summary>
    public event EventHandler<PuzzleContentChangedEventArgs>? ContentChanged;

    /// <summary>
    /// Zählt inhaltliche Änderungen über Dokumentwechsel hinweg. Ein Solver-Ergebnis passt nur zum Rätsel, wenn sich
    /// dieser Wert seit dem Start der Suche nicht geändert hat.
    /// </summary>
    public long ContentVersion => _contentVersion;

    /// <summary>Angezeigte Lösung (Wert pro Knoten-Id) oder null.</summary>
    public IReadOnlyDictionary<int, int>? SolutionValues { get; private set; }

    /// <summary>Knoten, in denen sich zwei gefundene Lösungen unterscheiden (bei mehrdeutigen Rätseln hervorgehoben).</summary>
    public IReadOnlySet<int> SolutionDifferences { get; private set; } = NoNodes;

    public bool HasSolution => SolutionValues is not null;

    /// <summary>Lösungswert eines Knotens, falls eine Lösung angezeigt wird.</summary>
    public int? SolutionValue(int nodeId) => SolutionValues is { } values && values.TryGetValue(nodeId, out var value) ? value : null;

    public bool IsSolutionDifference(int nodeId) => SolutionDifferences.Contains(nodeId);

    /// <summary>Zeigt eine Lösung in den Knoten an (ohne das Rätsel zu verändern).</summary>
    /// <param name="values">Wert pro Knoten-Id.</param>
    /// <param name="differences">Optional: Knoten, in denen sich zwei Lösungen unterscheiden.</param>
    public void ShowSolution(IReadOnlyDictionary<int, int> values, IEnumerable<int>? differences = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Lösung und Erklärung schließen sich auf der Zeichenfläche aus: Eine angezeigte Lösung beendet den Erklär-Modus.
        ClearExplanationCore();
        SolutionValues = new Dictionary<int, int>(values);
        SolutionDifferences = differences is null ? NoNodes : differences.ToHashSet();
        OnPropertyChanged(nameof(SolutionValues));
        OnPropertyChanged(nameof(HasSolution));
        RaiseVisualChanged();
    }

    /// <summary>Blendet die angezeigte Lösung aus.</summary>
    public void HideSolution()
    {
        if (ClearSolutionCore())
        {
            RaiseVisualChanged();
        }
    }

    /// <summary>
    /// Vor einer inhaltlichen Änderung: Lösung ausblenden und Erklär-Modus beenden (ohne eigenes Neuzeichnen) und Version
    /// erhöhen.
    /// </summary>
    private (bool SolutionWasShown, bool ExplanationWasShown) ClearDisplayForChange()
    {
        _contentVersion++;
        return (ClearSolutionCore(), ClearExplanationCore());
    }

    private bool ClearSolutionCore()
    {
        if (SolutionValues is null)
        {
            return false;
        }

        SolutionValues = null;
        SolutionDifferences = NoNodes;
        OnPropertyChanged(nameof(SolutionValues));
        OnPropertyChanged(nameof(HasSolution));
        return true;
    }

    private void RaiseContentChanged(bool documentReplaced, (bool Solution, bool Explanation) shown) =>
        ContentChanged?.Invoke(this, new PuzzleContentChangedEventArgs(documentReplaced, shown.Solution, shown.Explanation));
}
