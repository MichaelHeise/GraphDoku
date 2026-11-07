using Avalonia.Controls;
using Avalonia.Input;
using GraphDoku.App.Controls;
using GraphDoku.App.ViewModels;

namespace GraphDoku.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        RegisterShortcuts();
        KeyDown += OnEditKeyFallback;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Tastenkürzel mit Strg (Windows/Linux) und Cmd (macOS). Rückgängig/Wiederholen laufen nur, wenn kein Textfeld den
    /// Fokus hat (dort gilt das Rückgängig des Textfelds).
    /// </summary>
    private void RegisterShortcuts()
    {
        foreach (var modifier in new[] { KeyModifiers.Control, KeyModifiers.Meta })
        {
            Add(Key.Z, modifier, vm => RunUnlessTextBox(vm.Editor.UndoCommand.Execute));
            Add(Key.Y, modifier, vm => RunUnlessTextBox(vm.Editor.RedoCommand.Execute));
            Add(Key.Z, modifier | KeyModifiers.Shift, vm => RunUnlessTextBox(vm.Editor.RedoCommand.Execute));
            Add(Key.N, modifier, vm => vm.NewCommand.Execute(null));
            Add(Key.O, modifier, vm => vm.OpenCommand.Execute(null));
            Add(Key.S, modifier, vm => vm.SaveCommand.Execute(null));
            Add(Key.S, modifier | KeyModifiers.Shift, vm => vm.SaveAsCommand.Execute(null));
            Add(Key.A, modifier, vm => RunUnlessTextBox(_ => vm.Editor.SelectAll()));
            Add(Key.G, modifier, vm => vm.Editor.CreateGroupFromSelection());
            Add(Key.E, modifier, vm => vm.GenerateEdgesCommand.Execute(null));
            Add(Key.D0, modifier, vm => vm.Editor.FitView());
        }

        Add(Key.F5, KeyModifiers.None, vm => vm.Solve.SolveCommand.Execute(null));
        Add(Key.F6, KeyModifiers.None, vm => vm.Solve.CheckUniquenessCommand.Execute(null));
        Add(Key.F7, KeyModifiers.None, vm => vm.Explain.ExplainCommand.Execute(null));
        KeyDown += OnStepNavigationKey;
    }

    /// <summary>
    /// Erklär-Modus: ← / → einen Schritt zurück bzw. vor, Pos1 / Ende zum Anfang bzw. Ende – wenn die Zeichenfläche oder
    /// eine Schaltfläche (z. B. „Vor“) den Fokus hat. Textfelder, Listen, Auswahllisten und der Schieberegler behalten
    /// ihre eigene Bedeutung dieser Tasten (der Schieberegler bewegt sich dabei ohnehin schrittweise).
    /// </summary>
    private void OnStepNavigationKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || ViewModel is not { Explain.IsActive: true } vm || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (FocusManager?.GetFocusedElement() is not (null or GraphCanvas or Button or CheckBox))
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Left => vm.Explain.PreviousCommand,
            Key.Right => vm.Explain.NextCommand,
            Key.Home => vm.Explain.FirstCommand,
            Key.End => vm.Explain.LastCommand,
            _ => null,
        };
        if (command is not null)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void Add(Key key, KeyModifiers modifiers, Action<MainWindowViewModel> action)
    {
        var gesture = new KeyGesture(key, modifiers);
        KeyDown += (_, e) =>
        {
            if (!e.Handled && gesture.Matches(e) && ViewModel is { } vm)
            {
                action(vm);
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Entf, ⌫ und Given-Eingabe auch dann, wenn nicht die Zeichenfläche, sondern z. B. die Gruppenliste oder eine
    /// Schaltfläche des Panels den Fokus hat (sonst wirkte Entf nach Wahl einer Gruppe in der Liste nicht). Ausgenommen
    /// sind Textfelder, Auswahllisten und Menüs, die diese Tasten selbst brauchen.
    /// </summary>
    private void OnEditKeyFallback(object? sender, KeyEventArgs e)
    {
        if (e.Handled || ViewModel is not { } vm || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        if (FocusManager?.GetFocusedElement() is GraphCanvas or TextBox or ComboBox or ComboBoxItem or MenuItem)
        {
            return;
        }

        e.Handled = GraphCanvas.HandleEditKey(vm.Editor, e.Key);
    }

    private void RunUnlessTextBox(Action<object?> action)
    {
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        action(null);
    }

    /// <summary>Eine noch laufende Suche oder Erklärung beim Schließen abbrechen (sie liefe sonst im Hintergrund weiter).</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (ViewModel?.Solve.CancelCommand is { } cancel && cancel.CanExecute(null))
        {
            cancel.Execute(null);
        }

        if (ViewModel?.Explain.CancelCommand is { } cancelExplain && cancelExplain.CanExecute(null))
        {
            cancelExplain.Execute(null);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { } vm || !vm.Editor.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        if (await vm.ConfirmDiscardChangesAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
