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
- Erklär-Modus (Paket 6); `Propagate` ist vorhanden, wird dort ggf. verfeinert.
- Validierungsmeldungen sind reine Texte (die Element-Id steht nur im Text). Braucht der Editor (Paket 3) einen
  Verweis auf das fehlerhafte Element zum Markieren, wird dort ein strukturierter Meldungstyp ergänzt.

**Bekannte Probleme**

- Die Gitea-CI konnte lokal nicht ausgeführt werden; die Schritte wurden lokal mit denselben Befehlen nachgestellt.
  Der erste Lauf auf dem Ubuntu-Runner steht noch aus.
- Keine Headless-UI-Tests: Avalonia.Headless.XUnit 12 benötigt xUnit v3 (siehe `docs/decisions.md`, Nr. 7).
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
- Tests (Solver 83): drei klassische Sudokus mit hinterlegter Lösung (unabhängig geprüft), unlösbares und
  mehrdeutiges Sudoku (genau 2 Lösungen), je Regeltyp positive und negative Fälle mit Abgleich gegen vollständiges
  Durchprobieren (unabhängiger Lösungsprüfer in den Testhilfen), Zusammenspiel aller Regeln, Zeitlimit und Abbruch
  (synchron und asynchron), Wertebereich pro Knoten, Given außerhalb des Wertebereichs, unbekannte Regel, fehlender
  Adapter, eigene Regel über beide Registries.
- OR-Tools lädt auf macOS arm64 (lokal getestet); das Paket bringt die nativen Bibliotheken für linux-x64 (CI),
  linux-arm64, osx-x64 und win-x64 mit (Ausgabeverzeichnis `runtimes/<rid>/native`).
- Gesamt: 241 Tests (Core 144, Solver 83, Explainer 2, App 12), alle grün in Release.

**Offen**

- Anbindung an die App (Paket 4).

**Bekannte Probleme**

- Der Linux-Lauf der Solver-Tests (native OR-Tools-Bibliothek auf Ubuntu) ist erst mit dem nächsten CI-Lauf belegt;
  lokal gab es keine Linux-Umgebung.
- Zeitlimit- und Abbruchtests nutzen ein unlösbares Färbungsrätsel (Mycielski-Graph M6 mit 5 Farben), das CP-SAT
  9.15 lokal nicht in 20 s widerlegen kann. Sollte eine künftige OR-Tools-Version es schnell widerlegen, müssen diese
  Tests ein anderes schweres Rätsel bekommen.
