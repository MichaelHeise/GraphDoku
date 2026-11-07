using Avalonia.Controls;

namespace GraphDoku.App.Views;

/// <summary>Einfaches Meldungsfenster mit frei wählbaren Schaltflächen; Ergebnis ist der Index der gewählten Schaltfläche (-1 = geschlossen).</summary>
public partial class MessageWindow : Window
{
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
}
