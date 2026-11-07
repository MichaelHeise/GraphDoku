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
| 5 – Generator und alle Regeln im Editor | Kanten-Generator, Beispielrätsel (Killer, Kropki, Hexadoku, Dreiecksgitter) | erledigt |
| 6 – Erklär-Modus | Kandidaten, Propagation mit Begründung, Schrittleiste | erledigt |
| 7 – optional: Zellformen | Dreieck, Quadrat, Sechseck als Darstellung | erledigt |

Alle sieben Pakete sind umgesetzt. Zusammenfassung mit allen Abweichungen vom Plan, offenen Punkten und
Qualitätssicherung: [`docs/implementation-summary.md`](docs/implementation-summary.md). Details: `docs/progress.md`;
Entscheidungen und Abweichungen einzeln begründet: `docs/decisions.md`.

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
| Auswählen | Werkzeug „Auswahl“: Klick, Shift-Klick (hinzufügen/entfernen), Rahmen ziehen (Shift: hinzufügen; mit Strg/Cmd beginnt der Rahmen auch auf einem Knoten), Strg/Cmd+A |
| Verschieben | ausgewählte Knoten ziehen; sie rasten auf Gitterpunkte ein |
| Löschen | Entf (Mac: fn+⌫) löscht ausgewählte Knoten (mit ihren Kanten), die gewählte Kante oder Gruppe |
| Givens | Knoten auswählen und Ziffern tippen (mehrstellig nacheinander, bei 0–15 auch A–F); ⌫ löscht Givens |
| Kante | Werkzeug „Kante ziehen“, Regel in der Werkzeugleiste wählen, von Knoten zu Knoten ziehen; Kante anklicken, um Regel und Parameter zu ändern |
| Kanten-Generator | „Erzeugen …“ neben der Kantenregel, Bearbeiten → Kanten erzeugen … oder Strg/Cmd+E: Abstandsregel (direkte Nachbarn, inkl. diagonal, Springerzug nur auf dem Quadratgitter, freier Abstand von–bis mit Toleranz), Regel und Parameter, bei „Kleiner als“ die Richtung, Knotenmenge (ausgewählte Knoten oder alle). Die Vorschau zeigt, wie viele Kanten entstehen und welche Paare schon eine solche Kante haben; „Kanten erzeugen“ fügt alle als **einen** Schritt für „Rückgängig“ ein |
| Gruppe | Knoten auswählen (die Reihenfolge zählt, z. B. für Thermometer), im Panel Regel wählen, „Gruppe erstellen“ (Strg/Cmd+G); Beschriftung, Farbe, Regel, Parameter und Reihenfolge im Panel |
| Thermometer | erste Zelle (Kolben) anklicken, dann die weiteren der Reihe nach mit Shift-Klick; die Nummern an den Knoten zeigen die Reihenfolge; „Reihenfolge umkehren“ im Gruppen-Panel |
| Kleiner als | Kante vom kleineren zum größeren Wert ziehen; „Richtung umkehren“ im Kanten-Panel |
| Eigener Wertebereich | Knoten auswählen, im Panel „Eigener Wertebereich“ von–bis eingeben, „Bereich setzen“; „Globalen Bereich verwenden“ entfernt ihn |
| Gruppen hervorheben | Häkchen in der Gruppenliste bzw. „Alle ein“/„Alle aus“ |
| Ansicht | Mausrad zoomt, mittlere/rechte Maustaste oder Leertaste + Ziehen verschiebt, „Einpassen“ (Strg/Cmd+0) |
| Knoten als Zellen | Ansicht → „Knoten als Zellen (…)“ oder unten rechts „Knoten als ○ Kreise ○ Zellen“: Knoten erscheinen als Quadrate, Dreiecke bzw. Sechsecke des Gitters statt als Kreise. Alles lässt sich weiter bedienen (Klick irgendwo in die Zelle; Kanten genau auf ihrem Steg oder ihrer Marke am Zellrand anklicken – Ziehen von dort verschiebt die Zelle; Rahmenauswahl mitten im Rätsel mit **Strg/Cmd + Ziehen**, mit Shift zusätzlich zur Auswahl). Reine Ansicht: ändert weder das Rätsel noch die Datei und gilt bis zum Beenden der App |
| Rückgängig | Strg/Cmd+Z, Wiederholen Strg/Cmd+Y oder Shift+Strg/Cmd+Z (alle Bearbeitungen) |
| Dateien | Öffnen (Strg/Cmd+O), Speichern (Strg/Cmd+S), Speichern unter (Shift+Strg/Cmd+S); „ *“ im Titel = ungespeichert |

Unfertige Rätsel lassen sich speichern und wieder öffnen; was noch fehlt (z. B. eine Gruppe mit zu wenigen Knoten),
steht im Statusbereich. Kantenmarken: Ungleich = Linie, Differenz = weißer Punkt (mit k, falls nicht 1), Verhältnis =
schwarzer Punkt (mit Faktor, falls nicht 2), Kleiner als = Pfeil vom kleineren zum größeren Wert; bei Kanten über
einen Knoten hinweg sitzt die Marke im ersten freien Stück neben diesem Knoten. Summen-Gruppen (Killer-Käfige)
zeigen ihre Zielsumme klein oben links in ihrer ersten Zelle; Thermometer erscheinen als Linie mit Kolben am Anfang.
In der Zelldarstellung sitzen die Marken auf der Mitte der gemeinsamen Seite zweier Zellen, Kanten ohne Marke
(„Ungleich“) als kurzer Steg über jedem Zellrand, den sie kreuzen.

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
| Kandidaten | Häkchen „Kandidaten“ (auch Menü): kleine Ziffern im Kreis bzw. in der Zelle, 3 × 3 bei 1–9, 4 × 4 bei 16 Werten (0–F); sind die Knoten dafür zu klein, steht die Anzahl „(n)“ bzw. bei sehr kleinen Knoten nur der gerade gestrichene Wert im Knoten (Zoomen hilft) |
| Beenden | „Erklärung beenden“; jede Bearbeitung des Rätsels beendet die Erklärung ebenfalls |

Farben auf der Zeichenfläche: **schwarz** Givens, **blau** hergeleitete Werte, **orange** geratene Werte;
**gelb** hinterlegt die Knoten des aktuellen Schritts (rot durchgestrichen: gerade gestrichene Kandidaten),
**orange** mit Marke „Raten“ eine Verzweigung, **grün** die Knoten, die Gruppe bzw. die Kante der verantwortlichen
Regel; **rot umrandet** ein Knoten ohne Kandidaten (Widerspruch) – bei Kreisen als Ring außen, in der Zelldarstellung als Rahmen innen in der Zelle. Wie die Lösung ist die Erklärung nur Anzeige: Sie ändert das Rätsel nicht und wird nicht gespeichert.

Rendering-Rauchtest: `dotnet test tests/GraphDoku.App.Tests --filter Rendering` rendert das Hauptfenster headless
(Skia, auch mit gelöstem und mehrdeutigem Sudoku, im Erklär-Modus und in der Zelldarstellung) und legt PNGs in `$GRAPHDOKU_RENDER_DIR` bzw. im Temp-Verzeichnis unter `GraphDoku-Render` ab.

## Beispielrätsel

Die Dateien in `puzzles/` lassen sich über Datei → Öffnen laden; alle Tests prüfen sie automatisch (gültig, lösbar,
außer `klassisch-leer.json` eindeutig).

| Datei | Beschreibung |
| --- | --- |
| `klassisch-leer.json` | Klassisches 9×9 ohne Givens (Vorlage zum Selbstausfüllen; nicht eindeutig) |
| `klassisch-leicht.json` | Klassisches 9×9, 30 Givens; der Erklär-Modus löst es ohne Verzweigung |
| `klassisch-mittel.json` | Klassisches 9×9, 24 Givens; der Erklär-Modus braucht einige Verzweigungen |
| `klassisch-schwer.json` | Klassisches 9×9, 21 Givens; der Erklär-Modus braucht mehrere Verzweigungen |
| `killer-sudoku.json` | Killer-Sudoku 9×9: 28 farbige Käfige mit Summe (`Sum`, Summe oben links im Käfig), 3 Givens |
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
  implementation-summary.md  Zusammenfassung der Umsetzung (Abweichungen, offene Punkte)
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
- Beim Hexadoku (Wertebereich genau 0–15) zeigt der Editor 10–15 als A–F an und nimmt A–F als Eingabe, auch bei Knoten mit eigenem Bereich innerhalb 0–15; gespeichert werden immer Zahlen.

## CI

Gitea Actions (`.gitea/workflows/ci.yml`) läuft bei jedem Push und Pull Request auf einem Ubuntu-Runner
(`ubuntu-latest`): Restore, Release-Build und alle Tests; die Testergebnisse (TRX) werden als Artefakt hochgeladen.
Für den headless Rendering-Rauchtest installiert die CI bei Bedarf `libfontconfig1` (von SkiaSharp benötigt).

## Veröffentlichen (Release)

Ein Versions-Tag baut auf dem Gitea-Runner alle Pakete und legt ein Release mit den Downloads an:

```bash
git tag v1.0.0
git push origin v1.0.0
```

`.gitea/workflows/release.yml` baut und testet dann, erzeugt mit `tools/package.sh` die Pakete, prüft das
Linux-Paket mit einem Selbsttest und hängt alles an das Release:

| Datei | Inhalt |
| --- | --- |
| `GraphDoku-<Version>-windows-x64.zip` | Ordner mit `GraphDoku.App.exe` |
| `GraphDoku-<Version>-macos-arm64.zip`, `…-macos-x64.zip` | `GraphDoku.app` (Apple Silicon bzw. Intel), ad hoc signiert |
| `GraphDoku-<Version>-linux-x64.tar.gz` | Ordner mit `GraphDoku.App`, Symbol und `.desktop`-Vorlage |
| `SHA256SUMS-<Version>.txt` | Prüfsummen |

- Alle Pakete sind eigenständig (kein installiertes .NET nötig) und enthalten Lizenz, Drittanbieter-Hinweise und die
  Beispielrätsel (Ordner `Beispiele`).
- Windows braucht zusätzlich die „Microsoft Visual C++ Redistributable 2015–2022 (x64)“ (von OR-Tools vorausgesetzt,
  auf den meisten PCs schon vorhanden): <https://aka.ms/vs/17/release/vc_redist.x64.exe>. Fehlt sie, meldet die App
  das beim ersten Lösen mit diesem Link.
- Tags mit Zusatz (`v1.1.0-beta.1`) werden als Vorabversion markiert.
- Probelauf ohne Release: in Gitea unter Actions → Release → „Workflow ausführen“; die Pakete liegen dann als Artefakt
  am Lauf.
- macOS: Die App ist nur ad hoc signiert (ohne Apple-Entwicklerkonto). Beim ersten Start meldet macOS einen nicht
  verifizierten Entwickler; öffnen über Systemeinstellungen → Datenschutz & Sicherheit → „Dennoch öffnen“.
- Die macOS-Pakete signiert der Linux-Runner mit `rcodesign`; Apples `codesign` gibt es dort nicht. Nach Änderungen an
  Paketierung oder Signatur einmal einen Probelauf machen, das macOS-Artefakt auf einem Mac entpacken und
  `codesign --verify --deep --strict GraphDoku.app` ausführen.
- Legt der Token der Action kein Release an (HTTP 401/403), ein Repository-Secret `RELEASE_TOKEN` mit der Berechtigung
  `write:repository` anlegen.

Pakete lokal bauen (macOS signiert mit `codesign`, Linux braucht `rcodesign` für die macOS-Ziele):

```bash
tools/package.sh 1.0.0                     # alle Ziele nach artifacts/release
tools/package.sh 1.0.0 artifacts/release win-x64 linux-x64
```

Ein fertiges Paket lässt sich ohne Fenster prüfen: `GraphDoku.App --selbsttest [Rätseldatei]` löst, prüft die
Eindeutigkeit und erklärt (Exit-Code 0 bei Erfolg). Unter Windows ist die App ein Fensterprogramm: Die Ausgabe erscheint
nicht in der Konsole, und den Exit-Code liefert nur ein wartender Aufruf, z. B.
`(Start-Process .\GraphDoku.App.exe -ArgumentList '--selbsttest' -Wait -PassThru).ExitCode` in PowerShell. Das App-Symbol erzeugt `tools/icon/build-icons.sh` (macOS).

## Lizenz

Proprietär, alle Rechte vorbehalten – siehe `LICENSE`. Komponenten Dritter: `THIRD-PARTY-NOTICES.md`.
