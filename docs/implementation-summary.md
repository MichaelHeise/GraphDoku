# GraphDoku – Implementierungszusammenfassung

Stand: 7. November 2026, Branch `feature/initial-implementation` (Grundlage: Commit `2b83b1e`, nicht gepusht).
Quellen: `docs/decisions.md` (Nr. 1–169), `docs/progress.md`, Git-Historie, die Berichte der Implementierungs-,
Review- und Fixer-Agenten aller Pakete und das Abschluss-Review.

Verweise der Form „Nr. X“ beziehen sich auf `docs/decisions.md`. Dort steht zu jedem Punkt die ausführliche
Begründung.

---

## 1. Kurzfassung

**Was gebaut wurde:** Eine Avalonia-Desktop-App (.NET 10). Sie modelliert Sudoku-Varianten als Constraint-Graph
(Knoten, Kanten, Gruppen), löst sie mit Google OR-Tools (CP-SAT) und erklärt den Lösungsweg Schritt für Schritt. Alle
sieben Arbeitspakete der Spezifikation sind umgesetzt, auch das optionale Paket 7. Dazu gehören ein Editor für
Quadrat-, Dreiecks- und Sechseckgitter mit Undo/Redo, alle acht Katalogregeln, der Kanten-Generator, Lösen und
Eindeutigkeitsprüfung im Hintergrund, der Erklär-Modus mit Schrittleiste und Kandidatenanzeige sowie die umschaltbare
Zelldarstellung. In `puzzles/` liegen neun Beispielrätsel.

### Stand pro Paket

| Paket | Inhalt | Stand | Tests danach (gesamt) | Review-Befunde (davon schwer) | Commits |
| --- | --- | --- | --- | --- | --- |
| 1 | Gerüst, Kernmodell, JSON, CI | erledigt | 159 | 10 (0) | `de79830`, `ac4bc0c` |
| 2 | OR-Tools-Solver | erledigt | 247 | 7 (0) | `a7d9f86`, `3d18c88` |
| 3 | Editor (Grundfunktionen) | erledigt | 363 | 10 (3) | `1692881`, `fbc340d` |
| 4 | Lösen in der App | erledigt | 417 | 5 (1) | `658447f`, `8c5842c` |
| 5 | Generator (Core) und Beispielrätsel | erledigt | 607 im Branch, 635 nach dem Merge | 3 (0) | `90fe42e`, `b606e59`, Merge `cb38ccc`, `9c14cb7` |
| 5 | Editor-Teil (Generator-Dialog, alle Regeln, Darstellung) | erledigt | 677 | 5 (0) | `6ac150a`, `2b88d20` |
| 6 | Erklär-Modus (Bibliothek) | erledigt | 335 im Branch | 8 (0) | `8859471`, `2642b68`, Merge `7cd1e35`, `01044f4` |
| 6 | Erklär-Modus (Oberfläche) | erledigt | 533 | 7 (0) | `6d91f11`, `43479f5` |
| 7 | Zellformen (optional) | erledigt | 735 | 6 (2) | `2924921`, `812029f` |
| – | Abschluss-Review (ganzes Projekt) | erledigt | **779** | 4 schwer (2 verschiedene Probleme), 12 kleinere | `7384ab9`, `a933cc7`, `2b83b1e` |

**Tests heute:** 779, alle grün. Davon Core 244, Solver 105, Explainer 82 und App 348. Gemessen wurde im
Release-Build auf macOS und in einem Linux-Container mit den Schritten der CI. Der Build läuft mit
`TreatWarningsAsErrors` ohne Warnungen.

### So wurde gebaut

- **Ein Multi-Agenten-Workflow pro Paket:** Ein Agent implementierte. Danach prüften Review-Agenten aus drei
  Blickwinkeln: Spezifikation, Korrektheit sowie Qualität bzw. Bedienbarkeit. Ein Fixer-Agent behob anschließend die
  Befunde oder lehnte sie begründet ab, mit jeweils eigenem Commit („Review-Befunde behoben“). Für die Pakete liefen
  vier Workflows mit zusammen 44 Agenten.
- **Parallele Tracks in Git-Worktrees:** Die Bibliothek des Erklär-Modus (Paket 6) entstand im Branch
  `feature/wp6-explainer` parallel zu Paket 3/4. Kanten-Generator und Beispielrätsel (Paket 5, Core) entstanden im
  Branch `feature/wp5-generator` parallel zur Oberfläche des Erklär-Modus. Beide Branches wurden mit `--no-ff` ohne
  Konflikte zusammengeführt. Ihre Notizdateien gingen in `decisions.md` und `progress.md` ein (aus G1–G17 wurden
  Nr. 116–132). Branches und Worktrees wurden danach gelöscht.
- **Abschluss-Review über das ganze Projekt:** Ein weiterer Durchgang prüfte Core, App sowie Repo und CI. In einem
  eigenen Prüfschritt wurde gezielt versucht, die schweren Befunde zu widerlegen. Alle vier wurden bestätigt, keiner
  widerlegt. Drei davon betrafen dasselbe Linux-Problem. Behoben wurden sie in drei Commits (Core, App, Repo).
- **Linux-CI-Nachweis in Docker:** Beim Beheben der Linux-Fehler wurden die Schritte aus `.gitea/workflows/ci.yml`
  in `mcr.microsoft.com/dotnet/sdk:10.0` nachgestellt, auf linux/arm64 und linux/amd64 (emuliert). Zum Schluss liefen
  sie noch einmal auf linux/amd64 mit einem `git archive`-Export von `2b83b1e`. Ergebnis: im ersten Durchgang grün,
  779/779.

---

## 2. Abweichungen vom Plan

Die Spezifikation erlaubt Abweichungen nur, „wenn etwas technisch nicht geht“, und verlangt dann einen Eintrag in
`decisions.md`. **Nicht alle folgenden Punkte waren technisch erzwungen.** Einige sind Auslegungen offener Stellen,
einige bewusste kleine Erweiterungen. Jeder Punkt ist in `decisions.md` begründet.
**★ markiert die Punkte, die am ehesten eine Rückmeldung des Projektinhabers verdienen.**

### 2.1 Architektur und Struktur

| | Plan (Spezifikation) | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| ★ | Abhängigkeiten „App → Explainer, Solver → Core“ | **App → Explainer → Solver → Core**, die App greift auch direkt auf Solver und Core zu | Der Erklär-Modus holt sich für „Verzweigung“ die OR-Tools-Lösung über `ISolver`. Architekturtests prüfen die Richtung auf Assembly- und auf `.csproj`-Ebene | 10 |
| ★ | Drei Testprojekte (Core, Solver, Explainer) | **Zusätzlich `tests/GraphDoku.App.Tests`**: ViewModel-Tests, Akzeptanztests für Editor, Lösen und Erklären, headless Rendering-Tests | „UI-Logik gehört in ViewModels, damit sie testbar ist“. Ohne eigenes Testprojekt wäre sie ungetestet geblieben | 11 |
| ★ | `CandidateState` gehört zu Paket 6 (Erklär-Modus) | **`CandidateState` liegt in Core**, seit Paket 6 auch `CandidateSet` (unveränderliche Bitmenge) | `IRule.Propagate(CandidateState)` liegt laut Spezifikation in Core, also muss der Typ dort liegen. Unveränderliche Mengen machen Schnappschüsse billig | 20, 82 |
| | `IRule` mit `AddToModel(...)` und `Propagate(...)` | `AddToModel` ist **nicht** Teil von `IRule`. Die OR-Tools-Übersetzung sitzt als Adapter-Registry (Typname → Adapter) im Solver-Projekt | Core bleibt frei von OR-Tools. Die Spezifikation erlaubt diesen Weg ausdrücklich | 19, 39 |
| | `Propagate` „streicht Kandidaten oder setzt Werte“ | `Propagate` verändert den Zustand nicht, sondern **liefert die Änderungen** mit `Reason`. Der Aufrufer wendet sie an | So kann der Erklär-Modus jede Änderung einzeln protokollieren | 19 |
| | `IRule`: Typname, Parameterprüfung, zwei Fähigkeiten | Zusätzlich `DisplayName` (deutsch), `Kind`, `MinNodes`, `Parameters` und `CreateDefaultParams()` | Der Editor baut daraus das Eigenschaften-Panel und neue Elemente mit gültigen Standardwerten | 19 |
| | `Solve(puzzle)` → Lösung oder „unlösbar“, `CountSolutions(puzzle, limit)` | `Solve(puzzle, options, token)` → `SolveResult` mit fünf Status (gelöst, unlösbar, Zeitlimit, abgebrochen, ungültig). `CountSolutions(..., limit = 2, ...)` → `SolutionCountResult`. Synchron, dazu `SolveAsync`/`CountSolutionsAsync` | Zeitlimit und Abbruch verlangt die Spezifikation ohnehin. Ein späterer Lern-Solver muss nur zwei einfache Methoden implementieren. Der Lösungsvertrag ist seit dem Abschluss-Review an der Schnittstelle festgeschrieben und wird geprüft | 35, 36, 163 |
| | Spezifikation im Ordner `Docu/` | **verschoben nach `docs/spec/`** (Commit `de79830`) | Alle Dokumente liegen unter `docs/` | 12 |

### 2.2 Technik, Pakete und Build

| | Plan / Erwartung | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| ★ | „FluentAssertions erlaubt“ | **FluentAssertions 7.2.2**. Auf 8.x wird nie aktualisiert | Ab Version 8 gilt eine kommerzielle Lizenz (Xceed). Version 7 steht unter Apache-2.0, und das Repo ist proprietär | 6 |
| ★ | In der Teamplanung: `Avalonia.Headless.XUnit` für UI-Tests | **nicht verwendet.** Stattdessen `Avalonia.Headless` und `Avalonia.Skia` mit selbst gestarteter Sitzung, xUnit bleibt bei 2.9.3 | `Avalonia.Headless.XUnit` 12.1.5 verlangt xUnit v3. Seit Paket 3 gibt es trotzdem headless Fenster- und Rendering-Tests | 7, 47 |
| ★ | – | **`nuget.config` im Repo** (nur nuget.org, Package Source Mapping) | Eine weitere Paketquelle auf dem Entwicklungsrechner löste mit Central Package Management den Fehler NU1507 aus. So ist der Restore lokal und in der CI reproduzierbar | 3 |
| ★ | – | **Avalonia-Build-Telemetrie abgeschaltet**: `Directory.Build.targets` ersetzt das Ziel `AvaloniaStats`, die CI setzt `AVALONIA_TELEMETRY_OPTOUT=1` | Proprietäres Projekt, keine Netzwerkfunktionen gewünscht | 8 |
| | `GraphDoku.sln` | Klassisches `.sln`-Format (`--format sln`) | .NET 10 erzeugt sonst `.slnx` | 4 |
| | „aktuelle .NET-LTS“ | `global.json` verlangt 10.0.100 mit `rollForward: latestFeature` | Jedes 10.0-Feature-Band funktioniert, lokal wie in der CI | 1 |
| | – (Projektvorgabe `TreatWarningsAsErrors`) | NuGet-Sicherheitswarnungen NU1901–NU1904 bleiben Warnungen. In Testprojekten sind CA1707, CA2007, CA1861 und CA1859 aus | Eine neu veröffentlichte Schwachstelle soll den Build nicht ohne Codeänderung brechen. Testnamen dürfen Unterstriche enthalten | 2 |
| | Wertebereich frei | Höchstens **4096 Werte** pro Wertebereich | Kandidatenmengen und Solver-Tabellen bleiben klein (Hexadoku braucht 16) | 16 |
| | JSON über System.Text.Json | `JsonDocument`/`Utf8JsonWriter` statt automatischer Serialisierung von DTOs | So gibt es deutsche Fehlermeldungen mit Pfad, unbekannte Felder werden strikt abgelehnt, und die Feldreihenfolge ist fest | 24, 25 |

### 2.3 Regeln und Solver

| | Plan | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| | Standardwerte nur teilweise angegeben | `Difference.k` und `Sum.target` sind Pflicht. Der Editor schlägt k = 1 und Zielsumme 10 vor. `Ratio.factor` ≥ 1, Standard 2. `CoverRange` fällt auf den Rätselbereich zurück | Offene Stellen der Spezifikation festgelegt | 22 |
| | „Gerichtet, wenn die Regel es verlangt“ | Kantenregeln nur an Kanten, Gruppenregeln nur an Gruppen, keine Schleifen. Doppelte Kanten und mehrere Knoten auf einer Position werden **nicht** abgelehnt | Doppelte Kanten sind redundant, aber harmlos. Editor und Generator vermeiden sie | 23 |
| | „alle acht Regeln übersetzt“ | `Ratio` als **Tabellen-Constraint** statt Reifikation. `CoverRange` als Bereich plus `AllDifferent`, ohne eigene Zählbedingung | Ohne freie Hilfsvariablen bleibt die Lösungszählung exakt | 40 |
| | `CountSolutions` | Gezählt werden **verschiedene Knotenbelegungen**, nicht CP-SAT-Belegungen | Freie Hilfsvariablen in eigenen Adaptern würden ein eindeutiges Rätsel sonst als mehrdeutig zeigen | 44 |
| | Abbruch per `CancellationToken` | `SolveWrapper` direkt statt `CpSolver` | Mit `CpSolver` könnte ein Abbruch kurz vor dem Start verloren gehen | 41 |
| | „Lösung oder unlösbar“ | Ungültige Rätsel liefern den Status `Invalid` mit Meldungen statt einer Ausnahme. Ein Given außerhalb des Wertebereichs gilt als ungültig, nicht als unlösbar | Gleiches Muster in Solver, Erklär-Modus und Generator | 37 |
| | Zeitlimit konfigurierbar | `SolverOptions` enthält **nur** das Zeitlimit (Standard 30 s) | Weitere CP-SAT-Parameter wären Scope Creep | 38 |
| | Springerzug im Generator | **nur auf dem Quadratgitter.** Auf Dreieck und Sechseck gibt es eine Meldung, und der Dialog bietet ihn dort nicht an | Für diese Gitter gibt es keine übliche Definition. Der freie Abstand kann ihn ersetzen | 29, 136 |
| | „frei wählbarer Abstand“ | Abstand als **Bereich [min, max] mit Toleranz**. Genau d entspricht [d, d] | Erlaubt etwa „alle Knoten im Umkreis 1,5“ ohne eine zweite Regelart | 117 |
| | „Doppelte Kanten werden vermieden“ | Der Generator vergleicht **ungerichtet und ohne Parameter**. Einzeln gezogene Kanten zählen im Editor nur bei gleicher Richtung als doppelt | Eine erzeugte Gegenkante bei „Kleiner als“ wäre sofort ein Widerspruch | 54, 119 |
| | `LessThan` im Generator | Wird nicht abgelehnt, sondern über eine **Richtungsregel** erzeugt (Werte steigen bzw. fallen in Leserichtung) | So ist der Generator für alle Kantenregeln gleich bedienbar | 120 |

### 2.4 Erklär-Modus

| | Plan | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| ★ | Feste Reihenfolge: (1) naked single, (2) hidden single, (3) Regelstreichungen | **Davor eine Stufe 0, „Feststehenden Wert streichen“** (AllDifferent, CoverRange und NotEqual bei schon gesetzten Werten) | Ohne sie liefen hidden singles auf veralteten Kandidaten, und ein naked single konnte sichtbar eine Regel verletzen. **Das ist eine Auslegung („einfachster Schluss“). Sie ist als vom Projektinhaber zu bestätigen markiert und noch nicht bestätigt.** Folge: mehr Schritte, „leicht“ hat 200 statt 120 | 95 |
| ★ | Verzweigung nur, „wenn die Schleife ohne Raten nicht weiterkommt“ | Mit den Techniken der Spezifikation (Singles und Regelstreichungen, keine Paare, kein Pointing) braucht **`klassisch-mittel` drei Verzweigungen**, `klassisch-schwer` mehrere | Weitere Techniken verlangt die Spezifikation nicht. Ihr Akzeptanzkriterium (leichtes Sudoku ohne Verzweigung) ist erfüllt | `progress.md` Paket 6 |
| | Hidden single in AllDifferent- und CoverRange-Gruppen | Bei `CoverRange` immer. Bei `AllDifferent` nur, wenn die aktuellen Kandidaten genau so viele Werte umfassen wie die Gruppe Knoten hat (Schubfachprinzip). Andere Gruppenregeln bekommen keine | Bei `AllDifferent` mit größerem Wertebereich muss nicht jeder Wert vorkommen | 88 |
| | Reihenfolge innerhalb von Stufe 3 offen | Nach Rang: einfache Streichungen, dann rechnende Kantenregeln, dann Thermometer und Summe, dann eigene Regeln | Entspricht dem menschlichen Vorgehen | 88 |
| | Solver-Lösung bei Verzweigung | Der Solver wird **höchstens einmal pro Erklärung** gefragt (`CountSolutions`, Limit 2), und nur wenn die Logik festsitzt. „Leicht“ ruft ihn nie auf. Bei mehrdeutigen Rätseln endet die Erklärung in der ersten Lösung | Ein Aufruf liefert Lösung und Eindeutigkeit zugleich | 90 |
| | Ergebnis: Liste von Schritten | Dazu sieben Status (u. a. `SolvedAmbiguous`, `SolvedUnverified`, `Stuck`), Zwischenstände alle 16 Schritte, Abbruch ohne Ausnahme | Navigation, Zeitlimit und Abbruch brauchen klare Ausgänge | 90–93 |
| | – | **Eigene Regeln:** Der Solver bestätigt eine ohne Raten erreichte Belegung. Eine falsch schließende eigene Regel führt zu „Interner Fehler“ statt zu einem falschen Ergebnis | Die Katalogregeln sind geprüft, eigene Regeln nicht | 96, 97 |
| | `Propagate` je Regel | In Paket 6 verschärft: Kantenregeln und Thermometer vollständig konsistent, `Sum` mit Tiefensuche pro Kandidat und **Suchbudget von 200 000 Schritten**. `AllDifferent` und `CoverRange` streichen bewusst nur festgelegte Werte | Nur sichere Schlüsse. Ein Test vergleicht sie mit vollständigem Durchprobieren | 84, 99 |
| | UI direkt über die Engine | Eigene Schnittstelle `IPuzzleExplainer`. Lösen und Erklären sperren sich nicht gegenseitig | Tests brauchen einen Ersatz für blockierende Läufe und Ausnahmen | 101, 110 |

### 2.5 Editor und Oberfläche

| | Plan | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| | Alle Regeln erst in Paket 5 | **Schon in Paket 3** sind alle acht Regeln wählbar, samt „Richtung umkehren“ und „Reihenfolge umkehren“ | Das Eigenschaften-Panel entsteht generisch aus `IRule.Parameters` | 56 |
| | Wertebereich „pro Knoten überschreibbar“ | Im Editor erst seit **Paket 5 (Editor-Teil)** bearbeitbar. Geladen, gespeichert und beachtet wurde er von Anfang an | Lücke aus Paket 3, nachgeholt | 56, 144 |
| | Öffnen | Ohne inhaltliche Prüfung (`validate: false`). Formatfehler werden weiter abgelehnt, inhaltliche Probleme stehen im Statusbereich | Unfertig gespeicherte Rätsel sollen sich weiter bearbeiten lassen | 57 |
| | Buttons „Lösen“ / „Eindeutigkeit prüfen“ | Eine **eigene Lösen-Leiste** mit fester Höhe und das Menü „Lösen“ (F5/F6), nicht in der Werkzeugleiste | Die Werkzeugleiste brach sonst schon bei 1280 px um | 76, 80 |
| | „Lösung optisch von Givens unterscheidbar“ | Lösungswerte blau, Givens schwarz und halbfett. Bei Mehrdeutigkeit zwei Lösungen abrufbar, Unterschiede orange. Eine **veraltete Lösung wird ausgeblendet**, nicht nur markiert | Eine alte Lösung neben neuen Givens wäre leicht falsch zu lesen | 71–73, 81 |
| | – | Das Zeitlimit wird **nicht** gespeichert | Kein Einstellungsspeicher, kein Scope Creep | 74 |
| | Kanten-Generator „im Editor“ | Als **modaler Dialog** mit Live-Vorschau. Das Ergebnis ist ein einziger Undo-Schritt. Die Schaltfläche heißt „Erzeugen …“ | Das Panel rechts gehört der Auswahl. Der kürzere Text verhindert den Umbruch der Werkzeugleiste | 133–135, 145 |
| | Gruppen „farbig hervorheben“ | Die Umrisse aller Gruppen werden zusammengefasst. Geteilte Grenzen erscheinen grau und umso kräftiger, je mehr Gruppen sie teilen. Fläche bekommen nur die gewählte Gruppe und Gruppen mit eigener Farbe | Bei der Vorlage übermalten sich 27 Umrisse, Blöcke waren nicht zu erkennen. **Folge:** Ein Käfig ohne eigene Farbe ist ohne Auswahl nur als Linienverstärkung zu sehen | 61 |
| | – | Zielsummen von `Sum`-Gruppen erscheinen auf der Zeichenfläche. **Gruppenbeschriftungen werden nicht gezeichnet** | Die Vorlage hätte drei Beschriftungen pro Zelle | 141 |
| | – | Das helle Theme ist fest eingestellt | Zeichenfläche und Bedienelemente passen so zusammen | 59 |
| | Thermometer-Reihenfolge, LessThan-Richtung | **Kein neuer Bedienweg:** Die Reihenfolge ergibt sich aus der Auswahl (Shift-Klick, Nummern an den Knoten), dazu „Reihenfolge umkehren“. Die Richtung von „Kleiner als“ ist die Ziehrichtung. Einzelne Knoten lassen sich nicht umsortieren | Die Bedienung aus Paket 3 deckte die Anforderung schon ab | 140 |
| ★ | Paket 7: „Darstellung umschaltbar, Speicherformat bleibt kompatibel“ | Die Zelldarstellung ist eine reine Sitzungseinstellung. Sie steht **nicht in der Datei** und wird **nicht als App-Einstellung gespeichert**. Nach einem Neustart erscheinen wieder Kreise | Ein neues Feld in der Datei würde ältere Programmstände brechen. Ein Einstellungsdienst wäre größer als das Paket selbst | 152 |
| | Umschalter | Im Menü „Ansicht“ und unten rechts im Statusbereich, nicht in der Werkzeugleiste | Sonst bricht die Werkzeugleiste bei 1280 px um | 153 |
| | – | In der Zelldarstellung erscheinen Kanten als **Stege über den Zellrändern**. Rahmenauswahl auf Zellen per **Strg/Cmd + Ziehen** (neue Geste, gilt in beiden Darstellungen). Beim Verschieben rastet der Mittelpunkt der gepackten Zelle ein | Zellen füllen die Fläche lückenlos, ohne diese Lösungen wären Kanten und Rahmen nicht erreichbar | 154–159 |
| | Rückfrage bei ungespeicherten Änderungen | Ein Schließen des Dialogs ohne Schaltfläche gilt seit dem Abschluss-Review als „Abbrechen“. Vorher wurde es als „Speichern“ gewertet | Fehler im Abschluss-Review gefunden | 165 |

### 2.6 Beispielrätsel

| | Plan | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| ★ | Killer-Sudoku | **Mit 3 Givens**, nicht ohne | Mit dem Kriterium „ohne Verzweigung erklärbar“ fand sich in 40 Versuchen keine Variante ohne Givens. Der Erklär-Modus kennt keine Killer-Techniken wie die 45er-Regel | 127 |
| ★ | „ein Rätsel auf Dreiecksgitter“ (Konzept nennt Tridoku) | **Tridoku-ähnliches Dreiecks-Sudoku mit eigenen Regeln**: 9 Regionen, 3 Außenseiten, 168 `NotEqual`-Kanten zwischen sich berührenden Dreiecken verschiedener Regionen, 33 Givens. **Kein originalgetreues Tridoku** | Die zusätzlichen Linienregeln des Originals wurden bewusst nicht nachgebaut. Die Regeln stehen vollständig in der Datei und sind vom Solver geprüft | 130 |
| | „lösbar und eindeutig“ | Strenger: Die fünf Variantenrätsel sind **ohne Verzweigung erklärbar** | Sie sollen den Erklär-Modus vorführen. Folge: etwas mehr Givens als für bloße Eindeutigkeit nötig | 126 |
| | „Sudoku mit Kropki-Punkten“ | Alle Punkte der Lösung, **keine negative Bedingung** (ein fehlender Punkt sagt nichts aus), 2 Givens | Die negative Bedingung war nicht verlangt und wäre nur über viele Zusatzkanten ausdrückbar | 128 |
| | Hexadoku „0–F bzw. 1–16“ | Werte 0–15 (Anzeige 0–F), Zeilen, Spalten und Blöcke als `CoverRange` | So kommt `CoverRange` in einem Beispiel vor | 129 |
| | – | **Zusätzlich** ein Thermo-Sudoku 6×6 | Damit kommen alle acht Katalogregeln in den Beispielen vor | 131 |
| ★ | Akzeptanz Paket 5: „Jedes Beispielrätsel ist lösbar und eindeutig“ | **Ausnahme `klassisch-leer.json`**: eine bewusst mehrdeutige Vorlage ohne Givens. Die Tests erwarten für sie ausdrücklich „mehrdeutig“ | Sie dient als mehrdeutiges Testrätsel für Paket 2 und 6 und in App-Tests | 160 |
| | – | Erzeugt wurden die Beispiele einmalig mit **Testcode** (`ExamplePuzzleGenerator`, über eine Umgebungsvariable gesteuert). Abgelegt sind statische Dateien | Der normale Testlauf erzeugt nichts, er lädt und prüft nur | 125 |
| | – | Die drei klassischen Rätsel stammen aus bekannten Quellen (Wikipedia-Beispiel, Project Euler 96, Arto Inkala). Unlösbare und mehrdeutige Testfälle stehen nur in den Tests | Lösungen und Einstufung wurden unabhängig vorab geprüft | 42 |

### 2.7 CI und Repo

| | Plan | Umgesetzt | Grund | Nr. |
| --- | --- | --- | --- | --- |
| ★ | Gitea-Actions-CI | **`actions/upload-artifact@v3`** statt v4, dazu `checkout@v4`, `setup-dotnet@v4` und `cache@v4` | Ältere Gitea-Versionen unterstützen v4 nicht | 9 |
| | „ohne manuelle Schritte“ auf Ubuntu | Die CI installiert bei Bedarf `libfontconfig1` (mit `sudo`, wenn nötig). Die frühere Aussage „keine Systempakete nötig“ ist überholt | SkiaSharp ist gegen `libfontconfig.so.1` gelinkt | 48 (überholt 9) |
| ★ | „Ein Paket ist erst fertig, wenn alle Tests grün sind“ | Die Angaben „alle grün“ der einzelnen Pakete wurden **auf macOS gemessen**. Auf Linux waren **zwei Rendering-Tests bis zum Abschluss-Review rot** (Subpixel-Textglättung). Behoben durch Graustufen-Text in der Testumgebung und eine robustere Pixelprüfung | Erst im Abschluss-Review wurde in einem Linux-Container nachgestellt | 164, 169 |
| ★ | CI läuft auf dem Ubuntu-Runner | **Auf dem echten Gitea-Runner ist die CI nie gelaufen** (nichts gepusht). Die Schritte wurden in Docker nachgestellt (Debian-Image, Actions emuliert) | Kein Zugriff auf den Runner | 164, 169 |
| ★ | Lizenz nicht festgelegt | **Proprietäre `LICENSE`** („Alle Rechte vorbehalten“). `THIRD-PARTY-NOTICES.md` nennt alle Komponenten, auch die nativen OR-Tools-Bibliotheken (u. a. COIN-OR unter EPL-2.0) | Vorgabe des Projektinhabers. Die Lizenzzuordnung der nativen Komponenten wurde nicht juristisch geprüft | 6, 46 |

### 2.8 Prozess

| | Plan | Umgesetzt | Grund |
| --- | --- | --- | --- |
| ★ | „nach jedem Arbeitspaket … committen“ (kein Branch genannt) | Alle Commits liegen im **Branch `feature/initial-implementation`**. `main` steht unverändert auf `d81daa4` („Docs and instructions“). **Nichts ist gepusht** | Vorgabe des Auftrags (Feature-Branch, nicht pushen). So bleibt `main` bis zur Prüfung unverändert |
| ★ | „Arbeite die Arbeitspakete der Reihe nach ab“ | **Nicht streng der Reihe nach.** Paket 6 (Bibliothek) entstand parallel zu Paket 3/4. Paket 5 wurde geteilt: Der Core-Teil entstand parallel zur Oberfläche von Paket 6, der Editor-Teil folgte erst nach Paket 6 | Die Bibliothek des Erklär-Modus und der Generator hängen nur von Core ab und ließen sich unabhängig bauen. Die Tracks blieben in getrennten Worktrees, die Merges verliefen ohne Konflikte |
| | `docs/decisions.md` und `docs/progress.md` | Die parallelen Tracks schrieben eigene Notizdateien. Beim Merge gingen sie in `decisions.md` ein, die Nummern G1–G17 wurden zu Nr. 116–132 | Damit sich die parallelen Teams nicht gegenseitig in die Dokumente schreiben |
| | – | Zu jedem Paket gab es eine Review- und eine Fixer-Runde und am Ende ein Abschluss-Review | Qualitätssicherung über die Vorgaben der Spezifikation hinaus |
| | – | Die Bedienung am echten Desktop wurde **nicht von Hand geprüft**. Die Agenten hatten keinen Bildschirmzugriff, die App wurde auf macOS nur gestartet | siehe Abschnitt 5 |

---

## 3. Kleinere Anpassungen und Interpretationen

- **Ids:** positiv, getrennte Zähler für Knoten, Kanten und Gruppen, Lücken erlaubt (nach dem Löschen). Neue Ids sind
  „größte + 1“, im Editor und im Generator mindestens 1 und ohne Überlauf (Nr. 15, 124, 149, 166).
- **Modell:** veränderliche Klassen für den Editor. Strukturvergleich über `PuzzleComparer`, nicht über `Equals`
  (Nr. 14).
- **`RuleParams`:** typisierte Zugriffe. `"1"` und `1.5` sind keine ganzen Zahlen (Nr. 17). `RuleRegistry` ist
  unveränderlich, Typnamen sind case-sensitiv (Nr. 21).
- **JSON:** `null` bei optionalen Feldern bedeutet „nicht gesetzt“. Kommentare und abschließende Kommas werden
  toleriert. Gespeichert wird ohne Prüfung, damit unfertige Rätsel gesichert werden können. Ausgabe mit LF, UTF-8
  ohne BOM, Umlaute unmaskiert. Einzelne UTF-16-Surrogate gelten als Formatfehler (Nr. 25, 26, 161).
- **Geometrie:** y zeigt nach unten. Das Dreieck (x, y) zeigt nach oben, wenn x + y gerade ist. Sechsecke mit spitzer
  Oberseite und axialen Koordinaten. „Diagonal“ heißt gemeinsame Ecke (Dreieck 12, Sechseck 6). Freier Abstand mit
  Toleranz 1e-6. Nachbarn ohne int-Überlauf (Nr. 27–30, 162).
- **Vorlage „Klassisch 9×9“:** Knoten zeilenweise. Gruppen 1–9 sind Zeilen, 10–18 Spalten, 19–27 Blöcke. Givens als
  81 Zeichen, `0` oder `.` für leer (Nr. 33).
- **Hexadoku:** A–F erscheint, wenn der Rätselbereich genau 0–15 ist, auch bei eigenem Knotenbereich innerhalb 0–15.
  Gespeichert werden Zahlen (Nr. 32, 150).
- **Meldungen:** überall deutsch. Zeiten im deutschen Format („300 ms“, „1,5 s“, nie „0 s“), ohne ICU-Kulturdaten
  (Nr. 45, 123).
- **Undo/Redo:** Command-Muster mit `EditHistory`. Gesten und mehrstellige Eingaben ergeben je einen Schritt. Kein
  Undo-Schritt für Auswahl, Sichtbarkeit von Gruppen und Zoom/Pan. Neu, Öffnen und Vorlage starten einen leeren
  Verlauf (Nr. 50).
- **Auswahl:** exklusiv, also Knoten *oder* Kante *oder* Gruppe. Die Rahmenauswahl ordnet nach Gitterzeile
  (Nr. 51, 65).
- **Tastatur:** Ziffern ergeben mehrstellige Givens, mit A–F bei 0–15 und „-“ für negative Werte. ⌫ löscht Givens,
  Entf (Mac: fn+⌫) löscht die Auswahl. Diese Tasten wirken auch bei Fokus in Liste oder Panel. Der Präfixtest ist
  arithmetisch, ohne Aufzählen der Werte (Nr. 52, 64, 167).
- **Gesten:** Knoten malen mit Interpolation. Auf dem Dreiecksgitter wird nur um gerade Versätze verschoben, nie auf
  belegte Punkte. Leertaste + Ziehen verschiebt die Ansicht und endet auch bei Fokusverlust (Nr. 53, 168).
- **Löschen:** Ein gelöschter Knoten nimmt seine Kanten mit. Gruppen, die dadurch leer werden, verschwinden ebenfalls
  (Nr. 55).
- **Darstellung:** Gruppen ohne eigene Farbe bekommen eine Palettenfarbe, die nicht gespeichert wird. Gewählte Kanten
  sind deutlicher hervorgehoben. Kantenmarken liegen über den Knoten und bei langen Kanten im freien Stück.
  „Ungleich“-Linien sind dünner und heller (Nr. 59, 62, 142, 147).
- **Bedienelemente:** Das Panel zeigt deutsche Parameterbezeichnungen und Hinweise. Die Werkzeugleiste bricht um.
  Der XAML-Wert `PlaceholderText` ersetzt den veralteten `Watermark` (Nr. 68, 69, 151).
- **Einpassen:** Eine eingepasste Ansicht wird bei Größenänderung neu eingepasst, aber nicht während einer Geste
  (Nr. 77, 79).
- **Erklär-Modus:** Position n zeigt den Zustand *nach* n Schritten. Givens sind schwarz, hergeleitete Werte blau,
  geratene orange. Der aktuelle Schritt ist gelb, die Begründung grün. Ein Knoten ohne Kandidaten bekommt einen roten
  Ring. Das Kandidatenraster hat 3 × 3 bzw. 4 × 4 Felder. Sehr kleine Knoten zeigen nur den gestrichenen Wert. Die
  Pfeiltasten wirken auch bei Fokus auf Schaltflächen (Nr. 104–109, 112–115).
- **Statuszeile:** passende Grundmeldung nach dem Laden statt der festen Startmeldung (Nr. 143).
- **Zielsummen:** Auf dem Sechseckgitter liegen sie außerhalb des Knotenkreises (Nr. 148).
- **Code-Stil-Analyse:** `EnforceCodeStyleInBuild` bleibt aus. Toter Code wurde von Hand entfernt (Nr. 168).
- **Testrätsel für Zeitlimit und Abbruch:** ein unlösbares Färbungsproblem auf dem Mycielski-Graphen M6 (Nr. 43).

---

## 4. Bekannte Einschränkungen und offene Punkte

Stand nach dem Abschluss-Review. Was später behoben wurde, fehlt hier.

**Nicht verifiziert**

- Auf dem echten Gitea-Runner (`ubuntu-latest`) ist die CI noch nie gelaufen. Nachgestellt wurde sie nur im
  Debian-basierten SDK-Container.
- Die Bedienung am echten Desktop (Maus, Tastatur, Fokus, Dialoge) wurde nicht von Hand getestet. Die App wurde auf
  macOS nur gestartet. Windows und ein Linux-Desktop wurden gar nicht ausprobiert.
- Stufe 0 des Erklär-Modus (Nr. 95) wartet noch auf die Bestätigung durch den Projektinhaber.
- Die Lizenzen der nativen OR-Tools-Komponenten wurden nach den Projektangaben zugeordnet und nicht juristisch
  geprüft (Nr. 46).

**Funktionale Lücken (nicht gefordert oder bewusst offen)**

- Prüfmeldungen und Fehler eines ungültigen Rätsels erscheinen nur als Text. Das betroffene Element wird auf der
  Zeichenfläche nicht markiert.
- Es gibt keine App-Einstellungen: Zeitlimit und Darstellung (Kreise oder Zellen) gelten nur für die Sitzung.
- Ein Fortschritt in Prozent fehlt beim Lösen, es läuft nur ein Balken. Der Modellaufbau vor der Suche lässt sich
  nicht abbrechen.
- Der Erklär-Modus kennt nur die Techniken der Spezifikation, deshalb braucht „mittel“ drei Verzweigungen. Es gibt
  keinen Sprung zur nächsten Verzweigung, und die Knoten von `Contradiction` werden nicht eigens hervorgehoben.
- `Sum`-Propagation: Ist das Suchbudget erschöpft, sind die Schlüsse unvollständig (aber weiterhin sicher). Innerhalb
  eines einzelnen `Propagate`-Aufrufs wird das Abbruch-Token nicht geprüft.
- Gruppenbeschriftungen werden nicht gezeichnet. Knoten mit eigenem Wertebereich sind auf der Zeichenfläche nicht
  markiert. Eine bestehende Gruppe lässt sich nur umkehren, nicht umsortieren. Kopieren und Einfügen gibt es nicht.
- Für den Wechsel der Darstellung gibt es kein Tastenkürzel.
- `Puzzle.NextNodeId/NextEdgeId/NextGroupId` in Core können bei ungeprüften Dateien mit Ids ≤ 0 oder `int.MaxValue`
  weiterhin unbrauchbare Ids liefern. Editor und Generator umgehen das mit eigenen Prüfungen (Nr. 124, 149, 166).
  Betroffen wären nur künftige Aufrufer.

**Darstellung und Bedienung**

- Bei Mindestgröße (900 × 560) ist die Zeichenfläche im Erklär-Modus nur etwa 245 px hoch, Kandidaten sind dann
  nicht zu sehen. Beim Hexadoku steht bei 1280 × 800 ohne Zoom nur die Anzahl „(n)“ im Knoten.
- Lange Meldungen der Lösen-Leiste werden nach zwei Zeilen gekürzt, der Volltext steht im Tooltip.
- Ein Käfig ohne eigene Farbe ist in einem Sudoku ohne Auswahl nur als Linienverstärkung zu sehen (Nr. 61).
- Haben dieselben Knoten Kanten mit Marke zum direkten und zum übernächsten Nachbarn, liegen beide Marken
  übereinander (Nr. 147).
- Zelldarstellung:
  - Die Rahmenauswahl auf Zellen braucht Strg/Cmd.
  - Kantenstege im Dreiecks-Sudoku sind kleine Ziele (etwa 3–4 px).
  - An den Gitterecken entstehen kleine Sterne aus sich kreuzenden Stegen.
  - „Kleiner als“-Pfeile sind klein.
  - Marken langer Kanten auf Dreiecks- und Sechseckgittern liegen nicht zwingend genau auf einem Zellrand.
- Auf dem Mac löscht ⌫ Givens. Knoten löscht man mit fn+⌫.

**Technisch**

- Die fünf Generator-„Tests“ (`ExamplePuzzleGenerator`) erscheinen im normalen Lauf als bestanden, tun dort aber
  nichts (Nr. 125).
- Die Zeitlimit- und Abbruchtests setzen voraus, dass CP-SAT das M6-Färbungsrätsel nicht schnell widerlegt. Eine
  künftige OR-Tools-Version könnte das ändern (Nr. 43).
- TRX-Dateinamen enthalten einen Zeitstempel. Rein theoretisch könnten sich zwei Testprojekte, die in derselben
  Sekunde fertig werden, gegenseitig überschreiben. Beobachtet wurde das nicht.

---

## 5. Qualitätssicherung

### Tests pro Projekt (Stand `2b83b1e`)

| Projekt | Tests | Schwerpunkte |
| --- | --- | --- |
| `GraphDoku.Core.Tests` | 244 | Modell, Validierung, JSON-Round-Trip (alle Dateien in `puzzles/`), Regeln und Propagation (Abgleich mit vollständigem Durchprobieren), Geometrie, Kanten-Generator auf allen drei Gittern |
| `GraphDoku.Solver.Tests` | 105 | drei klassische Sudokus mit unabhängig geprüfter Lösung, unlösbar, mehrdeutig, je Regel positive und negative Fälle mit Durchprobieren, Zeitlimit und Abbruch, jedes Beispielrätsel lösbar und eindeutig |
| `GraphDoku.Explainer.Tests` | 82 | „leicht“ ohne Verzweigung, Konsistenz jedes Schritts mit der Lösung, Verzweigungen, Widerspruch, mehrdeutig, ungültig, Abbruch, jedes Beispielrätsel (5 der 82 sind die Generator-Facts) |
| `GraphDoku.App.Tests` | 348 | ViewModels (Undo/Redo je Bearbeitung, Gesten, Givens, Dateien, Lösen, Erklären, Generator, Zellformen), Akzeptanztests (Sudoku im Editor bauen, speichern, öffnen; jedes Beispiel lösen und prüfen; „leicht“ Schritt für Schritt erklären), headless Fenster- und Rendering-Tests mit Pixelprüfung, Architekturtests |
| **Summe** | **779** | alle grün, Release, macOS und Linux-Container |

### Review-Runden pro Paket

| Paket | Befunde | davon schwer | vom Fixer behoben¹ | abgelehnt bzw. zurückgestellt |
| --- | --- | --- | --- | --- |
| 1 | 10 | 0 | 7 | 1: strukturierter Meldungstyp für Prüfmeldungen (noch offen) |
| 2 | 7 | 0 | 4 | 1 teilweise: die Zählmeldung zeigte nie „0 s“ |
| 3 | 10 | 3 | 10 | – |
| 4 | 5 | 1 | 5 | – |
| 5 (Core) | 3 | 0 | 3 | 1: Käfigsummen auf der Zeichenfläche, an den Editor-Teil übergeben und dort erledigt (Nr. 141) |
| 5 (Editor) | 5 | 0 | 5 | – |
| 6 (Bibliothek) | 8 | 0 | 8 | – |
| 6 (Oberfläche) | 7 | 0 | 6 | 1 teilweise: der Auswahlring ersetzt den Hervorhebungsring, das ist gewollt (Nr. 105) |
| 7 | 6 | 2 | 4 | 1 teilweise: die Sterne an den Dreiecksecken bleiben (Nr. 158) |
| Abschluss-Review | 4 schwer, 12 kleinere | 4 (2 verschiedene) | alle schweren, dazu die in Nr. 160–169 genannten kleineren | – |

¹ Gezählt sind die Einträge des Fixers. Doppelte Befunde (etwa dreimal „`null` bei optionalen Feldern“ in Paket 1)
fasste er zu einem Eintrag zusammen.

Schwere Befunde der Pakete waren unter anderem:

- falsche Undo-Schritte beim Gruppenwechsel und ein Absturz bei doppelten Gruppen-Ids (Paket 3)
- nicht erkennbare Blockgrenzen (Paket 3)
- eine bei Mindestgröße zu kleine Zeichenfläche (Paket 4)
- in der Zelldarstellung keine Rahmenauswahl im gefüllten Gitter, und Kantenstege fingen etwa 28 % der Klicks in
  Zellen (Paket 7)

Im Abschluss-Review waren es die roten Linux-Rendering-Tests und der Dialog „ungespeicherte Änderungen“, dessen
Schließen ohne Schaltfläche als „Speichern“ zählte.

### Linux-CI

Die Schritte aus `ci.yml` liefen in `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.403, linux/amd64) auf einem Export
von `2b83b1e`:

- `libfontconfig1` wurde nachinstalliert.
- Restore ohne Fehler, Release-Build mit 0 Warnungen.
- 779/779 Tests grün, vier TRX-Dateien.
- Die gerenderten PNGs zeigen lesbaren Text.
- `ci.yml` ist syntaktisch gültiges YAML.

Vor Nr. 164 waren dort zwei Rendering-Tests rot (App 316/318).

### Was nicht verifiziert wurde

- **Der echte Gitea-Runner:** Die Actions (`checkout`, `setup-dotnet`, `cache`, `upload-artifact@v3`) wurden nur
  emuliert. Ubuntu statt Debian, `sudo` und Cache sind noch nicht belegt.
- **Echte Desktop-Bedienung:** Das Verhalten ist über ViewModel-Tests und headless Tastatur- und Mauseingaben im
  echten Hauptfenster belegt, das Aussehen über headless gerenderte Bilder (Skia), die während der Entwicklung
  angesehen wurden. Ein Mensch hat die App nicht bedient.
- **Andere Betriebssysteme:** Windows und ein Linux-Desktop wurden nicht ausprobiert.
- **Leistung großer Rätsel:** Laufzeiten wurden nur für die Beispielrätsel gemessen (Erklärung höchstens etwa
  190 ms beim Hexadoku, `progress.md` Paket 5).

---

## 6. Nächste Schritte

1. **Prüfen und pushen:** Diese Zusammenfassung und `decisions.md` durchsehen. Stufe 0 des Erklär-Modus (Nr. 95)
   bestätigen oder verwerfen. Dann `feature/initial-implementation` pushen und den ersten Lauf auf dem Gitea-Runner
   beobachten (fontconfig/`sudo`, `upload-artifact@v3`, OR-Tools nativ auf linux-x64).
2. **Von Hand testen:** Die App auf macOS und, wenn möglich, Windows und Linux bedienen, vor allem Maus, Fokus,
   Tastenkürzel, Dialoge und die Zelldarstellung im Dreiecks-Sudoku.
3. **Nach `main` übernehmen** (Pull Request), sobald CI und Handtest grün sind. Den liegengebliebenen Worktree
   `../GraphDocu-try` (losgelöster HEAD `812029f`) aufräumen.
4. **Später, laut Spezifikation:** einen eigenen Lern-Solver als zweite `ISolver`-Implementierung (die Schnittstelle
   ist dafür ausgelegt, Nr. 35/163). Optional: Prüfmeldungen auf der Zeichenfläche markieren und einen
   Einstellungsspeicher für Zeitlimit und Darstellung anlegen.
