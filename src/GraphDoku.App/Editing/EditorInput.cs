namespace GraphDoku.App.Editing;

/// <summary>Werkzeuge der Zeichenfläche.</summary>
public enum EditorTool
{
    /// <summary>Auswählen (Klick, Shift-Klick, Rahmen) und Verschieben von Knoten.</summary>
    Select,

    /// <summary>Knoten setzen (Klick oder Ziehen über mehrere Gitterpunkte).</summary>
    PlaceNode,

    /// <summary>Kante von Knoten zu Knoten ziehen.</summary>
    DrawEdge,

    /// <summary>Ansicht verschieben.</summary>
    Pan,
}

/// <summary>Maustaste (unabhängig von Avalonia, damit die Logik ohne Oberfläche testbar ist).</summary>
public enum PointerButton
{
    Left,
    Middle,
    Right,
}

/// <summary>Zusatztasten.</summary>
[Flags]
public enum EditorModifiers
{
    None = 0,
    Shift = 1,

    /// <summary>Strg bzw. Cmd (macOS).</summary>
    Command = 2,
}
