using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Editing;
using GraphDoku.Core.Model;

namespace GraphDoku.App.ViewModels;

/// <summary>
/// Darstellung der Knoten (Paket 7): als Kreise (Standard) oder als Zellen in der Form des Gitters. Reine
/// Ansichtseinstellung wie das Ein- und Ausblenden von Gruppen: kein Undo-Schritt, macht das Dokument nicht
/// „ungespeichert“, steht nicht in der Rätseldatei und bleibt beim Öffnen eines anderen Rätsels erhalten (Nr. 152).
/// </summary>
public sealed partial class EditorViewModel
{
    /// <summary>Darstellung der Knoten auf der Zeichenfläche.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCircleShape), nameof(IsCellShape), nameof(ToolHint))]
    public partial NodeShape NodeShape { get; set; }

    /// <summary>Knoten als Kreise (für Menü und Werkzeugleiste).</summary>
    public bool IsCircleShape
    {
        get => NodeShape == NodeShape.Circle;
        set
        {
            if (value)
            {
                NodeShape = NodeShape.Circle;
            }
            else if (IsCircleShape)
            {
                // Ein Umschalter (Werkzeugleiste) ohne Gegenstück: Abwählen heißt dann die andere Darstellung.
                NodeShape = NodeShape.Cell;
            }
        }
    }

    /// <summary>Knoten als Zellen in der Form des Gitters (für Menü und Werkzeugleiste).</summary>
    public bool IsCellShape
    {
        get => NodeShape == NodeShape.Cell;
        set
        {
            if (value)
            {
                NodeShape = NodeShape.Cell;
            }
            else if (IsCellShape)
            {
                NodeShape = NodeShape.Circle;
            }
        }
    }

    /// <summary>Menütext der Zelldarstellung mit der Form des aktuellen Gitters, z. B. „Knoten als Zellen (Sechsecke)“.</summary>
    public string CellShapeText => $"Knoten als Zellen ({CellShapeName(Puzzle.Grid)})";

    /// <summary>Deutsche Mehrzahl der Zellform eines Gittertyps.</summary>
    public static string CellShapeName(GridType grid) => grid switch
    {
        GridType.Triangle => "Dreiecke",
        GridType.Hex => "Sechsecke",
        _ => "Quadrate",
    };

    /// <summary>Schaltet zwischen Kreisen und Zellen um.</summary>
    [RelayCommand]
    public void ToggleNodeShape() => NodeShape = NodeShape == NodeShape.Circle ? NodeShape.Cell : NodeShape.Circle;

    partial void OnNodeShapeChanged(NodeShape value)
    {
        StatusMessage = value == NodeShape.Cell
            ? $"Knoten werden als Zellen ({CellShapeName(Puzzle.Grid)}) dargestellt. Das Rätsel und die Datei ändern sich dadurch nicht."
            : "Knoten werden als Kreise dargestellt.";
        RaiseVisualChanged();
    }
}
