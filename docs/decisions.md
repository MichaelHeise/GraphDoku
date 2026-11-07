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
   ~~Auf Linux sind keine zusätzlichen Systempakete nötig (die App-Tests starten keine Oberfläche; OR-Tools bringt
   seine nativen Bibliotheken mit).~~ Überholt durch Nr. 47/48: Seit Paket 3 laufen headless Rendering-Tests, und die
   CI installiert dafür `libfontconfig1`. OR-Tools bringt seine nativen Bibliotheken weiterhin selbst mit.

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
44. **Lösungszählung zählt verschiedene Knotenbelegungen** (Review Paket 2). CP-SAT meldet mit
    `enumerate_all_solutions` jede Belegung *aller* Modellvariablen. Ein Adapter mit freien Hilfsvariablen (etwa eine
    Boolesche Variable mit `OnlyEnforceIf`) würde dieselbe Knotenbelegung mehrfach melden und ein eindeutiges Rätsel
    als mehrdeutig erscheinen lassen. Der Zähl-Callback verwirft deshalb Wiederholungen über das Wertetupel der
    Knoten (`HashSet` mit strukturellem Vergleich). Die mitgelieferten Adapter brauchen das nicht (siehe Nr. 40), die
    Zählung hängt so aber nicht mehr von Adapter-Interna ab. Die Empfehlung „Hilfsvariablen durch die Knotenwerte
    festlegen“ steht weiterhin in der Doku von `IOrToolsRuleAdapter` und im README (Laufzeit). Kosten: Der Callback
    merkt sich alle gezählten Belegungen; bei üblichen Limits (2 für die Eindeutigkeit) ist das vernachlässigbar.
45. **Zeitangaben in Solver-Meldungen** im deutschen Format mit Dezimalkomma. Ab 1 s in Sekunden („1,5 s“), darunter
    in Millisekunden („300 ms“, „0,0001 ms“), damit ein positives Zeitlimit nie als „0 s“ erscheint. Das Zahlenformat
    wird als eigenes `NumberFormatInfo` (Dezimalkomma) erzeugt statt über `CultureInfo("de-DE")`, damit die Texte
    auch ohne ICU-Kulturdaten (z. B. schlanke Linux-Container) gleich sind. (Vorher: invariante Kultur mit
    Dezimalpunkt, „0.3 s“.)
46. **Native Bibliotheken der OR-Tools-Runtime-Pakete** stehen in `THIRD-PARTY-NOTICES.md` mit ihren eigenen
    Lizenzen (u. a. COIN-OR unter EPL-2.0). Die nuspec nennt nur pauschal Apache-2.0. Die Lizenzen wurden nach den
    Projektangaben der Komponenten zugeordnet, nicht aus den Paketen selbst gelesen (die Pakete enthalten keine
    Lizenztexte der Komponenten); vor einer Auslieferung sollte das juristisch geprüft werden.

## Editor (Paket 3)

47. **Rendering-Rauchtest mit Avalonia.Headless statt Avalonia.Headless.XUnit** (ergänzt Nr. 7): `GraphDoku.App.Tests`
    referenziert `Avalonia.Headless` und `Avalonia.Skia` (12.1.5) und startet die Sitzung selbst
    (`HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp))`, `UseSkia()`,
    `UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`, `WithInterFont()`); alle
    Rendering-Tests teilen sich die Sitzung über eine xUnit-Collection-Fixture und laufen über `Session.Dispatch`
    auf deren Dispatcher-Thread. So bleibt xUnit 2.9. Die Tests öffnen das echte Hauptfenster mit geladenem Rätsel,
    holen das Bild mit `CaptureRenderedFrame()` und legen PNGs ab (Verzeichnis aus `GRAPHDOKU_RENDER_DIR`, sonst
    `<Temp>/GraphDoku-Render`). Geprüft werden einfache Pixelmerkmale (Schrift in einem Knoten mit Given, keine in
    einem leeren Knoten); die Bilder wurden während der Entwicklung angesehen und Darstellungsfehler behoben.
48. **CI: `libfontconfig1`** (ersetzt die Aussage zu Systempaketen in Nr. 9): `libSkiaSharp.so` aus
    `SkiaSharp.NativeAssets.Linux` ist gegen `libfontconfig.so.1` gelinkt (ELF-`NEEDED`-Eintrag geprüft). Die CI
    installiert das Paket per `apt-get`, falls es fehlt (mit `sudo`, wenn der Runner nicht als root läuft). Ein
    X-Server wird nicht gebraucht. Die Variante
    `SkiaSharp.NativeAssets.Linux.NoDependencies` wurde verworfen, weil sie dieselbe Datei wie das von Avalonia.Skia
    mitgebrachte Paket liefert.
49. **ViewModels ohne Avalonia-Typen:** Die Zeichenfläche `GraphCanvas` (eigenes `Control` mit `Render`-Override und
    `DrawingContext`) zeichnet nur und reicht Zeiger-, Rad- und Tastaturereignisse in Bildschirmkoordinaten an das
    `EditorViewModel` weiter (eigene Typen `ScreenPoint`, `PointerButton`, `EditorModifiers`). Zoom und Pan liegen in
    `Viewport` (ViewModel-Schicht), Treffertests und alle Gesten im ViewModel. Dateiauswahl und Dialoge laufen über
    `IFileDialogService`/`IDialogService` (Avalonia-StorageProvider bzw. kleine modale Fenster; in Tests ersetzt).
    Gearbeitet wird mit lokalen Pfaden (`TryGetLocalPath`); eine Datei ohne lokalen Pfad wird mit Meldung abgelehnt.
50. **Undo/Redo als Command-Muster:** `IEditCommand` (Execute/Undo/TryMerge) und `EditHistory` (Name statt
    „UndoStack“ wegen Analyseregel CA1711). Befehle: Knoten setzen, verschieben, Givens setzen/löschen, Elemente
    löschen, Kante/Gruppe anlegen, Kante/Gruppe/Rätsel ändern. Zusammengeführt wird über einen
    Zusammenführungsschlüssel (ein Objekt pro Geste bzw. Eingabefeld): ein Ziehen (Verschieben, Knoten malen), eine
    mehrstellige Given-Eingabe und fortlaufendes Tippen in einem Feld des Eigenschaften-Panels ergeben je einen
    Schritt. Die Dirty-Anzeige vergleicht Zustands-Ids, stimmt also auch nach Undo/Redo zurück zum gespeicherten Stand.
    **Keine** Undo-Schritte: Auswahl, Ein-/Ausblenden von Gruppen, Zoom/Pan. Neu, Öffnen und Vorlage beginnen einen
    neuen, leeren Verlauf (kein Undo über einen Dokumentwechsel). Nach Undo/Redo wird die Auswahl nur bereinigt, nicht
    wiederhergestellt.
51. **Auswahl ist exklusiv:** Knoten *oder* eine Kante *oder* eine Gruppe. Eine Gruppe in der Liste zu wählen wählt die
    Gruppe selbst (nicht ihre Knoten), damit Entf die Gruppe und nicht ihre Knoten löscht. Rahmenauswahl ordnet die
    Knoten zeilenweise (oben nach unten, dann links nach rechts), Shift-Klick hängt an bzw. entfernt, Shift-Rahmen
    hängt an. Neue Gruppen übernehmen die Auswahlreihenfolge. **Reihenfolge sichtbar machen** (Thermometer): kleine
    Nummern an den Knoten (bei der Auswahl ab zwei Knoten, abschaltbar; immer bei der gewählten Gruppe), geordnete
    Gruppen zusätzlich als Thermometerlinie mit Kolben am ersten Knoten; im Gruppen-Panel „Reihenfolge umkehren“.
52. **Tastatur:** Ziffern tragen Givens in alle ausgewählten Knoten ein. Aufeinanderfolgende Ziffern bilden eine
    mehrstellige Zahl, solange sie im Wertebereich aller gewählten Knoten liegt (1–16: „1“, „6“ → 16; danach „2“ → 2,
    neue Zahl); ein Präfix, das selbst nicht im Bereich liegt, aber zu einem gültigen Wert führen kann, wartet auf
    weitere Ziffern (10–20: „1“ wartet, „5“ → 15); „-“ am Anfang für negative Bereiche. Eine neue Auswahl oder eine
    andere Bearbeitung beendet die Eingabe. Ist der Bereich genau 0–15, ist jedes Zeichen 0–9/A–F ein ganzer Wert
    (Nr. 32). **⌫ (Backspace) löscht die Givens der Auswahl, Entf (Delete; auf dem Mac fn+⌫) löscht die Auswahl**
    (Knoten mit ihren Kanten, die Kante oder die Gruppe); beides auch über Menü und Panel. Escape bricht eine Geste
    ab bzw. hebt die Auswahl auf. Strg (Windows/Linux) und Cmd (macOS): Z/Y/Shift+Z (Undo/Redo, nicht wenn ein
    Textfeld den Fokus hat – dort gilt dessen eigenes Undo), N, O, S, Shift+S, A, G (Gruppe), 0 (Einpassen).
    Eingaben auf der Zeichenfläche gehen nur an sie, wenn sie den Fokus hat (Klick hinein) – geändert durch Nr. 64.
53. **Gesten:** „Knoten setzen“ setzt per Klick oder Ziehen Knoten auf freie Gitterpunkte (entlang der Mausbewegung
    interpoliert, damit schnelles Ziehen keine Lücken lässt); ein Klick auf einen vorhandenen Knoten wählt ihn.
    Verschieben (Werkzeug „Auswahl“, gewählten Knoten ziehen): Der gegriffene Knoten rastet auf den nächsten
    Gitterpunkt, alle gewählten Knoten bewegen sich um denselben Gitterversatz; auf dem Dreiecksgitter nur um gerade
    Versätze (x + y), damit die Ausrichtung der Dreiecke und damit die Form erhalten bleibt. Auf von anderen Knoten
    belegte Punkte wird nicht geschoben (die Auswahl bleibt am letzten freien Punkt). Pan: mittlere oder rechte
    Maustaste, Leertaste + Ziehen oder Werkzeug „Ansicht verschieben“; Mausrad zoomt um den Mauszeiger
    (Maßstab 4–600 px pro Einheit).
54. **Kanten im Editor:** Die Regel neuer Kanten wird in der Werkzeugleiste gewählt (Standard „Ungleich“), neue Kanten
    erhalten `CreateDefaultParams()` (Difference: k = 1). Eine Kante mit gleicher Regel zwischen denselben Knoten
    (ungerichtet in beiden Richtungen, gerichtet nur gleiche Richtung) lehnt der Editor mit Meldung ab; verschiedene
    Regeln am selben Knotenpaar sind erlaubt (Nr. 23 bleibt: Dateien mit Doppelkanten werden nicht abgelehnt).
55. **Löschen eines Knotens** entfernt seine Kanten und nimmt ihn aus allen Gruppen; Gruppen, die dadurch leer werden,
    werden mitgelöscht (eine leere Gruppe hat keine Bedeutung). Undo stellt Listen und Reihenfolgen exakt wieder her.
56. **Eigenschaften-Panel** aus `IRule.Parameters`: ganze Zahlen als Textfeld (leer = Parameter weglassen; bei
    Pflichtparametern erscheint dann die Prüfmeldung, ungültiger Text wird mit Hinweis nicht übernommen),
    Wahrheitswerte als Häkchen, Auswahlwerte als Liste. Ein Regelwechsel setzt die Standardparameter der neuen Regel.
    Bereits in Paket 3 sind damit **alle acht Katalogregeln** an Kanten bzw. Gruppen wählbar, „Richtung umkehren“ für
    `LessThan` und „Reihenfolge umkehren“ für Gruppen inklusive (die Spezifikation nennt das erst in Paket 5; dort
    bleiben Kanten-Generator und Beispielrätsel). Ohne Auswahl zeigt das Panel Name und globalen Wertebereich des
    Rätsels (beides bearbeitbar, mit Undo); der Gittertyp wird nur bei „Neu“ gewählt. Eigene Wertebereiche pro Knoten
    sind im Editor noch nicht bearbeitbar (werden aber geladen, gespeichert und bei der Given-Eingabe beachtet).
57. **Öffnen ohne inhaltliche Prüfung:** Die App lädt mit `PuzzleSerializer.Load(..., validate: false)`. Formatfehler
    (z. B. falsche `formatVersion`, unbekannte Felder) werden weiterhin abgelehnt und als deutsche Meldung gezeigt;
    inhaltliche Probleme (unfertige Gruppen, fehlende Pflichtparameter, unbekannte Regeln) erscheinen im
    Statusbereich, damit unfertig gespeicherte Rätsel (Nr. 26) weiter bearbeitet werden können. Die Prüfmeldungen
    (`PuzzleValidator`) werden nach jeder Bearbeitung neu berechnet und bleiben reine Texte (siehe Paket 1, „Offen“).
58. **Dokument:** Start mit leerem Quadratgitter „Unbenannt“, Werte 1–9. Titel „Name * – GraphDoku“ (Dateiname, sonst
    Rätselname; „ *“ = ungespeichert). Vor Neu/Vorlage/Öffnen und beim Schließen des Fensters wird bei ungespeicherten
    Änderungen gefragt (Speichern / Verwerfen / Abbrechen). „Speichern“ ohne bekannten Pfad wirkt wie „Speichern
    unter“; vorgeschlagen wird der Rätselname mit `.json` (unzulässige Zeichen durch `_` ersetzt).
59. **Darstellung:** Knotenkreise mit Radius 0,38 (Quadrat, Sechseck) bzw. 0,2 Welteinheiten (Dreieck, dort liegen
    Nachbarn nur 1/√3 auseinander). Raster ab 8 px pro Einheit und höchstens 60 000 sichtbaren Zellen. Gruppen werden
    mit ihrem Umriss (Zellkanten, die keine andere Zelle der Gruppe teilt) in der Gruppenfarbe hervorgehoben (geändert durch Nr. 61); eine
    Fläche bekommen nur die gewählte Gruppe und Gruppen mit eigener Farbe – sonst ergäben die sich überlappenden
    Zeilen, Spalten und Blöcke der Vorlage einen unruhigen Flickenteppich (im Rendering-Rauchtest gesehen). Gruppen
    ohne eigene Farbe werden nach ihrer Id aus einer festen Palette gefärbt; die Farbe wird dann nicht gespeichert
    (`color` bleibt leer, „Automatisch“). Kantenmarken: `NotEqual` schlichte Linie, `Difference` weißer Punkt (mit
    „k“ bzw. „≥k“, außer bei k = 1 genau), `Ratio` schwarzer Punkt (mit Faktor, außer bei 2), `LessThan` Pfeil in der
    Mitte von `from` nach `to`, unbekannte Regel gestrichelt. Die App nutzt fest das helle Theme
    (`RequestedThemeVariant="Light"`), damit Zeichenfläche und Bedienelemente zusammenpassen.
60. Der Akzeptanztest prüft das im Editor gebaute Sudoku zusätzlich mit dem `OrToolsSolver` auf Eindeutigkeit
    (App-Tests erreichen den Solver über die Projektreferenz der App).

### Nacharbeiten nach Review (Paket 3)

61. **Gruppenumrisse zusammengefasst** (ersetzt die einfarbigen Umrisse aus Nr. 59): Bei der Vorlage lagen die
    Umrisse von 27 Gruppen auf denselben Zelllinien und übermalten sich; Blockgrenzen waren nicht zu erkennen. Jetzt
    werden die Umrisse aller sichtbaren Gruppen zusammengefasst (`GroupOutline.SharedBoundaries`): Ein Umrissstück,
    das nur zu einer Gruppe gehört, erscheint in deren Farbe (2 px); ein von mehreren Gruppen geteiltes Stück neutral
    dunkelgrau und umso kräftiger, je mehr Gruppen dort eine Grenze haben (2 → 1,85 px, 3 → 2,7 px, 4 → 3,55 px). Beim
    klassischen Sudoku sind das Zellgrenzen 2, Rand 3, Blockgrenzen 4 – die Blöcke treten hervor, ohne dass der Editor
    Regeln oder „Blöcke“ kennen muss. Gruppen mit eigener Farbe bekommen weiter Fläche und farbigen Umriss obenauf, die
    gewählte Gruppe Fläche und kräftigen Umriss ganz oben. Folge: Eine Gruppe mit automatischer Farbe, die sich ihre
    Grenzen mit anderen Gruppen teilt (z. B. ein Käfig in einem klassischen Sudoku), ist ohne Auswahl nur noch als
    Linienverstärkung zu sehen; wer sie farbig sehen will, gibt ihr eine eigene Farbe oder blendet die anderen Gruppen
    aus. Eine eigene Käfigdarstellung (gestrichelt, eingerückt) ist nicht gefordert und nicht umgesetzt.
62. **Gewählte Kante deutlicher:** Endknoten mit blauem Rand und Hof, Marke (Differenz-/Verhältnispunkt, Pfeil) mit
    Hof und dickerem Ring; ein Differenzpunkt wird hellblau gefüllt.
63. **Panel-Vorlagen werden wiederverwendet:** Avalonia nutzt die Vorlage des Eigenschaften-Panels beim Wechsel von
    einer Gruppe zur nächsten wieder. Hatte jedes Panel eine eigene Farbliste, übernahm die ComboBox beim Austausch
    der Liste die bisherige (wertgleiche) Auswahl ins neue Panel – jeder Gruppenwechsel erzeugte zwei Undo-Schritte
    „Gruppenfarbe ändern“ und machte das Dokument „ungespeichert“. Alle Listen, an die Panel-ComboBoxen gebunden sind,
    sind deshalb **eine gemeinsame Instanz** (Farbliste statisch, Regel-Listen im Editor, Auswahlwerte aus der
    Parameterbeschreibung der Regel). Eine eigene Farbe außerhalb der Palette steht nicht mehr als Sonder-Eintrag in
    der Liste (das hätte wieder eine eigene Liste je Panel erfordert), sondern als Hinweis unter der Liste; die Liste
    zeigt dann keine Auswahl. Headless-Test im echten Fenster: Wechsel zwischen Gruppen und Kanten erzeugt keinen
    Undo-Schritt.
64. **Bearbeitungstasten auch außerhalb der Zeichenfläche** (ändert Nr. 52): Entf, ⌫, Ziffern, A–F und „-“ wirken
    auch, wenn die Gruppenliste, eine Schaltfläche oder ein Häkchen den Fokus hat (das Fenster reicht sie an
    dieselbe Tastenbehandlung wie die Zeichenfläche weiter). Ausgenommen sind Textfelder, Auswahllisten und Menüs.
    Damit löscht Entf eine in der Liste gewählte Gruppe wie in Nr. 51 beschrieben. Leertaste und Escape bleiben bei der
    Zeichenfläche (Leertaste bedient sonst Schaltflächen und Listen).
65. **Rahmenauswahl nach Gitterzeile:** Die Reihenfolge („zeilenweise“, Nr. 51) richtet sich nach der Gitterzeile
    (y-Koordinate, bei allen drei Gittern die Zeile) und innerhalb der Zeile nach der waagerechten Position, nicht nach
    der Bildschirmhöhe des Mittelpunkts – auf dem Dreiecksgitter liegen die Schwerpunkte von Auf- und Ab-Dreiecken
    einer Zeile auf verschiedener Höhe, ein per Rahmen erstelltes Thermometer lief sonst im Zickzack.
66. **Mehrfach vergebene Gruppen-Ids** in einer ohne inhaltliche Prüfung geöffneten Datei (Nr. 57) bringen den Editor
    nicht mehr zum Absturz (die Gruppenliste ordnet Einträge je Id der Reihe nach zu). Die Prüfmeldung bleibt; Auswahl,
    Löschen und Ein-/Ausblenden wirken auf alle Gruppen mit dieser Id. Andere Fehler (doppelte Knoten-/Kanten-Ids,
    unbekannte Regeln, falsche Parametertypen) waren laut Review bereits unkritisch.
67. **Fehler der Dateiauswahl** (z. B. eine gewählte Datei ohne lokalen Pfad, Nr. 49) erscheinen als deutsche Meldung
    („Öffnen fehlgeschlagen“ / „Speichern fehlgeschlagen“) statt als unbehandelte Ausnahme aus dem Befehl; die
    Rückfrage beim Schließen bricht dann ab, das Dokument bleibt offen.
68. **Deutsche Parameterbezeichnungen** im Panel ohne Änderung am Core (`ParameterTexts` in der App): k → „Abstand k“,
    mode → „Modus“ (Werte „genau k“/„mindestens k“), factor → „Faktor“, target → „Zielsumme“, allowRepeats →
    „Wiederholungen erlaubt“, min/max → „Kleinster/Größter Wert“. Unbekannte Namen (eigene Regeln) erscheinen
    unverändert. Die Beschreibungstexte unter den Feldern stammen weiter aus `RuleParameter.Description` (Core) und
    nennen dort die JSON-Werte („exact“, „atLeast“).
69. **Werkzeugleiste bricht um** (`WrapPanel`), damit bei schmalem Fenster (Mindestgröße 900 × 560) Rückgängig,
    Wiederholen und Einpassen sichtbar bleiben (Rauchtest bei Mindestgröße). `TextBox.Watermark` (in Avalonia 12
    veraltet, Warnung AVLN5001) durch `PlaceholderText` ersetzt; XAML-Compilerwarnungen fallen nicht unter
    `TreatWarningsAsErrors`, deshalb wurde die Warnung zuvor übersehen.

## Lösen in der App (Paket 4)

70. **`SolveViewModel` mit injiziertem `ISolver`:** Lösen, Eindeutigkeit prüfen, Abbrechen und das Anzeigen/Ausblenden
    von Lösungen liegen in einem eigenen ViewModel (`MainWindowViewModel.Solve`). Der Solver ist jetzt ein
    **Pflichtparameter** des `MainWindowViewModel`-Konstruktors (`files, dialogs, solver, registry = null`); die App
    übergibt `new OrToolsSolver()`, Tests einen `FakeSolver` (Busy-, Abbruch- und Meldungstests) oder den echten Solver
    (Integrationstests). Gerechnet wird über `SolveAsync`/`CountSolutionsAsync` (Nr. 35): Das Rätsel wird beim Start im
    UI-Thread kopiert, die Suche läuft im Thread-Pool, die Oberfläche bleibt bedienbar. Es läuft höchstens eine Suche
    gleichzeitig („Lösen“ und „Eindeutigkeit prüfen“ sind währenddessen gesperrt).
71. **Lösung = reiner Anzeigezustand im `EditorViewModel`** (`ShowSolution`/`HideSolution`, `SolutionValues`,
    `SolutionDifferences`; Datei `EditorViewModel.Solution.cs`): Sie steht nicht im `Puzzle`, ist kein Undo-Schritt,
    macht das Dokument nicht „ungespeichert“ und wird deshalb nie gespeichert (Test: gespeicherte Datei gleich dem
    Original, ohne zusätzliche `given`). Darstellung: **Lösungswerte blau (#1d4ed8) in normaler Strichstärke, Givens
    schwarz und halbfett**; bei einem mehrdeutigen Rätsel werden Knoten, in denen sich die beiden Lösungen
    unterscheiden, orange (Wert, Ring, heller Hintergrund) markiert. Eine Legende steht in der Lösen-Leiste, solange
    eine Lösung angezeigt wird.
72. **Veraltete Lösungen werden ausgeblendet, nicht nur markiert:** Jede inhaltliche Änderung (Bearbeitung, Rückgängig,
    Wiederholen, Neu/Öffnen/Vorlage) blendet eine angezeigte Lösung sofort aus, die Lösen-Leiste meldet „Die Lösung
    wurde ausgeblendet, weil das Rätsel geändert wurde“, und auch „Lösung anzeigen“ ist dann gesperrt. Grund: Eine
    grau markierte alte Lösung wäre neben neuen Givens leicht falsch zu lesen. Erkannt wird das über einen neuen
    Zähler `EditHistory.Revision` (steigt bei Ausführen/Rückgängig/Wiederholen, nicht beim Speichern) und
    `EditorViewModel.ContentVersion` (zusätzlich bei Dokumentwechsel) mit dem Ereignis `ContentChanged`. Auswahl,
    Zoom/Pan, Ein-/Ausblenden von Gruppen und Speichern lassen die Lösung stehen.
    - **Bearbeitung während einer laufenden Suche:** Die Suche läuft weiter (sie rechnet auf einer Kopie), ihr Ergebnis
      wird am Ende verworfen („Das Rätsel wurde während der Suche geändert …“). **Dokumentwechsel während der Suche**
      bricht sie ab. Schließen des Fensters bricht eine laufende Suche ebenfalls ab.
73. **„Lösen“ und „Eindeutigkeit prüfen“:** „Lösen“ (`Solve`) trägt eine gefundene Lösung sofort ein und prüft die
    Eindeutigkeit nicht (Hinweis in der Meldung). „Eindeutigkeit prüfen“ (`CountSolutions` mit Limit 2) trägt nichts
    ein, sondern meldet „eindeutig lösbar“ bzw. „mehrdeutig: mindestens zwei verschiedene Lösungen … unterscheiden sich
    in n Knoten“ und **bietet die gefundenen Lösungen an** („Lösung anzeigen“ bzw. „Lösung 1 anzeigen“ /
    „Lösung 2 anzeigen“, Unterschiede orange). Endet die Prüfung durch Zeitlimit oder Abbruch mit einer gefundenen
    Lösung, kann auch diese angezeigt werden; die Eindeutigkeit gilt dann als nicht geklärt.
74. **Zeitlimit als einfaches Textfeld in Sekunden** in der Lösen-Leiste, Standard aus `SolverOptions.DefaultTimeLimit`
    (30 s), gilt für beide Befehle. Dezimalkomma und -punkt werden akzeptiert (`SolverTimeText`), erlaubt sind 0,1 bis
    3600 s; bei ungültiger Eingabe erscheint eine rote Meldung und „Lösen“/„Eindeutigkeit prüfen“ sind gesperrt. Das
    Zeitlimit wird nicht gespeichert (weder im Rätsel noch als Programmeinstellung) – kein Einstellungsspeicher, kein
    Scope Creep.
75. **Meldungen (deutsch) und Farben der Lösen-Leiste:** grün = gelöst / eindeutig; gelb = mehrdeutig, Zeitlimit,
    abgebrochen, veraltet; rot = unlösbar, ungültig, Solver-Fehler; blau = Suche läuft (Fortschrittsbalken ohne Prozent,
    Text, „Abbrechen“). Unlösbar, Zeitlimit und Zähl-Ergebnisse verwenden die Texte des Solvers (Paket 2), beim
    Zeitlimit ergänzt um „Ein höheres Zeitlimit kann helfen.“; bei ungültigem Rätsel folgt die Liste der Fehler des
    Solvers (`PuzzleValidator`). Wirft ein Solver eine Ausnahme (z. B. native Bibliothek fehlt), erscheint „Der Solver ist
    mit einem Fehler abgebrochen: …“ statt eines Absturzes (dafür ist CA1031 an genau dieser Stelle unterdrückt). Dauern
    werden als „170 ms“ bzw. „2,4 s“ angezeigt.
76. **Eigene Lösen-Leiste statt Schaltflächen in der Werkzeugleiste:** Im ersten Entwurf standen „Lösen“,
    „Eindeutigkeit prüfen“ und das Zeitlimit in der Werkzeugleiste; im Rendering-Rauchtest brach sie dadurch schon bei
    der Standardgröße 1280 × 800 in zwei Zeilen um, und die Ergebnismeldung brauchte eine weitere Leiste. Jetzt gibt es
    unter der Werkzeugleiste eine ständig sichtbare Leiste: links Befehle und Zeitlimit, rechts Fortschritt bzw.
    Ergebnis, Fehlerliste, Lösungs-Schaltflächen und Legende (ohne Ergebnis ein kurzer Bedienhinweis). Zusätzlich gibt
    es das Menü „Lösen“ (Lösen F5, Eindeutigkeit prüfen F6, Abbrechen, Lösung anzeigen/ausblenden).
77. **Einpassen bleibt beim Ändern der Zeichenflächengröße erhalten:** Im Rendering-Rauchtest war nach dem Lösen die
    unterste Sudoku-Zeile abgeschnitten, weil die Lösen-Leiste mit der Meldung höher wurde und die Zeichenfläche
    schrumpfte. Wurde die Ansicht seit dem letzten „Einpassen“ (auch dem automatischen beim Laden) nicht gezoomt oder
    verschoben (`EditorViewModel.IsViewFitted`), passt `SetViewSize` sie bei einer Größenänderung neu ein; sonst bleibt
    die Ansicht des Nutzers unverändert. Das wirkt auch beim Ändern der Fenstergröße.
78. **Tests:** `SolveViewModelTests` (Ersatzsolver: Anzeige ohne Änderung am Rätsel, Busy-Zustand, Abbrechen von Lösen
    und Prüfen, Zeitlimit-Eingabe und -Weitergabe, veraltete Lösung nach Bearbeitung/Undo/Redo, Bearbeitung und
    Dokumentwechsel während der Suche, Speichern ohne Lösung, unlösbar, Zeitlimit, ungültig mit Fehlerliste, Ausnahme,
    eindeutig, mehrdeutig mit Unterschieden). `SolveIntegrationTests` mit dem echten `OrToolsSolver`: **jede Datei in
    `puzzles/`** wird über „Öffnen“ geladen und mit dem Lösen-Befehl gelöst (jeder Knoten hat einen Wert im
    Wertebereich, Givens bleiben, AllDifferent-Gruppen unabhängig geprüft, außerdem muss das Rätsel mit der Lösung als
    Givens lösbar bleiben – das prüft auch künftige Regeln aus Paket 5); zusätzlich Eindeutigkeit je Datei (leer =
    mehrdeutig, sonst eindeutig), unlösbares, mehrdeutiges (zwei Lösungen, Unterschiede) und ungültiges Rätsel, veraltete
    Lösung nach Bearbeitung. Rendering-Rauchtests: gelöstes Sudoku (blaue Lösungswerte, schwarze Givens per Pixelprüfung,
    nach einer Bearbeitung keine blauen Werte mehr), mehrdeutiges Sudoku (orange Unterschiede), laufende Suche mit
    sichtbarer „Abbrechen“-Schaltfläche, Abbruch, ungültiges Rätsel, falsches Zeitlimit; alle Bilder wurden angesehen.
79. **Neu-Einpassen (Nr. 77) nicht während einer Ziehgeste:** Ändert sich die Größe der Zeichenfläche, während eine Geste
    läuft (z. B. weil die erste Bearbeitung beim Knotensetzen die Lösung ausblendet und sich die Lösen-Leiste ändert),
    bleibt die Ansicht stehen und `SetViewSize` merkt sich das Einpassen. Es wird beim Loslassen (`PointerReleased`)
    bzw. Abbrechen (`CancelGesture`) nachgeholt, aber nur, wenn die Ansicht dann noch eingepasst ist (nach Verschieben
    der Ansicht entfällt es).
80. **Lösen-Leiste mit stabiler Höhe:** Links stehen in der ersten Zeile „Lösen“, „Eindeutigkeit prüfen“ und das
    Zeitlimit, in einer zweiten, immer reservierten Zeile die Schaltflächen „Lösung (1) anzeigen“, „Lösung 2 anzeigen“,
    „Lösung ausblenden“ (vorher rechts unter der Meldung). Rechts stehen Meldung (höchstens zwei Zeilen, sonst gekürzt,
    Volltext als Tooltip) und eine einzeilige Legende. Dadurch ist die Leiste in allen Zuständen gleich hoch (68 px);
    nur die Fehlerliste eines ungültigen Rätsels (und eine zusätzliche Zeitlimit-Fehlerzeile) macht sie höher – das ist
    bewusst so, denn dort wird keine Lösung angezeigt. Die Meldungen wurden dafür gekürzt, die Kernaussage steht vorn
    (z. B. „Das Rätsel ist mehrdeutig: mindestens zwei Lösungen, verschieden in 12 Knoten (2 ms). …“), die Legende lautet
    „Blau: Lösungswerte · Schwarz: Givens · Orange: abweichende Knoten“. Ein Rendering-Test prüft die gleiche Höhe in
    sechs Zuständen bei 900 × 560 und 1280 × 800.
81. **„Eindeutigkeit prüfen“ bei angezeigter Lösung:** Ist das Ergebnis mehrdeutig und war bereits eine Lösung
    eingetragen (aus „Lösen“), wird sie durch Lösung 1 mit orange markierten Unterschieden ersetzt; die Meldung sagt,
    dass die eingetragene Lösung nur eine davon ist. Ohne angezeigte Lösung trägt die Prüfung weiterhin nichts ein
    (Nr. 73). Bei eindeutigem Ergebnis bleibt eine angezeigte Lösung stehen (sie ist dann die einzige). Bei genau einem
    abweichenden Knoten steht die Meldung im Singular.

## Erklär-Modus (Paket 6)

82. **Kandidatenmengen als unveränderliche Bitmengen (`CandidateSet`).** Grund: Der Erklär-Modus braucht billige
   Schnappschüsse. Mit unveränderlichen Mengen teilen Kopien alle unveränderten Knoten; `Clone()` ist O(Knotenanzahl).
   Die Bitmenge liegt über dem eigenen Wertebereich des Knotens (Versatz = Minimum), damit Knoten mit abweichendem
   Bereich funktionieren. `CandidateState.GetCandidates` liefert jetzt `CandidateSet` statt `IReadOnlySet<int>`
   (quellkompatibel, `CandidateSet` implementiert `IReadOnlySet<int>`); eine zurückgegebene Menge ändert sich später
   nicht mehr (vorher war sie eine Live-Ansicht). Minimum/Maximum heißen `MinValue`/`MaxValue`, damit sie nicht mit
   LINQ `Min()`/`Max()` kollidieren.
83. **`Reason.Constraint` ist nullable.** Grund: „einziger Kandidat“ und „Verzweigung“ hängen an keiner Regel; der
   Schritt soll trotzdem dieselbe `Reason`-Struktur (Text, beteiligte Knoten) tragen. Regeln setzen den Constraint
   weiterhin immer.
84. **Überarbeitung von `Propagate` (Korrektheit und Vollständigkeit):**
   - Alle Regeln liefern nur sichere Schlüsse; ein Test vergleicht für jede Regel 300 zufällige Kandidatenzustände mit
     vollständigem Durchprobieren.
   - **Vollständig konsistent** (streichen genau die Werte ohne erfüllende Belegung): `NotEqual`, `Difference`,
     `Ratio`, `LessThan` (Kantenkonsistenz, jetzt mit direkter Partnerprüfung statt aller Wertepaare), `Thermometer`
     (Schranken über „nächsten Kandidaten oberhalb/unterhalb“ statt nur Min/Max – vorher blieben bei Lücken Werte
     stehen, z. B. 5 < {2, 8} < x ergab x ≥ 7 statt x = 9) und `Sum` (neu: Tiefensuche pro Kandidat mit
     Schrankenabschneiden; ohne `allowRepeats` mit verschiedenen Werten. Beispiel: Summe 4 aus zwei verschiedenen
     Werten streicht jetzt die 2). Gefundene Belegungen markieren alle ihre Werte als gestützt, damit die Suche selten
     wiederholt wird. Budget: 200 000 Suchschritte pro Aufruf (`SumRule.SearchBudget`); danach keine weiteren
     Schlüsse dieser Art.
   - **Bewusst nicht vollständig**: `AllDifferent` und `CoverRange` streichen nur festgelegte Werte (und bei
     `CoverRange` Werte außerhalb des Bereichs). Hidden singles gehören laut Spezifikation in Stufe 2 der Schleife;
     weitergehende Techniken (Paare usw.) sind nicht verlangt.
   - Reihenfolge innerhalb von `Sum`: erst Schranken (verständlichster Text), dann Wiederholungsverbot, dann die
     vollständige Prüfung; doppelte Streichungen entfallen wie bisher (erste Begründung bleibt).
85. **Begründungstexte.** Streichungen festgelegter Werte in Gruppen haben einen vom Zielknoten unabhängigen Text,
   z. B. „Gruppe 3 „Zeile 3“ enthält bereits die 5 (Knoten 21). Alle Werte sind verschieden. Also ist die 5 in den
   übrigen Knoten der Gruppe nicht mehr möglich.“ (`CoverRange`: „Jede Zahl von 1 bis 9 kommt genau einmal vor.“,
   `Sum`: „In der Summe wiederholen sich keine Werte.“). `LessThan` nennt die Schranke des anderen Knotens,
   `Difference`/`Ratio` dessen Kandidaten (ab 13 Kandidaten gekürzt), `Sum` „genau x“ statt „x bis x“, `Thermometer`
   „muss … den Wert x haben“ statt „zwischen x und x“. Der Text bei `NotEqual` ist unverändert.
86. **Ein bestehender Test geändert:** `Sum_uses_bounds_of_other_nodes` prüft jetzt mit `allowRepeats = true` (dort
   bleiben 1, 2, 3); ohne Wiederholung streicht die stärkere Propagation zusätzlich die 2 (eigener neuer Test).
87. **Schritte und ihre Körnung:**
   - Ein gesetzter Wert ist immer ein eigener Schritt („gesetzter Wert“). Die Folgen für die Nachbarn sind **nicht**
     Teil dieses Schritts, sondern eigene Streichschritte (seit Nr. 95 in Stufe 0, sonst Stufe 3).
   - Ein Streichschritt fasst alle Änderungen **eines** `Propagate`-Ergebnisses zusammen, die dieselbe Begründung
     (gleicher Constraint und gleicher Text) haben – pro Knoten eine Änderung. So wird „Zeile 3 enthält bereits die 5“
     zu einem Schritt, der die 5 in allen übrigen Knoten der Zeile streicht (je gesetztem Wert und Gruppe ein Schritt).
     Bei Kanten-, Summen- und Thermometerregeln hängt der Text vom Zielknoten ab, dort ist es ein Schritt pro Knoten.
     Beteiligte Knoten = Vereinigung der Knoten aller zusammengefassten Begründungen.
   - Ein Knoten mit nur noch einem Kandidaten gilt erst als „gesetzt“, wenn ein Schritt „einziger Kandidat“ ihn setzt.
     Dieser Schritt ändert die Kandidaten nicht, nur die Menge der gesetzten Knoten (Snapshot `SetNodeIds`), damit die
     Oberfläche gesetzte Werte und offene Kandidaten unterscheiden kann.
   - Eine Regel, die selbst einen Wert setzt (`Assign`, nur bei eigenen Regeln), ergibt einen Schritt „gesetzter Wert“
     mit Technik `RulePropagation`.
88. **Feste Reihenfolge der Schlüsse**, nach jedem Schritt wieder von vorn (Stufe 0 siehe Nr. 95):
   0. Feststehende Werte streichen (seit Nr. 95).
   1. Einziger Kandidat: erster noch nicht gesetzter Knoten (Rätselreihenfolge) mit genau einem Kandidaten.
   2. Einziger Platz: Gruppen in Rätselreihenfolge, Werte aufsteigend. `CoverRange`: immer (jede Zahl des Bereichs
      muss vorkommen). `AllDifferent`: nur, wenn die Vereinigung der **aktuellen** Kandidaten genau so viele Werte hat
      wie die Gruppe Knoten (Schubfachprinzip; beim klassischen Sudoku immer). Andere Gruppenregeln (z. B. `Sum` ohne
      Wiederholung) bekommen keine hidden singles (Spezifikation nennt nur diese beiden).
   3. Regeln: sortiert nach Rang (`ExplanationEngine.GetRuleRank`), innerhalb des Rangs Rätselreihenfolge (erst
      Kanten, dann Gruppen); der erste Constraint mit wirksamer Änderung liefert den Schritt. Ränge: 0 = festgelegte
      Werte streichen (`AllDifferent`, `CoverRange`, `NotEqual`), 1 = Kantenregeln mit Rechnung (`LessThan`,
      `Difference`, `Ratio`), 2 = `Thermometer`, `Sum`, 3 = eigene Regeln. Die Spezifikation legt die Reihenfolge
      innerhalb von Stufe 3 nicht fest; „einfache Streichungen zuerst“ entspricht dem menschlichen Vorgehen.
89. **Widersprüche** werden vor jedem Schritt geprüft: Knoten ohne Kandidaten (Text nennt den letzten Schritt, der
   ihn verändert hat; Constraint und beteiligte Knoten stammen von dort), Zahl ohne Platz in einer `CoverRange`-Gruppe,
   `AllDifferent`-Gruppe mit weniger möglichen Werten als Knoten. Ohne vorherige Verzweigung ist das ein Beweis der
   Unlösbarkeit (Status `Unsolvable`, `Contradiction` gesetzt); der Solver wird dafür nicht gefragt.
90. **Verzweigung und Solver-Nutzung:**
   - Der Solver wird nur gefragt, wenn die Logik festsitzt, und dann genau einmal pro Erklärung:
     `CountSolutions(puzzle, limit: 2)`. Das liefert in einem Aufruf Lösung und Eindeutigkeit.
   - Knotenwahl: noch nicht gesetzter Knoten mit den wenigsten Kandidaten, bei Gleichstand die kleinste Id. Wert aus der
     ersten Solver-Lösung. Danach geht die normale Schleife weiter (weitere Verzweigungen nutzen dieselbe Lösung).
   - Ohne Verzweigung erklärt = eindeutig (die Logik schließt nur sicher und kommt ohne Annahme zur vollständigen
     Belegung), deshalb ruft z. B. „leicht“ den Solver nie auf.
   - **Mehrdeutig:** Status `SolvedAmbiguous`; alle Verzweigungswerte stammen aus der ersten gefundenen Lösung, die
     Erklärung endet genau in dieser Lösung. Die Schritte vor der ersten Verzweigung gelten für alle Lösungen.
   - Solver unlösbar → `Unsolvable` (ohne `Contradiction`); Zeitlimit ohne Lösung → `Stuck`; Zeitlimit mit einer
     Lösung → Erklärung mit dieser Lösung, Status `SolvedUnverified`; Abbruch → `Cancelled`.
91. **Navigation über Zwischenstände statt Schnappschuss pro Schritt:** Alle 16 Schritte wird ein Zwischenstand
    gespeichert, `GetSnapshot(n)` spielt ab dem nächstgelegenen höchstens 15 Schritte nach. Das Anwenden eines Schritts
    ist für Schleife und Nachspielen derselbe Code (deterministisch). Jeder Snapshot ist eine eigene Kopie.
92. **Abbruch** wie beim Solver ohne `OperationCanceledException`: Status `Cancelled`, die bis dahin erzeugten Schritte
    bleiben erhalten (navigierbar). Das Token wird vor jedem Schritt geprüft und an den Solver weitergereicht.
    `ExplainAsync` läuft per `Task.Run` auf einer Kopie des Rätsels (wie `SolverExtensions`).
93. **Ungültige Rätsel** (Prüfung mit `PuzzleValidator` und der Registry der Engine) liefern `Invalid` mit den
    deutschen Fehlern und ohne Zustände (`HasSnapshots = false`, `GetSnapshot` wirft).
94. **Testhilfen:** `GraphDoku.Explainer.Tests` bindet den unabhängigen `SolutionVerifier` aus den Solver-Tests als
    verlinkte Datei ein (keine Kopie) und kopiert `puzzles/*.json` ins Ausgabeverzeichnis.
95. **Stufe 0 „Feststehenden Wert streichen“ vor den Singles (Abweichung von der Reihenfolge der Spezifikation).**
    Streichungen von `AllDifferent`, `CoverRange` und `NotEqual` (Katalogregeln mit Rang 0), deren Begründung nur an
    Givens und schon gesetzten Werten hängt (alle Knoten der Begründung außer dem Zielknoten sind gesetzt), kommen vor
    naked und hidden singles; dazu zählen auch Werte außerhalb des `CoverRange`-Bereichs. Neue Technik
    `ExplanationTechnique.FixedValueElimination` („Feststehenden Wert streichen“), Schrittart weiterhin „gestrichene
    Kandidaten“. Ein Knoten mit nur einem Kandidaten, der noch nicht gesetzt ist, löst keine Stufe-0-Streichung aus: erst
    kommt der Schritt „einziger Kandidat“, dann seine Folgen. Gründe: (a) Vorher gewannen hidden singles gegen die
    noch ausstehenden Streichungen eines gesetzten Werts; die Kandidatenanzeige zeigte offensichtlich überholte
    Kandidaten, während schon eine schwierigere Technik lief („vom einfachsten zum schwierigsten“ stimmte für den Leser
    nicht). (b) Ein naked single konnte einen Wert setzen, den ein gesetzter Nachbar in derselben Gruppe schon hat
    (z. B. Knoten 1 und 2 mit Bereich [1, 1] in einer `AllDifferent`-Gruppe: „Knoten 1 = 1“, „Knoten 2 = 1“, dann erst
    der Widerspruch). Jetzt streicht Stufe 0 die 1 bei Knoten 2, der Widerspruch „Knoten 2 hat keinen Kandidaten mehr“
    folgt sofort, und kein Schritt verletzt sichtbar eine Regel. Eine eigene Prüfung „zwei gesetzte Knoten mit gleichem
    Wert“ in `FindContradiction` ist damit überflüssig. `Sum` (Wiederholungsverbot) bleibt in Stufe 3, weil sich dessen
    Streichungen nicht verlässlich von den Schrankenschlüssen trennen lassen. Die Spezifikation nennt die Stufen 1–3;
    die vorgezogene Stufe ist eine Auslegung („einfachster Schluss“) und sollte vom Lead bestätigt werden.
    **Bestätigt vom Projektinhaber am 7. November 2026** (beim Review von PR #1): Stufe 0 bleibt.
96. **Eigene Regel setzt einen gestrichenen Wert → Widerspruch statt Ausnahme.** Liefert eine Regel `Assign(v)` für
    einen Knoten, bei dem `v` kein Kandidat mehr ist, ist das vor der ersten Verzweigung ein Beweis der Unlösbarkeit:
    Status `Unsolvable`, `Contradiction` mit Constraint und Knoten der Regel und dem Text der Regel plus „Die 4 ist für
    Knoten 2 aber nicht mehr möglich.“ Nach einer Verzweigung bleibt es eine `InvalidOperationException` („Interner
    Fehler …“), weil dann eine Regel falsch geschlossen hat. Grund: Eine korrekte eigene Regel darf so einen Widerspruch
    zeigen; vorher warf `Explain` bzw. lieferte `ExplainAsync` eine fehlerhafte Task.
97. **Eigene Regeln: Solver bestätigt eine ohne Raten erreichte Belegung.** Der Status „eindeutig ohne Solver“ setzt
    voraus, dass jede Regel einen Verstoß der vollständigen Belegung durch eine Streichung meldet. Die Katalogregeln tun
    das (Fuzz-Test des Reviews), eigene Regeln müssen es nicht (z. B. eine Regel „alle gerade“ ohne Propagation ergab
    vorher „Solved, eindeutig“ für eine Belegung, die sie verletzt). Enthält das Rätsel Regeln außerhalb des Katalogs
    (erkannt am Assembly der Regelklasse, nicht am Namen) und wurde es ohne Verzweigung belegt, ruft die Engine einmal
    `CountSolutions(limit: 2)`: mindestens eine Lösung (gleich der Belegung) → `Solved` („der Solver bestätigt …“);
    keine Lösung, vollständig geprüft → `Unsolvable` ohne `Solution`; Zeitlimit oder Solver ohne Adapter für die Regel
    → `SolvedUnverified` mit Grund; Abbruch → `Cancelled`; eine abweichende Solver-Lösung → `InvalidOperationException`
    (eigene Regel schließt falsch). Die Doku von `IRule.Propagate` nennt jetzt Korrektheit als Pflicht und das Erkennen
    von Verstößen auf vollständigen Belegungen als Empfehlung. Ein neues `IRule.IsSatisfied` wurde bewusst nicht
    eingeführt (Schnittstellenänderung, doppelte Semantik zu den Solver-Adaptern). Ohne eigene Regeln ändert sich
    nichts (leicht ruft den Solver weiterhin nie auf). Der Test mit der Testregel „alle gleich“ bekam dafür einen
    Test-Adapter für OR-Tools; ohne Adapter ist das Ergebnis jetzt `SolvedUnverified` (eigener Test).
98. **Kürzere Begründungstexte.**
    - Hidden single: Umfassen die Wertebereiche der Gruppenknoten genau so viele Werte wie die Gruppe Knoten hat
      (klassisches Sudoku), lautet der Text „In Gruppe 10 „Spalte 1“ muss jede Zahl von 1 bis 9 genau einmal
      vorkommen. Die 9 ist nur noch in Knoten 55 möglich.“ (`CoverRange` immer so). Nur wenn das Schubfachprinzip
      wirklich gebraucht wird (Bereiche größer als die Gruppe), bleibt die Begründung ausführlich und nennt die Anzahl
      eindeutig („Alle 3 Knoten der Gruppe haben verschiedene Werte, und es kommen nur noch die Werte 1, 2, 3 in Frage,
      genauso viele wie Knoten. Also muss …“).
    - Verzweigung: `Summary` ohne Präfix „Verzweigung: “ (nur „Knoten 70 = 9“, die Art steht in `Kind`), Text „Ohne
      Raten geht es hier nicht weiter: Knoten 70 hat die wenigsten Kandidaten (3, 9); der Wert 9 wird aus der Lösung des
      Solvers übernommen.“ So steht „Verzweigung“ in `ToString()` nur einmal. Die Oberfläche sollte die Art eines
      Schritts (z. B. als Symbol oder Etikett) neben `Summary` zeigen.
    - Schubfach-Widerspruch mit einem Wert: „es kommt aber nur noch ein Wert in Frage“ statt „1 Werte“.
99. **Zwischenspeicher für `Propagate` (nur Katalogregeln).** Pro Constraint werden das letzte Ergebnis und die
    Kandidatenmengen seiner Knoten gemerkt; sind alle Mengen noch dieselben Objekte (unveränderliche `CandidateSet`),
    wird das Ergebnis wiederverwendet. Grund: Die teure, budgetierte Prüfung großer `Sum`-Gruppen lief sonst nach jedem
    Schritt erneut. Eigene Regeln werden nicht zwischengespeichert, weil ihr Ergebnis laut Vertrag nicht nur von den
    eigenen Knoten abhängen muss.
100. **Abbruch zwischen den Constraints.** Stufe 3 prüft das Token vor jedem `Propagate`-Aufruf; ein Abbruch dort führt
    direkt zu `Cancelled`, ohne den Solver für eine Verzweigung zu fragen.

### Erklär-Modus in der App (Paket 6, Oberfläche)

101. **`ExplainViewModel` mit eigener Schnittstelle `IPuzzleExplainer`:** Die Oberfläche des Erklär-Modus liegt in
    `MainWindowViewModel.Explain` (neben `Solve`). Gerechnet wird über `IPuzzleExplainer.ExplainAsync(puzzle,
    solverOptions, token)`; die App nutzt `EnginePuzzleExplainer`, der pro Lauf eine `ExplanationEngine` mit dem
    Solver der App, der Registry des Editors und dem **Zeitlimit der Lösen-Leiste** erzeugt (die Engine nimmt die
    Solver-Optionen im Konstruktor). Ist das Zeitlimit ungültig, ist „Erklären“ wie „Lösen“ gesperrt. Grund für die
    Schnittstelle: `ExplanationResult` hat keinen öffentlichen Konstruktor, und `ExplanationEngine` ist versiegelt;
    für Tests mit blockierender Berechnung oder einer Ausnahme braucht es einen Ersatz (`FakeExplainer`, der das
    Ergebnis selbst mit der echten Engine berechnet). Der Konstruktor `MainWindowViewModel(files, dialogs, solver,
    registry = null, explainer = null)` bleibt quellkompatibel.
102. **Erklärung als Anzeigezustand im Editor** (`EditorViewModel.Explanation` vom Typ `ExplanationView`, Datei
    `EditorViewModel.Explanation.cs`), wie die Lösung (Nr. 71): nicht im Rätsel, kein Undo-Schritt, nie gespeichert.
    `ExplanationView` enthält den Zustand nach n Schritten (`GetSnapshot(n)`), den aktuellen Schritt, die geänderten
    Knoten, die übrigen Knoten der Begründung (`InvolvedNodeIds` ohne die geänderten), den Constraint, die bisher
    geratenen Knoten und die vom Schritt gestrichenen Werte. Lösung und Erklärung schließen sich aus: „Erklären“
    blendet eine angezeigte Lösung aus (sie bleibt über „Lösung anzeigen“ abrufbar), das Anzeigen einer Lösung beendet
    den Erklär-Modus. `PuzzleContentChangedEventArgs` hat dafür zusätzlich `ExplanationWasShown` (optionaler
    Konstruktorparameter).
103. **Ende des Modus bei Änderungen** (wie veraltete Lösungen, Nr. 72): Jede inhaltliche Änderung (Bearbeitung,
    Rückgängig, Wiederholen) beendet den Modus; die Schrittleiste bleibt mit gelber Meldung „Die Erklärung wurde
    beendet, weil das Rätsel geändert wurde. „Erklären“ (F7) erklärt den neuen Stand.“ und „Schließen“ stehen. Ein neues
    Dokument beendet ihn ohne Meldung. Auswahl, Zoom/Pan und Ein-/Ausblenden von Gruppen lassen ihn stehen. Während der
    Berechnung: Bearbeitung → Ergebnis wird am Ende verworfen; Dokumentwechsel und Schließen des Fensters → Abbruch.
    Solange der Modus aktiv ist, lässt sich weiter bearbeiten (keine Sperre); die erste Bearbeitung beendet ihn.
104. **Position und „aktueller Schritt“:** Position n (0 bis N) zeigt den Zustand **nach** n Schritten; der „aktuelle“
    Schritt ist der zuletzt angewendete (Schritt n), damit Wirkung und Begründung zusammen zu sehen sind (der gesetzte
    Wert steht schon im Knoten, gestrichene Kandidaten erscheinen noch rot durchgestrichen). Position 0 heißt
    „Ausgangslage“ (Givens und Wertebereiche). Anzeige „Schritt n von N“. Nach dem Berechnen beginnt der Modus bei 0.
    Der Schieberegler ist an `SliderValue` (double) gebunden, gerundet; Werte außerhalb werden begrenzt.
105. **Darstellung auf der Zeichenfläche:** Givens schwarz halbfett, hergeleitete Werte blau (#1d4ed8, wie
    Lösungswerte), durch eine Verzweigung gesetzte Werte orange (#c2410c) auch in allen späteren Schritten. Knoten des
    aktuellen Schritts gelb (#fef08a, Ring #ca8a04), bei einer Verzweigung orange (#fed7aa, Ring #ea580c) mit Marke
    „Raten“ (bei kleinen Knoten „?“); Knoten der Begründung grün (#bbf7d0, Ring #16a34a); die Gruppe der Regel grün
    umrandet und leicht grün hinterlegt (auch wenn ihre Hervorhebung ausgeblendet ist), eine Kante der Regel grün und
    dick. Knoten ohne Kandidaten (Widerspruch) rot umrandet. Gewählte Knoten behalten den blauen Auswahlring.
106. **Kandidatenraster:** Spaltenzahl = ⌈√(Anzahl Werte des Knotens)⌉ (9 → 3 × 3, 16 → 4 × 4, 4 → 2 × 2), jeder Wert an
    fester Stelle (Versatz zum Minimum des Knotenbereichs), Raster etwa so groß wie das einbeschriebene Quadrat
    (1,5 r bzw. 1,44 r ab vier Spalten). Werte über `ValueText` (bei 0–15 als 0–F, Nr. 32). Unter 5 px Schriftgröße
    oder bei mehr als 36 Werten steht stattdessen die Anzahl „(n)“. Im ersten Entwurf (1,6 r) berührten die Ziffern in
    den Ecken den Rand, die Durchstreichung war zu dick – im Rendering-Test gesehen und korrigiert. Kandidatentexte
    werden in der Zeichenfläche zwischengespeichert (viele kleine Texte pro Bild).
107. **Bedienelemente:** „Erklären“ in der ersten Zeile der Lösen-Leiste (rechts neben „Eindeutigkeit prüfen“), F7
    und Menü „Lösen“ (Erklären, Erklärung abbrechen, Kandidaten anzeigen, Erklärung beenden). Darunter eine
    **einzeilige Schrittleiste**, nur sichtbar bei Berechnung, aktivem Modus oder Meldung: « Anfang, ‹ Zurück, Vor ›,
    Ende », Schieberegler, „Schritt n von N“, Häkchen „Kandidaten“ (Standard: an), „Erklärung beenden“. Im aktiven
    Modus ist die Leiste neutral (`BarSeverity`); Ergebnis, Schritt und Begründung stehen im Panel rechts, das solange
    das Eigenschaften-Panel ersetzt (mit Legende und Tastenhinweis). Ohne aktiven Modus (ungültig, Fehler, veraltet,
    abgebrochen ohne Schritte) zeigt die Leiste die Meldung in der Ergebnisfarbe (wie Nr. 75) mit „Schließen“.
    Pfeiltasten als Symbole (◀ ▶) wurden vermieden, weil unklar war, ob Inter sie enthält; Guillemets sind sicher.
108. **Tasten:** ← / → einen Schritt zurück/vor, Pos1 / Ende zum Anfang/Ende – im Fenster abgefangen, wenn die
    Zeichenfläche, eine Schaltfläche, ein Häkchen oder nichts den Fokus hat. Textfelder, Listen, Auswahllisten und
    Menüs behalten ihre eigene Bedeutung der Tasten; der Schieberegler bewegt sich mit den Pfeiltasten ohnehin
    schrittweise. Ohne Strg/Cmd/Alt/Shift.
109. **Ergebnisse und Fehler:** Einstufung grün = gelöst (eindeutig); gelb = mehrdeutig, unbestätigt, festgefahren,
    abgebrochen, veraltet; rot = unlösbar, ungültig, Fehler; blau = Berechnung läuft. Meldetext ist die Meldung der
    Engine; bei Abbruch ergänzt um „Die ersten n Schritte können angesehen werden.“ (die Schritte bleiben im Modus
    ansehbar; ohne Schritte nur die Meldung). Bei „ungültig“ ersetzt ein eigener Satz die Meldung der Engine, weil diese
    die Fehler wiederholt, die ohnehin als Liste darunter stehen (im Rendering-Test gesehen). Eine
    `InvalidOperationException` der Engine („Interner Fehler …“, Nr. 96/97) erscheint als „Die Erklärung ist
    fehlgeschlagen. Interner Fehler: …“, jede andere Ausnahme als „Die Erklärung ist mit einem Fehler abgebrochen: …“
    (CA1031 an dieser Stelle unterdrückt, wie Nr. 75). Ein Test erzeugt den internen Fehler mit der echten Engine
    (Ersatzsolver mit falscher Lösung).
110. **Lösen und Erklären sperren sich nicht gegenseitig:** Beide rechnen auf Kopien; was zuletzt fertig wird, wird
    angezeigt (eine Lösung beendet den Modus, eine Erklärung blendet die Lösung aus). Eine gegenseitige Sperre hätte
    Änderungen am `SolveViewModel` erfordert und keinen fachlichen Gewinn gebracht.
111. **Bestehender Rendering-Test angepasst:** `Busy_and_error_states_render` sucht „Abbrechen“ jetzt nur in der
    Lösen-Leiste, weil die Schrittleiste eine zweite (unsichtbare) Schaltfläche „Abbrechen“ enthält.
112. **Roter Ring für Knoten ohne Kandidaten zusätzlich außen** (Review-Befund zu Paket 6): Bisher ersetzte die gelbe
    Hervorhebung des aktuellen Schritts den roten Ring. Weil die Engine direkt nach dem Schritt endet, der einen Knoten
    leert, ist dieser Knoten an der letzten Position immer Knoten des aktuellen Schritts – der rote Ring erschien im
    normalen „unlösbar“-Ablauf also nie. Jetzt bekommt ein Knoten ohne Kandidaten einen eigenen roten Ring **außerhalb**
    des Knotenrings (Abstand max(3 px, 0,14 r)); Hervorhebung (gelb/grün) und Auswahlring bleiben erhalten. Die Legende
    im Panel hat dafür eine Zeile „Knoten ohne Kandidaten (Widerspruch)“. Rendering-Test `erklaerung-widerspruch.png`.
113. **Sehr kleine Knoten zeigen den gestrichenen Wert** (Review-Befund): Unter 5 px Schriftgröße für das Raster und unter
    8 px Radius für „(n)“ war im Knoten nichts mehr zu sehen – bei Mindestgröße (900 × 560, Radius etwa 7 px) zeigte die
    Zeichenfläche nur noch, welche Knoten gelb sind. Jetzt steht in Knoten, die zu klein für das Raster sind, der vom
    aktuellen Schritt gestrichene Wert (mehrere ohne Trenner bzw. mit Komma bei mehrstelligen Werten) rot und
    durchgestrichen in der Mitte; sonst wie bisher „(n)“ ab 8 px Radius, darunter nichts. Die übrigen Kandidaten sind bei
    dieser Größe nicht zu sehen (Zoomen hilft); das Häkchen „Kandidaten“ blendet auch diese Anzeige aus.
114. **Menüeintrag „Kandidaten anzeigen“ mit `Mode=TwoWay`** (Review-Befund): `MenuItem.IsChecked` bindet in Avalonia 12
    standardmäßig nur in eine Richtung; der Menüeintrag schaltete daher nur sein eigenes Häkchen um. Ein Fenstertest wählt
    den Eintrag jetzt über die Tastatur (Menü öffnen, Eintrag fokussieren, Eingabe) und prüft ViewModel und Häkchen der
    Schrittleiste.
115. **Tastenhinweis ohne „Klick in die Zeichenfläche“** (Review-Befund): Ein Klick mit „Knoten setzen“ setzt einen Knoten,
    ein leichtes Ziehen mit „Auswahl“ verschiebt ihn – beides beendet die Erklärung (Nr. 103). Der Hinweis im Panel (und
    das README) nennt jetzt die Schrittleiste als Fokus („z. B. nach Klick auf „Vor ›““) und warnt vor diesen
    Bearbeitungen. Am Verhalten (Nr. 108) und am blauen Auswahlring, der die Hervorhebungsfarbe des Rings ersetzt
    (Nr. 105), ändert sich nichts. Der Hinweistext der Lösen-Leiste hat einen Tooltip mit dem vollen Text, weil er bei
    Mindestgröße abgeschnitten wird.

## Kanten-Generator und Beispielrätsel (Paket 5)

Entstanden im Branch `feature/wp5-generator` (nur Core, Tests und `puzzles/`; die Editor-Oberfläche für den
Generator folgt). In den Notizen des Branches hießen die Entscheidungen G1–G17; hier sind sie fortlaufend
nummeriert (G1–G8 → Nr. 116–123, G17 → Nr. 124, G9–G16 → Nr. 125–132).

### Kanten-Generator

116. **Reine Funktion plus Anwenden in einem Schritt.** `EdgeGenerator.Generate(puzzle, request)` verändert das Rätsel
    nicht und liefert ein `EdgeGenerationResult` mit den neuen Kanten (fortlaufende Ids ab `Puzzle.NextEdgeId()`,
    mindestens ab 1 – siehe Nr. 124), Anzahl passender und übersprungener Paare, Fehlern und einer deutschen Meldung.
    `result.ApplyTo(puzzle)` fügt Kopien ein (wirft, wenn das Ergebnis Fehler hat oder eine Id inzwischen vergeben
    ist). Grund: Der Editor kann Vorschau und Meldung zeigen und die Kanten in **einem** Undo-Befehl einfügen
    (Execute = `ApplyTo` bzw. Kanten hinzufügen, Undo = Kanten mit diesen Ids entfernen), ohne dass Core die
    Undo-Infrastruktur der App kennen muss.
117. **Abstandsregeln (`DistanceRule`).** `Orthogonal` (gemeinsame Kante: Quadrat 4, Dreieck 3, Sechseck 6),
    `WithDiagonals` (gemeinsame Ecke: 8 / 12 / 6), `KnightMove` (nur Quadratgitter, wie `GridGeometry`, Nr. 29) – alle über
    die vorhandenen Nachbarlisten von `GridGeometry`. **Freier Abstand** = euklidischer Abstand der Zellmittelpunkte in
    Welteinheiten im Bereich **[min, max] einschließlich, mit Toleranz** (Standard 1e-6); `DistanceRule.Exact(d)` ist der
    Bereich [d, d] und entspricht damit `GridGeometry.IsAtDistance` (Nr. 30). Der Bereich ist eine Erweiterung gegenüber
    „frei wählbarer Abstand“: Er erlaubt z. B. „alle Knoten im Umkreis 1,5“ ohne zweite Regelart. Bedingungen: endliche
    Zahlen, min > 0, max ≥ min, 0 ≤ Toleranz < min. Auf dem Dreiecksgitter haben Kantennachbarn den Abstand 1/√3, gleich
    ausgerichtete waagerechte Nachbarn den Abstand 1.
118. **Knotenmenge.** `NodeIds = null` heißt alle Knoten, sonst z. B. die Auswahl; **beide** Endknoten einer Kante
    müssen in der Menge liegen (Auslegung von „auf Auswahl anwenden“). Doppelte Ids in der Menge werden ignoriert,
    unbekannte Ids sind ein Fehler. Knoten an derselben Gitterposition werden nie miteinander verbunden. Enthält das Rätsel
    doppelte Knoten-Ids (Datei ohne Prüfung geöffnet, Nr. 57), verweigert der Generator mit Meldung.
119. **Doppelte Kanten.** Ein Paar wird übersprungen, wenn zwischen beiden Knoten bereits eine Kante **mit demselben
    Regeltyp** besteht oder in diesem Lauf erzeugt wurde – **ungerichtet verglichen und unabhängig von den Parametern**.
    Andere Regeltypen am selben Paar blockieren nicht. Abweichung von der Editor-Regel für einzeln gezogene Kanten (Nr. 54:
    gerichtete Kanten gelten nur in gleicher Richtung als doppelt): Eine vom Generator erzeugte Gegenkante `b → a` neben
    `a → b` („Kleiner als“) wäre sofort ein Widerspruch, und eine zweite `Difference`-Kante mit anderem `k` am selben Paar
    ist fast immer ein Bedienfehler. Ein zweiter Lauf mit derselben Einstellung erzeugt deshalb nichts („Alle n passenden
    Knotenpaare haben bereits eine Kante …“).
120. **Richtung (`EdgeDirectionPolicy`).** „Kleiner als“ wird **nicht** abgelehnt, sondern über eine Richtungsregel
    erzeugt: `ReadingOrder` (Standard) – From ist der Knoten, der in Leserichtung zuerst kommt (kleinere Gitterzeile y,
    dann kleinere waagerechte Weltkoordinate, dann kleinere Id; beim Sechseckgitter zählt die Weltposition x + y/2), die
    Werte steigen also nach rechts bzw. unten; `ReverseReadingOrder` umgekehrt. Einzelne Kanten dreht der Nutzer danach mit
    „Richtung umkehren“ (gibt es seit Paket 3). Ungerichtete Regeln bekommen dieselbe Ordnung (deterministische Dateien).
    Grund: So bleibt der Generator für alle Kantenregeln gleich bedienbar; ein „Größer-als-Sudoku“ lässt sich anlegen,
    indem man alle Nachbarn erzeugt und einzelne Pfeile umdreht.
121. **Regel und Parameter.** Die Regel muss in der Registry stehen und für Kanten gelten (Gruppenregeln wie `Sum` →
    Meldung „gilt für Gruppen, nicht für Kanten“). `Params = null` → `IRule.CreateDefaultParams()` (z. B. `Difference`
    k = 1). Geprüft wird über `IRule.Validate` an einer Probekante (Meldung mit Präfix „Ungültige Parameter für „Differenz“:
    …“). Jede erzeugte Kante bekommt eine eigene Kopie der Parameter.
122. **Fehler statt Ausnahmen.** Eingabefehler (unbekannte Regel, Gruppenregel, ungültige Parameter, Springerzug auf
    Dreieck/Sechseck, ungültiger Abstand, unbekannte Knoten) werden gesammelt und gemeinsam gemeldet; es entstehen dann
    keine Kanten. Nur `null`-Argumente werfen. Grund: wie Solver und Erklär-Modus (Nr. 37, 93) – die Oberfläche zeigt
    Meldungen, statt Ausnahmen abzufangen.
123. **Reihenfolge und Meldungen.** Kanten sind nach dem Knoten des Paars sortiert, der in Leserichtung zuerst kommt,
    dann nach dem zweiten – unabhängig von der Richtungsregel (bei `ReverseReadingOrder` also nach To, dann From).
    Meldungen: „12 Kanten „Ungleich“ erzeugt (direkte Nachbarn (gemeinsame Kante)). 4 Knotenpaare hatten bereits eine
    solche Kante.“, „Keine Kanten erzeugt: Kein Knotenpaar erfüllt die Abstandsregel „Abstand 1,5“.“ (Dezimalkomma wie
    Nr. 45, ohne ICU-Kulturdaten). `DistanceRule.Description` liefert die Texte für die spätere Auswahlliste.
124. **Kanten-Ids mindestens 1 (Review-Befund).** `Puzzle.NextEdgeId()` ist „größte Id + 1“ und kann bei einer
    ungeprüft geöffneten Datei (Nr. 57) mit Kanten-Ids ≤ 0 selbst eine Id ≤ 0 liefern (z. B. −4 bei einer vorhandenen Id
    −5); `PuzzleValidator` behandelt Ids ≤ 0 als Fehler (Nr. 66). Der Generator beginnt deshalb bei
    `Math.Max(1, NextEdgeId())`. Vorhandene ungültige Ids werden **nicht** gemeldet oder verweigert (anders als doppelte
    Knoten-Ids, Nr. 118): Die neuen Ids sind auch dann eindeutig und gültig (alle liegen über der größten vorhandenen Id),
    die Prüfmeldungen zeigen den alten Fehler weiterhin an, und der Generator erzeugt keinen neuen. Doppelte Kanten-Ids
    stören den Generator nicht, weil er nur über die Endknoten vergleicht.
    `Puzzle.NextEdgeId()` selbst wurde nicht geändert, weil der Editor es ebenfalls nutzt (die App war im Branch des
    Generators nicht zu ändern); dort besteht dieselbe Lücke für einzeln gezogene Kanten sowie für
    `NextNodeId`/`NextGroupId` – offen, siehe `docs/progress.md`, Paket 5. (Für den Editor erledigt durch Nr. 166.)

### Beispielrätsel

125. **Erzeugung als Testcode, statische Dateien.** `ExamplePuzzleGenerator` (Explainer-Tests, weil dort Solver und
    Erklär-Modus verfügbar sind) läuft nur, wenn `GRAPHDOKU_GENERATE_PUZZLES` auf ein Ausgabeverzeichnis zeigt, sonst
    kehren die fünf Facts sofort zurück (im normalen Lauf „bestanden“). Aufruf z. B.
    `GRAPHDOKU_GENERATE_PUZZLES=/tmp/out dotnet test tests/GraphDoku.Explainer.Tests -c Release --filter
    "FullyQualifiedName~ExamplePuzzleGenerator.Killer"`, danach die Datei nach `puzzles/` kopieren. Kein `[Fact(Skip)]`,
    weil sich übersprungene Tests in xUnit 2 nicht gezielt starten lassen; keine eigene Kategorie, weil dafür die CI einen
    Filter bräuchte. Feste Seeds (5001–5005); CP-SAT rechnet mit mehreren Threads, ein neuer Lauf ist deshalb nicht
    zwingend bitgleich. Der normale Testlauf erzeugt nichts, er lädt und prüft nur.
126. **Verfahren.** (1) Zufällige Lösung: Struktur ohne Givens, zufällige Startwerte setzen, solange CP-SAT eine Lösung
    findet, dann lösen. (2) Killer-Käfige bzw. Kropki-Punkte werden aus dieser Lösung abgeleitet. (3) Givens: entweder so
    lange an dem Knoten der ersten Verzweigung des Erklär-Modus den Lösungswert eintragen, bis die Erklärung ohne
    Verzweigung auskommt (Killer), oder vom voll ausgefüllten Gitter ausgehen (übrige). (4) Givens in zufälliger
    Reihenfolge entfernen, solange die Erklärung ohne Verzweigung bleibt; mehrere Durchläufe, das Ergebnis mit den
    wenigsten Givens gewinnt (Killer: 40 Käfigaufteilungen). **Kriterium „ohne Verzweigung erklärbar“ ist strenger als
    „eindeutig“** (die Logik schließt nur sicher, ohne Verzweigung erklärt = eindeutig, Nr. 90). Grund: Die Beispiele sollen
    den Erklär-Modus vorführen, ohne dass er raten muss. Folge: etwas mehr Givens als für bloße Eindeutigkeit nötig wären
    (vor allem beim Dreiecks-Sudoku: 33). Gespeichert wird nur nach erfolgreicher Prüfung (`PuzzleValidator` leer,
    `CountSolutions` eindeutig).
127. **Killer-Sudoku.** Klassisches 9×9 (Zeilen/Spalten/Blöcke `AllDifferent`, Beschriftungen wie die Vorlage) plus
    28 Käfige: zusammenhängend (gemeinsame Kanten), 2–4 Zellen (der Generator erlaubt bis 5), ohne doppelte Ziffer,
    `Sum` mit `target` und Standard `allowRepeats = false` (in der Datei weggelassen). Ein Killer ganz ohne Givens war
    mit dem Kriterium aus Nr. 126 in 40 Versuchen nicht zu finden (der Erklär-Modus kennt keine Killer-Techniken wie
    die 45er-Regel); das beste Ergebnis hat **3 Givens** („wenige“ laut Auftrag). Beschriftung „Käfig n“ (ohne Summe, damit sie beim Ändern der Zielsumme nicht
    veraltet), eigene Farbe je Käfig aus der Editor-Palette (Blau, Orange, Grün, Rot, Violett, Türkis, Pink), benachbarte
    Käfige verschieden gefärbt (gierige Färbung, Test prüft das). Mit eigener Farbe bekommen Käfige im Editor Fläche und
    farbigen Umriss (Nr. 59/61) – zuerst gewählte Pastellfarben waren im Rendering kaum zu sehen und wurden durch die
    Palettenfarben ersetzt (die Auswahlliste im Panel zeigt sie so auch an).
128. **Kropki-Sudoku.** Klassisches 9×9 mit **allen** Punkten der Lösung: schwarzer Punkt = `Ratio` mit `factor` 2,
    weißer Punkt = `Difference` mit `k` 1, `mode` `exact`. Ein Paar 1–2 erfüllt beides und bekommt den schwarzen Punkt.
    **Keine negative Bedingung** (fehlender Punkt sagt nichts aus; laut Auftrag nicht verlangt und mit dem Katalog nur über
    viele Zusatzkanten ausdrückbar). Punkte nur zwischen Kantennachbarn, höchstens einer je Paar (mit dem Kanten-Generator
    bestimmt). 2 Givens.
129. **Hexadoku.** 16×16, globaler Bereich **0–15** (Anzeige 0–9/A–F, Nr. 32), Zeilen, Spalten und 4×4-Blöcke als
    **`CoverRange`** ohne Parameter (Standard = Rätselbereich) – so kommt `CoverRange` in einem Beispiel vor. 90 Givens.
130. **Dreiecks-Sudoku (Tridoku-ähnlich, eigene Regeln).** Großes, nach oben zeigendes Dreieck der Seitenlänge 9:
    Zeile y = 0..8 hat die Zellen x = −y..y (2y + 1 Dreiecke; die Randzellen x = ±y zeigen nach oben, Ausrichtung nach
    `GridGeometry.IsUpTriangle`). Regeln:
    - 9 Regionen „Dreieck 1–9“ (`CoverRange`, Werte 1–9): die Dreiecke der Seitenlänge 3 eines um den Faktor 3
      vergrößerten Dreiecksgitters (6 zeigen nach oben, 3 nach unten); eine Zelle gehört zu der Region, die ihren
      Schwerpunkt enthält. Farben Blau/Orange abwechselnd (Regionen berühren sich nur über Kanten zwischen Auf- und
      Ab-Regionen).
    - 3 Außenseiten „Linke Seite“, „Rechte Seite“, „Grundseite“ (`AllDifferent`): je die 9 Dreiecke mit einer Kante auf
      der Seite.
    - Sich berührende Dreiecke (gemeinsame Ecke) **verschiedener** Regionen sind verschieden: 168 `NotEqual`-Kanten,
      erzeugt mit dem Kanten-Generator (`WithDiagonals`), Paare innerhalb einer Region weggelassen (dort gilt ohnehin
      „alle verschieden“). Zuerst wurden nur Kantennachbarn verbunden (27 Kanten); dann brauchte das Rätsel ohne
      Verzweigung 52 Givens. Mit der Eckenregel (wie das Berührungsverbot im Tridoku) sind es 33.
    Das Original-Tridoku (weitere Linienregeln) wurde bewusst nicht nachgebaut; die Regeln oben sind vollständig in der
    Datei beschrieben und vom Solver geprüft.
131. **Zusätzlich: Thermo-Sudoku 6×6** (optional laut Auftrag): Werte 1–6, Zeilen, Spalten und Blöcke 2 × 3
    (`AllDifferent`), 3 Thermometer (je 3 Zellen, orthogonal, steigende Lösungswerte), 4 „Kleiner als“-Kanten zwischen
    Nachbarn außerhalb der Thermometer, 5 Givens. Damit kommen alle acht Katalogregeln in den Beispielen vor.
132. **Namen und Dateinamen** deutsch und ohne Umlaute im Dateinamen: „Killer-Sudoku“, „Kropki-Sudoku“,
    „Hexadoku 16x16“, „Dreiecks-Sudoku“, „Thermo-Sudoku 6x6“.

## Kanten-Generator und Regeln im Editor (Paket 5, Editor-Teil)

133. **Generator als modaler Dialog „Kanten erzeugen“** (Bearbeiten → Kanten erzeugen …, Schaltfläche „Erzeugen …“
    rechts neben der Kantenregel, Strg/Cmd+E), nicht als Panel. Grund: Das Panel rechts gehört der Auswahl (Knoten,
    Kante, Gruppe); modal kann sich das Rätsel während der Vorschau nicht ändern, und die Auswahl beim Öffnen ist die
    Knotenmenge „Ausgewählte Knoten“. Die Logik liegt in `EdgeGeneratorViewModel`, der Dialog kommt über
    `IDialogService.ShowEdgeGeneratorAsync` (liefert nur „bestätigt“; eingefügt wird mit `EdgeGeneratorViewModel.Apply`),
    in Tests ersetzt.
134. **Vorschau bei jeder Änderung:** `EdgeGenerator.Generate` auf dem aktuellen Rätsel (auch beim freien Abstand mit
    256 Knoten unkritisch). Eigene Vorschautexte im Futur („144 neue Kanten „Ungleich“ (…). 4 Knotenpaare werden
    übersprungen (Kante bereits vorhanden).“), weil die Meldung des Generators „erzeugt“ sagt; nach dem Einfügen zeigt
    die Statuszeile die Meldung des Generators. Farben wie die Lösen-Leiste: grün = Kanten entstehen, gelb = nichts zu
    tun (kein passendes Paar oder alle vorhanden), rot = Fehler mit Liste. „Kanten erzeugen“ ist nur aktiv, wenn
    mindestens eine Kante entsteht; ohne neue Kanten gibt es keinen Undo-Schritt.
135. **Ein Undo-Schritt:** `AddGeneratedEdgesCommand` hält das `EdgeGenerationResult`; Ausführen/Wiederholen =
    `result.ApplyTo`, Rückgängig = genau die Kanten mit den Ids des Ergebnisses entfernen (Nr. 116). `Apply` erzeugt
    unmittelbar vor dem Einfügen noch einmal, damit nie eine veraltete Vorschau eingefügt wird.
136. **Abstandsregeln in der Liste:** Der Springerzug erscheint nur auf dem Quadratgitter; auf Dreieck/Sechseck steht
    stattdessen ein Hinweis („… Ein freier Abstand kann ihn ersetzen.“) – die Prüfung im Generator bleibt als Schutz.
    Freier Abstand: „Abstand [min] bis [max]“ (max leer = genau min) und „Toleranz“ (Standard 0,000001), Dezimalkomma
    oder -punkt; ein Hinweis nennt typische Abstände des jeweiligen Gitters. Unlesbare Zahlen und ungültige
    Parametertexte erscheinen als Fehler in der Vorschau.
137. **Regel und Parameter:** Voreinstellung ist die Kantenregel der Werkzeugleiste (daher die Schaltfläche direkt
    daneben). Die Parameterfelder sind dieselben wie im Eigenschaften-Panel (`RuleParameterViewModel`; die
    Feldvorlage wurde als `RuleParameterItem` nach `App.axaml` verschoben und wird von beiden Fenstern benutzt).
138. **Knotenmenge:** Voreinstellung „Ausgewählte Knoten (n)“, wenn beim Öffnen mindestens zwei Knoten ausgewählt
    sind, sonst „Alle Knoten (n)“; mit weniger als zwei ausgewählten Knoten ist „Ausgewählte Knoten“ gesperrt.
139. **Richtung** erscheint nur bei gerichteten Regeln („Kleiner als“): „Werte steigen in Leserichtung (nach rechts bzw.
    unten)“ = `ReadingOrder`, „Werte fallen in Leserichtung (steigen nach links bzw. oben)“ = `ReverseReadingOrder`.
140. **Alle Katalogregeln anlegbar – ohne neuen Bedienweg für die Reihenfolge.** Die Bedienung aus Paket 3 (Nr. 51, 56)
    deckt die Spezifikation bereits ab: Thermometer-Reihenfolge = Auswahlreihenfolge (Klick, dann Shift-Klicks; Nummern
    an den Knoten; Rahmenauswahl zeilenweise), „Reihenfolge umkehren“, Nummern und Kolben auf der Zeichenfläche;
    „Kleiner als“ = Ziehrichtung, „Richtung umkehren“, im Generator die Richtungsregel. Eine weitergehende Umordnung
    einzelner Knoten einer bestehenden Gruppe gibt es nicht (Gruppe aus neuer Auswahl erstellen). Ein Test legt alle
    acht Regeln im Editor an und löst das Ergebnis.
141. **Zielsummen auf der Zeichenfläche** für jede sichtbare (oder gewählte) `Sum`-Gruppe, nicht nur für „Käfige“: in
    der Zelle, die in Leserichtung zuerst kommt (`EditorGeometry.RowWiseOrder`), auf dem Quadratgitter oben links in der
    Zellecke, auf Dreieck/Sechseck zur oberen linken Ecke hin eingerückt; Schrift 0,2 Zellbreiten (Dreieck 0,13), 8–18 px,
    halbfett auf weißem, leicht durchscheinendem Grund; „?“ bei fehlendem oder ungültigem `target`; unter 6 px
    Knotenradius keine Summen. Ausgeblendete Gruppen zeigen keine Summe (wie ihre Hervorhebung). **Gruppenbeschriftungen
    werden nicht gezeichnet:** Bei Vorlage und Beispielen hätte jede Zelle drei Beschriftungen („Zeile 1“, „Spalte 1“,
    „Block 1“); sie stehen in der Gruppenliste.
142. **Lesbarkeit der Beispiele (nach Ansehen der Bilder):** Kantenmarken (Differenz-/Verhältnispunkte, Pfeile) werden
    nach den Knoten gezeichnet (vorher deckten die Kreise die Kropki-Punkte zur Hälfte ab), die Linien weiterhin davor.
    „Ungleich“-Linien ohne Auswahl dünner und heller (#8a8a8a, 1,3 px statt #4a4a4a, 1,8 px; das Dreiecks-Sudoku mit 168
    solchen Kanten war unruhig). Pfeile höchstens 0,42 × Spalt zwischen den Kreisen (mindestens 5 px) und mit weißem
    Rand. Thermometer: Linie 0,95 statt 0,7 Knotenradien breit, Kolben 1,3 statt 1,18 Knotenradien.
143. **Statuszeile ohne veraltete Startmeldung:** `EditorViewModel.LoadPuzzle` (und der Konstruktor) setzen eine zum
    Dokument passende Grundmeldung (leeres Rätsel: „Leeres Rätsel „Name“ (Gitter, Werte a..b). Mit „Knoten setzen“ …“,
    sonst „Rätsel „Name“: n Knoten, m Kanten, g Gruppen.“); Neu, Öffnen und Vorlage im Hauptfenster ersetzen sie wie
    bisher durch ihre eigene Meldung. Die feste Meldung „Neues Rätsel. …“ im Konstruktor des Hauptfensters entfällt.
144. **Eigener Wertebereich pro Knoten** (offen aus Paket 3, Nr. 56): im Auswahl-Panel zwei Zahlenfelder mit
    „Bereich setzen“ und „Globalen Bereich verwenden“ für alle ausgewählten Knoten – ein Undo-Schritt pro Schaltfläche
    (`SetNodeDomainsCommand`), nicht pro Tastendruck, damit halbe Eingaben („1“ auf dem Weg zu „12“) keine Zwischenstände
    erzeugen. Die Felder zeigen den gemeinsamen eigenen Bereich, sonst den globalen, und werden nur neu gefüllt, wenn sich
    die Bereiche der Auswahl ändern. Givens außerhalb des neuen Bereichs werden nicht abgelehnt, sondern in der
    Statuszeile genannt (die Prüfmeldungen zeigen sie ohnehin). Prüfung wie beim globalen Bereich
    (`NewPuzzleViewModel.CheckDomain`).
145. **Schaltfläche „Erzeugen …“ statt „Kanten erzeugen …“ in der Werkzeugleiste:** Mit dem längeren Text brach die
    Werkzeugleiste schon bei 1280 px Breite um („Einpassen“ in einer zweiten Zeile, im Rendering gesehen). Der volle
    Name steht im Menü und im Tooltip.
146. **Akzeptanztest nicht erweitert:** `SolveIntegrationTests` öffnet bereits jede Datei in `puzzles/` über
    `OpenFileAsync`, löst sie mit `SolveCommand` und prüft mit `CheckUniquenessCommand` die Eindeutigkeit (alle außer
    `klassisch-leer.json` eindeutig); das deckt die Paket-5-Akzeptanz auf ViewModel-Ebene. Neu ist ein Rendering-Test,
    der jede Datei über „Öffnen“ rendert.
147. **Kantenmarken nicht auf Knoten zwischen den Enden** (Review-Befund zu Nr. 142): Seit die Marken über den Knoten
    liegen, verdeckte die Marke einer Kante zum übernächsten Knoten (z. B. freier Abstand 2) den Wert des Knotens
    dazwischen. `EditorGeometry.EdgeMarkPosition` legt die Marke in die Mitte, wenn diese frei ist; liegt die Mitte in
    einem anderen Knotenkreis, kommt sie in das freie Stück der Strecke, dessen Mitte der Streckenmitte am nächsten
    liegt (bei Gleichstand das zum Anfang hin, also zwischen erstem und mittlerem Knoten); ohne freies Stück bleibt sie in
    der Mitte. Die Pfeilgröße richtet sich nach diesem freien Stück. Grenze: Haben dieselben Knoten zusätzlich Kanten zu
    ihren direkten Nachbarn mit Marke, liegen beide Marken im selben Spalt übereinander (kein Ausweichen zwischen
    Marken).
148. **Zielsummen auf dem Sechseckgitter außerhalb des Knotenkreises** (Review-Befund zu Nr. 141): Der Kreis füllt die
    Wabe fast aus (Radius 0,38, Innenkreis 0,5); 42 % zur Ecke lagen im Kreis direkt neben dem Wert. Jetzt wird das
    Etikett von der Mitte Richtung linker oberer Ecke gerade so weit gerückt, dass es den Kreis nicht berührt, höchstens
    bis zur Ecke. Quadrat und Dreieck unverändert.
149. **Kanten-Ids des Generators ohne Überlauf** (Review-Befund): Die erste Id wird in `long` berechnet (größte Id + 1,
    mindestens 1). Reichen die Ids bis `int.MaxValue` nicht für alle neuen Kanten, liefert der Generator einen Fehler
    („keine freien Kanten-Ids mehr übrig …“) statt Kollision oder negativer Ids; die Vorschau zeigt ihn, „Kanten erzeugen“
    ist gesperrt. Zusätzlich fängt `EdgeGeneratorViewModel.Apply` eine `InvalidOperationException` aus `ApplyTo` ab und
    meldet sie in der Statuszeile (vorher hätte sie die App beendet). `Puzzle.NextEdgeId()` selbst bleibt unverändert
    (offen aus dem Core-Teil).
150. **A–F-Darstellung nach dem Rätselbereich** (Review-Befund zu Nr. 32/144): Ob Werte eines Knotens als 0–F angezeigt
    und eingegeben werden, entscheidet `ValueText.IsHex(puzzle, node)`: globaler Bereich genau 0–15 und eigener Bereich
    des Knotens innerhalb 0–15. Ein eigener Bereich 8–15 im Hexadoku bleibt also bei A–F (vorher Rückfall auf Dezimal,
    „D“ wurde stillschweigend ignoriert). Umgekehrt zeigt ein Knoten mit eigenem Bereich 0–15 in einem Rätsel mit
    anderem globalem Bereich jetzt dezimal (vorher A–F). Hex-Zeichen außerhalb des eigenen Bereichs werden wie bei der
    Dezimaleingabe abgelehnt und gemeldet („Der Wert 3 liegt außerhalb des Wertebereichs 8..15.“).
151. **Deutsche Hinweise zu Parametern ohne JSON-Bezeichner** (Review-Befund): `ParameterTexts.Hint` ersetzt die
    Core-Beschreibung, wo sie JSON-Werte nennt: Differenz/`mode` „genau k oder mindestens k; Standard: genau k“,
    Summe/`allowRepeats` „dürfen sich Werte wiederholen? Standard: nein“ (vorher „Standard: false“ neben dem Häkchen).
    Zuordnung über Name und Core-Beschreibung, damit gleichnamige Parameter eigener Regeln ihre Beschreibung behalten.
    Die Beschreibungen im Core bleiben unverändert (sie nennen bewusst die Dateiwerte).

## Zellformen (Paket 7)

152. **Darstellung der Knoten als reine Ansichtseinstellung, nicht in der Rätseldatei und nicht dauerhaft gespeichert.**
     `EditorViewModel.NodeShape` (`NodeShape.Circle` = Standard laut Spezifikation, `NodeShape.Cell` = Zellen in der Form
     des Gitters: Quadrat, Dreieck nach oben/unten je nach `GridGeometry.IsUpTriangle`, Sechseck – die Umrisse kommen aus
     `GridGeometry.GetCellVertices`). Wie das Ein-/Ausblenden von Gruppen: kein Undo-Schritt, kein „ungespeichert“, kein
     `ContentChanged`; Lösung und Erklärung bleiben stehen. Die Einstellung gilt für die Sitzung und bleibt beim Öffnen,
     bei Neu und Vorlage erhalten. **Nicht in der Datei**, weil `PuzzleSerializer` unbekannte Felder ablehnt: Ein neues
     Feld machte mit Paket 7 gespeicherte Dateien für ältere Programmstände unlesbar (Akzeptanz „Speicherformat bleibt
     kompatibel“); außerdem ist die Darstellung keine Eigenschaft des Rätsels. **Nicht als App-Einstellung gespeichert**
     (die Aufgabe nennt das als optional): Dafür bräuchte die App erst einen Einstellungsdienst mit Dateiort pro
     Betriebssystem, Fehlerbehandlung und Testisolation – mehr Fläche als das Paket selbst; nach einem Neustart zeigt die
     App wieder Kreise. Belegt durch einen Test pro Beispieldatei: speichern, umschalten, speichern → byteweise gleiche
     Dateien; nur die Felder des Formats; Öffnen in der Zelldarstellung ergibt dasselbe Modell wie das Original.
153. **Umschalter im Menü Ansicht und rechts im Statusbereich, nicht in der Werkzeugleiste.** Menü: zwei Einträge als
     Optionsgruppe („Knoten als Kreise“, „Knoten als Zellen (Quadrate|Dreiecke|Sechsecke)“, der Text nennt die Form des
     geladenen Gitters), `IsChecked` mit `Mode=TwoWay` (Nr. 114). Statusbereich: „Knoten als ○ Kreise ○ Zellen“ unten
     rechts, wie die Ansichtsumschalter gängiger Programme. Ein Umschalter „Zellen“ in der Werkzeugleiste ließ sie bei
     1280 px umbrechen (im Rendering gesehen; zweite Zeile mit nur dieser Schaltfläche, Zeichenfläche etwa 35 px niedriger, vgl.
     Nr. 145) – das sollte Paket 5 gerade vermeiden. Dazu der Befehl `ToggleNodeShapeCommand` (ohne Tastenkürzel) und eine
     Statusmeldung beim Umschalten („… Das Rätsel und die Datei ändern sich dadurch nicht.“).
154. **Treffertests in der Zelldarstellung:** Ein Knoten wird überall in seinem Zellumriss getroffen
     (`EditorGeometry.PolygonContains`, konvex, Rand eingeschlossen; das ist auf allen drei Gittern dieselbe Zelle wie
     `GridGeometry.ToNearestGridPoint`, Test mit 2000 Zufallspunkten pro Gitter). Weil Zellen die Fläche lückenlos füllen,
     gäbe es sonst keine Stelle mehr, an der man eine Kante anklicken kann: Im Werkzeug „Auswahl“ haben Kanten an ihren
     sichtbaren Stegen über den Zellrändern und an ihrer Marke **Vorrang** vor der Zelle (Toleranz 6 px wie bisher),
     Shift-Klick ausgenommen (hängt wie bei Kreisen eine Zelle an). Im Innern (Wert, Kandidaten) und in den Ecken trifft
     man immer die Zelle. Kante ziehen, Knoten setzen (Klick in eine vorhandene Zelle wählt sie), Rahmenauswahl
     (Mittelpunkte) und Verschieben arbeiten unverändert über diese Treffertests. Bei Kreisen bleibt alles wie bisher.
155. **Verschieben rastet den Mittelpunkt der gepackten Zelle ein, nicht den Zeiger** (beide Darstellungen): Beim Start
     des Verschiebens wird der Versatz Zeiger − Mittelpunkt gemerkt und vor `EditorGeometry.SnapForMove` abgezogen. In der
     Zelldarstellung kann man eine Zelle direkt am Rand packen; ohne Versatz sprang sie nach den 4 px der Ziehschwelle in
     die Nachbarzelle. Bei Kreisen (Radius 0,38) war der Effekt kleiner, aber derselbe; Bedienung aus der Kreismitte (alle
     bisherigen Tests) ist unverändert.
156. **Zeichnen der Zellen:** Reihenfolge Raster → Zellflächen (weiß bzw. Farbe des Zustands, dünner heller Rand #b4b4ae) →
     Gruppenflächen, -umrisse und Thermometer → Kantenstege → Rahmen und Inhalt der Zellen → Kantenmarken, Summen,
     Nummern, „Raten“. Zellen mit eigener Farbe (Auswahl hellblau, Erklär-Modus gelb/orange/grün, abweichende Lösung
     orange) werden über den Gruppenflächen ein zweites Mal leicht eingerückt gefüllt, sonst mischte sich z. B. das Gelb
     des aktuellen Schritts mit dem Grün der Regelgruppe (im Rendering gesehen); Rahmen der Auswahl bzw. Hervorhebung
     liegen innerhalb der Zelle (zentrische Verkleinerung, `EditorGeometry.Inset`), der rote Rahmen eines Knotens ohne
     Kandidaten innen neben dem gelben statt als Ring außen. Wert, Kandidatenraster, Nummern und „Raten“ beziehen sich auf
     einen **Inhaltsradius** (`EditorGeometry.ContentRadius`): Quadrat 0,42, Sechseck 0,4 (das 4 × 4-Raster muss zwischen
     die Ecken passen), Dreieck 0,24 (Innenkreis 0,29) – Werte und Kandidaten werden in Zellen etwas größer als in Kreisen.
     Thermometer: Linie 0,7 statt 0,95 Inhaltsradien, Kolben 1,0 statt 1,3 (bleibt in der Zelle, liegt durchscheinend auf
     ihr). Zielsummen auf dem Sechseckgitter halten nur Abstand zur Schrift in der Mitte (0,55 Inhaltsradien) statt zum
     ganzen Kreis und bleiben so in der Zelle.
157. **Kanten in der Zelldarstellung als Stege über den Zellrändern** statt als Linie von Mitte zu Mitte: an jeder Stelle,
     an der die Strecke einen Zellrand kreuzt (`EditorGeometry.BorderCrossings`, Zelle für Zelle über den Austritt aus
     dem konvexen Umriss), ein Steg von einem halben Inhaltsradius Länge, ohne die Freihaltekreise (0,8 Inhaltsradien,
     `ClearRadius`) um Enden und andere Knoten (`CellEdgeStubs`). Bei Nachbarzellen ist das ein kurzer Steg mittig über
     der gemeinsamen Seite, bei Zellen, die sich nur in einer Ecke berühren, einer über der Ecke, bei längeren Kanten einer
     pro gekreuztem Rand. Marken (Kropki-Punkte, Pfeile) setzt weiterhin `EdgeMarkPosition`, jetzt mit dem
     Freihalteradius – bei Nachbarzellen genau auf der Mitte der gemeinsamen Seite, bei Kanten über einen Knoten hinweg
     auf dem ersten freien Rand. Grund (im Rendering gesehen): Mit den vollen freien Stücken liefen im Dreiecks-Sudoku
     (168 „Ungleich“-Kanten, viele zwischen Dreiecken, die sich nur in einer Ecke berühren) Linien durch die Ecken fremder
     Zellen und durch deren Kandidaten, an jeder Gitterecke entstand ein Stern. Der Treffertest für Kanten (Nr. 154)
     benutzt genau diese Stege.
158. **Kanten in der Zelldarstellung nur genau auf ihrem Steg treffen; Ziehen vom Steg verschiebt die Zelle**
     (Review-Befund zu Nr. 154): Bisher griff der Treffertest am ganzen Steg plus 6 px Toleranz zu beiden Seiten. Im
     Dreiecks-Sudoku (168 „Ungleich“-Kanten, an jeder Gitterecke drei Stege, Stege über den Schrägseiten der Zelle
     zwischen zwei Endzellen) trafen dadurch bei eingepasster Ansicht im Fenster 1280 × 800 rund 28 % der Klicks in
     Zellen eine Kante, knapp die Hälfte der Klicks in die Ecken und einige auf die Kandidaten – entgegen Nr. 154.
     Jetzt zählt ein Steg nur rund um die Stelle, an der die Kante den Zellrand kreuzt, und höchstens eine halbe
     Steglänge weit (`min(6 px, Steglänge/2)`, `EditorGeometry.CellEdgeStubTargets` liefert die Kreuzungsstelle);
     im Freihaltekreis um die Mitte der Zelle (Wert, Kandidaten) trifft man nie eine Kante. Unter mehreren Stegen
     (drei kreuzen sich an einer Dreiecksecke) gewinnt wie bisher der mit dem kleinsten Abstand zur Linie. Marken
     (Kropki-Punkte, Pfeile) unverändert. Nachgemessen: Klicks bei 85 % zur Ecke und im Freihaltekreis treffen im
     Dreiecks-Sudoku bei einfacher und doppelter Vergrößerung immer die Zelle; jede der 168 Kanten bleibt über einen
     ihrer Stege wählbar (Tests). Präzisierung von Nr. 154: „in den Ecken trifft man die Zelle“ gilt außerhalb des dort
     gezeichneten Stegs – wer genau auf den Steg über einer Ecke klickt, wählt die Kante, sonst wäre eine Kante zwischen
     Dreiecken, die sich nur in einer Ecke berühren, gar nicht wählbar (Kanten sind nur per Klick wählbar). Zusätzlich
     gilt ein Druck auf einen Steg erst beim Loslassen ohne Bewegung als Klick auf die Kante; wird gezogen, verschiebt
     das die Zelle unter dem Zeiger (Nr. 155: Packen am Rand). Die kleinen Sterne an den Gitterecken des
     Dreiecks-Sudokus bleiben: Es sind die Stege der drei Kanten zwischen gegenüberliegenden Dreiecken, die sich dort
     kreuzen (Länge je ein halber Inhaltsradius); jede andere Darstellung dieser Kanten verlöre die Zuordnung. Die
     frühere Angabe in `progress.md`, die Sterne seien „behoben“, war ungenau – behoben waren die langen Linien.
159. **Rahmenauswahl mit Strg/Cmd + Ziehen auch auf einem Knoten** (Review-Befund zu Nr. 154): In der Zelldarstellung
     füllen die Zellen die Fläche lückenlos, ein Rahmen ließ sich nur außerhalb aller Zellen beginnen – innere
     Bereiche (z. B. der mittlere Block eines 9 × 9) waren nicht per Rahmen wählbar, bei eingepasstem Hexadoku gab es
     kaum freie Fläche. Jetzt beginnt Ziehen mit gedrückter Strg-/Cmd-Taste immer einen Rahmen (mit zusätzlich Shift:
     zur Auswahl hinzufügen, wie beim Rahmen auf freier Fläche); Auswahlkriterium bleiben die Mittelpunkte. Strg/Cmd-Klick
     ohne Ziehen wirkt wie ein normaler Klick (mit Shift: umschalten). Gilt in beiden Darstellungen (bei Kreisen bisher
     ohne Bedeutung), damit die Bedienung nicht von der Ansicht abhängt. Strg/Cmd statt Shift, weil Shift auf einem
     Knoten schon beim Drücken umschaltet (Thermometer-Reihenfolge) und Ziehen ohne Taste weiterhin verschiebt. Der
     Werkzeughinweis nennt in der Zelldarstellung „Strg/Cmd + Ziehen wählt per Rahmen“ (gleich lang wie bisher, bleibt bei
     1280 px einzeilig).
160. **`puzzles/klassisch-leer.json` bleibt als absichtlich mehrdeutige Vorlage** (Abschluss-Review, Abweichung von der
     Paket-5-Akzeptanz „Jedes Beispielrätsel ist lösbar und eindeutig“): Die Datei stammt aus Paket 1 (leeres
     klassisches 9 × 9 ohne Givens) und ist bewusst keine Rätselaufgabe, sondern eine Vorlage zum Selbstausfüllen
     (README). Sie bleibt in `puzzles/`, weil sie dort mehrfach als Testdatei dient: als mehrdeutiges Rätsel für die
     Paket-2-Akzeptanz (Solver erkennt Mehrdeutigkeit), für den Erklär-Modus (Status „mehrdeutig“ mit Verzweigungen)
     und in den App-Tests für Rahmenauswahl und Rendering. Die Akzeptanz gilt für alle übrigen Dateien; die Tests
     (`ExamplePuzzleTests`, `SolveIntegrationTests`, `ExamplePuzzleExplanationTests`) nehmen genau diese eine Datei aus
     und erwarten für sie ausdrücklich „mehrdeutig“, sodass eine versehentlich mehrdeutige weitere Datei auffiele.
161. **Einzelne UTF-16-Surrogate in JSON-Texten sind ein Formatfehler** (Abschluss-Review): `JsonDocument` nimmt
     maskierte einzelne Surrogate wie `"\uD800"` an, `GetString()` bzw. `JsonProperty.Name` warfen dann aber
     `InvalidOperationException` – an der dokumentierten `PuzzleFormatException` vorbei, in der App unbehandelt beim
     Öffnen. `PuzzleSerializer.Deserialize` prüft jetzt vor dem Lesen alle Feldnamen und Zeichenketten des Dokuments
     und meldet jede ungültige Stelle mit Pfad (z. B. „Das Feld „edges[0].params.mode“ enthält eine ungültige
     Zeichenfolge (einzelnes UTF-16-Surrogat, z. B. \uD800)“). Gilt auch mit `validate: false`. Korrekt gepaarte
     Surrogate (Emoji) werden normal gelesen.
162. **Gitternachbarn ohne int-Überlauf** (Abschluss-Review): `GridGeometry` rechnet Nachbarn, Eckennachbarn und
     Springerzüge in `long` und lässt Ziele außerhalb des int-Bereichs weg. Vorher lief z. B. `int.MaxValue + 1` auf
     `int.MinValue` über, und der Kanten-Generator verband mit „direkt benachbart“ Zellen an entgegengesetzten Enden
     (der freie Abstand fand korrekt nichts). Keine Begrenzung der Koordinaten im Validator: Der Editor erzeugt solche
     Werte nie, und mit der Korrektur rechnen beide Generator-Modi auch an den Grenzen übereinstimmend; eine neue
     Grenze hätte bisher gültige Dateien abgelehnt.
163. **`ISolver.CountSolutions` muss die gefundenen Lösungen mitliefern** (Abschluss-Review, Präzisierung von Nr. 36):
     `SolutionCountResult.Solutions` enthält genau die ersten min(`Count`, `MaxStoredSolutions`) Lösungen, jede eine
     vollständige Belegung aller Knoten-Ids. Das steht jetzt an der Schnittstelle, und die Fabrikmethoden
     `Completed`/`Timeout`/`Cancelled` werfen `ArgumentException`, wenn die Anzahl nicht passt (negatives `Count`:
     `ArgumentOutOfRangeException`) – ein späterer eigener Solver, der nur zählt, scheitert so sofort und nicht erst im
     Erklär-Modus. Fehlt in der mitgelieferten Lösung der Wert für den Verzweigungsknoten, endet die Erklärung mit
     „Ohne Raten geht es nicht weiter“ (Status `Stuck`, Solvername in der Meldung) statt mit einer
     Indexausnahme. OR-Tools-Solver und alle Test-Solver erfüllen den Vertrag bereits.
164. **Rendering-Tests mit Graustufen-Textglättung** (Abschluss-Review, App): Auf Linux glättet Skia Text mit
     Subpixel-Farbsäumen (LCD). In einem linux/amd64- und einem linux/arm64-Container (`mcr.microsoft.com/dotnet/sdk:10.0`,
     Schritte wie `.gitea/workflows/ci.yml`) waren dadurch zwei Rendering-Tests rot: Der schwarze Given hatte blaue
     Ränder, die als Lösungsblau zählten, und kleine Käfigsummen („6“) hatten zu wenige ganz dunkle Pixel. Die
     Headless-Testumgebung setzt deshalb für jedes Fenster `TextOptions.TextRenderingMode = Antialias` (Graustufen, wie
     macOS ohnehin rendert), und zwar beim Anwenden der Fenstervorlage, also vor dem ersten Zeichnen – ein später gesetzter
     Wert (z. B. bei `Loaded`) galt erst nach dem nächsten Neuzeichnen und verfälschte Vorher/Nachher-Vergleiche. Die App
     selbst bleibt unverändert (Systemvorgabe). Die Käfigsummen-Prüfung vergleicht zusätzlich die Dunkelheitssumme
     (`Darkness`) mit einer anderen Zelle desselben Käfigs statt einer festen Zahl dunkler Pixel. Danach liefen
     alle CI-Schritte im linux/amd64-Container grün (779 Tests); der echte Ubuntu-Runner steht noch aus.
165. **Meldungsfenster ohne Schaltfläche geschlossen = Abbrechen** (Abschluss-Review, Präzisierung von Nr. 58):
     `ShowDialog<int>` liefert beim Schließen über die Titelleiste, Cmd+W oder Alt+F4 `default(int)` = 0; die Frage nach
     ungespeicherten Änderungen wertete das als „Speichern“ – die Datei wurde überschrieben, und Neu/Öffnen/Beenden ging
     weiter. `MessageWindow.ShowAsync` wartet jetzt auf `ShowDialog<int?>` und liefert ohne Schaltfläche
     `MessageWindow.ClosedResult` (−1, wie im Klassenkommentar schon beschrieben); der Dialogdienst behandelt das wie
     „Abbrechen“. Escape wirkte schon vorher als „Abbrechen“ (`IsCancel`).
166. **Neue Ids im Editor ohne int-Überlauf und mindestens 1** (Abschluss-Review, wie Nr. 149 für den Generator):
     Knoten setzen, Kante ziehen und Gruppe erstellen nehmen die Id aus `EditorViewModel.NextFreeId` („größte Id + 1“ in
     `long`, mindestens 1). Ist die größte Id schon `int.MaxValue` (gültig, Nr. 15), entsteht kein Element; die
     Statuszeile meldet „Keine freie Knoten-Id mehr (die größte vorhandene Id ist 2147483647); bitte die Knoten-Ids in
     der Datei verkleinern.“ (Kanten/Gruppen entsprechend). Vorher liefen `Puzzle.Next…Id()` auf `int.MinValue` über,
     zwei neue Elemente bekamen dieselbe negative Id, und ein Rückgängig entfernte beide. Freie Lücken unterhalb der
     größten Id werden bewusst nicht gesucht (Ids sollen nicht in der Mitte wieder auftauchen). Damit ist auch der offene
     Punkt „Ids ≤ 0“ für den Editor erledigt: Neben ungültigen Ids ≤ 0 beginnt der Editor bei 1. `Puzzle.Next…Id()`
     selbst bleibt unverändert (Core).
167. **Mehrstellige Werteingabe rechnet statt aufzuzählen** (Abschluss-Review): Ob die bisherige Eingabe der Anfang
     eines längeren erlaubten Werts ist, prüfte die App durch Aufzählen aller Werte des Bereichs. Ein ungeprüft
     geöffneter Bereich (Nr. 57) wie 1000000000..2147483647 blockierte so beim Tippen einer „0“ die Oberfläche. Jetzt
     wird geprüft, ob einer der Zahlenbereiche „Präfix + k Ziffern“ ([P·10^k, P·10^k + 10^k − 1], negativ gespiegelt;
     „0“ und „−0“ ohne Fortsetzung) den Schnitt aller beteiligten Bereiche trifft – höchstens 10 Vergleiche. Ein Test
     vergleicht das Ergebnis mit dem Aufzählen über mehrere kleine Bereiche.
168. **Leertaste-Verschieben endet auch bei Fokusverlust** (Abschluss-Review): Ging der Fokus bei gedrückter
     Leertaste an ein anderes Element (Klick auf eine Schaltfläche, Tab) oder das Fenster in den Hintergrund, kam das
     Loslassen nie bei der Zeichenfläche an, und jeder spätere Linksklick verschob die Ansicht. `GraphCanvas` setzt den
     Zustand jetzt auch bei `LostFocus` und beim Deaktivieren des Fensters zurück. Außerdem entfernt: die seit Paket 7
     ungenutzten `EditorGeometry.FreeSegments` und `GraphCanvas.NodeStrokeBrush` (die Code-Stil-Analyse im Build,
     `EnforceCodeStyleInBuild`, bleibt aus – sie wäre eine Änderung an den Build-Regeln aller Projekte und gehört nicht
     zu diesem Review).
169. **Abschluss-Review (Repo): Fortschrittsprotokoll bereinigt, Aussagen nicht nachträglich umgeschrieben.** Erledigte
     Punkte unter „Offen“ werden wie bisher durchgestrichen und mit dem Paket versehen, in dem sie erledigt wurden;
     die Testzahlen „alle grün in Release“ der einzelnen Pakete bleiben als Stand des jeweiligen Pakets stehen (lokal
     auf macOS gemessen) – dass auf Linux bis zum Abschluss-Review zwei Rendering-Tests rot waren, steht im Abschnitt
     „Abschluss-Review – Repo“ und bei Nr. 164. Bekannte Probleme stehen nur bei dem Paket, in dem sie entstanden sind
     (Nr. 158/159 nur bei Paket 7). Nr. 9 ist in der Aussage zu Linux-Systempaketen durch Nr. 48 überholt und
     entsprechend markiert.

## Review von PR #1

170. **Zeitabhängige Solver-Tests robuster** (erster echter Gitea-Lauf, PR #1): Auf dem Gitea-Runner schlugen
     `CountSolutions_reports_timeout_with_the_solutions_found_so_far` und
     `CountSolutions_can_be_cancelled_while_running` in beiden Läufen fehl – im 300-ms-Fenster fand die
     Einzel-Worker-Aufzählung des leeren Sudokus keine einzige Lösung. Lokal (macOS) und im Linux-x64-Container, auch
     mit nur einer CPU, liefen beide grün. Die Runner-Diagnose zeigte die Ursache: Der Runner hat 4 Kerne einer
     älteren CPU (AMD A8-7600) ohne CPU-Kontingent; dort laufen die vier Test-Assemblies und die parallelen
     CP-SAT-Suchen anderer Testklassen gleichzeitig, und 300 ms reichten nicht für die erste Lösung. Gegenmaßnahmen:
     Die Klasse `SolverBehaviorTests` läuft in einer eigenen, nicht parallelen Test-Collection, die beiden Tests
     bekommen 2 s statt 300 ms, und ihre Fehlermeldungen nennen Laufzeit und Kernzahl. Die CI gibt zur Diagnose
     `nproc`, das CPU-Kontingent der cgroup und `lscpu` aus. Danach liefen auf dem Runner alle Tests grün. Das
     Verhalten des Solvers selbst ist unverändert.
171. **Summenregel ohne Rekursion** (Review PR #1): Die Unterstützungssuche der Summenregel (`SumRule`,
     `SupportSearch`) war rekursiv, die Tiefe entsprach der Gruppengröße. Ab etwa 10.000 Knoten in einer Summengruppe
     – vom Validator zugelassen – führte das zu einem nicht abfangbaren StackOverflow, der die App beendete. Die Suche
     läuft jetzt iterativ mit eigenem Stapel. Ergebnisse, Reihenfolge, Begründungstexte und das Suchbudget (ein
     Schritt pro betretener Tiefe) sind unverändert; ein Vergleichstest gegen Brute Force und ein Lauf mit 20.000
     Knoten auf einem Thread mit 512 KB Stack sichern das ab.
172. **Einzelnes Surrogat im Text ist ein Formatfehler** (Review PR #1): `PuzzleSerializer.Deserialize(string)` ließ
     eine `ArgumentException` durch, wenn der übergebene String selbst ein einzelnes UTF-16-Surrogat enthielt (nicht
     die JSON-Maskierung, die schon Nr. 163 abdeckt). Sie wird jetzt wie dokumentiert zur `PuzzleFormatException` mit
     deutscher Meldung. Der Ladeweg der App war nicht betroffen (`File.ReadAllText` ersetzt ungültige Bytes).
173. **Speichern über temporäre Datei** (Review PR #1; Linkauflösung und Name der temporären Datei überholt durch Nr. 181): `PuzzleSerializer.Save` kürzte die Zieldatei mit
     `File.WriteAllText` zuerst auf null; ein Fehler mitten im Schreiben (volle Platte, getrenntes Netzlaufwerk)
     zerstörte so die zuletzt gespeicherte Fassung. Jetzt wird in eine versteckte temporäre Datei im selben
     Verzeichnis geschrieben, auf die Platte geschrieben (Flush) und per `File.Move(…, overwrite: true)` über das Ziel
     gelegt; bei jedem Fehler wird die temporäre Datei gelöscht. Damit das Verhalten sonst gleich bleibt:
     Schreibgeschützte Dateien werden weiter abgelehnt; kann im Verzeichnis keine temporäre Datei angelegt, die
     bestehende Datei aber beschrieben werden, wird wie früher direkt (nicht atomar) überschrieben; symbolische Links
     werden aufgelöst, sodass der Link erhalten bleibt; unter Unix übernimmt die neue Datei die Zugriffsrechte der
     alten. Nicht erhalten bleiben Windows-ACLs und Eigentümer/Gruppe der alten Datei. Eine interne Testnaht prüft
     einen Fehler mitten im Schreiben.
174. **Erklärstatus „Ungültig“ kann Schritte haben** (Review PR #1): Laut Doku-Kommentar hatte
     `ExplanationStatus.Invalid` nie Schritte. Lehnt der Solver das Rätsel aber erst ab, nachdem die Logik nicht
     weiterkam (z. B. eigene Regel ohne OR-Tools-Adapter), liefert die Engine `Invalid` mit den bisherigen Schritten
     und Zuständen. Die App nutzt diese Schritte bereits. Die Doku von `Invalid`, `HasSnapshots` und `GetSnapshot`
     beschreibt jetzt beide Fälle; das Verhalten ist unverändert und per Test festgehalten.
175. **Hidden Singles nur für Katalogregeln** (Review PR #1): Gruppen für Hidden Singles und Zählwidersprüche wurden
     nur am Regelnamen „AllDifferent“/„CoverRange“ erkannt. Eine eigene Regel unter diesem Namen, die z. B.
     Wiederholungen erlaubt, ergab einen falschen Widerspruch („Unlösbar“) oder einen falschen einzigen Platz. Die
     Gruppen verlangen jetzt zusätzlich die Katalogregel, wie schon die Eliminierung fester Werte (Nr. 95). Mit den
     mitgelieferten Regeln tritt der Fall nicht auf.
176. **Kanten-Marken in der Kreisdarstellung treffbar** (Review PR #1): In der Kreisdarstellung traf ein Klick eine
     Kante nur 6 px um die Verbindungslinie, auch wenn der gezeichnete Kropki-Punkt oder Pfeil größer war; weil Knoten
     vor Kanten geprüft wurden, wählte ein Klick auf den Teil eines beschrifteten Punkts über einem Knotenkreis den
     Knoten. Jetzt wird vor den Knoten die gezeichnete Marke selbst geprüft (Punkt mit gemessener Beschriftung im
     gezeichneten Radius, Pfeil genau in seinem Umriss, ohne Toleranz); nach den Knoten gilt wie bisher Linie und
     Marke mit 6 px Toleranz. Zeichnen und Treffertest nutzen dieselben Maße aus der neuen Klasse `EdgeMarks`; das
     gezeichnete Bild ist unverändert. Liegen Marken übereinander, gewinnt jetzt die zuletzt gezeichnete (gilt auch
     für die Zellformen).
177. **Weitere Maustaste während einer Geste – kein Fehler** (Review PR #1): Befund: Ein Rechtsklick während des
     Kantenziehens schließe die Geste ab, weil `PointerReleased` die Taste nicht prüft. Nicht nachvollziehbar:
     Avalonia meldet eine weitere Maustaste bei schon gehaltener Taste als Zeigerbewegung, nicht als Drücken oder
     Loslassen. Ein Fenstertest über den echten Eingabeweg sichert das ab; am Code wurde nichts geändert. Offen
     bleibt, dass ein zweiter Zeiger (Multitouch, Stift) eine laufende Geste beenden kann; dafür müsste die
     Zeichenfläche den Zeiger der Geste mitführen.
178. **Fehler veralteter Läufe als „veraltet“ melden** (Review PR #1): Lösen, Eindeutigkeit prüfen und Erklären
     prüften nur bei Erfolg, ob das Rätsel inzwischen geändert oder ersetzt wurde (Nr. 110). Ein Fehler aus einem
     alten Lauf erschien deshalb als roter Fehler für das neue Rätsel. Jetzt prüft auch der Fehlerzweig zuerst die
     Inhaltsversion und meldet dann dieselbe „veraltet“-Meldung wie bei einem veralteten Ergebnis.
179. **„Öffnen“ fragt nach Änderungen während der Dateiauswahl erneut** (Review PR #1): „Öffnen“ fragte nur vor der
     Dateiauswahl nach ungespeicherten Änderungen. Ist die Auswahl nicht modal (vermutlich bei manchen
     Linux-Desktops), konnten währenddessen gemachte Änderungen ohne Rückfrage ersetzt werden. Der Befehl merkt sich
     jetzt die Inhaltsversion vor der Auswahl; hat sie sich geändert und ist das Dokument geändert, wird vor dem Laden
     erneut gefragt. Bei „Abbrechen“ bleibt das Dokument erhalten.
180. **Regelparameter: Feld und Fehlermeldung passen zusammen** (Review PR #1): Ein ungültiger Text in einem
     ganzzahligen Parameterfeld (z. B. „x“ bei fehlendem optionalem Parameter) galt beim Neuladen des Panels als
     gleichwertig zum Modell und blieb stehen, die Fehlermeldung verschwand aber (z. B. nach „Rückgängig“). Jetzt ist
     ungültiger Text nie gleichwertig und wird auf den Stand des Modells zurückgesetzt – so verhielt sich das Feld
     schon bei gültigem Modellwert, und so verhalten sich die Wertebereichsfelder. Das gilt auch, wenn ein anderes
     Feld desselben Panels gültig bearbeitet wird. Ist der Modellwert selbst ungültig (z. B. „abc“ aus einer Datei),
     zeigt das Feld ihn mit der Meldung „Bitte eine ganze Zahl eingeben.“ statt ihn zu verbergen. Nur leerer Text gilt
     weiter als gleichwertig zu einem fehlenden Parameter.
181. **Speichern durch symbolische Links direkt, kurzer Name der temporären Datei** (Nachprüfung PR #1): Die
     unabhängige Nachprüfung der Korrekturen fand zwei Fehler in Nr. 173. (a) Die Linkauflösung mit
     `FileInfo.ResolveLinkTarget(returnFinalTarget: true)` hängt ein relatives Linkziel textuell an den übergebenen
     Pfad; das Betriebssystem löst „..“ aber vom echten Verzeichnis des Links aus. Lag der Link in einem Verzeichnis,
     das selbst über einen Link erreicht wurde, schrieb `Save` still in eine andere Datei – die gespeicherte Fassung
     ging verloren. Die eigene Linkauflösung ist entfernt: Ist die Zieldatei selbst ein symbolischer Link, schreibt
     `Save` wie vor dem Review direkt (nicht atomar) hindurch und das Betriebssystem folgt dem Link. Ein verlinktes
     Verzeichnis im Pfad ohne Dateilink am Ende nutzt weiter temporäre Datei und Umbenennen. (b) Der Name der
     temporären Datei war 38 Zeichen länger als der Zielname; bei Dateinamen ab etwa 217 Zeichen scheiterte das
     Speichern. Die temporäre Datei heißt jetzt `.graphdoku-<32 Hex>.tmp`. Nicht atomar bleibt das Speichern damit in
     zwei Fällen: durch einen Dateilink und in einem schreibgeschützten Verzeichnis (Nr. 173).
182. **„Kleiner als“-Pfeil nach seinem Umriss treffen** (Nachprüfung PR #1): Der Rückfall-Treffertest nach den Knoten
     (Nr. 176) gab dem Pfeil einen Kreis mit 1,25 halben Pfeillängen, innerhalb dessen der Abstand 0 galt. Damit
     gewann der Pfeil auch in den leeren Ecken dieses Kreises gegen eine genau getroffene Linie daneben – vor dem
     Review gewann die nächste Linie. Jetzt zählt der Abstand zum gezeichneten Umriss (`EdgeMarks.ArrowDistance`: 0 im
     Umriss, sonst bis zum nächsten Rand), mit derselben Toleranz von 6 px wie bei Linien; an Spitze und Ecken reicht
     die Trefferfläche damit bis 6 px über den Umriss. Kropki-Punkte bleiben unverändert: Ihre Trefferfläche endet am
     gezeichneten Rand, wie bestehende Tests festlegen; ein Klick in den Punkt trifft ihn ohnehin schon vor den
     Knoten.
