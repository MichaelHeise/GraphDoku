using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;

namespace GraphDoku.Core.Tests;

public class PuzzleSerializerTests
{
    private static string PuzzlesDirectory => Path.Combine(AppContext.BaseDirectory, "puzzles");

    /// <summary>Ein Rätsel, das jede Eigenschaft des Formats nutzt.</summary>
    private static Puzzle CreateFullPuzzle()
    {
        var puzzle = new Puzzle("Alles drin – äöü ß", GridType.Triangle, new ValueDomain(1, 4));
        puzzle.Nodes.Add(new Node(1, 0, 0, given: 1));
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Nodes.Add(new Node(3, 2, 0, domain: new ValueDomain(2, 4)));
        puzzle.Nodes.Add(new Node(4, -1, 3, given: 4, domain: new ValueDomain(4, 6)));
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        puzzle.Edges.Add(new Edge(2, 2, 3, "Difference", TestPuzzles.Params(("k", 1), ("mode", "atLeast"))));
        puzzle.Edges.Add(new Edge(3, 1, 3, "Ratio", TestPuzzles.Params(("factor", 2))));
        puzzle.Edges.Add(new Edge(4, 3, 4, "LessThan"));
        puzzle.Groups.Add(new Group(1, [1, 2, 3, 4], "AllDifferent", color: "#3366ff", label: "Reihe"));
        puzzle.Groups.Add(new Group(2, [1, 2, 3, 4], "CoverRange", TestPuzzles.Params(("min", 1), ("max", 4))));
        puzzle.Groups.Add(new Group(3, [2, 3], "Sum", TestPuzzles.Params(("target", 5), ("allowRepeats", false))));
        puzzle.Groups.Add(new Group(4, [1, 3, 4], "Thermometer", label: "Thermo"));
        return puzzle;
    }

    private static string Wrap(string body) => "{ \"formatVersion\": 1, \"name\": \"T\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 9 }, " + body + " }";

    private static void ShouldFail(string json, params string[] expectedMessages)
    {
        var act = () => PuzzleSerializer.Deserialize(json);
        var exception = act.Should().Throw<PuzzleFormatException>().Which;
        foreach (var expected in expectedMessages)
        {
            exception.Errors.Should().Contain(e => e.Contains(expected, StringComparison.Ordinal), $"erwartet wird eine Meldung mit „{expected}“, erhalten: {exception.Message}");
        }
    }

    [Fact]
    public void Round_trip_of_classic_sudoku_gives_equal_model()
    {
        var original = ClassicSudoku.Create("53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79");

        var json = PuzzleSerializer.Serialize(original);
        var loaded = PuzzleSerializer.Deserialize(json);

        PuzzleComparer.Differences(original, loaded).Should().BeEmpty();
        PuzzleSerializer.Serialize(loaded).Should().Be(json);
    }

    [Fact]
    public void Round_trip_with_all_features_gives_equal_model()
    {
        var original = CreateFullPuzzle();

        var loaded = PuzzleSerializer.Deserialize(PuzzleSerializer.Serialize(original));

        PuzzleComparer.Differences(original, loaded).Should().BeEmpty();
    }

    [Fact]
    public void Load_save_load_of_files_gives_equal_model()
    {
        var files = Directory.GetFiles(PuzzlesDirectory, "*.json");
        files.Should().NotBeEmpty();
        var tempFile = Path.Combine(Path.GetTempPath(), $"graphdoku-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var file in files)
            {
                var first = PuzzleSerializer.Load(file);
                PuzzleSerializer.Save(first, tempFile);
                var second = PuzzleSerializer.Load(tempFile);

                PuzzleComparer.Differences(first, second).Should().BeEmpty(Path.GetFileName(file));
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sample_file_klassisch_leer_matches_template()
    {
        var loaded = PuzzleSerializer.Load(Path.Combine(PuzzlesDirectory, "klassisch-leer.json"));
        PuzzleComparer.Differences(loaded, ClassicSudoku.Create(name: "Klassisch 9x9 (leer)")).Should().BeEmpty();
    }

    [Fact]
    public void Output_uses_camel_case_format_of_the_spec()
    {
        var json = PuzzleSerializer.Serialize(CreateFullPuzzle());

        json.Should().StartWith("{\n  \"formatVersion\": 1,\n  \"name\": \"Alles drin – äöü ß\",\n  \"grid\": \"triangle\",");
        json.Should().Contain("\"params\": {\n        \"k\": 1,\n        \"mode\": \"atLeast\"\n      }");
        json.Should().Contain("\"params\": {}");
        json.Should().Contain("\"color\": \"#3366ff\"").And.Contain("\"label\": \"Reihe\"");
        json.Should().NotContain("solution").And.NotContain("\r");
    }

    [Fact]
    public void Spec_example_can_be_read()
    {
        const string json = """
            {
              "formatVersion": 1,
              "name": "Klassisch 9x9",
              "grid": "square",
              "domain": { "min": 1, "max": 9 },
              "nodes": [ { "id": 1, "x": 0, "y": 0, "given": 5 } ],
              "edges": [ { "id": 1, "from": 1, "to": 2, "rule": "Difference", "params": { "k": 1, "mode": "exact" } } ],
              "groups": [ { "id": 1, "nodes": [1, 2, 3], "rule": "AllDifferent", "params": {} } ]
            }
            """;

        // Das Beispiel verweist auf nicht vorhandene Knoten 2 und 3: Lesen ohne Prüfung klappt, mit Prüfung nicht.
        var puzzle = PuzzleSerializer.Deserialize(json, validate: false);
        puzzle.Nodes.Should().ContainSingle().Which.Given.Should().Be(5);
        puzzle.Edges[0].Params.GetString("mode", null).Should().Be("exact");
        puzzle.Groups[0].Nodes.Should().Equal(1, 2, 3);

        ShouldFail(json, "Kante 1: Der Zielknoten 2 existiert nicht.", "Gruppe 1: Der Knoten 3 existiert nicht.");
    }

    [Fact]
    public void Missing_format_version_is_rejected()
    {
        ShouldFail("{ \"name\": \"x\" }", "Das Feld „formatVersion“ fehlt.");
    }

    [Theory]
    [InlineData("2", "Nicht unterstützte Formatversion 2: Diese Programmversion kann nur Dateien mit formatVersion 1 lesen.")]
    [InlineData("0", "Nicht unterstützte Formatversion 0")]
    [InlineData("\"1\"", "Das Feld „formatVersion“ muss eine ganze Zahl sein")]
    public void Other_format_versions_are_rejected(string version, string message)
    {
        ShouldFail("{ \"formatVersion\": " + version + ", \"grid\": \"square\", \"something\": [] }", message);
    }

    [Fact]
    public void Invalid_json_reports_line()
    {
        ShouldFail("{\n  \"formatVersion\": 1,\n  \"name\": \n}", "kein gültiges JSON (Zeile 4");
    }

    [Fact]
    public void Root_must_be_an_object()
    {
        ShouldFail("[1, 2]", "Die Datei muss ein JSON-Objekt enthalten.");
    }

    [Fact]
    public void Unknown_grid_type_is_rejected()
    {
        ShouldFail("{ \"formatVersion\": 1, \"grid\": \"octagon\", \"domain\": { \"min\": 1, \"max\": 9 } }",
            "Unbekannter Gittertyp „octagon“. Erlaubt sind: square, triangle, hex.");
    }

    [Fact]
    public void Missing_required_fields_are_reported_with_path()
    {
        ShouldFail("{ \"formatVersion\": 1, \"nodes\": [ { \"id\": 1, \"x\": 0 } ] }",
            "Rätsel: Das Feld „grid“ fehlt.",
            "Rätsel: Das Feld „domain“ (Wertebereich mit min und max) fehlt.",
            "nodes[0]: Das Feld „y“ fehlt.");
    }

    [Fact]
    public void Wrong_field_types_are_reported()
    {
        ShouldFail(Wrap("\"nodes\": [ { \"id\": \"eins\", \"x\": 0, \"y\": 1.5 } ], \"groups\": [ { \"id\": 1, \"nodes\": [1, \"a\"], \"rule\": \"AllDifferent\" } ]"),
            "nodes[0]: Das Feld „id“ muss eine ganze Zahl sein, ist aber die Zeichenkette \"eins\".",
            "nodes[0]: Das Feld „y“ muss eine ganze Zahl sein, ist aber die Zahl 1.5.",
            "groups[0].nodes[1]: Knoten-Ids müssen ganze Zahlen sein");
    }

    [Fact]
    public void Unknown_fields_are_rejected_including_solutions()
    {
        ShouldFail(Wrap("\"solution\": [1, 2], \"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0, \"value\": 3 } ]"),
            "Rätsel: Unbekanntes Feld „solution“.",
            "nodes[0]: Unbekanntes Feld „value“.");
    }

    [Fact]
    public void Unknown_rule_type_is_rejected_naming_rule_and_element()
    {
        ShouldFail(Wrap("\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 7, \"from\": 1, \"to\": 2, \"rule\": \"Kropki\", \"params\": {} } ]"),
            "Kante 7: Unbekannter Regeltyp „Kropki“. Bekannt sind:");
    }

    [Fact]
    public void Invalid_rule_params_are_rejected_with_understandable_message()
    {
        ShouldFail(Wrap("\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 3, \"from\": 1, \"to\": 2, \"rule\": \"Difference\", \"params\": { \"k\": 1, \"mode\": \"genau\", \"x\": 1 } } ]"),
            "Kante 3 (Difference): Parameter „mode“ muss einer der Werte \"exact\", \"atLeast\" sein, ist aber die Zeichenkette \"genau\".",
            "Kante 3 (Difference): Unbekannter Parameter „x“. Erlaubt sind: k, mode.");
    }

    [Fact]
    public void Params_must_be_an_object_and_may_be_omitted()
    {
        ShouldFail(Wrap("\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 1, \"from\": 1, \"to\": 2, \"rule\": \"NotEqual\", \"params\": [] } ]"),
            "edges[0]: Das Feld „params“ muss ein Objekt sein, ist aber eine Liste.");

        var puzzle = PuzzleSerializer.Deserialize(Wrap("\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 1, \"from\": 1, \"to\": 2, \"rule\": \"NotEqual\" } ]"));
        puzzle.Edges[0].Params.Count.Should().Be(0);
    }

    [Fact]
    public void Validation_errors_are_reported_on_load()
    {
        ShouldFail(Wrap("\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0, \"given\": 12 }, { \"id\": 1, \"x\": 1, \"y\": 0 } ]"),
            "Knoten 1: Der vorgegebene Wert 12 liegt außerhalb des Wertebereichs 1..9.",
            "Die Knoten-Id 1 ist mehrfach vergeben.");
    }

    [Fact]
    public void Missing_lists_mean_empty_lists()
    {
        var puzzle = PuzzleSerializer.Deserialize(Wrap("\"nodes\": []"));
        puzzle.Nodes.Should().BeEmpty();
        puzzle.Edges.Should().BeEmpty();
        puzzle.Groups.Should().BeEmpty();
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        var puzzle = PuzzleSerializer.Deserialize("{ // Kommentar\n \"formatVersion\": 1, \"grid\": \"hex\", \"domain\": { \"min\": 0, \"max\": 15 }, }");
        puzzle.Grid.Should().Be(GridType.Hex);
        puzzle.Domain.Should().Be(new ValueDomain(0, 15));
        puzzle.Name.Should().BeEmpty();
    }

    [Fact]
    public void Null_on_optional_fields_means_not_set()
    {
        var puzzle = PuzzleSerializer.Deserialize(
            "{ \"formatVersion\": 1, \"name\": null, \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 9 }, " +
            "\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0, \"domain\": null, \"given\": null }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], " +
            "\"edges\": [ { \"id\": 1, \"from\": 1, \"to\": 2, \"rule\": \"NotEqual\", \"params\": null } ], " +
            "\"groups\": [ { \"id\": 1, \"nodes\": [1, 2], \"rule\": \"AllDifferent\", \"params\": null, \"color\": null, \"label\": null } ] }");

        puzzle.Name.Should().BeEmpty();
        puzzle.Nodes[0].Domain.Should().BeNull();
        puzzle.Nodes[0].Given.Should().BeNull();
        puzzle.Edges[0].Params.Count.Should().Be(0);
        puzzle.Groups[0].Params.Count.Should().Be(0);
        puzzle.Groups[0].Color.Should().BeNull();
        puzzle.Groups[0].Label.Should().BeNull();
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("edges")]
    [InlineData("groups")]
    public void Null_lists_mean_empty_lists(string field)
    {
        var puzzle = PuzzleSerializer.Deserialize(Wrap($"\"{field}\": null"));
        puzzle.Nodes.Should().BeEmpty();
        puzzle.Edges.Should().BeEmpty();
        puzzle.Groups.Should().BeEmpty();
    }

    [Fact]
    public void Null_on_required_fields_is_rejected()
    {
        ShouldFail("{ \"formatVersion\": 1, \"grid\": \"square\", \"domain\": null }", "Rätsel.domain: Erwartet wird ein Objekt, gefunden wurde null.");
        ShouldFail(Wrap("\"nodes\": [ { \"id\": 1, \"x\": null, \"y\": 0 } ]"), "nodes[0]: Das Feld „x“ muss eine ganze Zahl sein");
        ShouldFail(Wrap("\"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 } ], \"groups\": [ { \"id\": 1, \"nodes\": null, \"rule\": \"Sum\" } ]"), "groups[0]: Das Feld „nodes“ muss eine Liste von Knoten-Ids sein");
    }

    [Theory]
    [InlineData("{ \"formatVersion\": 1, \"name\": \"\\uD800\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 } }", "Das Feld „name“ enthält eine ungültige Zeichenfolge")]
    [InlineData("{ \"formatVersion\": 1, \"name\": \"T\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 }, \"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 1, \"from\": 1, \"to\": 2, \"rule\": \"\\uDC00\" } ] }", "Das Feld „edges[0].rule“ enthält eine ungültige Zeichenfolge")]
    [InlineData("{ \"formatVersion\": 1, \"name\": \"T\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 }, \"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 1, \"from\": 1, \"to\": 2, \"rule\": \"Difference\", \"params\": { \"k\": 1, \"mode\": \"\\uD800x\" } } ] }", "Das Feld „edges[0].params.mode“ enthält eine ungültige Zeichenfolge")]
    [InlineData("{ \"formatVersion\": 1, \"name\": \"T\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 }, \"nodes\": [ { \"id\": 1, \"x\": 0, \"y\": 0 }, { \"id\": 2, \"x\": 1, \"y\": 0 } ], \"edges\": [ { \"id\": 1, \"from\": 1, \"to\": 2, \"rule\": \"Difference\", \"params\": { \"\\uDC00\": 1 } } ] }", "Das Feld „edges[0].params“ enthält einen Feldnamen mit einer ungültigen Zeichenfolge")]
    [InlineData("{ \"\\uD800\": 1, \"formatVersion\": 1, \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 } }", "Das Rätselobjekt enthält einen Feldnamen mit einer ungültigen Zeichenfolge")]
    public void Lone_utf16_surrogate_escape_is_a_format_error_not_a_crash(string json, string expected)
    {
        // Regression (Abschluss-Review): JsonDocument akzeptiert "\uD800", GetString() warf dann InvalidOperationException.
        ShouldFail(json, expected);
        var unvalidated = () => PuzzleSerializer.Deserialize(json, validate: false);
        unvalidated.Should().Throw<PuzzleFormatException>();
    }

    [Fact]
    public void Raw_lone_utf16_surrogate_in_the_text_is_a_format_error_not_a_crash()
    {
        // Regression (Review PR #1): Ein echtes einzelnes Surrogat im String (nicht die JSON-Maskierung "\uD800")
        // ließ JsonDocument.Parse eine ArgumentException werfen, obwohl nur PuzzleFormatException dokumentiert ist.
        var json = "{ \"formatVersion\": 1, \"name\": \"a\uD800b\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 } }";

        ShouldFail(json, "ungültige Zeichenfolge");
        var unvalidated = () => PuzzleSerializer.Deserialize(json, validate: false);
        unvalidated.Should().Throw<PuzzleFormatException>();
    }

    [Fact]
    public void Save_over_an_existing_file_replaces_it_and_leaves_no_temporary_files()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "raetsel.json");
            File.WriteAllText(path, "alter Inhalt, deutlich länger als nötig, damit Reste auffallen würden " + new string('x', 20_000));
            var puzzle = CreateFullPuzzle();

            PuzzleSerializer.Save(puzzle, path);
            PuzzleSerializer.Save(puzzle, path);

            File.ReadAllText(path).Should().Be(PuzzleSerializer.Serialize(puzzle));
            PuzzleComparer.Differences(puzzle, PuzzleSerializer.Load(path)).Should().BeEmpty();
            Directory.GetFileSystemEntries(directory).Should().Equal(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_that_fails_leaves_the_target_untouched_and_no_temporary_files()
    {
        // Das Ziel ist ein Verzeichnis: Die temporäre Datei lässt sich schreiben, das Ersetzen schlägt fehl.
        var directory = CreateTempDirectory();
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(directory, "raetsel.json")).FullName;
            var inner = Path.Combine(target, "inhalt.txt");
            File.WriteAllText(inner, "bleibt");

            var act = () => PuzzleSerializer.Save(CreateFullPuzzle(), target);

            act.Should().Throw<Exception>().Which.Should().Match<Exception>(e => e is IOException || e is UnauthorizedAccessException);
            File.ReadAllText(inner).Should().Be("bleibt");
            Directory.GetFileSystemEntries(directory).Should().Equal(target);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_into_a_read_only_directory_fails_without_touching_the_existing_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Schreibschutz für Verzeichnisse gibt es so nur unter Unix.
        }

        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "raetsel.json");
        File.WriteAllText(path, "bisher gespeichert");
        var original = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (CanCreateFileIn(directory))
            {
                return; // z. B. als root: Der Schreibschutz greift nicht, der Fehlerfall lässt sich nicht erzeugen.
            }

            var act = () => PuzzleSerializer.Save(CreateFullPuzzle(), path);

            act.Should().Throw<UnauthorizedAccessException>();
            File.ReadAllText(path).Should().Be("bisher gespeichert");
            Directory.GetFileSystemEntries(directory).Should().Equal(path);
        }
        finally
        {
            File.SetUnixFileMode(directory, original);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_over_a_read_only_file_fails_without_touching_it()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "raetsel.json");
        File.WriteAllText(path, "schreibgeschützt");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var act = () => PuzzleSerializer.Save(CreateFullPuzzle(), path);

            act.Should().Throw<UnauthorizedAccessException>();
            File.ReadAllText(path).Should().Be("schreibgeschützt");
            Directory.GetFileSystemEntries(directory).Should().Equal(path);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"graphdoku-{Guid.NewGuid():N}")).FullName;

    private static bool CanCreateFileIn(string directory)
    {
        var probe = Path.Combine(directory, "probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void Paired_surrogate_escape_is_read_normally()
    {
        var puzzle = PuzzleSerializer.Deserialize("{ \"formatVersion\": 1, \"name\": \"\\uD83D\\uDE00\", \"grid\": \"square\", \"domain\": { \"min\": 1, \"max\": 3 } }");
        puzzle.Name.Should().Be("\U0001F600");
    }
}
