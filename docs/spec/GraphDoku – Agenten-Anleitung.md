# GraphDoku – Agenten-Anleitung

Du implementierst GraphDoku: eine Avalonia-Desktop-App (.NET), die beliebige Sudoku-Varianten als Constraint-Graph modelliert, mit Google OR-Tools löst und einen menschlich nachvollziehbaren Lösungsweg erzeugt. Arbeite die Arbeitspakete der Reihe nach ab. Ein Paket ist erst fertig, wenn alle Akzeptanzkriterien erfüllt sind und alle Tests grün sind.

## Arbeitsweise

- Arbeite in kleinen, lauffähigen Schritten. Nach jedem Arbeitspaket: bauen, testen, committen (eine aussagekräftige Commit-Nachricht pro Paket oder Teilpaket).
- Halte dich strikt an die Festlegungen unten. Weiche nur ab, wenn etwas technisch nicht geht, und dokumentiere das dann in `docs/decisions.md` mit Grund.
- Kein Scope Creep: keine Zellformen jenseits von Kreis, Dreieck, Quadrat, Sechseck; keine Ausdruckssprache für Regeln; keine Netzwerk- oder Cloud-Funktionen.
- Richte in Paket 1 eine Gitea-Actions-CI ein (`.gitea/workflows/ci.yml`): Restore, Build und alle Tests bei jedem Push und Pull Request. Der Runner läuft auf einem Ubuntu-Rechner; die Pipeline muss dort ohne manuelle Schritte durchlaufen. Erweitere sie bei Bedarf in späteren Paketen.
- Schreibe zu jeder Regel und zu jeder Kernfunktion Unit-Tests. UI-Logik gehört in ViewModels, damit sie testbar ist.
- Führe eine `docs/progress.md`, in der du pro Paket festhältst: erledigt, offen, bekannte Probleme.

## Festlegungen

| Thema | Festlegung |
| --- | --- |
| Sprache / Runtime | C#, aktuelle .NET-LTS-Version, Nullable aktiviert |
| UI | Avalonia, MVVM mit CommunityToolkit.Mvvm |
| Solver | Google.OrTools (CP-SAT) über NuGet, hinter dem Interface `ISolver` |
| Tests | xUnit; FluentAssertions erlaubt |
| Speicherformat | JSON über System.Text.Json, Feld `formatVersion` = 1 |
| Ids | fortlaufende Ganzzahlen pro Rätsel |
| Positionen | ganzzahlige Gitterkoordinaten + Gittertyp (`square`, `triangle`, `hex`) |
| Wertebereich | global pro Rätsel, pro Knoten überschreibbar |
| Lösungen | werden nie in der Rätseldatei gespeichert |
| Darstellung | Knoten als Kreise; andere Formen erst in Paket 7 und nur optional |

## Projektstruktur

```text
GraphDoku.sln
src/
  GraphDoku.Core/        Datenmodell, Regeln, Serialisierung (keine UI-, keine Solver-Abhängigkeit)
  GraphDoku.Solver/      ISolver, OrToolsSolver
  GraphDoku.Explainer/   Erklär-Modus (Propagation + Schrittprotokoll)
  GraphDoku.App/         Avalonia-App
tests/
  GraphDoku.Core.Tests/
  GraphDoku.Solver.Tests/
  GraphDoku.Explainer.Tests/
puzzles/                 Beispielrätsel als JSON (auch als Testdaten)
docs/
  decisions.md
  progress.md
```

Abhängigkeiten nur in diese Richtung: App → Explainer, Solver → Core. Core kennt niemanden.

## Kernmodell

- `Puzzle`: Name, Gittertyp, globaler Wertebereich, Listen von `Node`, `Edge`, `Group`.
- `Node`: Id, X, Y, optionaler eigener Wertebereich, optionaler Given.
- `Edge`: Id, From, To, Regeltyp, Parameter. Gerichtet, wenn die Regel es verlangt (z. B. Kleiner als).
- `Group`: Id, geordnete Liste von Knoten-Ids, Regeltyp, Parameter, optionale Farbe und Beschriftung.
- `IRule`-Abstraktion: Jeder Regeltyp hat einen eindeutigen Typnamen, validiert seine Parameter und liefert zwei Fähigkeiten:
  1. `AddToModel(...)`: übersetzt die Regel in OR-Tools-Constraints (im Solver-Projekt implementiert, z. B. über ein Visitor- oder Adapter-Muster, damit Core frei von OR-Tools bleibt).
  2. `Propagate(CandidateState)`: streicht Kandidaten oder setzt Werte und liefert jede Änderung mit einer `Reason` (Regel-Id, beteiligte Knoten, kurzer Text auf Deutsch).
- Neue Regeln werden über eine Registry (Typname → Implementierung) eingebunden; das JSON-Format ändert sich dafür nicht.

Beispiel für das JSON-Format:

```json
{
  "formatVersion": 1,
  "name": "Klassisch 9x9",
  "grid": "square",
  "domain": { "min": 1, "max": 9 },
  "nodes": [ { "id": 1, "x": 0, "y": 0, "given": 5 } ],
  "edges": [ { "id": 1, "from": 1, "to": 2, "rule": "Difference", "params": { "k": 1, "mode": "exact" } } ],
  "groups": [ { "id": 1, "nodes": [1, 2, 3], "rule": "AllDifferent", "params": {} } ]
}
```

## Regelkatalog (Version 1)

| Typname | Gilt für | Parameter | Bedeutung |
| --- | --- | --- | --- |
| `AllDifferent` | Gruppe | – | alle Werte verschieden |
| `CoverRange` | Gruppe | `min`, `max` (Standard: Rätselbereich) | Gruppe enthält jede Zahl des Bereichs genau einmal; Anzahl Knoten muss zur Bereichsgröße passen |
| `Sum` | Gruppe | `target`, `allowRepeats` (Standard false) | Summe = target |
| `Thermometer` | Gruppe (geordnet) | – | Werte streng steigend in Listenreihenfolge |
| `NotEqual` | Kante | – | a ≠ b |
| `Difference` | Kante | `k`, `mode` = `exact` oder `atLeast` | abs(a − b) = k bzw. ≥ k |
| `Ratio` | Kante | `factor` (Standard 2) | a = factor·b oder b = factor·a |
| `LessThan` | gerichtete Kante | – | from < to |

**Kanten-Generator:** erzeugt für alle Knotenpaare, die eine Abstandsregel erfüllen (direkte Nachbarn orthogonal, Nachbarn inkl. diagonal, Springerzug, frei wählbarer Abstand), Kanten eines gewählten Regeltyps. Doppelte Kanten werden vermieden.

## Arbeitspakete

### Paket 1 – Gerüst und Kernmodell

- Solution und Projekte wie oben anlegen, Build und Tests laufen.
- Datenmodell, Regel-Registry, Parametervalidierung.
- JSON laden und speichern, inklusive `formatVersion`-Prüfung.
- Hilfsfunktion, die ein klassisches 9×9-Sudoku (81 Knoten, 27 `AllDifferent`-Gruppen) erzeugt.

**Akzeptanz:** Round-Trip-Test (laden → speichern → laden ergibt gleiches Modell); ungültige Parameter und unbekannte Regeltypen werden mit verständlicher Fehlermeldung abgelehnt.

### Paket 2 – OR-Tools-Solver

- `ISolver` mit mindestens: `Solve(puzzle)` → Lösung oder „unlösbar“; `CountSolutions(puzzle, limit)` für die Eindeutigkeitsprüfung (Limit 2 reicht).
- `OrToolsSolver`: eine Integer-Variable pro Knoten, Givens fixiert, alle acht Regeln übersetzt.
- Zeitlimit konfigurierbar, Abbruch per CancellationToken.

**Akzeptanz:** Tests mit mindestens drei klassischen Sudokus bekannter Lösung, einem unlösbaren und einem mehrdeutigen Rätsel; je Regeltyp ein kleiner Test, der genau diese Regel prüft.

### Paket 3 – Editor (Grundfunktionen)

- Zeichenfläche mit Zoom und Pan, Gitter sichtbar (Quadrat, Dreieck, Hexagon als Raster), Knoten als Kreise rasten auf Gitterpunkte ein.
- Knoten setzen, verschieben, löschen; Rahmenauswahl und Shift-Klick.
- Kanten per Drag von Knoten zu Knoten ziehen, Regeltyp und Parameter in einem Eigenschaften-Panel wählen.
- Gruppen aus der aktuellen Auswahl erstellen, Regel zuweisen, farbig hervorheben; Liste aller Gruppen zum Ein- und Ausblenden.
- Givens per Tastatur in ausgewählte Knoten eintragen.
- Undo/Redo für alle Bearbeitungen; Neu, Öffnen, Speichern.
- Vorlage „Klassisch 9×9“.

**Akzeptanz:** Ein klassisches Sudoku lässt sich komplett im Editor anlegen, speichern und wieder öffnen; Undo/Redo-Tests auf ViewModel-Ebene.

### Paket 4 – Lösen in der App

- Buttons „Lösen“ und „Eindeutigkeit prüfen“; Lösung wird in den Knoten angezeigt (optisch von Givens unterscheidbar).
- Lösen läuft asynchron, UI bleibt bedienbar, Abbrechen möglich.
- Unlösbar und mehrdeutig werden klar gemeldet.

**Akzeptanz:** Beispielrätsel aus `puzzles/` lassen sich in der App lösen.

### Paket 5 – Generator und vollständige Regeln im Editor

- Kanten-Generator im Editor: Abstandsregel und Regeltyp wählen, auf Auswahl oder alle Knoten anwenden.
- Alle Regeln aus dem Katalog im Editor anlegbar, inklusive Reihenfolge für `Thermometer` und Richtung für `LessThan`.
- Beispielrätsel anlegen: Killer-Sudoku, Sudoku mit Kropki-Punkten, ein Hexadoku (16×16, Werte 0–F bzw. 1–16) und ein Rätsel auf Dreiecksgitter.

**Akzeptanz:** Jedes Beispielrätsel ist lösbar und eindeutig.

### Paket 6 – Erklär-Modus

- `CandidateState`: Kandidatenmenge pro Knoten, startet aus Wertebereichen und Givens.
- `Propagate` für jede Regel implementieren, jeweils mit `Reason`.
- Erklär-Schleife: Wende Schlüsse in fester Reihenfolge vom einfachsten zum schwierigsten an, protokolliere jede Änderung als Schritt:
  1. Einziger Kandidat in einem Knoten (naked single).
  2. Zahl passt in einer `AllDifferent`/`CoverRange`-Gruppe nur an eine Stelle (hidden single).
  3. Kandidaten streichen über Kanten- und Gruppenregeln (Ungleich, Differenz, Verhältnis, Kleiner als, Summe, Thermometer).
- Kommt die Schleife ohne Raten nicht weiter: Schritt vom Typ „Verzweigung“ anlegen, Wert aus der OR-Tools-Lösung übernehmen, weiter propagieren.
- Ergebnis: geordnete Liste von Schritten (gesetzter Wert oder gestrichene Kandidaten, Knoten, Regel, Begründungstext).
- UI: Schrittleiste mit Vor/Zurück, Anfang/Ende und Schieberegler; aktuelle Zelle und Knoten der verantwortlichen Regel hervorgehoben; Begründungstext sichtbar; Kandidatenanzeige ein- und ausschaltbar.

**Akzeptanz:** Ein leichtes klassisches Sudoku wird ganz ohne Verzweigung erklärt; jeder Schritt ist mit der Endlösung konsistent (Test).

### Paket 7 – Optional: Zellformen

- Knoten können zusätzlich als Dreieck, Quadrat oder Sechseck dargestellt werden, passend zum Gittertyp. Nichts darüber hinaus.

**Akzeptanz:** Darstellung umschaltbar, Speicherformat bleibt kompatibel.

## Später, nicht jetzt

- Eigener Lern-Solver als zweite `ISolver`-Implementierung (macht der Nutzer eventuell selbst; die Schnittstelle muss das ohne Umbau erlauben).
- Ausdruckssprache für frei definierte Regeln.
