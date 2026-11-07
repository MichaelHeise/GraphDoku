using GraphDoku.App.Explaining;

namespace GraphDoku.App.ViewModels;

/// <summary>
/// Anzeige des Erklär-Modus auf der Zeichenfläche (Paket 6). Wie die Lösung (<c>EditorViewModel.Solution.cs</c>) ist
/// das reiner Anzeigezustand: nicht im Rätsel, kein Undo-Schritt, nie gespeichert. Jede inhaltliche Änderung des Rätsels
/// beendet den Modus; Lösung und Erklärung schließen sich gegenseitig aus.
/// </summary>
public sealed partial class EditorViewModel
{
    /// <summary>Angezeigter Zustand des Erklär-Modus oder <c>null</c> (normaler Editor).</summary>
    public ExplanationView? Explanation { get; private set; }

    public bool IsExplaining => Explanation is not null;

    /// <summary>Zeigt einen Zustand des Erklär-Modus an (blendet eine angezeigte Lösung aus).</summary>
    public void ShowExplanation(ExplanationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ClearSolutionCore();
        var wasExplaining = IsExplaining;
        Explanation = view;
        OnPropertyChanged(nameof(Explanation));
        if (!wasExplaining)
        {
            OnPropertyChanged(nameof(IsExplaining));
        }

        RaiseVisualChanged();
    }

    /// <summary>Beendet die Anzeige des Erklär-Modus.</summary>
    public void HideExplanation()
    {
        if (ClearExplanationCore())
        {
            RaiseVisualChanged();
        }
    }

    private bool ClearExplanationCore()
    {
        if (Explanation is null)
        {
            return false;
        }

        Explanation = null;
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(IsExplaining));
        return true;
    }
}
