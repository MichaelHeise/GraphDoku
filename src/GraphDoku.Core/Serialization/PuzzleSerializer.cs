using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Validation;

namespace GraphDoku.Core.Serialization;

/// <summary>
/// Liest und schreibt Rätsel im JSON-Format (System.Text.Json, camelCase, <c>formatVersion</c> = 1).
/// Lösungen werden nie geschrieben.
/// </summary>
public static class PuzzleSerializer
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Liest ein Rätsel aus JSON-Text.</summary>
    /// <param name="json">Der JSON-Text.</param>
    /// <param name="registry">Regel-Registry für die Prüfung; Standard: <see cref="RuleRegistry.Default"/>.</param>
    /// <param name="validate">Bei true (Standard) wird das Rätsel vollständig geprüft (Regeltypen, Parameter, Ids …).</param>
    /// <exception cref="PuzzleFormatException">Format- oder Inhaltsfehler, mit deutschen Meldungen.</exception>
    public static Puzzle Deserialize(string json, RuleRegistry? registry = null, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new PuzzleFormatException(
                $"Die Datei enthält kein gültiges JSON (Zeile {(ex.LineNumber ?? 0) + 1}, Position {(ex.BytePositionInLine ?? 0) + 1}).",
                ex);
        }
        catch (ArgumentException ex)
        {
            // Ein echtes einzelnes UTF-16-Surrogat im Text lässt sich nicht nach UTF-8 umwandeln.
            throw new PuzzleFormatException(
                "Die Datei enthält eine ungültige Zeichenfolge (einzelnes UTF-16-Surrogat) und ist kein gültiges JSON.",
                ex);
        }

        using (document)
        {
            // JsonDocument nimmt maskierte einzelne UTF-16-Surrogate (z. B. "\uD800") an, GetString() und
            // JsonProperty.Name werfen dann aber InvalidOperationException. Deshalb vor dem Lesen alle Texte prüfen.
            var textErrors = new List<string>();
            FindInvalidText(document.RootElement, string.Empty, textErrors);
            if (textErrors.Count > 0)
            {
                throw new PuzzleFormatException(textErrors);
            }

            var reader = new Reader();
            var puzzle = reader.ReadPuzzle(document.RootElement);
            if (reader.Errors.Count > 0)
            {
                throw new PuzzleFormatException(reader.Errors);
            }

            if (validate)
            {
                var errors = PuzzleValidator.Validate(puzzle!, registry);
                if (errors.Count > 0)
                {
                    throw new PuzzleFormatException(errors);
                }
            }

            return puzzle!;
        }
    }

    /// <summary>Liest eine Rätseldatei (UTF-8).</summary>
    /// <exception cref="PuzzleFormatException">Format- oder Inhaltsfehler.</exception>
    public static Puzzle Load(string path, RuleRegistry? registry = null, bool validate = true) =>
        Deserialize(File.ReadAllText(path, Encoding.UTF8), registry, validate);

    /// <summary>Schreibt ein Rätsel als eingerückten JSON-Text. Das Rätsel wird dabei nicht geprüft.</summary>
    public static string Serialize(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            Write(writer, puzzle);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>
    /// Speichert ein Rätsel als UTF-8-Datei (ohne BOM). Geschrieben wird zuerst in eine temporäre Datei im selben
    /// Verzeichnis, die dann das Ziel ersetzt: Scheitert das Schreiben (z. B. volle Platte), bleibt die bisherige
    /// Datei unverändert. Ein symbolischer Link wird aufgelöst (ersetzt wird die Datei, auf die er zeigt), und unter
    /// Unix übernimmt die neue Datei die Zugriffsrechte der bisherigen. Lässt sich im Verzeichnis keine Datei anlegen,
    /// die bestehende Datei aber beschreiben, wird sie wie früher direkt (nicht atomar) überschrieben.
    /// </summary>
    public static void Save(Puzzle puzzle, string path) => Save(puzzle, path, OpenTemporaryFile);

    /// <summary>Wie <see cref="Save(Puzzle, string)"/>; <paramref name="openTemporary"/> öffnet die temporäre Datei (Testnaht).</summary>
    internal static void Save(Puzzle puzzle, string path, Func<string, Stream> openTemporary)
    {
        ArgumentNullException.ThrowIfNull(path);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(puzzle));
        var fullPath = ResolveLinks(Path.GetFullPath(path));
        var exists = File.Exists(fullPath);
        if (exists && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReadOnly))
        {
            // Das Ersetzen per Umbenennen würde den Schreibschutz unter Unix übergehen.
            throw new UnauthorizedAccessException($"Die Datei „{fullPath}“ ist schreibgeschützt.");
        }

        UnixFileMode? mode = exists && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(fullPath) : null;
        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        Stream stream;
        try
        {
            stream = openTemporary(tempPath);
        }
        catch (UnauthorizedAccessException) when (exists)
        {
            // Verzeichnis nicht beschreibbar, die Datei selbst aber schon: wie bisher direkt überschreiben.
            File.WriteAllBytes(fullPath, bytes);
            return;
        }

        try
        {
            using (stream)
            {
                if (mode is { } unixMode && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(tempPath, unixMode);
                }

                stream.Write(bytes);
                if (stream is FileStream fileStream)
                {
                    fileStream.Flush(flushToDisk: true);
                }
                else
                {
                    stream.Flush();
                }
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static FileStream OpenTemporaryFile(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    /// <summary>Löst symbolische Links auf, damit das Ersetzen die Zieldatei trifft und nicht den Link.</summary>
    private static string ResolveLinks(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.LinkTarget is null ? fullPath : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath;
    }

    /// <summary>Löscht eine temporäre Datei, ohne den eigentlichen Fehler zu überdecken.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die temporäre Datei bleibt dann liegen; der ursprüngliche Fehler ist wichtiger.
        }
    }

    /// <summary>Sammelt alle Feldnamen und Zeichenketten, die sich nicht als gültiger UTF-16-Text lesen lassen.</summary>
    private static void FindInvalidText(JsonElement element, string path, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    string name;
                    try
                    {
                        name = property.Name;
                    }
                    catch (InvalidOperationException)
                    {
                        errors.Add($"{Describe(path)} enthält einen Feldnamen mit einer ungültigen Zeichenfolge (einzelnes UTF-16-Surrogat, z. B. \\uD800). Die Datei ist keine gültige Rätseldatei.");
                        continue;
                    }

                    FindInvalidText(property.Value, path.Length == 0 ? name : $"{path}.{name}", errors);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FindInvalidText(item, $"{path}[{index}]", errors);
                    index++;
                }

                break;
            case JsonValueKind.String:
                try
                {
                    _ = element.GetString();
                }
                catch (InvalidOperationException)
                {
                    errors.Add($"{Describe(path)} enthält eine ungültige Zeichenfolge (einzelnes UTF-16-Surrogat, z. B. \\uD800). Die Datei ist keine gültige Rätseldatei.");
                }

                break;
        }

        static string Describe(string path) => path.Length == 0 ? "Das Rätselobjekt" : $"Das Feld „{path}“";
    }

    private static void Write(Utf8JsonWriter writer, Puzzle puzzle)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", Puzzle.CurrentFormatVersion);
        writer.WriteString("name", puzzle.Name);
        writer.WriteString("grid", GridTypeNames.ToName(puzzle.Grid));
        WriteDomain(writer, "domain", puzzle.Domain);

        writer.WriteStartArray("nodes");
        foreach (var node in puzzle.Nodes)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", node.Id);
            writer.WriteNumber("x", node.X);
            writer.WriteNumber("y", node.Y);
            if (node.Domain is { } domain)
            {
                WriteDomain(writer, "domain", domain);
            }

            if (node.Given is int given)
            {
                writer.WriteNumber("given", given);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("edges");
        foreach (var edge in puzzle.Edges)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", edge.Id);
            writer.WriteNumber("from", edge.From);
            writer.WriteNumber("to", edge.To);
            writer.WriteString("rule", edge.Rule);
            WriteParams(writer, edge.Params);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("groups");
        foreach (var group in puzzle.Groups)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", group.Id);
            writer.WriteStartArray("nodes");
            foreach (var nodeId in group.Nodes)
            {
                writer.WriteNumberValue(nodeId);
            }

            writer.WriteEndArray();
            writer.WriteString("rule", group.Rule);
            WriteParams(writer, group.Params);
            if (group.Color is { } color)
            {
                writer.WriteString("color", color);
            }

            if (group.Label is { } label)
            {
                writer.WriteString("label", label);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDomain(Utf8JsonWriter writer, string name, ValueDomain domain)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("min", domain.Min);
        writer.WriteNumber("max", domain.Max);
        writer.WriteEndObject();
    }

    private static void WriteParams(Utf8JsonWriter writer, RuleParams parameters)
    {
        writer.WriteStartObject("params");
        foreach (var (name, value) in parameters)
        {
            writer.WritePropertyName(name);
            value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>Liest das JSON-Dokument und sammelt dabei alle Formatfehler.</summary>
    private sealed class Reader
    {
        private static readonly string[] PuzzleFields = ["formatVersion", "name", "grid", "domain", "nodes", "edges", "groups"];
        private static readonly string[] DomainFields = ["min", "max"];
        private static readonly string[] NodeFields = ["id", "x", "y", "domain", "given"];
        private static readonly string[] EdgeFields = ["id", "from", "to", "rule", "params"];
        private static readonly string[] GroupFields = ["id", "nodes", "rule", "params", "color", "label"];

        public List<string> Errors { get; } = [];

        public Puzzle? ReadPuzzle(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Errors.Add("Die Datei muss ein JSON-Objekt enthalten.");
                return null;
            }

            if (!CheckFormatVersion(root))
            {
                return null;
            }

            CheckFields(root, "Rätsel", PuzzleFields);
            var name = ReadString(root, "name", "Rätsel", required: false) ?? string.Empty;

            var grid = GridType.Square;
            var gridName = ReadString(root, "grid", "Rätsel", required: true);
            if (gridName is not null && !GridTypeNames.TryParse(gridName, out grid))
            {
                Errors.Add($"Unbekannter Gittertyp „{gridName}“. Erlaubt sind: {string.Join(", ", GridTypeNames.All)}.");
            }

            var domain = ReadDomain(root, "Rätsel", required: true) ?? new ValueDomain(1, 9);
            var puzzle = new Puzzle(name, grid, domain);

            foreach (var (element, path) in ReadArray(root, "nodes"))
            {
                if (ReadNode(element, path) is { } node)
                {
                    puzzle.Nodes.Add(node);
                }
            }

            foreach (var (element, path) in ReadArray(root, "edges"))
            {
                if (ReadEdge(element, path) is { } edge)
                {
                    puzzle.Edges.Add(edge);
                }
            }

            foreach (var (element, path) in ReadArray(root, "groups"))
            {
                if (ReadGroup(element, path) is { } group)
                {
                    puzzle.Groups.Add(group);
                }
            }

            return puzzle;
        }

        private bool CheckFormatVersion(JsonElement root)
        {
            if (!root.TryGetProperty("formatVersion", out var version))
            {
                Errors.Add($"Das Feld „formatVersion“ fehlt. Erwartet wird \"formatVersion\": {Puzzle.CurrentFormatVersion}.");
                return false;
            }

            if (!RuleParams.TryReadInt(version, out var number))
            {
                Errors.Add($"Das Feld „formatVersion“ muss eine ganze Zahl sein, ist aber {RuleParams.Describe(version)}.");
                return false;
            }

            if (number != Puzzle.CurrentFormatVersion)
            {
                Errors.Add($"Nicht unterstützte Formatversion {number}: Diese Programmversion kann nur Dateien mit formatVersion {Puzzle.CurrentFormatVersion} lesen.");
                return false;
            }

            return true;
        }

        private Node? ReadNode(JsonElement element, string path)
        {
            if (!ExpectObject(element, path))
            {
                return null;
            }

            CheckFields(element, path, NodeFields);
            var id = ReadInt(element, "id", path, required: true);
            var x = ReadInt(element, "x", path, required: true);
            var y = ReadInt(element, "y", path, required: true);
            var given = ReadInt(element, "given", path, required: false);
            var domain = ReadDomain(element, path, required: false);
            return id is null || x is null || y is null ? null : new Node(id.Value, x.Value, y.Value, given, domain);
        }

        private Edge? ReadEdge(JsonElement element, string path)
        {
            if (!ExpectObject(element, path))
            {
                return null;
            }

            CheckFields(element, path, EdgeFields);
            var id = ReadInt(element, "id", path, required: true);
            var from = ReadInt(element, "from", path, required: true);
            var to = ReadInt(element, "to", path, required: true);
            var rule = ReadString(element, "rule", path, required: true);
            var parameters = ReadParams(element, path);
            return id is null || from is null || to is null || rule is null || parameters is null
                ? null
                : new Edge(id.Value, from.Value, to.Value, rule, parameters);
        }

        private Group? ReadGroup(JsonElement element, string path)
        {
            if (!ExpectObject(element, path))
            {
                return null;
            }

            CheckFields(element, path, GroupFields);
            var id = ReadInt(element, "id", path, required: true);
            var rule = ReadString(element, "rule", path, required: true);
            var parameters = ReadParams(element, path);
            var color = ReadString(element, "color", path, required: false);
            var label = ReadString(element, "label", path, required: false);

            List<int>? nodes = null;
            if (!element.TryGetProperty("nodes", out var nodesElement))
            {
                Errors.Add($"{path}: Das Feld „nodes“ fehlt.");
            }
            else if (nodesElement.ValueKind != JsonValueKind.Array)
            {
                Errors.Add($"{path}: Das Feld „nodes“ muss eine Liste von Knoten-Ids sein, ist aber {RuleParams.Describe(nodesElement)}.");
            }
            else
            {
                nodes = [];
                var index = 0;
                foreach (var item in nodesElement.EnumerateArray())
                {
                    if (RuleParams.TryReadInt(item, out var nodeId))
                    {
                        nodes.Add(nodeId);
                    }
                    else
                    {
                        Errors.Add($"{path}.nodes[{index}]: Knoten-Ids müssen ganze Zahlen sein, gefunden wurde {RuleParams.Describe(item)}.");
                        nodes = null;
                        break;
                    }

                    index++;
                }
            }

            return id is null || rule is null || parameters is null || nodes is null
                ? null
                : new Group(id.Value, nodes, rule, parameters, color, label);
        }

        private RuleParams? ReadParams(JsonElement element, string path)
        {
            if (!element.TryGetProperty("params", out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return new RuleParams();
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                Errors.Add($"{path}: Das Feld „params“ muss ein Objekt sein, ist aber {RuleParams.Describe(value)}.");
                return null;
            }

            var result = new RuleParams();
            foreach (var property in value.EnumerateObject())
            {
                if (result.Contains(property.Name))
                {
                    Errors.Add($"{path}.params: Der Parameter „{property.Name}“ ist mehrfach angegeben.");
                    continue;
                }

                result.SetRaw(property.Name, property.Value);
            }

            return result;
        }

        private ValueDomain? ReadDomain(JsonElement element, string path, bool required)
        {
            if (!element.TryGetProperty("domain", out var value) || (!required && value.ValueKind == JsonValueKind.Null))
            {
                if (required)
                {
                    Errors.Add($"{path}: Das Feld „domain“ (Wertebereich mit min und max) fehlt.");
                }

                return null;
            }

            var domainPath = $"{path}.domain";
            if (!ExpectObject(value, domainPath))
            {
                return null;
            }

            CheckFields(value, domainPath, DomainFields);
            var min = ReadInt(value, "min", domainPath, required: true);
            var max = ReadInt(value, "max", domainPath, required: true);
            return min is null || max is null ? null : new ValueDomain(min.Value, max.Value);
        }

        private List<(JsonElement Element, string Path)> ReadArray(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return [];
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                Errors.Add($"Das Feld „{name}“ muss eine Liste sein, ist aber {RuleParams.Describe(value)}.");
                return [];
            }

            return value.EnumerateArray().Select((element, index) => (element, $"{name}[{index}]")).ToList();
        }

        private bool ExpectObject(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            Errors.Add($"{path}: Erwartet wird ein Objekt, gefunden wurde {RuleParams.Describe(element)}.");
            return false;
        }

        private void CheckFields(JsonElement element, string path, string[] allowed)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    Errors.Add($"{path}: Das Feld „{property.Name}“ ist mehrfach angegeben.");
                }
                else if (!allowed.Contains(property.Name))
                {
                    Errors.Add($"{path}: Unbekanntes Feld „{property.Name}“. Erlaubt sind: {string.Join(", ", allowed)}.");
                }
            }
        }

        private int? ReadInt(JsonElement element, string name, string path, bool required)
        {
            if (!element.TryGetProperty(name, out var value) || (!required && value.ValueKind == JsonValueKind.Null))
            {
                if (required)
                {
                    Errors.Add($"{path}: Das Feld „{name}“ fehlt.");
                }

                return null;
            }

            if (RuleParams.TryReadInt(value, out var result))
            {
                return result;
            }

            Errors.Add($"{path}: Das Feld „{name}“ muss eine ganze Zahl sein, ist aber {RuleParams.Describe(value)}.");
            return null;
        }

        private string? ReadString(JsonElement element, string name, string path, bool required)
        {
            if (!element.TryGetProperty(name, out var value) || (!required && value.ValueKind == JsonValueKind.Null))
            {
                if (required)
                {
                    Errors.Add($"{path}: Das Feld „{name}“ fehlt.");
                }

                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            Errors.Add($"{path}: Das Feld „{name}“ muss eine Zeichenkette sein, ist aber {RuleParams.Describe(value)}.");
            return null;
        }
    }
}
