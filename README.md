# GraphDoku

GraphDoku ist eine Desktop-App (Avalonia, .NET 10), die beliebige Sudoku-Varianten als **Constraint-Graph** modelliert:
Zellen sind **Knoten** mit Wertebereich, Beziehungen zwischen zwei Zellen sind **Kanten**, Regeln für Zellmengen hängen
an **Gruppen**. Gelöst wird mit Google OR-Tools (CP-SAT); ein Erklär-Modus macht den Lösungsweg Schritt für Schritt
nachvollziehbar. Anlass sind die Rätsel der Langen Nacht der Mathematik (Tridoku, Hexadoku, Sonderregeln).

Die verbindliche Spezifikation liegt in `docs/spec/`.

## Stand und Fahrplan

| Paket | Inhalt | Stand |
| --- | --- | --- |
| 1 – Gerüst und Kernmodell | Solution, Datenmodell, Regel-Registry, Parameterprüfung, JSON laden/speichern, Vorlage „Klassisch 9×9“, CI | erledigt |
| 2 – OR-Tools-Solver | `ISolver`, Lösen, Eindeutigkeit prüfen, Zeitlimit, Abbruch | erledigt |
| 3 – Editor | Zeichenfläche, Knoten/Kanten/Gruppen bearbeiten, Undo/Redo, Öffnen/Speichern | erledigt |
| 4 – Lösen in der App | „Lösen“, „Eindeutigkeit prüfen“, asynchron mit Abbruch | erledigt |
| 5 – Generator und alle Regeln im Editor | Kanten-Generator, Beispielrätsel (Killer, Kropki, Hexadoku, Dreiecksgitter) | Generator (Core) und Beispielrätsel erledigt, Editor-Oberfläche offen |
| 6 – Erklär-Modus | Kandidaten, Propagation mit Begründung, Schrittleiste | erledigt |
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

## Editor bedienen

Fenster: links die **Gruppenliste**, in der Mitte die **Zeichenfläche**, rechts das **Eigenschaften-Panel**, unten der
**Statusbereich** (letzte Aktion, Hinweis zum Werkzeug, Prüfmeldungen des Rätsels).

| Aufgabe | So geht es |
| --- | --- |
| Neues Rätsel | Datei → Neu … (Name, Gittertyp Quadrat/Dreieck/Sechseck, globaler Wertebereich) oder Vorlage „Klassisch 9×9“ |
| Knoten setzen | Werkzeug „Knoten setzen“, klicken oder über mehrere Gitterpunkte ziehen |
| Auswählen | Werkzeug „Auswahl“: Klick, Shift-Klick (hinzufügen/entfernen), Rahmen ziehen (Shift: hinzufügen), Strg/Cmd+A |
| Verschieben | ausgewählte Knoten ziehen; sie rasten auf Gitterpunkte ein |
| Löschen | Entf (Mac: fn+⌫) löscht ausgewählte Knoten (mit ihren Kanten), die gewählte Kante oder Gruppe |
| Givens | Knoten auswählen und Ziffern tippen (mehrstellig nacheinander, bei 0–15 auch A–F); ⌫ löscht Givens |
| Kante | Werkzeug „Kante ziehen“, Regel in der Werkzeugleiste wählen, von Knoten zu Knoten ziehen; Kante anklicken, um Regel und Parameter zu ändern |
| Gruppe | Knoten auswählen (die Reihenfolge zählt, z. B. für Thermometer), im Panel Regel wählen, „Gruppe erstellen“ (Strg/Cmd+G); Beschriftung, Farbe, Regel, Parameter und Reihenfolge im Panel |
| Gruppen hervorheben | Häkchen in der Gruppenliste bzw. „Alle ein“/„Alle aus“ |
| Ansicht | Mausrad zoomt, mittlere/rechte Maustaste oder Leertaste + Ziehen verschiebt, „Einpassen“ (Strg/Cmd+0) |
| Rückgängig | Strg/Cmd+Z, Wiederholen Strg/Cmd+Y oder Shift+Strg/Cmd+Z (alle Bearbeitungen) |
| Dateien | Öffnen (Strg/Cmd+O), Speichern (Strg/Cmd+S), Speichern unter (Shift+Strg/Cmd+S); „ *“ im Titel = ungespeichert |

Unfertige Rätsel lassen sich speichern und wieder öffnen; was noch fehlt (z. B. eine Gruppe mit zu wenigen Knoten),
steht im Statusbereich. Kantenmarken: Ungleich = Linie, Differenz = weißer Punkt (mit k, falls nicht 1), Verhältnis =
schwarzer Punkt (mit Faktor, falls nicht 2), Kleiner als = Pfeil vom kleineren zum größeren Wert.

## Lösen in der App

Die **Lösen-Leiste** unter der Werkzeugleiste (auch Menü „Lösen“):

| Aufgabe | So geht es |
| --- | --- |
| Lösung suchen | „Lösen“ (F5): Die Lösung erscheint in den Knoten – **blau** die gelösten Werte, **schwarz** die Givens. Das Rätsel selbst wird nicht verändert und die Lösung nie gespeichert. |
| Eindeutigkeit | „Eindeutigkeit prüfen“ (F6) sucht nach bis zu zwei Lösungen: eindeutig, mehrdeutig oder unlösbar. Bei mehrdeutigen Rätseln zeigen „Lösung 1 anzeigen“/„Lösung 2 anzeigen“ zwei verschiedene Lösungen, die abweichenden Knoten sind **orange**. |
| Abbrechen | Während der Suche laufen Balken und „Abbrechen“; der Editor bleibt bedienbar. |
| Zeitlimit | Feld „Zeitlimit“ in Sekunden (Standard 30, 0,1–3600). |
| Lösung ausblenden | „Lösung ausblenden“; jede Bearbeitung des Rätsels blendet die Lösung automatisch aus. |

Die Farbe der Leiste zeigt das Ergebnis: grün (gelöst, eindeutig), gelb (mehrdeutig, Zeitlimit, abgebrochen, veraltet),
rot (unlösbar, ungültig – mit Liste der Probleme).

## Erklär-Modus

„Erklären“ (Lösen-Leiste, Menü „Lösen“ oder F7) berechnet einen menschlich nachvollziehbaren Lösungsweg: erst
feststehende Werte streichen, dann einziger Kandidat, einziger Platz in einer Gruppe, dann Schlüsse der Kanten- und
Gruppenregeln; nur wenn das nicht weiterhilft, wird verzweigt (der Wert kommt aus der Lösung des Solvers). Die
Berechnung läuft im Hintergrund und lässt sich abbrechen (die bis dahin gefundenen Schritte bleiben ansehbar); das
Zeitlimit der Lösen-Leiste gilt auch hier.

| Aufgabe | So geht es |
| --- | --- |
| Schritte ansehen | Schrittleiste: « Anfang, ‹ Zurück, Vor ›, Ende » oder Schieberegler; „Schritt n von N“ (0 = Ausgangslage) |
| Tasten | wenn die Schrittleiste den Fokus hat (z. B. nach Klick auf „Vor ›“): ← / → ein Schritt, Pos1 / Ende zum Anfang bzw. Ende. Vorsicht: Ein Klick in die Zeichenfläche mit „Knoten setzen“ oder das Verschieben eines Knotens ist eine Bearbeitung und beendet die Erklärung |
| Begründung | Panel rechts: Ergebnis, Art und Schlussweise des Schritts, Kurzfassung, Begründungstext, verantwortliche Regel |
| Kandidaten | Häkchen „Kandidaten“ (auch Menü): kleine Ziffern im Kreis, 3 × 3 bei 1–9, 4 × 4 bei 16 Werten (0–F); sind die Knoten dafür zu klein, steht die Anzahl „(n)“ bzw. bei sehr kleinen Knoten nur der gerade gestrichene Wert im Knoten (Zoomen hilft) |
| Beenden | „Erklärung beenden“; jede Bearbeitung des Rätsels beendet die Erklärung ebenfalls |

Farben auf der Zeichenfläche: **schwarz** Givens, **blau** hergeleitete Werte, **orange** geratene Werte;
**gelb** hinterlegt die Knoten des aktuellen Schritts (rot durchgestrichen: gerade gestrichene Kandidaten),
**orange** mit Marke „Raten“ eine Verzweigung, **grün** die Knoten, die Gruppe bzw. die Kante der verantwortlichen
Regel; **rot umrandet** (außen) ein Knoten ohne Kandidaten (Widerspruch). Wie die Lösung ist die Erklärung nur Anzeige: Sie ändert das Rätsel nicht und wird nicht gespeichert.

Rendering-Rauchtest: `dotnet test tests/GraphDoku.App.Tests --filter Rendering` rendert das Hauptfenster headless
(Skia, auch mit gelöstem und mehrdeutigem Sudoku und im Erklär-Modus) und legt PNGs in `$GRAPHDOKU_RENDER_DIR` bzw. im Temp-Verzeichnis unter `GraphDoku-Render` ab.

## Beispielrätsel

Die Dateien in `puzzles/` lassen sich über Datei → Öffnen laden; alle Tests prüfen sie automatisch (gültig, lösbar,
außer `klassisch-leer.json` eindeutig).

| Datei | Beschreibung |
| --- | --- |
| `klassisch-leer.json` | Klassisches 9×9 ohne Givens (Vorlage zum Selbstausfüllen; nicht eindeutig) |
| `klassisch-leicht.json` | Klassisches 9×9, 30 Givens; der Erklär-Modus löst es ohne Verzweigung |
| `klassisch-mittel.json` | Klassisches 9×9, 24 Givens; der Erklär-Modus braucht einige Verzweigungen |
| `klassisch-schwer.json` | Klassisches 9×9, 21 Givens; der Erklär-Modus braucht mehrere Verzweigungen |
| `killer-sudoku.json` | Killer-Sudoku 9×9: 28 farbige Käfige mit Summe (`Sum`), 3 Givens (Summen derzeit nur im Eigenschaften-Panel sichtbar) |
| `kropki-sudoku.json` | Kropki-Sudoku 9×9: schwarze Punkte (`Ratio`, Faktor 2) und weiße Punkte (`Difference`, k = 1), 2 Givens |
| `hexadoku-16x16.json` | Hexadoku 16×16 mit Werten 0–15 (Anzeige 0–F), Zeilen, Spalten und 4×4-Blöcke als `CoverRange`, 90 Givens |
| `dreiecks-sudoku.json` | Dreiecks-Sudoku (Tridoku-ähnlich) auf dem Dreiecksgitter: 9 Regionen, 3 Außenseiten, sich berührende Dreiecke verschiedener Regionen ungleich (`NotEqual`), 33 Givens |
| `thermo-sudoku-6x6.json` | Thermo-Sudoku 6×6 mit 3 Thermometern und 4 „Kleiner als“-Kanten, 5 Givens |

Die fünf Varianten-Rätsel sind eindeutig und ohne Verzweigung erklärbar; erzeugt wurden sie einmalig mit Testcode
(`ExamplePuzzleGenerator`, siehe `docs/decisions.md`, Nr. 125).

## Projektstruktur

```text
GraphDoku.sln
src/
  GraphDoku.Core/        Datenmodell, Regeln, Kandidaten, Geometrie, JSON (keine UI-, keine Solver-Abhängigkeit)
  GraphDoku.Solver/      ISolver, OrToolsSolver (CP-SAT) mit Regel-Adaptern
  GraphDoku.Explainer/   Erklär-Modus: ExplanationEngine (Schritte, Zustände, Begründungen)
  GraphDoku.App/         Avalonia-App (MVVM mit CommunityToolkit.Mvvm)
tests/
  GraphDoku.Core.Tests/
  GraphDoku.Solver.Tests/
  GraphDoku.Explainer.Tests/
  GraphDoku.App.Tests/   ViewModel-Tests, Akzeptanztests Editor, Lösen und Erklären, Rendering-Rauchtests (headless)
puzzles/                 Beispielrätsel als JSON (auch Testdaten), siehe „Beispielrätsel“
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
- `Generation`: `EdgeGenerator` (Kanten nach Abstandsregel erzeugen, `EdgeGenerationRequest` → `EdgeGenerationResult`), `DistanceRule`, `EdgeDirectionPolicy`

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
- Beim Hexadoku (Wertebereich genau 0–15) zeigt der Editor 10–15 als A–F an und nimmt A–F als Eingabe; gespeichert werden immer Zahlen.

## CI

Gitea Actions (`.gitea/workflows/ci.yml`) läuft bei jedem Push und Pull Request auf einem Ubuntu-Runner
(`ubuntu-latest`): Restore, Release-Build und alle Tests; die Testergebnisse (TRX) werden als Artefakt hochgeladen.
Für den headless Rendering-Rauchtest installiert die CI bei Bedarf `libfontconfig1` (von SkiaSharp benötigt).

## Lizenz

Proprietär, alle Rechte vorbehalten – siehe `LICENSE`. Komponenten Dritter: `THIRD-PARTY-NOTICES.md`.
