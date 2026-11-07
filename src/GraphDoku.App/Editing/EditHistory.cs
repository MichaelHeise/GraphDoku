using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Undo/Redo-Verlauf für ein Rätsel. Jede Bearbeitung läuft über <see cref="Execute"/>. Zusätzlich wird der
/// „gespeicherte Zustand“ verfolgt, damit <see cref="IsDirty"/> auch nach Rückgängig/Wiederholen stimmt.
/// </summary>
public sealed class EditHistory
{
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private long _nextStateId = 1;
    private long _savedStateId;

    public EditHistory(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        Puzzle = puzzle;
    }

    /// <summary>Wird nach jeder Änderung (Ausführen, Rückgängig, Wiederholen, Speichern, Leeren) ausgelöst.</summary>
    public event EventHandler? Changed;

    public Puzzle Puzzle { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? UndoDescription => CanUndo ? _undo[^1].Command.Description : null;

    public string? RedoDescription => CanRedo ? _redo[^1].Command.Description : null;

    /// <summary>Oberster Befehl des Undo-Stapels (z. B. um eine laufende Eingabe fortzusetzen).</summary>
    public IEditCommand? LastCommand => CanUndo ? _undo[^1].Command : null;

    /// <summary>true, wenn der aktuelle Zustand nicht dem zuletzt gespeicherten entspricht.</summary>
    public bool IsDirty => CurrentStateId != _savedStateId;

    private long CurrentStateId => CanUndo ? _undo[^1].StateId : 0;

    /// <summary>Führt den Befehl aus, legt ihn auf den Stapel (oder verschmilzt ihn mit dem obersten) und leert Redo.</summary>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute(Puzzle);
        _redo.Clear();
        if (CanUndo && _undo[^1].Command.TryMerge(command))
        {
            // Der Zustand hat sich geändert: neue Zustands-Id, damit ein Speicherstand dazwischen als veraltet gilt.
            _undo[^1] = _undo[^1] with { StateId = _nextStateId++ };
        }
        else
        {
            _undo.Add(new Entry(command, _nextStateId++));
        }

        OnChanged();
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Command.Undo(Puzzle);
        _redo.Add(entry);
        OnChanged();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.Command.Execute(Puzzle);
        _undo.Add(entry);
        OnChanged();
        return true;
    }

    /// <summary>Merkt den aktuellen Zustand als gespeichert.</summary>
    public void MarkSaved()
    {
        _savedStateId = CurrentStateId;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record Entry(IEditCommand Command, long StateId);
}
