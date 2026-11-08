using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Generation;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Explainer;
using GraphDoku.Solver.OrTools;
using Xunit.Abstractions;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Erzeugt die Bildschirmfotos des Benutzerhandbuchs (<c>docs/benutzerhandbuch/bilder/</c>) headless aus dem echten
/// Hauptfenster und den Dialogen.
/// </summary>
/// <remarks>
/// Läuft nur, wenn die Umgebungsvariable <c>GRAPHDOKU_GUIDE_IMAGES</c> auf ein Ausgabeverzeichnis zeigt; sonst kehren alle
/// Facts sofort zurück (im normalen Lauf „bestanden“) – dasselbe Muster wie der Beispielrätsel-Generator
/// (docs/decisions.md Nr. 125). Aufruf:
/// <c>GRAPHDOKU_GUIDE_IMAGES=docs/benutzerhandbuch/bilder dotnet test tests/GraphDoku.App.Tests -c Release
/// --filter FullyQualifiedName~UserGuideScreenshots</c>. Die Bilder werden danach angesehen und eingecheckt; der normale
/// Testlauf erzeugt nichts. Hauptfenster in 1280 × 800, Text in Graustufen geglättet (siehe <see cref="HeadlessTestApp"/>).
/// </remarks>
[Collection(HeadlessTests.Name)]
public sealed class UserGuideScreenshots(HeadlessSessionFixture fixture, ITestOutputHelper output)
{
    /// <summary>Erklär-Modus etwas größer, damit die Kandidaten in den Knoten lesbar bleiben.</summary>
    private const int ExplainWidth = 1360;

    private const int ExplainHeight = 1000;

    private static string? OutputDirectory =>
        Environment.GetEnvironmentVariable("GRAPHDOKU_GUIDE_IMAGES") is { Length: > 0 } directory ? directory : null;

    [Fact]
    public Task Main_window_with_classic_sudoku() => Run(async () =>
    {
        var (window, _) = await OpenAsync("klassisch-leicht.json");
        Save(window, "hauptfenster-editor.png");
        window.Close();
    });

    [Fact]
    public Task Killer_cage_selected() => Run(async () =>
    {
        var (window, viewModel) = await OpenAsync("killer-sudoku.json");
        var editor = viewModel.Editor;
        var cage = editor.Puzzle.Groups.Where(g => g.Rule == "Sum").OrderBy(g => g.Id).First(g => g.Nodes.Count >= 3);
        editor.SelectGroup(cage.Id);
        editor.Properties.Should().BeOfType<GroupPropertiesViewModel>();
        Save(window, "editor-killer.png");
        window.Close();
    });

    [Fact]
    public Task Kropki_dots() => Run(async () =>
    {
        var (window, _) = await OpenAsync("kropki-sudoku.json");
        Save(window, "editor-kropki.png");
        window.Close();
    });

    [Fact]
    public Task Edge_generator_dialog() => Run(() =>
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        var generator = new EdgeGeneratorViewModel(editor)
        {
            SelectedRule = editor.EdgeRules.Single(r => r.TypeName == "Difference"),
        };
        generator.SelectedDistance = generator.DistanceOptions.Single(o => o.Kind == DistanceKind.Orthogonal);
        generator.UseAllNodes = true;
        var window = new EdgeGeneratorWindow { DataContext = generator };
        ShowSharp(window);
        Save(window, "kanten-generator.png");
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task New_puzzle_dialog() => Run(() =>
    {
        var window = new NewPuzzleWindow { DataContext = new NewPuzzleViewModel { Name = "Mein Rätsel" } };
        ShowSharp(window);
        Save(window, "neues-raetsel.png");
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task Solved_classic_sudoku() => Run(async () =>
    {
        var (window, viewModel) = await OpenAsync("klassisch-leicht.json");
        await viewModel.Solve.SolveCommand.ExecuteAsync(null);
        viewModel.Solve.Outcome.Should().Be(SolveOutcome.Solved, viewModel.Solve.ResultMessage);
        Save(window, "geloest.png");
        window.Close();
    });

    [Fact]
    public Task Explanation_mode_early_step() => Run(async () =>
    {
        var (window, viewModel) = await OpenAsync("klassisch-leicht.json", ExplainWidth, ExplainHeight);
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.IsActive.Should().BeTrue(explain.StatusMessage);
        var step = explain.Result!.Steps.First(s => s.Index >= 3 && s.Kind == StepKind.EliminateCandidates);
        explain.Position = step.Index + 1;
        Save(window, "erklaermodus.png");
        window.Close();
    });

    [Fact]
    public Task Explanation_mode_branch_step() => Run(async () =>
    {
        var (window, viewModel) = await OpenAsync("klassisch-mittel.json", ExplainWidth, ExplainHeight);
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        var branch = explain.Result!.Steps.First(s => s.Kind == StepKind.Branch);
        explain.Position = branch.Index + 1;
        explain.IsBranchStep.Should().BeTrue();
        Save(window, "erklaermodus-verzweigung.png");
        window.Close();
    });

    [Fact]
    public Task Triangle_cells() => Run(async () =>
    {
        var (window, viewModel) = await OpenAsync("dreiecks-sudoku.json");
        viewModel.Editor.NodeShape = NodeShape.Cell;
        Save(window, "dreieck-zellen.png");
        window.Close();
    });

    [Fact]
    public Task Hexagon_cells() => Run(() =>
    {
        var (window, viewModel) = Open(HexagonPuzzle());
        viewModel.Editor.NodeShape = NodeShape.Cell;
        Save(window, "hexagon-zellen.png");
        window.Close();
        return Task.CompletedTask;
    });

    /// <summary>
    /// Kleines Wabenrätsel: 19 Zellen als Sechseck (Radius 2), Werte 1–7, die mittlere Rosette (Mitte und ihre sechs
    /// Nachbarn) verschieden, dazu ein Käfig und ein paar Givens.
    /// </summary>
    private static Puzzle HexagonPuzzle()
    {
        var puzzle = new Puzzle("Waben", GridType.Hex, new ValueDomain(1, 7));
        for (var r = -2; r <= 2; r++)
        {
            for (var q = -2; q <= 2; q++)
            {
                if (Math.Abs(q + r) <= 2)
                {
                    puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), q, r));
                }
            }
        }

        var byPosition = puzzle.Nodes.ToDictionary(n => (n.X, n.Y));
        int Id(int q, int r) => byPosition[(q, r)].Id;
        byPosition[(0, 0)].Given = 4;
        byPosition[(1, -1)].Given = 2;
        byPosition[(-1, 1)].Given = 6;
        byPosition[(-2, 0)].Given = 3;
        byPosition[(2, -2)].Given = 5;
        puzzle.Groups.Add(new Group(1, [Id(0, 0), Id(1, 0), Id(0, 1), Id(-1, 1), Id(-1, 0), Id(0, -1), Id(1, -1)], "AllDifferent", label: "Rosette"));
        puzzle.Groups.Add(new Group(2, [Id(1, 1), Id(2, 0), Id(2, -1)], "Sum", new RuleParams().Set("target", 12), color: "#dc2626", label: "Käfig"));
        return puzzle;
    }

    private Task Run(Func<Task> body) => OutputDirectory is null
        ? Task.CompletedTask
        : fixture.Session.Dispatch(async () =>
        {
            await body();
            return true;
        }, CancellationToken.None);

    /// <summary>Dialoge sind klein; doppelte Auflösung hält ihren Text im Handbuch scharf.</summary>
    private static void ShowSharp(Window dialog)
    {
        dialog.Show();
        dialog.SetRenderScaling(2);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> OpenAsync(string fileName, int width = 1280, int height = 800)
    {
        var (window, viewModel) = Open(null, width, height);
        var path = Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName);
        (await viewModel.OpenFileAsync(path)).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    private static (MainWindow Window, MainWindowViewModel ViewModel) Open(Puzzle? puzzle, int width = 1280, int height = 800)
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        if (puzzle is not null)
        {
            viewModel.Editor.LoadPuzzle(puzzle);
        }

        var window = new MainWindow { DataContext = viewModel, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    private void Save(Window window, string fileName)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Kein Bild gerendert.");
        frame.PixelSize.Width.Should().BeLessThanOrEqualTo(1400);
        Directory.CreateDirectory(OutputDirectory!);
        var path = Path.Combine(OutputDirectory!, fileName);
        frame.Save(path, PngBitmapEncoderOptions.Default);
        output.WriteLine($"Gespeichert: {path}");
    }
}
