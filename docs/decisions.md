# Entscheidungen und Abweichungen

Dieses Dokument hält Entscheidungen fest, die die Spezifikation (`docs/spec/`) präzisieren oder von ihr abweichen –
jeweils mit Grund. Neue Einträge kommen ans Ende des jeweiligen Abschnitts.

## Umgebung und Build (Paket 1)

1. **.NET 10 (LTS)**, `net10.0`. `global.json` verlangt SDK `10.0.100` mit `rollForward: latestFeature`, damit jedes
   10.0-Feature-Band (z. B. lokal 10.0.401) funktioniert und die CI über `setup-dotnet` das aktuelle 10.0-SDK holt.
2. **Central Package Management** (`Directory.Packages.props`) und gemeinsame Einstellungen in `Directory.Build.props`:
   Nullable, ImplicitUsings, `LangVersion latest`, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`.
   - NuGet-Sicherheitswarnungen (NU1901–NU1904) bleiben Warnungen, damit eine neu veröffentlichte Schwachstelle in
     einem transitiven Paket nicht ohne Codeänderung den Build bricht.
   - In Testprojekten sind CA1707 (Unterstriche in Testnamen), CA2007, CA1861 und CA1859 abgeschaltet (`.editorconfig`).
   - `tests/Directory.Build.props` bindet xUnit, Test-SDK und FluentAssertions für alle Testprojekte ein.
3. **`nuget.config` im Repo** mit nur `nuget.org` und Package Source Mapping. Grund: Auf dem Entwicklungsrechner ist
   eine weitere Paketquelle konfiguriert; mit Central Package Management führt das zu NU1507 (als Fehler). Außerdem
   macht es Restore lokal und in der CI reproduzierbar.
4. **Solution im klassischen `.sln`-Format** (`dotnet new sln --format sln`), weil .NET 10 standardmäßig `.slnx` erzeugt,
   die Spezifikation aber `GraphDoku.sln` nennt.
5. **Paketversionen:** Google.OrTools 9.15.6755, Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent /
   Avalonia.Fonts.Inter 12.1.5, CommunityToolkit.Mvvm 8.4.2, xunit 2.9.3, xunit.runner.visualstudio 3.1.5,
   Microsoft.NET.Test.Sdk 18.11.0, FluentAssertions 7.2.2. Alle wurden auf macOS arm64 erfolgreich wiederhergestellt.
   xunit.runner.visualstudio bleibt auf der 3.x-Linie (aktuellste Version für xUnit 2); 4.x ist eine neue Hauptversion,
   die hier keinen Vorteil bringt.
6. **FluentAssertions nur 7.x:** Ab Version 8 steht FluentAssertions unter einer kommerziellen Lizenz (Xceed). Dieses
   Repository ist proprietär; Version 7.2.2 steht unter Apache-2.0. Ein Upgrade auf 8.x ist ausgeschlossen.
7. **Kein Avalonia.Headless.XUnit (Abweichung von der Teamplanung):** Avalonia.Headless.XUnit 12.1.5 setzt
   `xunit.v3.extensibility.core` 3.2.2 voraus und passt damit nicht zu xUnit 2.9. Die App-Tests prüfen deshalb nur
   ViewModels (ohne Avalonia-Typen). Werden später echte Headless-UI-Tests gebraucht, wird `GraphDoku.App.Tests` auf
   xUnit v3 umgestellt.
8. **Avalonia-Build-Telemetrie abgeschaltet:** Das transitive Paket `Avalonia.BuildServices` sendet beim Build anonyme
   Nutzungsdaten. `Directory.Build.targets` ersetzt dessen Ziel `AvaloniaStats` durch ein leeres Ziel (geprüft: die
   Aufgabe läuft danach nicht mehr); die CI setzt zusätzlich `AVALONIA_TELEMETRY_OPTOUT=1`. Grund: proprietäres
   Projekt, keine Netzwerkfunktionen gewünscht.
9. **CI (Gitea Actions):** `actions/checkout@v4`, `actions/setup-dotnet@v4` (Version aus `global.json`),
   `actions/cache@v4` für NuGet (Schlüssel aus `Directory.Packages.props`, allen `.csproj`, `nuget.config` und
   `global.json`; `NUGET_PACKAGES` liegt dafür im Workspace). Für das Hochladen der TRX-Dateien wird
   **`actions/upload-artifact@v3`** statt v4 verwendet, weil v4 von älteren Gitea-Versionen nicht unterstützt wird.
   Auf Linux sind keine zusätzlichen Systempakete nötig (die App-Tests starten keine Oberfläche; OR-Tools bringt
   seine nativen Bibliotheken mit).

## Projektstruktur (Paket 1)

10. **Abhängigkeitsrichtung App → Explainer → Solver → Core** (die App darf zusätzlich direkt Solver und Core
    referenzieren). Die Spezifikation schreibt „App → Explainer, Solver → Core“; präzisiert, weil der Erklär-Modus für
    den Schritt „Verzweigung“ die OR-Tools-Lösung über `ISolver` braucht. Core referenziert kein anderes Projekt und
    keine UI-/Solver-Bibliothek. Abgesichert durch Architekturtests auf zwei Ebenen: Assembly-Verweise (was der Code
    tatsächlich benutzt) und `ProjectStructureTests` in `GraphDoku.App.Tests`, die die `ProjectReference`- und
    `PackageReference`-Einträge der .csproj-Dateien lesen (fallen auch auf, solange ein falscher Verweis noch ungenutzt
    ist). Erlaubte Pakete: Core und Explainer keine, Solver nur Google.OrTools.
11. **Zusätzliches Testprojekt `tests/GraphDoku.App.Tests`** für ViewModel-Tests (in der Spezifikation nicht
    aufgeführt). Grund: „UI-Logik gehört in ViewModels, damit sie testbar ist“; die CI deckt es von Anfang an ab.
12. Die Spezifikationsdokumente liegen unter **`docs/spec/`** (vorher `Docu/`).
13. In Paket 1 enthalten `GraphDoku.Solver` und `GraphDoku.Explainer` noch keinen Code. `GraphDoku.Solver` referenziert
    aber bereits Google.OrTools; `GraphDoku.Solver.Tests` enthält einen Rauchtest, der ein kleines CP-SAT-Modell löst
    (prüft, dass die native Bibliothek auf macOS und in der Linux-CI lädt). `GraphDoku.Explainer.Tests` enthält
    Architekturtests.

## Kernmodell (Paket 1)

14. **Veränderliche Modellklassen** (`Puzzle`, `Node`, `Edge`, `Group`) für den späteren Editor. Strukturgleichheit
    (für den Round-Trip-Test) liefert `PuzzleComparer.AreEqual/Differences` statt überschriebener `Equals`-Methoden
    (veränderliche Objekte mit Wertgleichheit wären in Hash-Sammlungen fehleranfällig). `Puzzle.Clone()` kopiert tief.
15. **Ids:** positive Ganzzahlen, getrennte Zähler für Knoten, Kanten und Gruppen (wie im JSON-Beispiel). Lücken sind
    erlaubt (z. B. nach dem Löschen); `NextNodeId()` usw. liefern „größte Id + 1“. Id ≤ 0 und doppelte Ids sind Fehler.
16. **`ValueDomain`** (`min`..`max`, inklusive) als unveränderlicher Record. Zusätzliche Grenze: höchstens
    **4096 Werte** pro Wertebereich (`ValueDomain.MaxSize`), damit Kandidatenmengen klein bleiben (Hexadoku braucht 16).
17. **`RuleParams`**: kleine Hülle um eine Zuordnung Name → `JsonElement` mit typisierten Zugriffen
    (`GetInt`, `GetIntOrNull`, `GetBool`, `GetString` mit Standardwerten; falscher Typ → `RuleParamsException` mit
    deutscher Meldung). Gleichheit unabhängig von der Reihenfolge über `JsonElement.DeepEquals`. Ganze Zahlen müssen
    JSON-Zahlen ohne Nachkommastellen im Int32-Bereich sein (`"1"` oder `1.5` sind ungültig).
18. **`IConstraint`** als gemeinsame Sicht auf Kanten und Gruppen (`Ref`, `Rule`, `Params`, `NodeIds`; bei Kanten
    `[From, To]`). `ConstraintRef` = Art + Id (z. B. „Kante 3“, „Gruppe 5“) – das ist die „Regel-Id“ der `Reason`.
19. **`IRule` liegt in Core** und hat neben Typname und Fähigkeiten aus der Spezifikation zusätzlich `DisplayName`
    (deutsch), `Kind` (`Edge`, `DirectedEdge`, `Group`, `OrderedGroup`), `MinNodes`, `Parameters`
    (Parameterbeschreibungen für Editor und Meldungen) und `CreateDefaultParams()`.
    - `Validate(constraint, puzzle)` prüft Parameter (unbekannte Namen, Pflichtparameter, Typen, Wertebereiche) und die
      regelspezifische Knotenanzahl. Strukturelle Prüfungen (existieren Knoten? passt die Regel zu Kante/Gruppe?)
      macht `PuzzleValidator`.
    - **`AddToModel` ist nicht Teil von `IRule`**: Die OR-Tools-Übersetzung kommt in Paket 2 als Adapter-Registry
      (Typname → Adapter) ins Solver-Projekt, damit Core frei von OR-Tools bleibt (von der Spezifikation so erlaubt).
    - **`Propagate(state, constraint)` verändert den Zustand nicht**, sondern liefert die Änderungen
      (`CandidateChange`: Knoten, Streichen/Setzen, Werte, `Reason`); der Aufrufer wendet sie mit
      `CandidateState.Apply` an. Interpretation von „streicht Kandidaten oder setzt Werte“: So kann der Erklär-Modus
      jede Änderung einzeln als Schritt protokollieren.
    - `Propagate` ist bereits in Paket 1 für alle acht Regeln einfach und korrekt (nur sichere Schlüsse) umgesetzt:
      festgelegte Werte streichen (AllDifferent, CoverRange, NotEqual, Sum ohne Wiederholung), Bereich einschränken
      (CoverRange), Kantenkonsistenz (Difference, Ratio, LessThan), Schrankenpropagation (Sum, Thermometer).
      „Hidden singles“ gehören nicht in `Propagate`, sondern in Stufe 2 der Erklär-Schleife (Paket 6). Paket 6 kann
      die Schlüsse verfeinern.
    - Ein `Propagate`-Ergebnis enthält jede Streichung (Knoten, Wert) höchstens einmal: Mehrere Ableitungsschritte
      einer Regel (z. B. Schranken und Wiederholungsverbot bei `Sum`) lesen denselben Zustand; `RuleBase` entfernt
      Doppelte, die erste Begründung bleibt. So protokolliert der Erklär-Modus keine Schritte ohne Wirkung.
20. **`CandidateState` liegt in Core** (die Spezifikation nennt ihn bei Paket 6), weil `IRule.Propagate` ihn braucht. Er
    kennt sein `Puzzle` (z. B. für den Standardbereich von `CoverRange`). Ein Given außerhalb des Wertebereichs ergibt
    eine leere Kandidatenmenge (Widerspruch).
21. **`RuleRegistry`** ist unveränderlich: `RuleRegistry.Default` enthält die acht Katalogregeln; weitere Regeln
    kommen über `registry.With(rule)` bzw. den Konstruktor hinzu, ohne Änderung am JSON-Format. Typnamen sind
    case-sensitiv.
22. **Auslegung der Regelparameter** (die Spezifikation nennt nicht überall Standardwerte):
    - `Difference`: `k` ist Pflicht (ganze Zahl ≥ 0), `mode` hat den Standard `exact`. Für neue Kanten schlägt der
      Editor `k = 1` vor (`RuleParameter.SuggestedValue`).
    - `Ratio`: `factor` ganze Zahl ≥ 1, Standard 2.
    - `Sum`: `target` ist Pflicht (Vorschlag für neue Gruppen: 10, damit `CreateDefaultParams()` gültige Parameter
      liefert); `allowRepeats = false` bedeutet zusätzlich paarweise verschiedene Werte.
    - `CoverRange`: `min` und `max` fallen einzeln auf den globalen Rätselbereich zurück (nicht auf eigene
      Knotenbereiche); die Knotenanzahl muss genau der Bereichsgröße entsprechen.
    - Mindestanzahl Knoten: `AllDifferent` 2, `Thermometer` 2, `Sum` 1, `CoverRange` 1 (plus Größenprüfung),
      Kantenregeln immer genau 2.
23. **Auslegung „Richtungsanforderung“ bei Kanten:** Jede Kante hat `from` und `to`; nur bei `DirectedEdge`-Regeln
    (`LessThan`) ist die Richtung bedeutsam (from < to). Geprüft wird: Kantenregeln nur an Kanten, Gruppenregeln nur
    an Gruppen (z. B. `LessThan` an einer Gruppe ist ein Fehler), keine Schleifen (from = to), keine doppelten Knoten
    in einer Gruppe. **Nicht** abgelehnt werden doppelte Kanten (redundant, aber harmlos; der Generator in Paket 5
    vermeidet sie) und mehrere Knoten an derselben Gitterposition.

## JSON-Format (Paket 1)

24. Gelesen wird über `JsonDocument`, geschrieben über `Utf8JsonWriter` (beides System.Text.Json) statt über
    automatische (De-)Serialisierung von DTOs. Grund: deutsche Fehlermeldungen mit Pfadangabe (z. B.
    `nodes[3]: Das Feld „x“ fehlt.`), strikte Ablehnung unbekannter Felder und eine feste Feldreihenfolge wie im
    Beispiel der Spezifikation.
25. Regeln beim Lesen:
    - `formatVersion` wird zuerst geprüft; fehlt sie oder ist sie ≠ 1, gibt es eine klare Meldung und sonst nichts.
    - Pflichtfelder: `grid`, `domain`; bei Knoten `id`, `x`, `y`; bei Kanten `id`, `from`, `to`, `rule`; bei Gruppen
      `id`, `nodes`, `rule`. Optional: `name` (Standard leer), `nodes`/`edges`/`groups` (Standard leer), `params`
      (Standard `{}`), `given`, Knoten-`domain`, `color`, `label`. `null` bei optionalen Feldern gilt als „nicht gesetzt“
      (auch bei `nodes`/`edges`/`groups`, `params` und Knoten-`domain`); `null` bei Pflichtfeldern ist ein Fehler.
    - Unbekannte Felder werden abgelehnt (dadurch auch ein Feld wie `solution`).
    - Kommentare und abschließende Kommas werden toleriert (Dateien sollen von Hand bearbeitbar sein).
    - `PuzzleSerializer.Deserialize/Load` prüft standardmäßig das ganze Rätsel mit `PuzzleValidator`
      (unbekannte Regeltypen, Parameter, Ids …); mit `validate: false` lässt sich das abschalten.
    - Alle Fehler werden gesammelt und gemeinsam in einer `PuzzleFormatException` gemeldet.
26. Regeln beim Schreiben: eingerückt, LF-Zeilenenden, UTF-8 ohne BOM, Umlaute unmaskiert, `params` immer
    geschrieben (ggf. `{}`), optionale Felder nur wenn gesetzt. Gespeichert wird **ohne** Prüfung, damit der Editor
    auch unfertige Rätsel sichern kann.

## Geometrie (Paket 1)

27. `GridGeometry` in Core. Weltkoordinaten mit y nach unten:
    - Quadrat: Mittelpunkt (x, y), Kantenlänge 1.
    - Dreieck: Kantenlänge 1, Zelle (x, y) zeigt nach oben, wenn (x + y) gerade ist; Mittelpunkt = Schwerpunkt bei
      (x/2, y·h + 2h/3) bzw. (x/2, y·h + h/3) mit h = √3/2. Kantennachbarn haben den Abstand 1/√3.
    - Sechseck: spitze Oberseite, axial q = x, r = y, Mittelpunkt (q + r/2, r·√3/2); Nachbarn haben Abstand 1.
28. Nachbarschaften: „orthogonal“ = gemeinsame Kante (Quadrat 4, Dreieck 3, Sechseck 6); „inkl. diagonal“ = gemeinsame
    Ecke (Quadrat 8, Dreieck 12, Sechseck 6 – im Sechseckgitter teilen Eckennachbarn immer auch eine Kante). Ein Test
    gleicht die Offsets mit den Zellumrissen (`GetCellVertices`) ab.
29. **Springerzug nur auf dem Quadratgitter**; auf Dreieck/Sechseck wirft `GetKnightMoves` eine
    `NotSupportedException` mit deutscher Meldung (Empfehlung: freien Abstand verwenden). Grund: Für diese Gitter gibt
    es keine allgemein übliche Definition.
30. **Freier Abstand** = Abstand der Mittelpunkte in Welteinheiten gleich d, mit Toleranz (Standard 1e-6).
31. Zusätzlich vorbereitet für Paket 3: `GetCellVertices` (Zellumrisse) und `ToNearestGridPoint` (Einrasten).
32. Hexadoku: Wertebereich 0–15; die Oberfläche soll 10–15 als A–F anzeigen, wenn der Bereich genau 0–15 ist. Das
    Speicherformat bleibt numerisch.

## Vorlage und App (Paket 1)

33. `ClassicSudoku.Create(givens?, name?)`: Name „Klassisch 9x9“, Knoten 1–81 zeilenweise mit x = Spalte, y = Zeile;
    Gruppen 1–9 „Zeile n“, 10–18 „Spalte n“, 19–27 „Block n“ (Blöcke zeilenweise von links oben), alle `AllDifferent`.
    Givens als 81 Zeichen: `1`–`9`, `0` oder `.` für leer, Leerraum wird ignoriert.
34. Die App zeigt in Paket 1 ein Platzhalterfenster (Titel, Hinweis, Liste der registrierten Regeln, Knopf
    „Klassisches 9×9 erzeugen“). MVVM über CommunityToolkit.Mvvm (partielle `[ObservableProperty]`-Eigenschaften,
    `[RelayCommand]`), kompilierte Bindungen sind Standard.

## Solver (Paket 2)

35. **`ISolver` ist synchron und blockierend**, asynchron wird über die Erweiterungsmethoden `SolveAsync` und
    `CountSolutionsAsync` (`SolverExtensions`, `Task.Run`) gearbeitet. Grund: Ein späterer eigener Lern-Solver muss nur
    zwei einfache Methoden implementieren und bekommt die asynchrone Nutzung in der App geschenkt. Die asynchronen
    Varianten kopieren das Rätsel vor dem Start (`Puzzle.Clone()`), damit Bearbeitungen im Editor die laufende Suche
    nicht stören, und liefern bei Abbruch ein Ergebnis mit Status `Cancelled` statt einer `OperationCanceledException`.
    Die Schnittstelle verwendet nur Core-Typen.
36. **Signatur erweitert gegenüber der Spezifikation** („`Solve(puzzle)` → Lösung oder unlösbar“): `Solve(puzzle,
    options, token)` liefert `SolveResult` mit den Status `Solved`, `Unsolvable`, `Timeout`, `Cancelled` und
    `Invalid`; `CountSolutions(puzzle, limit = 2, options, token)` liefert `SolutionCountResult` mit `Count` (höchstens
    `limit`), Status `Completed`/`Timeout`/`Cancelled`/`Invalid` und bis zu zwei Beispiellösungen. `IsUnique` gilt nur
    nach vollständiger Suche (bei `limit` = 1 also nie), `IsAmbiguous` schon, sobald zwei Lösungen gefunden sind (auch
    nach Zeitlimit). Lösungen sind `IReadOnlyDictionary<int, int>` (Knoten-Id → Wert).
37. **Ungültige Rätsel werfen keine Ausnahme**, sondern liefern `Invalid` mit den deutschen Meldungen des
    `PuzzleValidator` (unbekannte Regeltypen, Parameter, Ids …). Das gilt auch für einen **Given außerhalb des
    (eigenen oder globalen) Wertebereichs** – der Validator meldet ihn bereits als Fehler, deshalb nicht „unlösbar“.
    Ein Given innerhalb eines eigenen Knotenbereichs, der über den globalen Bereich hinausgeht, ist gültig.
    Programmierfehler (null-Argumente, `limit` < 1, Zeitlimit ≤ 0) werfen dagegen `ArgumentException`s.
38. **`SolverOptions`** enthält nur das Zeitlimit (`TimeLimit`, Standard 30 s, Wanduhr). Weitere CP-SAT-Parameter
    (Worker-Anzahl, Seed) werden bewusst nicht nach außen gegeben (kein Scope Creep). Lösen nutzt die
    Standard-Worker-Anzahl von CP-SAT; Zählen nutzt `enumerate_all_solutions` mit einem Worker.
39. **OR-Tools-Übersetzung als Adapter-Registry** (`OrToolsAdapterRegistry`, Typname → `IOrToolsRuleAdapter`), analog
    zur `RuleRegistry` in Core. Eine Regel, die in der `RuleRegistry` steht, aber keinen Adapter hat, ergibt `Invalid`
    mit Meldung („Gruppe 1: Der Solver kann die Regel „X“ nicht übersetzen …“). `OrToolsModelBuilder` ist öffentlich,
    damit das Modell ohne Lösen geprüft werden kann.
40. **Übersetzung der Regeln:**
    - `AllDifferent`: `AddAllDifferent`.
    - `CoverRange`: jeder Knoten in [min, max] (Standard: Rätselbereich, wie in Core) plus `AddAllDifferent`. Da der
      Validator Knotenanzahl = Bereichsgröße erzwingt, folgt daraus „jeder Wert genau einmal“; eine zusätzliche
      Zählbedingung pro Wert ist nicht nötig.
    - `Sum`: lineare Gleichung; mit `allowRepeats = false` zusätzlich `AddAllDifferent`.
    - `Thermometer`: `v[i] < v[i+1]` in Listenreihenfolge. `LessThan`: `from < to`. `NotEqual`: `a ≠ b`.
    - `Difference`: Hilfsvariable `d = |a − b|` (`AddAbsEquality`), dann `d = k` bzw. `d ≥ k`.
    - `Ratio`: **Tabellen-Constraint** (`AddAllowedAssignments`) über alle erlaubten Wertepaare statt Reifikation mit
      Booleschen Variablen. Grund: Ohne freie Hilfsvariablen bleibt die Lösungsanzahl beim Zählen exakt (bei
      `factor = 1` wären sonst beide Richtungs-Booleans wahr und dieselbe Belegung würde doppelt gezählt). Die Tabelle
      ist klein (Wertebereiche ≤ 4096). Gibt es kein erlaubtes Paar, wird das Modell unerfüllbar gemacht.
    - Alle Hilfsvariablen sind durch die Knotenwerte eindeutig bestimmt; Tests vergleichen die gezählten Lösungen
      mit vollständigem Durchprobieren.
41. **Abbruch:** Der Solver verwendet `SolveWrapper` direkt statt `CpSolver`. Grund: `CpSolver` legt seinen nativen
    Wrapper erst in `Solve` an und gibt ihn danach frei; ein `StopSearch` kurz vor dem Start ginge verloren, eines
    kurz nach dem Ende könnte einen freigegebenen Wrapper treffen. Mit eigenem `SolveWrapper` wird die
    Token-Registrierung (ruft `StopSearch`) vor dem Wrapper aufgelöst; ein `StopSearch` vor dem Start wirkt
    (lokal geprüft: Status sofort `Unknown`). `Unknown` wird bei angefordertem Abbruch als `Cancelled`, sonst als
    `Timeout` gemeldet; eine bereits gefundene Lösung wird auch bei gleichzeitigem Abbruch zurückgegeben.
42. **Beispielrätsel** `puzzles/klassisch-leicht.json` (das bekannte Beispiel-Sudoku aus der Wikipedia, 30 Givens,
    nur mit „naked/hidden single“ lösbar – Voraussetzung für die Akzeptanz von Paket 6), `klassisch-mittel.json`
    (Project Euler 96, Gitter 06, 24 Givens) und `klassisch-schwer.json` (Arto Inkala, 21 Givens). Die erwarteten
    Lösungen, die Eindeutigkeit und die Einstufung (Singles genügen / genügen nicht) wurden vorab mit einem
    unabhängigen Backtracking-Programm ermittelt und werden in den Tests zusätzlich mit einem eigenen
    Sudoku-Prüfer und einem Singles-Löser abgesichert. Unlösbares und mehrdeutiges Testrätsel sind Abwandlungen von
    „leicht“ und stehen nur in den Tests, nicht in `puzzles/`.
43. **Zeitlimit- und Abbruchtests** verwenden ein Färbungsrätsel auf dem Mycielski-Graphen M6 (47 Knoten,
    236 `NotEqual`-Kanten, 5 Farben): unlösbar, für CP-SAT aber schwer zu widerlegen. Der Zähl-Abbruch nutzt das leere
    klassische Sudoku mit unbegrenztem Limit.
