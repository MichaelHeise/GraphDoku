using GraphDoku.App.Editing;
using GraphDoku.App.Services;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;

namespace GraphDoku.App.Tests;

/// <summary>Datei-Befehle, Titel und Rückfragen des Hauptfensters.</summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeFileDialogService _files = new();
    private readonly FakeDialogService _dialogs = new();

    public void Dispose() => _temp.Dispose();

    private MainWindowViewModel Create()
    {
        var viewModel = new MainWindowViewModel(_files, _dialogs);
        viewModel.Editor.SetViewSize(1000, 800);
        return viewModel;
    }

    private static void MakeEdit(MainWindowViewModel viewModel)
    {
        viewModel.Editor.Tool = EditorTool.PlaceNode;
        viewModel.Editor.Click(new GridPoint(12, 12)); // außerhalb aller Vorlagen-Knoten
    }

    [Fact]
    public void Title_shows_document_name_and_dirty_marker()
    {
        var viewModel = Create();
        viewModel.Title.Should().Be("Unbenannt – GraphDoku");

        MakeEdit(viewModel);
        viewModel.Title.Should().Be("Unbenannt * – GraphDoku");

        viewModel.Editor.UndoCommand.Execute(null);
        viewModel.Title.Should().Be("Unbenannt – GraphDoku");
    }

    [Fact]
    public async Task File_picker_failures_are_reported_instead_of_crashing_the_command()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        var before = viewModel.Editor.Snapshot();

        _files.NextFailure = new IOException("Die gewählte Datei hat keinen lokalen Pfad und kann nicht verwendet werden.");
        await viewModel.SaveAsCommand.ExecuteAsync(null);
        _dialogs.Errors.Should().ContainSingle().Which.Title.Should().Be("Speichern fehlgeschlagen");
        _dialogs.Errors[0].Message.Should().Contain("keinen lokalen Pfad");
        viewModel.FilePath.Should().BeNull();
        viewModel.Editor.IsDirty.Should().BeTrue();

        _dialogs.UnsavedChangesAnswer = UnsavedChangesChoice.Discard;
        _files.NextFailure = new IOException("Die gewählte Datei hat keinen lokalen Pfad und kann nicht verwendet werden.");
        await viewModel.OpenCommand.ExecuteAsync(null);
        _dialogs.Errors.Should().HaveCount(2);
        _dialogs.Errors[1].Title.Should().Be("Öffnen fehlgeschlagen");
        viewModel.Editor.Puzzle.ShouldEqual(before);

        // Rückfrage beim Schließen → „Speichern“ → Auswahl scheitert: nicht fortfahren, Dokument bleibt.
        _dialogs.UnsavedChangesAnswer = UnsavedChangesChoice.Save;
        _files.NextFailure = new IOException("kein lokaler Pfad");
        (await viewModel.ConfirmDiscardChangesAsync()).Should().BeFalse();
        _dialogs.Errors.Should().HaveCount(3);
    }

    [Fact]
    public async Task Classic_template_is_loaded_as_a_clean_document()
    {
        var viewModel = Create();

        await viewModel.NewClassicCommand.ExecuteAsync(null);

        viewModel.Editor.Puzzle.ShouldEqual(ClassicSudoku.Create());
        viewModel.Title.Should().Be("Klassisch 9x9 – GraphDoku");
        viewModel.FilePath.Should().BeNull();
        viewModel.Editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task New_creates_an_empty_puzzle_with_the_chosen_grid_and_domain()
    {
        var viewModel = Create();
        _dialogs.NewPuzzleAnswer = new NewPuzzleOptions("Hexadoku", GridType.Hex, new ValueDomain(0, 15));

        await viewModel.NewCommand.ExecuteAsync(null);

        viewModel.Editor.Puzzle.Should().BeEquivalentTo(new { Name = "Hexadoku", Grid = GridType.Hex, Domain = new ValueDomain(0, 15) });
        viewModel.Editor.Puzzle.Nodes.Should().BeEmpty();
        viewModel.Title.Should().Be("Hexadoku – GraphDoku");
    }

    [Fact]
    public async Task Cancelled_new_dialog_keeps_the_document()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        _dialogs.NewPuzzleAnswer = null;

        await viewModel.NewCommand.ExecuteAsync(null);

        viewModel.Editor.Puzzle.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Unsaved_changes_are_asked_for_and_cancel_aborts()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        _dialogs.UnsavedChangesAnswer = UnsavedChangesChoice.Cancel;

        await viewModel.NewClassicCommand.ExecuteAsync(null);

        _dialogs.UnsavedChangesQuestions.Should().Be(1);
        viewModel.Editor.Puzzle.Nodes.Should().HaveCount(1);

        _dialogs.UnsavedChangesAnswer = UnsavedChangesChoice.Discard;
        await viewModel.NewClassicCommand.ExecuteAsync(null);
        viewModel.Editor.Puzzle.Nodes.Should().HaveCount(81);

        await viewModel.NewClassicCommand.ExecuteAsync(null);
        _dialogs.UnsavedChangesQuestions.Should().Be(2, "ein unverändertes Dokument braucht keine Rückfrage");
    }

    [Fact]
    public async Task Save_choice_in_the_unsaved_changes_question_saves_first()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        var path = _temp.File("zwischenstand.json");
        _files.SavePaths.Enqueue(path);
        _dialogs.UnsavedChangesAnswer = UnsavedChangesChoice.Save;

        await viewModel.NewClassicCommand.ExecuteAsync(null);

        File.Exists(path).Should().BeTrue();
        PuzzleSerializer.Load(path).Nodes.Should().HaveCount(1);
        viewModel.Editor.Puzzle.Nodes.Should().HaveCount(81);
        viewModel.FilePath.Should().BeNull();

        MakeEdit(viewModel);
        _files.SavePaths.Enqueue(null);
        await viewModel.NewClassicCommand.ExecuteAsync(null);
        viewModel.Editor.Puzzle.Nodes.Should().HaveCount(82, "Speichern abgebrochen heißt: nichts verwerfen");
        viewModel.Editor.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Save_without_path_asks_for_one_and_marks_the_document_clean()
    {
        var viewModel = Create();
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create(name: "Mein: Rätsel"));
        MakeEdit(viewModel);
        var path = _temp.File("raetsel.json");
        _files.SavePaths.Enqueue(path);

        await viewModel.SaveCommand.ExecuteAsync(null);

        _files.SuggestedNames.Should().Equal("Mein_ Rätsel.json");
        viewModel.FilePath.Should().Be(path);
        viewModel.Title.Should().Be("raetsel.json – GraphDoku");
        viewModel.Editor.IsDirty.Should().BeFalse();
        viewModel.Editor.StatusMessage.Should().Be("Gespeichert: raetsel.json");

        // Erneutes Speichern nutzt den Pfad ohne Rückfrage.
        MakeEdit(viewModel);
        await viewModel.SaveCommand.ExecuteAsync(null);
        _files.SuggestedNames.Should().HaveCount(1);
        PuzzleSerializer.Load(path, validate: false).ShouldEqual(viewModel.Editor.Puzzle);
    }

    [Fact]
    public async Task Save_as_always_asks_and_cancel_changes_nothing()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        _files.SavePaths.Enqueue(null);

        await viewModel.SaveAsCommand.ExecuteAsync(null);

        viewModel.FilePath.Should().BeNull();
        viewModel.Editor.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Unfinished_puzzles_can_be_saved_and_reopened_with_problems_listed()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        viewModel.Editor.CreateGroupFromSelection(); // AllDifferent mit nur einem Knoten
        viewModel.Editor.HasValidationProblems.Should().BeTrue();
        var path = _temp.File("unfertig.json");

        (await viewModel.SaveToFileAsync(path)).Should().BeTrue();
        viewModel.Editor.StatusMessage.Should().Contain("noch 1 Problem");

        var reopened = Create();
        (await reopened.OpenFileAsync(path)).Should().BeTrue();
        reopened.Editor.Puzzle.Groups.Should().ContainSingle();
        reopened.Editor.ValidationProblems.Should().ContainSingle().Which.Should().Contain("mindestens 2 Knoten");
        _dialogs.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_reports_invalid_files_in_german_and_keeps_the_document()
    {
        var viewModel = Create();
        MakeEdit(viewModel);
        var path = _temp.File("kaputt.json");
        await File.WriteAllTextAsync(path, """{ "formatVersion": 2, "grid": "square", "domain": { "min": 1, "max": 9 } }""");
        _files.OpenPaths.Enqueue(path);
        _dialogs.UnsavedChangesAnswer = UnsavedChangesChoice.Discard;

        await viewModel.OpenCommand.ExecuteAsync(null);

        _dialogs.Errors.Should().ContainSingle();
        _dialogs.Errors[0].Title.Should().Be("Öffnen fehlgeschlagen");
        _dialogs.Errors[0].Message.Should().Contain("kaputt.json").And.Contain("formatVersion");
        viewModel.Editor.StatusMessage.Should().Be("Öffnen fehlgeschlagen.");
        viewModel.Editor.Puzzle.Nodes.Should().HaveCount(1);
        viewModel.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task Open_reports_missing_files()
    {
        var viewModel = Create();
        (await viewModel.OpenFileAsync(_temp.File("gibtsnicht.json"))).Should().BeFalse();
        _dialogs.Errors.Should().ContainSingle().Which.Message.Should().Contain("konnte nicht gelesen werden");
    }

    [Fact]
    public async Task Save_reports_write_errors()
    {
        var viewModel = Create();
        (await viewModel.SaveToFileAsync(Path.Combine(_temp.Path, "fehlt", "x.json"))).Should().BeFalse();
        _dialogs.Errors.Should().ContainSingle().Which.Title.Should().Be("Speichern fehlgeschlagen");
        viewModel.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task Example_puzzles_open_without_problems()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "puzzles");
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            var viewModel = Create();
            (await viewModel.OpenFileAsync(file)).Should().BeTrue(file);
            viewModel.Editor.HasValidationProblems.Should().BeFalse(file);
            viewModel.Title.Should().Be($"{Path.GetFileName(file)} – GraphDoku");
        }

        _dialogs.Errors.Should().BeEmpty();
    }

    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GraphDoku.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Das Repository-Verzeichnis (mit GraphDoku.sln) wurde nicht gefunden.");
    }
}
