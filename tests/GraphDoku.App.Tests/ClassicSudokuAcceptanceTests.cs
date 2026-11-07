using GraphDoku.App.Editing;
using GraphDoku.App.Services;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>
/// Akzeptanz Paket 3: Ein klassisches Sudoku wird vollständig über die Editor-Befehle angelegt (Knoten setzen,
/// 27 Gruppen aus Rahmenauswahlen, Beschriftungen, Givens per Tastatur), gespeichert und wieder geöffnet.
/// </summary>
public sealed class ClassicSudokuAcceptanceTests : IDisposable
{
    // Wikipedia-Beispiel (wie puzzles/klassisch-leicht.json).
    private const string Givens =
        "53..7...." + "6..195..." + ".98....6." + "8...6...3" + "4..8.3..1" + "7...2...6" + ".6....28." + "...419..5" + "....8..79";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Classic_sudoku_can_be_built_in_the_editor_saved_and_reopened()
    {
        var files = new FakeFileDialogService();
        var dialogs = new FakeDialogService { NewPuzzleAnswer = new NewPuzzleOptions(ClassicSudoku.DefaultName, GridType.Square, new ValueDomain(1, 9)) };
        var window = new MainWindowViewModel(files, dialogs, new OrToolsSolver());
        var editor = window.Editor;
        editor.SetViewSize(1200, 900);

        // Neu: Quadratgitter, Werte 1–9.
        await window.NewCommand.ExecuteAsync(null);

        // 81 Knoten: jede Zeile in einem Zug malen (Werkzeug „Knoten setzen“).
        editor.Tool = EditorTool.PlaceNode;
        for (var row = 0; row < 9; row++)
        {
            editor.DragGrid(new GridPoint(0, row), new GridPoint(8, row));
        }

        editor.Puzzle.Nodes.Should().HaveCount(81);
        editor.Puzzle.Nodes.Select(n => n.Id).Should().Equal(Enumerable.Range(1, 81));
        editor.FitView();

        // 27 Gruppen aus Rahmenauswahlen (Werkzeug „Auswahl“), Regel „Alle verschieden“, mit Beschriftung.
        editor.Tool = EditorTool.Select;
        editor.NewGroupRule = editor.GroupRules.Single(r => r.TypeName == "AllDifferent");
        for (var row = 0; row < 9; row++)
        {
            CreateGroup(editor, -0.4, row - 0.4, 8.4, row + 0.4, $"Zeile {row + 1}");
        }

        for (var column = 0; column < 9; column++)
        {
            CreateGroup(editor, column - 0.4, -0.4, column + 0.4, 8.4, $"Spalte {column + 1}");
        }

        for (var block = 0; block < 9; block++)
        {
            var left = block % 3 * 3;
            var top = block / 3 * 3;
            CreateGroup(editor, left - 0.4, top - 0.4, left + 2.4, top + 2.4, $"Block {block + 1}");
        }

        // Givens per Tastatur: Knoten anklicken, Ziffer tippen.
        for (var i = 0; i < 81; i++)
        {
            if (Givens[i] != '.')
            {
                editor.Click(new GridPoint(i % 9, i / 9));
                editor.Type(Givens[i].ToString());
            }
        }

        var expected = ClassicSudoku.Create(Givens);
        editor.Puzzle.ShouldEqual(expected);
        editor.HasValidationProblems.Should().BeFalse();
        window.Title.Should().Be($"{ClassicSudoku.DefaultName} * – GraphDoku");

        // Speichern (fragt nach dem Pfad, weil noch keiner bekannt ist).
        var path = _temp.File("klassisch.json");
        files.SavePaths.Enqueue(path);
        await window.SaveCommand.ExecuteAsync(null);
        window.Title.Should().Be("klassisch.json – GraphDoku");
        dialogs.Errors.Should().BeEmpty();

        // In einem frischen Fenster öffnen.
        var reopenedWindow = new MainWindowViewModel(files, dialogs, new OrToolsSolver());
        files.OpenPaths.Enqueue(path);
        await reopenedWindow.OpenCommand.ExecuteAsync(null);

        reopenedWindow.Editor.Puzzle.ShouldEqual(expected);
        reopenedWindow.Editor.Puzzle.ShouldEqual(editor.Puzzle);
        reopenedWindow.Editor.HasValidationProblems.Should().BeFalse();
        reopenedWindow.Title.Should().Be("klassisch.json – GraphDoku");
        reopenedWindow.Editor.Groups.Should().HaveCount(27);

        // Das im Editor gebaute Rätsel ist ein echtes, eindeutig lösbares Sudoku.
        var count = new OrToolsSolver().CountSolutions(reopenedWindow.Editor.Puzzle, 2);
        count.IsUnique.Should().BeTrue(count.Message);
    }

    private static void CreateGroup(EditorViewModel editor, double x1, double y1, double x2, double y2, string label)
    {
        editor.RubberBand(x1, y1, x2, y2);
        editor.SelectedNodeIds.Should().HaveCount(9, label);
        editor.CreateGroupFromSelection();
        var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
        panel.Label = label;
    }
}
