using Avalonia.Controls;

namespace GraphDoku.App.Views;

/// <summary>
/// Einfaches Meldungsfenster mit frei wählbaren Schaltflächen. Ergebnis von <see cref="ShowAsync"/> ist der Index der
/// gewählten Schaltfläche, <see cref="ClosedResult"/>, wenn das Fenster ohne Schaltfläche geschlossen wurde
/// (Schließfeld der Titelleiste, Cmd+W, Alt+F4).
/// </summary>
public partial class MessageWindow : Window
{
    /// <summary>Ergebnis, wenn das Fenster ohne Schaltfläche geschlossen wurde.</summary>
    public const int ClosedResult = -1;

    public MessageWindow()
        : this(string.Empty, string.Empty, ["OK"])
    {
    }

    public MessageWindow(string title, string message, IReadOnlyList<string> buttons, int defaultIndex = 0, int cancelIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], IsDefault = i == defaultIndex, IsCancel = i == cancelIndex };
            button.Click += (_, _) => Close(index);
            Buttons.Children.Add(button);
        }
    }

    /// <summary>
    /// Zeigt das Fenster modal. Nicht <c>ShowDialog&lt;int&gt;</c> direkt verwenden: Beim Schließen ohne Schaltfläche
    /// liefert Avalonia <c>default(int)</c> = 0, also die erste Schaltfläche (Nr. 165).
    /// </summary>
    public async Task<int> ShowAsync(Window owner)
    {
        var result = await ShowDialog<int?>(owner).ConfigureAwait(true);
        return result ?? ClosedResult;
    }
}
