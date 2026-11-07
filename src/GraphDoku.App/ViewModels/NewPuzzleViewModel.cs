using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.App.Services;
using GraphDoku.Core.Model;

namespace GraphDoku.App.ViewModels;

/// <summary>Gittertyp mit deutschem Namen für Auswahllisten.</summary>
public sealed record GridOption(GridType Grid, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Dialog „Neu“: Name, Gittertyp und globaler Wertebereich.</summary>
public sealed partial class NewPuzzleViewModel : ViewModelBase
{
    public NewPuzzleViewModel()
    {
        SelectedGrid = GridOptions[0];
        Validate();
    }

    public IReadOnlyList<GridOption> GridOptions { get; } =
        [.. Enum.GetValues<GridType>().Select(g => new GridOption(g, GridNames.ToGerman(g)))];

    [ObservableProperty]
    public partial string Name { get; set; } = "Unbenannt";

    [ObservableProperty]
    public partial GridOption SelectedGrid { get; set; }

    [ObservableProperty]
    public partial string DomainMin { get; set; } = "1";

    [ObservableProperty]
    public partial string DomainMax { get; set; } = "9";

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsValid { get; set; }

    /// <summary>Deutsche Meldung, wenn der Bereich unbrauchbar ist (null = in Ordnung).</summary>
    public static string? CheckDomain(ValueDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!domain.IsValid)
        {
            return $"Das Minimum ({domain.Min}) darf nicht größer als das Maximum ({domain.Max}) sein.";
        }

        return domain.Size > ValueDomain.MaxSize ? $"Der Wertebereich darf höchstens {ValueDomain.MaxSize} Werte umfassen." : null;
    }

    /// <summary>Die gewählten Einstellungen oder null, wenn die Eingaben ungültig sind.</summary>
    public NewPuzzleOptions? ToOptions()
    {
        if (!TryGetDomain(out var domain) || CheckDomain(domain) is not null)
        {
            return null;
        }

        return new NewPuzzleOptions(string.IsNullOrWhiteSpace(Name) ? "Unbenannt" : Name.Trim(), SelectedGrid.Grid, domain);
    }

    partial void OnDomainMinChanged(string value) => Validate();

    partial void OnDomainMaxChanged(string value) => Validate();

    private void Validate()
    {
        Error = TryGetDomain(out var domain) ? CheckDomain(domain) : "Minimum und Maximum müssen ganze Zahlen sein.";
        IsValid = Error is null;
    }

    private bool TryGetDomain(out ValueDomain domain)
    {
        if (int.TryParse(DomainMin?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var min)
            && int.TryParse(DomainMax?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var max))
        {
            domain = new ValueDomain(min, max);
            return true;
        }

        domain = new ValueDomain(1, 9);
        return false;
    }
}
