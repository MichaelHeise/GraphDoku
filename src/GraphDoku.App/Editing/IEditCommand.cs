using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Eine rückgängig machbare Bearbeitung des Rätsels (Command-Muster). <see cref="Execute"/> wird beim ersten
/// Ausführen und beim Wiederholen aufgerufen, <see cref="Undo"/> beim Rückgängigmachen. Undo wird nur auf genau dem
/// Zustand aufgerufen, den <see cref="Execute"/> hinterlassen hat (Stapelprinzip).
/// </summary>
public interface IEditCommand
{
    /// <summary>Kurze deutsche Beschreibung, z. B. „Knoten setzen“ (für Menü und Statuszeile).</summary>
    string Description { get; }

    void Execute(Puzzle puzzle);

    void Undo(Puzzle puzzle);

    /// <summary>
    /// Versucht, die bereits ausgeführte Bearbeitung <paramref name="later"/> in diese aufzunehmen (z. B. fortlaufendes
    /// Ziehen). Liefert true, wenn danach ein einziger Rückgängig-Schritt beide Bearbeitungen umfasst.
    /// </summary>
    bool TryMerge(IEditCommand later);
}

/// <summary>Basis für Befehle, die über einen Zusammenführungsschlüssel (z. B. eine Ziehgeste) verschmelzen.</summary>
public abstract class EditCommandBase : IEditCommand
{
    protected EditCommandBase(object? mergeKey) => MergeKey = mergeKey;

    /// <summary>Befehle mit demselben (nicht leeren) Schlüssel und Typ werden zusammengeführt.</summary>
    public object? MergeKey { get; }

    public abstract string Description { get; }

    public abstract void Execute(Puzzle puzzle);

    public abstract void Undo(Puzzle puzzle);

    public bool TryMerge(IEditCommand later)
    {
        if (MergeKey is null || later is not EditCommandBase other || other.GetType() != GetType() || !ReferenceEquals(other.MergeKey, MergeKey))
        {
            return false;
        }

        MergeFrom(other);
        return true;
    }

    /// <summary>Übernimmt die Wirkung von <paramref name="later"/> (gleicher Typ, gleicher Schlüssel).</summary>
    protected abstract void MergeFrom(EditCommandBase later);
}
