# Fortschritt

## Paket 1 – Gerüst und Kernmodell

**Erledigt**

- Solution `GraphDoku.sln` mit `src/GraphDoku.Core`, `GraphDoku.Solver`, `GraphDoku.Explainer`, `GraphDoku.App` und
  `tests/GraphDoku.Core.Tests`, `Solver.Tests`, `Explainer.Tests`, `App.Tests`; `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`, `nuget.config`.
- Kernmodell: `Puzzle`, `Node`, `Edge`, `Group`, `ValueDomain` (global und pro Knoten), Givens, `RuleParams`,
  `GridType`, `PuzzleComparer` (Strukturvergleich), `Puzzle.Clone()`.
- Regeln: `IRule`, `RuleBase`, `RuleRegistry` mit allen acht Katalogregeln (Typname, Art, Parameterprüfung,
  Standardwerte, `CoverRange`-Größenprüfung) und einfacher, korrekter `Propagate`-Implementierung mit `Reason`.
- `CandidateState` (Grundfunktionen), `CandidateChange`, `Reason`.
- `PuzzleValidator`: ungültige/doppelte Ids, fehlende Knoten, Given außerhalb des Wertebereichs, ungültige
  Wertebereiche, Schleifen, doppelte Gruppenknoten, Regelart passend zu Kante/Gruppe, unbekannte Regeltypen,
  Parameter und Knotenanzahl.
- JSON laden/speichern mit `formatVersion`-Prüfung und deutschen Fehlermeldungen (`PuzzleSerializer`).
- `GridGeometry` für Quadrat-, Dreiecks- und Sechseckgitter (Weltkoordinaten, Umrisse, Nachbarn, Springerzug,
  Abstand, Einrasten).
- Vorlage `ClassicSudoku` und Beispieldatei `puzzles/klassisch-leer.json`.
- Avalonia-App startet mit Platzhalter-Hauptfenster und MVVM-Verdrahtung (lokal auf macOS gestartet).
- Repo-Hygiene: `.gitignore`, `.gitattributes`, `.editorconfig`, `LICENSE` (proprietär), `THIRD-PARTY-NOTICES.md`,
  `README.md`; Gitea-CI `.gitea/workflows/ci.yml`.
- Nacharbeiten nach Review: `null` bei optionalen Listen/`params`/Knoten-`domain` wird als „nicht gesetzt“ gelesen,
  `Sum` liefert gültige Standardparameter (`target` = 10), `Propagate` liefert keine doppelten Streichungen,
  Architekturtests prüfen zusätzlich die Projekt- und Paketverweise der .csproj-Dateien.
- Tests: 159 (Core 144, Solver 1, Explainer 2, App 12), alle grün in Release.
- Akzeptanz: Round-Trip-Tests (Vorlage, Rätsel mit allen Merkmalen, alle Dateien in `puzzles/`); ungültige Parameter
  und unbekannte Regeltypen werden mit verständlicher Meldung abgelehnt (getestet).

**Offen**

- ~~`ISolver` und OR-Tools-Adapter pro Regel (Paket 2).~~ Erledigt in Paket 2.
- ~~Erklär-Modus (Paket 6); `Propagate` ist vorhanden, wird dort ggf. verfeinert.~~ Erledigt in Paket 6.
- Validierungsmeldungen sind reine Texte (die Element-Id steht nur im Text). Der Editor (Paket 3) zeigt sie als
  Liste im Statusbereich; ein strukturierter Meldungstyp zum Markieren des Elements ist weiterhin offen.

**Bekannte Probleme**

- Die Gitea-CI konnte lokal nicht ausgeführt werden; die Schritte wurden lokal mit denselben Befehlen nachgestellt.
  Der erste Lauf auf dem Ubuntu-Runner steht noch aus. (Abschluss-Review: inzwischen in Linux-Containern nachgestellt,
  siehe „Abschluss-Review – App“.)
- ~~Keine Headless-UI-Tests: Avalonia.Headless.XUnit 12 benötigt xUnit v3 (siehe `docs/decisions.md`, Nr. 7).~~
  Seit Paket 3 gibt es Rendering-Rauchtests mit Avalonia.Headless ohne das xUnit-Paket (Nr. 47).
- Der OR-Tools-Rauchtest braucht beim ersten Laden der nativen Bibliothek einige Sekunden.

## Paket 2 – OR-Tools-Solver

**Erledigt**

- `ISolver` (synchron, blockierend): `Solve(puzzle, options, token)` → `SolveResult` mit Status `Solved` (Wert pro
  Knoten-Id), `Unsolvable`, `Timeout`, `Cancelled`, `Invalid` (deutsche Fehlerliste) und deutscher `Message`;
  `CountSolutions(puzzle, limit = 2, options, token)` → `SolutionCountResult` (`Count`, `IsUnique`, `IsUnsolvable`,
  `IsAmbiguous`, `LimitReached`, bis zu zwei Beispiellösungen). Asynchrone Varianten `SolveAsync` und
  `CountSolutionsAsync` (`SolverExtensions`, Task.Run auf einer Kopie des Rätsels).
- `SolverOptions` mit `TimeLimit` (Standard 30 s, muss > 0 sein).
- `OrToolsSolver`: prüft das Rätsel mit `PuzzleValidator`, legt eine CP-SAT-Integer-Variable pro Knoten an (eigener
  oder globaler Wertebereich, Given fixiert) und übersetzt jede Kante/Gruppe über `OrToolsAdapterRegistry`
  (Typname → `IOrToolsRuleAdapter`). Adapter für alle acht Katalogregeln. Fehlender Adapter → `Invalid` mit Meldung
  (Regel und Kante/Gruppe).
- Zeitlimit über `max_time_in_seconds`, Abbruch: `CancellationToken` ruft `StopSearch` des laufenden CP-SAT auf.
- Beispielrätsel `puzzles/klassisch-leicht.json` (nur mit Singles lösbar), `klassisch-mittel.json`,
  `klassisch-schwer.json`, alle eindeutig.
- Tests (Solver 89): drei klassische Sudokus mit hinterlegter Lösung (unabhängig geprüft), unlösbares und
  mehrdeutiges Sudoku (genau 2 Lösungen), je Regeltyp positive und negative Fälle mit Abgleich gegen vollständiges
  Durchprobieren (unabhängiger Lösungsprüfer in den Testhilfen), Zusammenspiel aller Regeln, Zeitlimit und Abbruch
  (synchron und asynchron), Wertebereich pro Knoten, Given außerhalb des Wertebereichs, unbekannte Regel, fehlender
  Adapter, eigene Regel über beide Registries.
- OR-Tools lädt auf macOS arm64 (lokal getestet); das Paket bringt die nativen Bibliotheken für linux-x64 (CI),
  linux-arm64, osx-arm64, osx-x64 und win-x64 mit (Ausgabeverzeichnis `runtimes/<rid>/native`).
- Review-Befunde behoben: Lösungszählung zählt verschiedene Knotenbelegungen (unabhängig von freien
  Hilfsvariablen eigener Adapter), Zeitlimit in Meldungen im deutschen Format („300 ms“, „1,5 s“, nie „0 s“),
  Lizenzen der nativen OR-Tools-Bibliotheken (u. a. COIN-OR, EPL-2.0) in `THIRD-PARTY-NOTICES.md`.
- Gesamt: 247 Tests (Core 144, Solver 89, Explainer 2, App 12), alle grün in Release.

**Offen**

- ~~Anbindung an die App (Paket 4).~~ Erledigt in Paket 4.

**Bekannte Probleme**

- Der Linux-Lauf der Solver-Tests (native OR-Tools-Bibliothek auf Ubuntu) ist erst mit dem nächsten CI-Lauf belegt;
  lokal gab es keine Linux-Umgebung. (Abschluss-Review: in einem Linux-Container nachgestellt, die Solver-Tests
  sind dort grün, siehe „Abschluss-Review – App“; der Lauf auf dem echten Gitea-Runner steht weiterhin aus.)
- Zeitlimit- und Abbruchtests nutzen ein unlösbares Färbungsrätsel (Mycielski-Graph M6 mit 5 Farben), das CP-SAT
  9.15 lokal nicht in 20 s widerlegen kann. Sollte eine künftige OR-Tools-Version es schnell widerlegen, müssen diese
  Tests ein anderes schweres Rätsel bekommen.

## Paket 3 – Editor (Grundfunktionen)

**Erledigt**

- Zeichenfläche `GraphCanvas` (eigenes Control, Zeichnen per `DrawingContext`): Raster für Quadrat-, Dreiecks- und
  Sechseckgitter, Knoten als Kreise mit Givens (bei Bereich 0–15 als A–F), Kanten mit Regelmarken (Ungleich schlicht,
  Differenz weißer Punkt, Verhältnis schwarzer Punkt, Kleiner als Pfeil), Gruppen als farbiger Umriss, gewählte
  Gruppe flächig, Thermometer als Linie mit Kolben, Reihenfolge-Nummern, Auswahlrahmen, Kantenvorschau.
- Zoom (Mausrad um den Zeiger) und Pan (mittlere/rechte Maustaste, Leertaste + Ziehen, Werkzeug), „Einpassen“.
- Werkzeuge „Auswahl“, „Knoten setzen“, „Kante ziehen“, „Ansicht verschieben“. Knoten setzen (Klick oder Ziehen),
  verschieben (einrastend, Mehrfachauswahl, Dreiecksgitter formtreu, keine Überlagerung), löschen (Entf);
  Rahmenauswahl, Shift-Klick, Alles auswählen, Escape.
- Kanten per Ziehen von Knoten zu Knoten; Regel neuer Kanten in der Werkzeugleiste; Eigenschaften-Panel mit Regelwahl
  und Parameterfeldern aus den Parameterbeschreibungen der Regel; Richtung umkehren (Kleiner als), Kante löschen.
- Gruppen aus der Auswahl (Reihenfolge bleibt erhalten), Regel und Parameter, Farbe (Palette oder automatisch),
  Beschriftung, Reihenfolge umkehren, löschen; Gruppenliste mit Ein-/Ausblenden der Hervorhebung.
- Givens per Tastatur (mehrstellig, negative Werte, A–F bei 0–15), ⌫ löscht Givens.
- Undo/Redo für alle Bearbeitungen (Command-Muster, `EditHistory`), zusammenhängende Gesten und Eingaben als ein
  Schritt; Strg/Cmd+Z, Strg/Cmd+Y, Shift+Strg/Cmd+Z.
- Neu (Name, Gittertyp, globaler Wertebereich), Vorlage „Klassisch 9×9“, Öffnen, Speichern, Speichern unter
  (StorageProvider, JSON über `PuzzleSerializer`, deutsche Fehlermeldungen), Rückfrage bei ungespeicherten Änderungen
  (auch beim Schließen), Dirty-Anzeige im Titel; Name und Wertebereich des Rätsels im Panel bearbeitbar.
- Statusbereich: letzte Aktion, Werkzeughinweis, Prüfmeldungen des `PuzzleValidator` nach jeder Bearbeitung.
- Tests (App 111): Verlauf, Viewport, Geometriehilfen, Werkzeuge und Gesten, Undo/Redo je Bearbeitungsart (jeweils
  exakter Strukturvergleich vor/nach), Given-Eingabe, Datei-Befehle mit Fehlerfällen, Rendering-Rauchtests
  (Hauptfenster mit klassischem Sudoku, alle drei Gitter mit Kanten, Gruppen, Auswahl; Dialog „Neu“) mit
  Pixelprüfung.
- **Akzeptanz:** `ClassicSudokuAcceptanceTests` baut über die Editor-Befehle ein klassisches Sudoku (81 Knoten gemalt,
  27 `AllDifferent`-Gruppen aus Rahmenauswahlen mit Beschriftung, 30 Givens per Tastatur), speichert es über
  „Speichern“, öffnet es in einem neuen Fenster und vergleicht mit `PuzzleComparer` gegen `ClassicSudoku.Create` und
  gegen den Editorstand; zusätzlich prüft der Solver die Eindeutigkeit.
- Gesamt nach Paket 3: 346 Tests (Core 144, Solver 89, Explainer 2, App 111), alle grün in Release.
- Review-Befunde behoben (Nr. 61–69): Gruppenwechsel im Fenster erzeugt keine falschen Undo-Schritte mehr (gemeinsame
  Farbliste), kein Absturz bei mehrfach vergebenen Gruppen-Ids, Rahmenauswahl auf dem Dreiecksgitter zeilenweise,
  Fehler der Dateiauswahl als Meldung, Blockgrenzen der Vorlage betont (zusammengefasste Gruppenumrisse), gewählte
  Kante mit Endknoten hervorgehoben, Entf/⌫/Ziffern auch bei Fokus in Gruppenliste oder Panel, deutsche
  Parameterbezeichnungen, umbrechende Werkzeugleiste, Build ohne XAML-Warnung. Neue Headless-Tests im echten Fenster
  (Auswahlwechsel ohne Undo-Schritt, Tastatur bei Fokus außerhalb der Zeichenfläche, Mindestgröße, Blocklinien).
- Gesamt: 363 Tests (Core 144, Solver 89, Explainer 2, App 128), alle grün in Release.

**Offen**

- ~~Eigene Wertebereiche pro Knoten im Editor bearbeiten~~ – erledigt in Paket 5 (Editor-Teil), Nr. 144.
- Prüfmeldungen markieren das betroffene Element noch nicht auf der Zeichenfläche.
- ~~Lösungswerte in den Knoten (Paket 4)~~ erledigt in Paket 4; ~~Kanten-Generator und Beispielrätsel (Paket 5)~~
  erledigt in Paket 5.
- Kein Kopieren/Einfügen (nicht gefordert). ~~Summenbeschriftung von Käfigen~~ – erledigt in Paket 5 (Editor-Teil).

**Bekannte Probleme**

- Die Bedienung am echten Desktop wurde nur kurz geprüft (App startet auf macOS und läuft); Maus- und Tastaturlogik
  ist über ViewModel-Tests abgedeckt, das Aussehen über die headless gerenderten Bilder.
- Der Rendering-Rauchtest braucht auf Linux `libfontconfig.so.1`; die CI installiert es bei Bedarf (Nr. 48). Der
  erste Lauf auf dem Ubuntu-Runner steht noch aus. (Abschluss-Review: Auf Linux waren zwei Rendering-Tests wegen
  Subpixel-Textglättung rot; behoben, Nr. 164.)
- Auf dem Mac ist die Taste ⌫ Backspace: Sie löscht Givens, nicht Knoten; Knoten löscht fn+⌫ (Entf), das Menü oder
  das Panel (Nr. 52).

## Paket 4 – Lösen in der App

**Erledigt**

- Lösen-Leiste unter der Werkzeugleiste und Menü „Lösen“: „Lösen“ (F5), „Eindeutigkeit prüfen“ (F6), „Abbrechen“,
  „Lösung anzeigen“ bzw. „Lösung 1/2 anzeigen“, „Lösung ausblenden“, Zeitlimit in Sekunden (Standard 30 s aus
  `SolverOptions`, 0,1–3600 s, Dezimalkomma).
- `SolveViewModel` (`MainWindowViewModel.Solve`) mit injiziertem `ISolver` (App: `OrToolsSolver`); Suche asynchron über
  `SolveAsync`/`CountSolutionsAsync`, Fortschrittsbalken und Text während der Suche, Abbrechen über
  `CancellationToken`; die Oberfläche bleibt bedienbar.
- Lösung als reiner Anzeigezustand im Editor (nicht im Rätsel, kein Undo-Schritt, nicht „ungespeichert“, nie
  gespeichert): Lösungswerte blau und normal, Givens schwarz und halbfett, Legende in der Leiste. Mehrdeutig: beide
  gefundenen Lösungen abrufbar, Unterschiede orange markiert.
- Deutsche Meldungen mit farbiger Leiste für gelöst, eindeutig, mehrdeutig (mindestens zwei Lösungen, Anzahl der
  abweichenden Knoten), unlösbar, Zeitlimit (mit Hinweis), abgebrochen, ungültig (mit Fehlerliste), Solver-Fehler und
  veraltet.
- Jede inhaltliche Änderung (auch Rückgängig/Wiederholen, Dokumentwechsel) blendet eine angezeigte Lösung aus; eine
  Bearbeitung während der Suche verwirft deren Ergebnis, ein Dokumentwechsel bricht sie ab, Schließen des Fensters
  ebenfalls (Nr. 72).
- Eingepasste Ansicht wird bei Größenänderung der Zeichenfläche neu eingepasst (Nr. 77).
- Tests (App 174, davon neu 46): `SolveViewModelTests` mit Ersatzsolver, `SolveIntegrationTests` mit OR-Tools,
  Rendering-Rauchtests für gelöstes und mehrdeutiges Sudoku, laufende Suche, Abbruch, ungültiges Rätsel und falsches
  Zeitlimit (Bilder angesehen; dabei die eigene Lösen-Leiste und das Neu-Einpassen eingeführt, Nr. 76/77).
- **Akzeptanz:** `SolveIntegrationTests.Every_example_puzzle_can_be_solved_in_the_app` öffnet jede Datei in `puzzles/`
  über das Hauptfenster-ViewModel und löst sie mit dem Lösen-Befehl und dem echten `OrToolsSolver` (Lösung geprüft);
  dazu Tests für unlösbar, mehrdeutig, Abbruch und veraltete Lösung nach Bearbeitung.
- Gesamt: 409 Tests (Core 144, Solver 89, Explainer 2, App 174), alle grün in Release.
- **Review-Befunde behoben (Nr. 79–81):** Neu-Einpassen wartet bis zum Ende einer Ziehgeste; Lösen-Leiste mit fester
  Höhe (68 px in allen Zuständen außer „ungültig“ mit Fehlerliste), dadurch bei Mindestgröße 900 × 560 Zeichenfläche
  307 statt ca. 230 px hoch; „Eindeutigkeit prüfen“ nach „Lösen“ ersetzt die angezeigte Lösung bei Mehrdeutigkeit durch
  Lösung 1 mit orange markierten Unterschieden; Meldung im Singular bei einem abweichenden Knoten. Neue Tests: 3 zum
  Einpassen während Gesten, 3 zur Mehrdeutig-Meldung/-Anzeige, Rendering-Test der Leistenhöhe bei 900 × 560 und
  1280 × 800 (Bilder angesehen). Gesamt jetzt 417 Tests (App 182), alle grün in Release.

**Offen**

- Das Zeitlimit wird nicht über einen Programmstart hinaus gemerkt (kein Einstellungsspeicher, Nr. 74).
- Kein Fortschritt in Prozent (CP-SAT liefert keinen sinnvollen Fortschrittswert); nur ein laufender Balken.
- Ein ungültiges Rätsel zeigt die Fehler als Text; die betroffenen Elemente werden auf der Zeichenfläche noch nicht
  markiert (wie bei den Prüfmeldungen, Paket 3).

**Bekannte Probleme**

- Lange Meldungen der Lösen-Leiste werden nach zwei Zeilen mit „…“ gekürzt (Volltext als Tooltip), vor allem bei
  Mindestbreite; die Kernaussage steht jeweils am Anfang (Nr. 80).
- Die Bedienung am echten Desktop wurde nicht geprüft (kein Bildschirmzugriff); Verhalten über ViewModel-Tests, Aussehen
  über die headless gerenderten Bilder belegt.
- Der Abbruch einer laufenden Suche wirkt über `StopSearch` von CP-SAT; bei sehr großen Modellen kann der Modellaufbau
  vor dem Start der Suche selbst nicht unterbrochen werden (bei den Beispielrätseln wenige Millisekunden).

## Paket 5 – Generator (Core) und Beispielrätsel

Entstand parallel zu Paket 6 (Oberfläche) im Branch `feature/wp5-generator` (nur Core, Tests und `puzzles/`) und wurde
danach zusammengeführt. Die Editor-Oberfläche für den Generator kam als eigener Schritt dazu (siehe „Paket 5 –
Editor-Teil“).
Entscheidungen: Nr. 116–132.

**Erledigt**

- Kanten-Generator in Core (`GraphDoku.Core.Generation`): `DistanceRule`, `EdgeGenerator`, `EdgeGenerationRequest`,
  `EdgeGenerationResult`, `EdgeDirectionPolicy`. 55 Unit-Tests (`tests/GraphDoku.Core.Tests/EdgeGeneratorTests.cs`) auf
  allen drei Gittertypen.
- Fünf neue Beispielrätsel in `puzzles/` (alle eindeutig und ohne Verzweigung erklärbar):

  | Datei | Gitter | Werte | Knoten | Givens | Kanten | Gruppen |
  | --- | --- | --- | --- | --- | --- | --- |
  | `killer-sudoku.json` | Quadrat | 1–9 | 81 | 3 | 0 | 27 Zeilen/Spalten/Blöcke + 28 Käfige (`Sum`) |
  | `kropki-sudoku.json` | Quadrat | 1–9 | 81 | 2 | 14 `Ratio` (schwarz) + 24 `Difference` (weiß) | 27 |
  | `hexadoku-16x16.json` | Quadrat | 0–15 | 256 | 90 | 0 | 48 `CoverRange` |
  | `dreiecks-sudoku.json` | Dreieck | 1–9 | 81 | 33 | 168 `NotEqual` | 9 Regionen (`CoverRange`) + 3 Seiten (`AllDifferent`) |
  | `thermo-sudoku-6x6.json` (zusätzlich) | Quadrat | 1–6 | 36 | 5 | 4 `LessThan` | 18 + 3 `Thermometer` |

- Tests: `tests/GraphDoku.Solver.Tests/ExamplePuzzleTests.cs` (jede Datei in `puzzles/` lädt, ist gültig, lösbar und
  eindeutig per `OrToolsSolver.CountSolutions(limit: 2)`, Lösung unabhängig mit `SolutionVerifier` geprüft; dazu
  Strukturprüfungen je neuem Beispiel) und `tests/GraphDoku.Explainer.Tests/ExamplePuzzleExplanationTests.cs` (Erklär-Modus
  für jede Datei konsistent mit der Lösung, Laufzeit gemessen; die neuen Beispiele ohne Verzweigung; die typische Regel
  jedes Beispiels begründet mindestens einen Schritt).
- Erzeugungscode als Testcode: `tests/GraphDoku.Explainer.Tests/Generation/ExamplePuzzleGenerator.cs` (nicht in der App,
  Nr. 125).
- Nach dem Zusammenführen: Die App-Tests, die jede Datei in `puzzles/` öffnen, lösen und auf Eindeutigkeit prüfen
  (`SolveIntegrationTests`, je 9 Fälle; `MainWindowViewModelTests`), laufen mit den neuen Dateien ohne Änderung durch.
  Der veraltete Kommentar in `SolveIntegrationTests` („alle Beispielrätsel bestehen bisher nur aus AllDifferent“) wurde
  korrigiert; die dortige allgemeine Prüfung (Lösung als Givens bleibt lösbar) deckt die neuen Regeln ab.
- Gesamt nach dem Zusammenführen: 635 Tests (Core 230, Solver 104, Explainer 81, App 220), alle grün in Release.
  (Im Branch allein: App 192, weil dort Paket 6 (Oberfläche) noch fehlte.)

**Offen**

- ~~Editor-Oberfläche für den Generator~~ – erledigt, siehe „Paket 5 – Editor-Teil“.
- ~~Käfigsummen werden auf der Zeichenfläche nicht angezeigt~~ – erledigt, siehe „Paket 5 – Editor-Teil“ (Nr. 141).
- `Puzzle.NextEdgeId()` (wie `NextNodeId`/`NextGroupId`) kann bei einer ungeprüft geöffneten Datei mit Ids ≤ 0 selbst
  eine Id ≤ 0 liefern; der Generator umgeht das (Nr. 124), der Editor für einzeln gezogene Kanten, Knoten und Gruppen
  noch nicht.

**Bekannte Probleme / Hinweise**

- Die fünf Generator-„Tests“ (`ExamplePuzzleGenerator.*`) erscheinen im normalen Testlauf als bestanden, tun dort aber
  nichts (Nr. 125).
- Das Dreiecks-Sudoku hat 168 `NotEqual`-Kanten; der Editor zeichnet sie als Linien, das Bild ist dicht, aber lesbar
  (Rendering mit einem nicht eingecheckten Headless-Hilfsprogramm angesehen). Seit dem Editor-Teil sind „Ungleich“-Linien
  dünner und heller (Nr. 142), das Bild ist deutlich ruhiger.

**Laufzeiten** (Entwicklungsrechner, macOS arm64, Release), Erklär-Modus (`ExplanationEngine.Explain`, ohne
Testprüfungen):

| Datei | Schritte | Verzweigungen | Zeit |
| --- | --- | --- | --- |
| `hexadoku-16x16.json` | 669 | 0 | ≈ 190 ms |
| `killer-sudoku.json` | 328 | 0 | ≈ 70 ms |
| `kropki-sudoku.json` | 321 | 0 | ≈ 50 ms |
| `dreiecks-sudoku.json` | 242 | 0 | ≈ 20 ms |
| `thermo-sudoku-6x6.json` | 106 | 0 | ≈ 7 ms |

Der Test erlaubt großzügig 30 s pro Erklärung (nur als Schutz gegen grobe Regressionen). `CountSolutions` für alle
Beispiele zusammen dauert etwa eine Sekunde. Erzeugung (einmalig): Killer ≈ 25 s (40 Versuche), Hexadoku ≈ 60 s
(6 Durchläufe mit je 256 Erklärungen), die übrigen wenige Sekunden.

## Paket 5 – Editor-Teil (Kanten-Generator, alle Regeln, Darstellung)

**Erledigt**

- **Kanten-Generator im Editor:** Dialog „Kanten erzeugen“ (Bearbeiten → Kanten erzeugen …, Schaltfläche „Erzeugen …“
  neben der Kantenregel in der Werkzeugleiste, Strg/Cmd+E). Wählbar: Abstandsregel (direkte Nachbarn, inkl. diagonal,
  Springerzug nur auf dem Quadratgitter, freier Abstand mit kleinstem/größtem Abstand und Toleranz, Dezimalkomma),
  Regel der neuen Kanten mit den Parameterfeldern des Eigenschaften-Panels (gemeinsame Vorlage `RuleParameterItem`),
  Richtung bei „Kleiner als“ (Werte steigen bzw. fallen in Leserichtung) und Knotenmenge (ausgewählte Knoten oder alle).
  Jede Änderung zeigt sofort eine Vorschau aus `EdgeGenerator.Generate` („144 neue Kanten „Ungleich“ (…).“, übersprungene
  Paare, Fehler rot mit Liste); „Kanten erzeugen“ ist nur aktiv, wenn mindestens eine Kante entstünde, und fügt alle
  Kanten als **ein** Undo-Schritt ein (`AddGeneratedEdgesCommand`: Ausführen = `result.ApplyTo`, Rückgängig = genau diese
  Ids entfernen). Die Statuszeile zeigt danach die Meldung des Generators.
- **Alle Katalogregeln im Editor anlegbar** (geprüft per Test, der alle acht im Editor anlegt und löst): Kantenregeln per
  Ziehen (Richtung von „Kleiner als“ = Ziehrichtung, „Richtung umkehren“) oder Generator (Richtungsregel); Gruppenregeln
  aus der Auswahl. **Thermometer-Reihenfolge** = Auswahlreihenfolge (Klick, dann Shift-Klicks; Nummern an den Knoten),
  „Reihenfolge umkehren“ im Gruppen-Panel, Nummern und Kolben auf der Zeichenfläche. Keine Lücke gefunden, die neuen Code
  brauchte; abgesichert durch Tests (Shift-Klick-Reihenfolge, Umkehren mit Undo, Ziehrichtung bei „Kleiner als“).
- **Zeichenfläche:** Zielsummen aller sichtbaren `Sum`-Gruppen klein oben links in der ersten Zelle (Leserichtung) des
  Käfigs; Kantenmarken (Kropki-Punkte, Pfeile) über den Knoten statt halb verdeckt darunter; „Ungleich“-Linien dünner und
  heller; Pfeile passen in den Spalt zwischen den Kreisen; Thermometer mit breiterer Linie und größerem Kolben.
- **Eigener Wertebereich pro Knoten** (Lücke aus Paket 3): im Auswahl-Panel „Bereich setzen“ / „Globalen Bereich
  verwenden“ für alle ausgewählten Knoten, je ein Undo-Schritt (`SetNodeDomainsCommand`), Anzeige global/eigen/gemischt,
  Hinweis bei Givens außerhalb des neuen Bereichs.
- **Statuszeile:** Die feste Startmeldung „Neues Rätsel. …“ blieb stehen, wenn ein Rätsel auf anderem Weg geladen wurde
  (z. B. in den Rendering-Tests). Jetzt setzt `EditorViewModel.LoadPuzzle` eine zum Dokument passende Grundmeldung
  („Leeres Rätsel „…“ (Quadrat, Werte 1..9). Mit „Knoten setzen“ …“ bzw. „Rätsel „…“: 81 Knoten, …“), die Öffnen, Neu und
  Vorlage durch ihre eigene Meldung ersetzen.
- **Rendering angesehen:** jede Datei aus `puzzles/` über „Öffnen“ (`beispiel-*.png`), Killer-Summen (auch gezoomt und mit
  ausgeblendeten Gruppen), Generator-Dialog (Auswahl, „Kleiner als“, Fehler, Dreiecksgitter ohne Springerzug),
  Auswahl-Panel mit eigenem Wertebereich. Dabei behoben: Käfigsummen fehlten, Kropki-Punkte und Pfeile waren halb von den
  Kreisen verdeckt, das Dreiecks-Sudoku war durch 168 dunkle Linien unruhig, die Werkzeugleiste brach mit der neuen
  Schaltfläche bei 1280 px um (deshalb „Erzeugen …“ statt „Kanten erzeugen …“). Hexadoku (0–F) und Dreiecks-Sudoku sind
  bei 1280 × 800 lesbar.
- **Akzeptanz:** Jedes Beispielrätsel ist lösbar und eindeutig – auf App-Ebene über die ViewModel-Befehle geprüft
  (`SolveIntegrationTests`: jede Datei in `puzzles/` mit `OpenFileAsync` öffnen, `SolveCommand`, `CheckUniquenessCommand`;
  bestand schon und deckt die neuen Dateien ab, daher nicht erweitert), dazu Core-/Solver-/Explainer-Tests aus dem
  Core-Teil.
- Tests: `EdgeGeneratorViewModelTests` (12: Vorschau ohne Änderung, Einfügen als ein Undo-Schritt mit Undo/Redo, übersprungene
  Paare und zweiter Lauf ohne Undo-Schritt, Knotenmenge Auswahl/alle, Auswahl unter zwei Knoten, Richtung bei „Kleiner
  als“, Parameter, Fehler bei Parametern und Zahlen, freier Abstand mit Dezimalkomma/Bereich/Toleranz, Springerzug nur
  auf dem Quadratgitter, Standardregel aus der Werkzeugleiste, Befehl des Hauptfensters mit Bestätigen/Abbrechen),
  `EditorRuleEditingTests` (9: alle Katalogregeln, Thermometer-Reihenfolge, „Kleiner als“-Richtung, eigener Wertebereich
  mit Undo, Fehler, Eingabe bleibt stehen, Statuszeile), Rendering (+12: jede Beispieldatei, Killer-Summen mit
  Pixelprüfung, Generator-Dialog, Auswahl-Panel).
- Gesamt: 668 Tests (Core 230, Solver 104, Explainer 81, App 253), alle grün in Release.

**Offen**

- Gruppenbeschriftungen (`label`) werden auf der Zeichenfläche nicht gezeichnet (stehen in der Gruppenliste); bei der
  Vorlage wären es 27 Beschriftungen übereinander. Nur Zielsummen von `Sum`-Gruppen erscheinen.
- Knoten mit eigenem Wertebereich sind auf der Zeichenfläche nicht markiert (nur im Panel sichtbar).
- Die Reihenfolge einer bestehenden Gruppe lässt sich nur umkehren; für eine andere Reihenfolge wird die Gruppe aus einer
  neuen Auswahl erstellt.
- ~~`Puzzle.NextEdgeId()`/`NextNodeId`/`NextGroupId` bei Ids ≤ 0 (siehe Core-Teil) – unverändert offen.~~ Im Editor
  erledigt im Abschluss-Review (Nr. 166): neue Ids mindestens 1 und ohne Überlauf.

**Bekannte Probleme**

- Der Generator-Dialog ist modal; Auswahl und Rätsel lassen sich während der Vorschau nicht ändern (gewollt, Nr. 133).
- Die Bedienung am echten Desktop wurde nicht geprüft (kein Bildschirmzugriff); Verhalten über ViewModel-Tests, Aussehen
  über die headless gerenderten Bilder.
- Haben Knoten zugleich Kanten mit Marke zum direkten und zum übernächsten Nachbarn, liegen beide Marken im selben
  Spalt übereinander (Nr. 147).

**Review-Befunde (Nachbesserung Paket 5, Editor)**

- Marken langer Kanten (z. B. freier Abstand 2) lagen auf dem Knoten dazwischen und verdeckten dessen Wert; jetzt im
  freien Stück der Kante (Nr. 147), Unit-Tests für `EditorGeometry.EdgeMarkPosition` und Rendering-Test
  (`lange-kanten.png`: Differenz, „Kleiner als“, Verhältnis mit Abstand 2, Givens bleiben lesbar).
- Eigener Wertebereich im Hexadoku (z. B. 8–15) schaltete die A–F-Anzeige und -Eingabe ab; jetzt entscheidet der
  Rätselbereich (Nr. 150), Hex-Eingaben außerhalb des eigenen Bereichs werden gemeldet. ViewModel-Test.
- Generator bei Kanten-Id `int.MaxValue`: Vorschau meldete Erfolg, „Kanten erzeugen“ hätte die App beendet; jetzt Fehler
  in der Vorschau und abgefangene Ausnahme in `Apply` (Nr. 149). Core- und ViewModel-Tests.
- Zielsummen auf dem Sechseckgitter lagen im Knotenkreis neben dem Wert; jetzt außerhalb (Nr. 148), Rendering-Test
  mit Pixelprüfung (`summen-sechseck.png`).
- Hinweis zum Modus der Differenz zeigte JSON-Bezeichner („"exact": …“), ebenso „Standard: false“ bei Wiederholungen;
  jetzt deutsche Hinweise (Nr. 151), Test über alle Katalogparameter.
- Gesamt danach: 677 Tests (Core 232, Solver 104, Explainer 81, App 260), alle grün in Release.

## Paket 6 – Erklär-Modus (Bibliotheksteil)

Entstand parallel zu Paket 3/4 im Branch `feature/wp6-explainer` und wurde danach zusammengeführt.

**Erledigt**

- `CandidateSet` (Core, neu): unveränderliche Bitmenge pro Knoten über dessen eigenen Wertebereich (beliebiger Versatz,
  auch negative Werte, bis 4096 Werte), aufsteigende Aufzählung ohne Zwischenspeicher, `MinValue`/`MaxValue`/
  `SingleValue`, `Without`, `Only`, `NextAbove`/`NextBelow`, Wertgleichheit, implementiert `IReadOnlySet<int>`.
- `CandidateState` überarbeitet: hält pro Knoten eine `CandidateSet` (Knotenreihenfolge des Rätsels in `NodeIds`);
  `Clone()` kopiert nur ein Feld von Verweisen; `HasSameCandidates` für Vergleiche; `Apply` meldet, ob sich etwas
  geändert hat. Startzustand aus globalem bzw. eigenem Wertebereich und Givens wie bisher.
- `Reason.Constraint` ist jetzt `ConstraintRef?` (für Schlüsse ohne Regel: einziger Kandidat, Verzweigung).
- `Propagate` aller acht Regeln überprüft und verbessert (Einzelheiten unter „Entscheidungen“): Kantenregeln mit
  vollständiger Kantenkonsistenz in linearer Zeit, `Thermometer` vollständig konsistent auch bei Lücken in den
  Kandidaten, `Sum` mit vollständiger Prüfung jedes Kandidaten (mit Suchbudget), sprechendere deutsche Begründungen.
- `GraphDoku.Explainer`: `ExplanationEngine` (nimmt `ISolver`, optional `RuleRegistry` und `SolverOptions`) mit
  `Explain(puzzle, token)` und `ExplainAsync(puzzle, token)`; Ergebnis `ExplanationResult` mit `Status`
  (`Solved`, `SolvedAmbiguous`, `SolvedUnverified`, `Unsolvable`, `Stuck`, `Invalid`, `Cancelled`), deutscher
  `Message`, geordneten `Steps`, `Solution`, `SolverSolution`, `Contradiction`, `Errors` und
  `GetSnapshot(n)` (Zustand nach n Schritten, 0 = Start) für Vor/Zurück/Schieberegler.
- `ExplanationStep`: `Kind` (gesetzter Wert / gestrichene Kandidaten / Verzweigung), `Technique`, `Changes`
  (Knoten + Werte), `Reason` (Constraint, beteiligte Knoten, Text), dazu `NodeId`, `Value`, `NodeIds`,
  `Summary` („Knoten 3, 4, 6: streiche 5“), `Description`; deutsche Namen über `ToGermanText()`.
- `ExplanationSnapshot`: Kandidaten (eigene Kopie) und gesetzte Knoten (Givens + gesetzte Werte), `GetSetValue`.
- Tests: Core 175 (vorher 144; neu u. a. `CandidateSetTests`, `RulePropagationReasonTests` mit Abgleich gegen
  vollständiges Durchprobieren für alle Regeln), Explainer 59 (vorher 2). Gesamt 335 Tests, alle grün in Release.
- Akzeptanz: `puzzles/klassisch-leicht.json` wird ohne Verzweigung vollständig erklärt (200 Schritte: 149 Streichungen
  feststehender Werte, 51 einzige Kandidaten; der Solver wird dabei gar nicht aufgerufen). Konsistenztest für leicht/mittel/schwer und zehn kleine Rätsel (je eine Regel, dazu ein
  4×4-Killer ohne Givens): jeder gesetzte Wert ist der Lösungswert, kein gestrichener Kandidat ist der Lösungswert,
  jeder Zwischenzustand enthält die Lösung. Verzweigungstest (mittel braucht 3 Verzweigungen, schwer ebenfalls mehrere; Knotenwahl und „festgefahren“ geprüft), mehrdeutiges
  Rätsel, Widerspruch durch Logik, Unlösbarkeit nur über den Solver, Zeitlimit des Solvers, ungültiges Rätsel, Abbruch.

- Review-Befunde (zweite Runde) behoben, siehe Nr. 95–100: Stufe 0 „feststehende Werte streichen“ vor den
  Singles (behebt auch Singles, die einen schon vorhandenen Wert ein zweites Mal setzten), Widerspruch statt Ausnahme
  bei einer eigenen Regel, die einen gestrichenen Wert setzt, Solver-Bestätigung bei eigenen Regeln ohne Verzweigung,
  kürzere Texte für hidden singles und Verzweigungen, Zwischenspeicher für `Propagate`, Abbruchprüfung zwischen den
  Constraints.

**Offen**

- ~~UI des Erklär-Modus~~ – erledigt, siehe „Paket 6 – Erklär-Modus (Oberfläche)“.
- Weitere Techniken (naked/hidden pairs, Pointing usw.) sind nicht Teil der Spezifikation; „mittel“ braucht deshalb
  drei Verzweigungen.

**Bekannte Probleme**

- `Sum` prüft in einem `Propagate`-Aufruf jeden Kandidaten per Tiefensuche (Budget 200 000 Suchschritte pro Aufruf);
  ist das Budget erschöpft, werden die restlichen Kandidaten nicht geprüft (weiterhin nur sichere Schlüsse, aber
  unvollständig). Seit dem Zwischenspeicher (Nr. 99) wird die Suche nur wiederholt, wenn sich Kandidaten der
  Gruppe geändert haben. Gemessen (Release, warm): drei Summengruppen mit je 20 Knoten (Werte 1..64, Ziel 500) und einer
  Ungleich-Kette, 206 Schritte und 54 Verzweigungen: 1,03 s ohne, 0,36 s mit Zwischenspeicher. Innerhalb eines
  einzelnen `Propagate`-Aufrufs wird das Abbruch-Token nicht geprüft.
- Weil die Folgen jedes gesetzten Werts jetzt sofort eingetragen werden (Stufe 0), hat der Lösungsweg mehr Schritte
  (leicht 200 statt 120, mittel 202, schwer 209): pro gesetztem Wert meist ein Streichschritt je Gruppe. Dafür zeigt die
  Kandidatenanzeige nie veraltete Kandidaten, und naked singles erscheinen, wo ein Mensch sie sieht (leicht: 51 naked,
  0 hidden statt vorher 2 naked, 49 hidden; mittel 41/13, schwer 38/12).
- Eine eigene, nicht korrekte Regel (Registry-Erweiterung) kann zu einem Widerspruch nach einer Verzweigung, zu einem
  gestrichenen Lösungswert oder zu einer Solver-Lösung abweichend von der hergeleiteten Belegung führen; die Engine
  wirft dann eine `InvalidOperationException` mit deutscher Meldung („Interner Fehler …“), statt ein falsches Ergebnis
  zu melden. Die Oberfläche fängt diese Ausnahme ab und zeigt sie als Meldung (Paket 6, Oberfläche).

## Paket 6 – Erklär-Modus (Oberfläche)

**Erledigt**

- „Erklären“ in der Lösen-Leiste, im Menü „Lösen“ und per F7: Die Erklärung (`ExplanationEngine` über
  `IPuzzleExplainer`/`EnginePuzzleExplainer`) läuft asynchron auf einer Kopie des Rätsels; Fortschritt und
  „Abbrechen“ in der Schrittleiste; das Zeitlimit der Lösen-Leiste gilt auch für die Solver-Aufrufe der Erklärung.
  Abbruch behält die bis dahin gefundenen Schritte (ansehbar).
- `ExplainViewModel` (`MainWindowViewModel.Explain`): Position 0 (Ausgangslage) bis N, Vor/Zurück, Anfang/Ende,
  Schieberegler (`SliderValue`, gerundet und begrenzt), „Schritt n von N“, Kandidatenanzeige ein/aus, „Erklärung
  beenden“; Schrittart (Etikett, Verzweigung orange „Verzweigung – geraten“), Schlussweise, Kurzfassung, deutscher
  Begründungstext und verantwortliche Regel („Gruppe 3 „Zeile 3“ · Alle verschieden“) im Panel rechts, dazu die
  Meldung der Engine farbig (grün/gelb/rot) und die Liste der Verzweigungen.
- Zeichenfläche (`ExplanationView` als Anzeigezustand im Editor): Givens schwarz, hergeleitete Werte blau, geratene
  Werte orange; Knoten des aktuellen Schritts gelb, Verzweigungsknoten orange mit Marke „Raten“, Knoten der Begründung
  grün, Gruppe bzw. Kante der Regel grün umrandet bzw. hervorgehoben; Kandidaten als kleines Raster im Kreis (3 × 3
  bei 9 Werten, 4 × 4 bei 16, bei 0–15 als 0–F), vom aktuellen Schritt gestrichene Kandidaten rot durchgestrichen;
  Knoten ohne Kandidaten (Widerspruch) mit eigenem rotem Ring außen (auch wenn der Knoten zugleich gelb
  hervorgehoben ist).
- Tasten: ← / → einen Schritt, Pos1 / Ende zum Anfang bzw. Ende, wenn die Zeichenfläche (oder eine Schaltfläche)
  den Fokus hat; der Hinweis im Panel nennt die Schrittleiste als Fokus, weil ein Klick in die Zeichenfläche je nach
  Werkzeug eine Bearbeitung ist.
- Jede inhaltliche Änderung (Bearbeitung, Rückgängig/Wiederholen) beendet den Modus mit Hinweis in der Leiste; ein
  neues Dokument beendet ihn ohne Hinweis; eine Bearbeitung während der Berechnung verwirft deren Ergebnis, ein
  Dokumentwechsel und das Schließen des Fensters brechen sie ab. Lösung und Erklärung schließen sich auf der
  Zeichenfläche aus.
- Meldungen für alle Ausgänge (gelöst, mehrdeutig, unbestätigt, unlösbar, festgefahren, ungültig mit Fehlerliste,
  abgebrochen) und für Ausnahmen der Engine („Die Erklärung ist fehlgeschlagen. Interner Fehler: …“).
- Tests (App 207, davon neu 25): `ExplainViewModelTests` (20: Start, Navigation mit Grenzen und Schieberegler,
  Hervorhebung für **jeden** Schritt von „leicht“, Kandidaten ein/aus, Ende nach Bearbeitung/Undo/neuem Dokument,
  Auswahl und Ansicht lassen den Modus stehen, Beenden, Lösung ↔ Erklärung, Abbruch mit und ohne Schritte,
  Bearbeitung und Dokumentwechsel während der Berechnung, „Interner Fehler“ der echten Engine, andere Ausnahmen,
  ungültig, Widerspruch, mehrdeutig, festgefahren, Zeitlimit, Verzweigungen in „mittel“), `ExplanationWindowTests`
  (F7, Pfeiltasten, Pos1/Ende, Schieberegler im echten Fenster), Rendering-Rauchtests (mittlerer Schritt mit und ohne
  Kandidaten, Ende, Verzweigung in „mittel“, Hexadoku 16 × 16 mit 0–F, Mindestgröße, Berechnung läuft, abgebrochen,
  veraltet, ungültig – alle Bilder angesehen, dabei Kandidatenraster, Durchstreichung, Farbe der Leiste und Texte
  nachgebessert).
- **Akzeptanz (auch auf ViewModel-Ebene):** `ExplainViewModelTests.Easy_classic_sudoku_is_explained_without_branching_and_ends_in_the_solution`
  öffnet `puzzles/klassisch-leicht.json` über „Öffnen“, erklärt mit dem echten `OrToolsSolver`, geht jeden Schritt
  mit „Vor“ durch (kein Schritt ist eine Verzweigung, kein gesetzter Wert weicht von der Solver-Lösung ab, kein
  Lösungswert wird gestrichen) und prüft, dass der letzte Zustand genau die Lösung ist.
- Gesamt: 530 Tests (Core 175, Solver 89, Explainer 59, App 207), alle grün in Release.

**Offen**

- Kein Sprung zur nächsten Verzweigung und keine Markierung der Verzweigungen am Schieberegler (die Schrittnummern
  stehen im Panel).
- Der Widerspruch einer unlösbaren Erklärung wird am Ende nur über die Meldung, die rot durchgestrichenen Kandidaten
  und den roten Außenring des Knotens ohne Kandidaten gezeigt; die Knoten von `ExplanationResult.Contradiction`
  werden nicht eigens hervorgehoben.

**Bekannte Probleme**

- Bei Mindestgröße (900 × 560) bleibt der Zeichenfläche im Erklär-Modus etwa 245 px Höhe (Schrittleiste zusätzlich
  zur Lösen-Leiste); die Knoten haben dann etwa 7 px Radius. Das Kandidatenraster und die Anzahl „(n)“ erscheinen
  dabei **nicht**; sichtbar bleiben nur die Hervorhebung und der vom aktuellen Schritt gestrichene Wert (rot
  durchgestrichen). Zum Ansehen der Kandidaten muss man zoomen oder das Fenster vergrößern. (Die frühere Notiz „die
  Kandidaten sind dann sehr klein“ war falsch – sie verschwanden ganz.)
- Beim 16 × 16-Hexadoku sind die Kandidaten bei 1280 × 800 ohne Zoom zu klein für das Raster; dann steht nur die
  Anzahl „(n)“ im Knoten. Ab etwa 1920 × 1200 oder nach Vergrößern erscheint das 4 × 4-Raster mit 0–F.
- Die Bedienung am echten Desktop wurde nicht geprüft (kein Bildschirmzugriff); Verhalten über ViewModel-Tests und
  headless Tastatureingaben, Aussehen über die gerenderten Bilder belegt.

**Review-Befunde (Nachbesserung Paket 6, Oberfläche)**

- Roter Ring für Knoten ohne Kandidaten erschien im „unlösbar“-Ablauf nie (die gelbe Hervorhebung des letzten
  Schritts gewann); jetzt eigener Außenring (Entscheidung Nr. 112), Rendering-Test mit dem Widerspruchs-Sudoku.
- Menüeintrag „Kandidaten anzeigen“ änderte nur sein eigenes Häkchen (`MenuItem.IsChecked` ist in Avalonia 12
  standardmäßig OneWay); jetzt `Mode=TwoWay`, Fenstertest über das Menü (Nr. 114).
- Bei Mindestgröße waren die Kandidaten ganz verschwunden; jetzt steht wenigstens der gestrichene Wert im Knoten
  (Nr. 113), Rendering-Test prüft ihn.
- Regeln an Kanten (Regeltext „Kante n (Knoten a – b) · Kleiner als“, grüne Knoten, grüne dicke Kante) sind jetzt
  getestet: ViewModel-Test und Rendering-Test mit einer „Kleiner als“-Kette aus drei Knoten (`erklaerung-kante.png`).
  Das Hexadoku-Zoombild zoomt auf Knoten 1, damit die geänderte Zeile 1 mit Hervorhebung und gestrichenem Kandidaten
  im Bild ist (geprüft).
- Tastenhinweis im Panel nennt nicht mehr „nach Klick in die Zeichenfläche“ (ein Klick mit „Knoten setzen“ beendet die
  Erklärung), Hinweis der Lösen-Leiste mit Tooltip (Nr. 115).
- Gesamt danach: 533 Tests (Core 175, Solver 89, Explainer 59, App 210), alle grün in Release.

## Paket 7 – Zellformen (optional)

**Erledigt**

- **Darstellung umschaltbar:** Knoten als Kreise (Standard) oder als Zellen in der Form des Gitters – Quadrate,
  Dreiecke nach oben/unten, Sechsecke (Umrisse aus `GridGeometry.GetCellVertices`). Nichts darüber hinaus.
  Umschalten über Ansicht → „Knoten als Kreise“ / „Knoten als Zellen (…)“ (der Text nennt die Form des Gitters) und
  „Knoten als ○ Kreise ○ Zellen“ rechts im Statusbereich (Nr. 153); `EditorViewModel.NodeShape`,
  `IsCircleShape`/`IsCellShape`, `ToggleNodeShapeCommand`, `CellShapeText`.
- **Speicherformat unverändert:** Die Darstellung ist eine Ansichtseinstellung der Sitzung (kein Undo-Schritt, kein
  „ungespeichert“, bleibt beim Öffnen anderer Rätsel), steht nicht in der Rätseldatei und wird nicht als
  App-Einstellung gespeichert (Nr. 152).
- **Alles funktioniert auch mit Zellen:** Auswahl, Shift-Klick, Rahmenauswahl, Verschieben (Mittelpunkt der gepackten
  Zelle rastet ein, Nr. 155), Kante ziehen, Knoten setzen; Treffer überall im Zellumriss, Kanten an ihren Stegen und
  Marken am Zellrand (Nr. 154). Kantenmarken (Kropki-Punkte, Pfeile) auf der Mitte der gemeinsamen Seite, Kanten als
  kurze Stege über den gekreuzten Zellrändern (Nr. 157); Gruppenflächen und -umrisse, Blockgrenzen, Zielsummen,
  Thermometer, Auswahlnummern; Givens, Lösung (blau, abweichend orange), Erklär-Modus (gelb/orange/grün, „Raten“,
  roter Rahmen bei Widerspruch, grüne Regelkante), Kandidatenraster 3 × 3 / 4 × 4 mit 0–F (Nr. 156).
- **Rendering angesehen** (`zellen-*.png`): klassisch 9 × 9 (Auswahl, Gruppe, gelöst, Erklärung mit Kandidaten, Zoom),
  Dreiecks-Sudoku (Auswahl Auf-/Ab-Dreieck, Kante, gelöst, Erklärung mit Zoom), Hexadoku 16 × 16 (mit 0–F-Kandidaten
  und Zoom), Sechseckgitter mit Kanten, Summe und Thermometer, jedes Beispielrätsel, Widerspruch, Verzweigung,
  Regelkante, Umschalter bei Mindestgröße. Dabei behoben: Werkzeugleiste brach mit einem Umschalter bei 1280 px um
  (daher Statusbereich), Gelb des aktuellen Schritts vom Grün der Regelgruppe überlagert (Zellen werden erneut gefüllt),
  Linien quer durch fremde Dreiecke im Dreiecks-Sudoku (jetzt Stege an den Rändern; an den Gitterecken bleiben kleine
  Sterne aus den Stegen der Eck-Kanten, Nr. 158),
  Auswahlrahmen am Zellrand halb unter der Nachbarzelle (jetzt eingerückt).
- Tests (App 310, davon neu 50): `NodeShapeTests` (34: Umschalten ohne Undo/„ungespeichert“/Inhaltsänderung,
  Optionsgruppen-Eigenschaften, bleibt beim Öffnen, Zellumriss-Treffer für Ecken und Nachbarn auf allen drei Gittern,
  Übereinstimmung mit dem nächsten Gitterpunkt für 2000 Zufallspunkte je Gitter, Klick/Shift-Klick in Zellecken, Kante
  am Rand vs. Zelle im Innern, Verschieben am Rand gepackter Zellen auf allen Gittern, Kante zwischen Zellecken ziehen,
  „Knoten setzen“ in vorhandener Zelle, **byteweise gleiche Datei vor/nach dem Umschalten und Round-Trip für jede
  Beispieldatei**, Radien passen in die Zellen, Polygon-Test und Einrücken, Randkreuzungen und Stege),
  `NodeShapeWindowTests` (Menü Ansicht im echten Fenster), Rendering (+15: klassisch, Dreieck, Hexadoku, Sechseckgitter,
  Widerspruch/Verzweigung/Regelkante, jede Beispieldatei, Umschalter im Statusbereich und einzeilige Werkzeugleiste).
- Gesamt: 727 Tests (Core 232, Solver 104, Explainer 81, App 310), alle grün in Release.

**Review-Befunde (Fixer)**

- Rahmenauswahl mitten in einem gefüllten Rätsel war in der Zelldarstellung nicht möglich (jeder Druck traf eine
  Zelle → Verschieben). Behoben: Strg/Cmd + Ziehen beginnt immer einen Rahmen, mit Shift zusätzlich (Nr. 159);
  Werkzeughinweis in der Zelldarstellung angepasst.
- Dreiecks-Sudoku: Kantenstege fingen etwa 28 % der Klicks in Zellen, viele Eckklicks und einige auf den Kandidaten.
  Behoben: Stege greifen nur rund um die Randkreuzung (höchstens halbe Steglänge), nie im Freihaltekreis; Ziehen
  vom Steg verschiebt die Zelle (Nr. 158). Die kleinen Sterne an den Gitterecken sind die sich kreuzenden Stege der
  Eck-Kanten und bleiben (Begründung in Nr. 158; die frühere Angabe „behoben“ war ungenau).
- README: roter Rahmen bei Widerspruch in der Zelldarstellung innen statt Ring außen; Rahmenauswahl mit Strg/Cmd.
- Tests (+8, App 318): Strg/Cmd-Rahmen um den mittleren Block in `klassisch-leer.json` (9 Knoten, nichts verschoben,
  Shift hängt an, Klick ohne Ziehen), Strg/Cmd-Rahmen bei Kreisen, Dreiecks-Sudoku: Ecken (85 %) und Freihaltekreis
  treffen bei einfacher und doppelter Vergrößerung immer die Zelle, jede Kante über einen Steg wählbar, Ziehen vom
  Steg verschiebt die Zelle; im echten Fenster: Statusbereich-Umschalter „Kreise/Zellen“ per Maus (beide Richtungen,
  folgt dem ViewModel) und Strg + Ziehen auf der Zeichenfläche.
- Gesamt: 735 Tests (Core 232, Solver 104, Explainer 81, App 318), alle grün in Release.

**Offen**

- Die Darstellung wird nicht über einen Neustart hinweg gemerkt (keine App-Einstellungen, Nr. 152).
- Kein Tastenkürzel für das Umschalten.
- Marken langer Kanten (über andere Knoten hinweg) liegen auf Dreiecks- und Sechseckgittern im ersten freien Stück, nicht
  zwingend genau auf einem Zellrand (auf dem Quadratgitter schon).

**Bekannte Probleme**

- Der Umschalter nimmt dem Werkzeughinweis im Statusbereich etwa 210 px Breite; bei 1280 px bleibt der Hinweis
  einzeilig, bei Mindestbreite (900 px) war er schon vorher zweizeilig.
- Pfeile („Kleiner als“) sind in Zellen eher klein (sie passen in den Spalt zwischen den Inhaltskreisen zweier
  Nachbarzellen, etwa 0,14 Zellbreiten).
- Kanten zwischen Dreiecken, die sich nur in einer Ecke berühren, aber nicht gegenüberliegen (zwei Zellen weiter in
  derselben Zeile), erscheinen als zwei Stege an den Rändern der Zelle dazwischen – korrekt, aber auf den ersten Blick
  nicht von zwei Kanten zu dieser Zelle zu unterscheiden (wählen zeigt die Endzellen).
- Kantenstege sind bei eingepasster Ansicht im Dreiecks-Sudoku kleine Ziele (Fangbereich etwa 3–4 px um die
  Randkreuzung); Zoomen vergrößert sie. Der Preis dafür, dass Ecken und Inneres der Zellen frei bleiben (Nr. 158).
- Die Rahmenauswahl über Zellen braucht Strg/Cmd; ohne Taste verschiebt Ziehen wie bisher (Nr. 159).
- Die Bedienung am echten Desktop wurde nicht geprüft (kein Bildschirmzugriff); Verhalten über ViewModel- und
  headless-Fenstertests, Aussehen über die gerenderten Bilder.

## Abschluss-Review – Core, Solver, Explainer

- `PuzzleSerializer`: Einzelne UTF-16-Surrogate (`"\uD800"`) in Texten oder Feldnamen ergeben eine
  `PuzzleFormatException` mit Pfad statt einer unbehandelten `InvalidOperationException` (Nr. 161).
- `GridGeometry`: Nachbarn, Eckennachbarn und Springerzüge laufen an den int-Grenzen nicht mehr über; der
  Kanten-Generator verbindet dort keine weit entfernten Zellen mehr (Nr. 162).
- `ISolver.CountSolutions`: Vertrag „Lösungen werden mitgeliefert“ dokumentiert und in den Fabrikmethoden von
  `SolutionCountResult` geprüft; der Erklär-Modus meldet eine unvollständige Solver-Lösung als „Stuck“ statt
  abzustürzen (Nr. 163).
- `puzzles/klassisch-leer.json` als bewusst mehrdeutige Vorlage begründet (Nr. 160), Datei unverändert.
- Tests (+14: Core 244, Solver 105, Explainer 82, App 318 = 749), alle grün in Release.

## Abschluss-Review – App

- Linux-CI: Zwei Rendering-Tests waren auf Linux rot (blaue Subpixel-Säume am schwarzen Given, zu wenige dunkle
  Pixel bei kleinen Käfigsummen). Die Headless-Tests rendern Text jetzt in Graustufen, die Käfigsummen-Prüfung
  vergleicht Dunkelheitssummen (Nr. 164). Nachgestellt mit den Schritten aus `.gitea/workflows/ci.yml` in
  `mcr.microsoft.com/dotnet/sdk:10.0` (Debian, nicht der echte Ubuntu-Runner): Release-Build ohne Warnungen,
  alle 779 Tests grün auf linux/amd64 (emuliert) – vorher App 316/318. Der Lauf auf dem echten Gitea-Runner steht
  weiterhin aus.
- „Ungespeicherte Änderungen“: Schließen des Dialogs über die Titelleiste/Cmd+W/Alt+F4 gilt als „Abbrechen“ statt
  „Speichern“ (Nr. 165).
- Editor: Knoten, Kanten und Gruppen bekommen auch nach einer Id `int.MaxValue` keine doppelten negativen Ids mehr –
  stattdessen eine Meldung; neben Ids ≤ 0 beginnt der Editor bei 1 (Nr. 166).
- Werteingabe: Ein riesiger (ungeprüfter) Wertebereich blockiert die Oberfläche beim Tippen nicht mehr (Nr. 167).
- Leertaste-Verschieben bleibt nicht mehr hängen, wenn der Fokus bei gedrückter Taste wechselt (Nr. 168).
- Toter Code entfernt: `EditorGeometry.FreeSegments`, `GraphCanvas.NodeStrokeBrush`.
- Tests (+30 in App: `EditorLimitTests`, zwei Fenstertests): Core 244, Solver 105, Explainer 82, App 348 = 779, alle
  grün in Release (macOS).

## Abschluss-Review – Repo

- `docs/progress.md`: erledigte Punkte unter „Offen“ einheitlich durchgestrichen (Erklär-Modus in Paket 1,
  Kanten-Generator und Beispielrätsel in Paket 3), Einleitung von Paket 5 (Core) aktualisiert (Editor-Teil ist fertig),
  die Bekannten Probleme zu Nr. 158/159 nur noch bei Paket 7 (waren in Paket 4, 5 und 6 hineinkopiert), Linux-Hinweis
  bei Paket 2 ergänzt.
- Die Angaben „alle grün in Release“ der einzelnen Pakete sind lokal auf macOS gemessen. Auf Linux waren bis zum
  Abschluss-Review (App) zwei Rendering-Tests rot; seit Nr. 164 sind im Linux-Container alle Tests grün. Der erste Lauf
  auf dem echten Gitea-Runner (`ubuntu-latest`) steht weiterhin aus (Nr. 169).
- `docs/decisions.md` Nr. 9: Aussage „keine zusätzlichen Systempakete auf Linux“ als durch Nr. 48 überholt markiert.
- `THIRD-PARTY-NOTICES.md`: transitive Abhängigkeit `Avalonia.FreeDesktop.AtSpi` 12.1.5 (MIT, Linux-Desktop) ergänzt.
- Keine Code-Änderungen, daher keine neuen Tests.
