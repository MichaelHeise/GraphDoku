# GraphDoku

GraphDoku ist eine Desktop-App (Avalonia, .NET 10), die beliebige Sudoku-Varianten als **Constraint-Graph** modelliert:
Zellen sind **Knoten** mit Wertebereich, Beziehungen zwischen zwei Zellen sind **Kanten**, Regeln für Zellmengen hängen
an **Gruppen**. Gelöst wird mit Google OR-Tools (CP-SAT); ein Erklär-Modus soll den Lösungsweg Schritt für Schritt
nachvollziehbar machen. Anlass sind die Rätsel der Langen Nacht der Mathematik (Tridoku, Hexadoku, Sonderregeln).

Die verbindliche Spezifikation liegt in `docs/spec/`.

## Stand und Fahrplan

| Paket | Inhalt | Stand |
| --- | --- | --- |
| 1 – Gerüst und Kernmodell | Solution, Datenmodell, Regel-Registry, Parameterprüfung, JSON laden/speichern, Vorlage „Klassisch 9×9“, CI | erledigt |
| 2 – OR-Tools-Solver | `ISolver`, Lösen, Eindeutigkeit prüfen, Zeitlimit, Abbruch | erledigt |
| 3 – Editor | Zeichenfläche, Knoten/Kanten/Gruppen bearbeiten, Undo/Redo, Öffnen/Speichern | offen |
| 4 – Lösen in der App | „Lösen“, „Eindeutigkeit prüfen“, asynchron mit Abbruch | offen |
| 5 – Generator und alle Regeln im Editor | Kanten-Generator, Beispielrätsel (Killer, Kropki, Hexadoku, Dreiecksgitter) | offen |
| 6 – Erklär-Modus | Kandidaten, Propagation mit Begründung, Schrittleiste | offen |
| 7 – optional: Zellformen | Dreieck, Quadrat, Sechseck als Darstellung | offen |

Details: `docs/progress.md`; Abweichungen von der Spezifikation: `docs/decisions.md`.

## Regelkatalog (Version 1)

| Typname | Gilt für | Parameter | Bedeutung |
| --- | --- | --- | --- |
| `AllDifferent` | Gruppe | – | alle Werte verschieden |
| `CoverRange` | Gruppe | `min`, `max` (Standard: Rätselbereich) | jede Zahl des Bereichs genau einmal; Knotenanzahl = Bereichsgröße |
| `Sum` | Gruppe | `target` (Pflicht), `allowRepeats` (Standard `false`) | Summe = target |
| `Thermometer` | geordnete Gruppe | – | Werte streng steigend in Listenreihenfolge |
| `NotEqual` | Kante | – | a ≠ b |
| `Difference` | Kante | `k` (Pflicht, ≥ 0), `mode` = `exact` (Standard) oder `atLeast` | \|a − b\| = k bzw. ≥ k |
| `Ratio` | Kante | `factor` (Standard 2, ≥ 1) | a = factor·b oder b = factor·a |
| `LessThan` | gerichtete Kante | – | from < to |

## Voraussetzungen

- .NET SDK 10.0 (siehe `global.json`; neuere 10.0-Feature-Bänder werden akzeptiert)
- macOS, Linux oder Windows

## Bauen, Starten, Testen

```bash
dotnet restore
dotnet build
dotnet run --project src/GraphDoku.App      # App starten
dotnet test                                 # alle Tests
```

Wie in der CI:

```bash
dotnet build GraphDoku.sln -c Release
dotnet test GraphDoku.sln -c Release --no-build
```

Hinweis: Avalonia bringt eine Build-Telemetrie mit (`Avalonia.BuildServices`). Sie ist im Repo über
`Directory.Build.targets` abgeschaltet; zusätzlich kann `AVALONIA_TELEMETRY_OPTOUT=1` gesetzt werden.

## Projektstruktur

```text
GraphDoku.sln
src/
  GraphDoku.Core/        Datenmodell, Regeln, Kandidaten, Geometrie, JSON (keine UI-, keine Solver-Abhängigkeit)
  GraphDoku.Solver/      ISolver, OrToolsSolver (CP-SAT) mit Regel-Adaptern
  GraphDoku.Explainer/   Erklär-Modus (ab Paket 6)
  GraphDoku.App/         Avalonia-App (MVVM mit CommunityToolkit.Mvvm)
tests/
  GraphDoku.Core.Tests/
  GraphDoku.Solver.Tests/
  GraphDoku.Explainer.Tests/
  GraphDoku.App.Tests/   ViewModel-Tests
puzzles/                 Beispielrätsel als JSON (auch Testdaten): klassisch-leer, -leicht, -mittel, -schwer
docs/
  spec/                  Spezifikation (Projektkonzept, Agenten-Anleitung)
  decisions.md           Entscheidungen und Abweichungen
  progress.md            Fortschritt pro Paket
```

Abhängigkeiten nur in diese Richtung: App → Explainer → Solver → Core (die App darf auch direkt auf Solver und Core
zugreifen). Core kennt niemanden.

Wichtige Typen in `GraphDoku.Core`:

- `Model`: `Puzzle`, `Node`, `Edge`, `Group`, `ValueDomain`, `RuleParams`, `GridType`, `PuzzleComparer`
- `Rules`: `IRule`, `RuleBase`, `RuleRegistry`, Katalogregeln unter `Rules/Catalog`
- `Candidates`: `CandidateState`, `CandidateChange`, `Reason`
- `Geometry`: `GridGeometry` (Weltkoordinaten, Nachbarschaften, Springerzug, Abstände, Einrasten)
- `Serialization`: `PuzzleSerializer`, `PuzzleFormatException`
- `Validation`: `PuzzleValidator`
- `Templates`: `ClassicSudoku`

Wichtige Typen in `GraphDoku.Solver`:

- `ISolver` mit `Solve(puzzle, options, token)` → `SolveResult` (gelöst mit Wert pro Knoten-Id / unlösbar / Zeitlimit /
  abgebrochen / ungültig) und `CountSolutions(puzzle, limit = 2, …)` → `SolutionCountResult` (Eindeutigkeit).
  Die Methoden arbeiten synchron; `SolveAsync`/`CountSolutionsAsync` (`SolverExtensions`) laufen im Hintergrund.
- `SolverOptions` (Zeitlimit, Standard 30 s); Abbruch per `CancellationToken`.
- `OrTools.OrToolsSolver`: eine Integer-Variable pro Knoten, Givens fixiert, jede Regel über einen Adapter
  (`IOrToolsRuleAdapter`, Registry `OrToolsAdapterRegistry`, Schlüssel = Typname der Regel).

Neue Regel einbinden: `IRule` implementieren und in `RuleRegistry` aufnehmen (Core) sowie einen
`IOrToolsRuleAdapter` in `OrToolsAdapterRegistry` (Solver) – das JSON-Format bleibt gleich. Hilfsvariablen eines
Adapters sollten durch die Knotenwerte festgelegt sein; die Lösungszählung zählt ohnehin nur verschiedene
Knotenbelegungen, freie Hilfsvariablen machen sie aber langsamer.

## JSON-Format (Kurzfassung)

```json
{
  "formatVersion": 1,
  "name": "Klassisch 9x9",
  "grid": "square",
  "domain": { "min": 1, "max": 9 },
  "nodes": [ { "id": 1, "x": 0, "y": 0, "given": 5 }, { "id": 2, "x": 1, "y": 0, "domain": { "min": 1, "max": 4 } } ],
  "edges": [ { "id": 1, "from": 1, "to": 2, "rule": "Difference", "params": { "k": 1, "mode": "exact" } } ],
  "groups": [ { "id": 1, "nodes": [1, 2], "rule": "AllDifferent", "params": {}, "color": "#3366ff", "label": "Zeile 1" } ]
}
```

- `formatVersion` ist Pflicht und muss `1` sein; andere Versionen werden mit Meldung abgelehnt.
- `grid`: `square`, `triangle` (Zelle (x, y) zeigt nach oben, wenn x + y gerade ist) oder `hex` (axiale Koordinaten q = x, r = y).
- Ids sind positive Ganzzahlen, fortlaufend pro Rätsel und getrennt gezählt für Knoten, Kanten und Gruppen.
- `domain` am Knoten überschreibt den globalen Wertebereich; `given` ist optional.
- `params` enthält nur die Parameter der Regel; unbekannte Parameter, unbekannte Felder und unbekannte Regeltypen
  werden mit verständlicher deutscher Meldung abgelehnt (mit Angabe von Kante/Gruppe).
- Lösungen werden nie gespeichert.
- Beim Hexadoku (Wertebereich genau 0–15) wird die App 10–15 als A–F anzeigen (ab dem Editor, Paket 3); gespeichert werden immer Zahlen.

## CI

Gitea Actions (`.gitea/workflows/ci.yml`) läuft bei jedem Push und Pull Request auf einem Ubuntu-Runner
(`ubuntu-latest`): Restore, Release-Build und alle Tests; die Testergebnisse (TRX) werden als Artefakt hochgeladen.

## Lizenz

Proprietär, alle Rechte vorbehalten – siehe `LICENSE`. Komponenten Dritter: `THIRD-PARTY-NOTICES.md`.
