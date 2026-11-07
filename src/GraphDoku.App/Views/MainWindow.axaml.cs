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
            Add(Key.D0, modifier, vm => vm.Editor.FitView());
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
