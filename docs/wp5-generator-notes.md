# Paket 5 – Kanten-Generator und Beispielrätsel (Core- und Datenteil)

Notizen aus dem Worktree `feature/wp5-generator`. Beim Zusammenführen werden sie in `docs/progress.md` und
`docs/decisions.md` übernommen. Die Editor-Oberfläche für den Generator kommt in einem späteren Schritt (nach dem Merge).

## Stand

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
- Erzeugungscode als Testcode: `tests/GraphDoku.Explainer.Tests/Generation/ExamplePuzzleGenerator.cs` (nicht in der App).
- Build und alle Tests grün (Core 230, Solver 104, Explainer 81, App 192). Die App-Tests, die jede Datei in `puzzles/`
  öffnen, lösen und auf Eindeutigkeit prüfen (`SolveIntegrationTests`, `MainWindowViewModelTests`), laufen mit den neuen
  Dateien ohne Änderung durch.

**Offen**

- Editor-Oberfläche für den Generator (Abstandsregel, Regeltyp, Parameter, Richtung, „Auswahl oder alle Knoten“) – laut
  Auftrag ein späterer Schritt nach dem Merge. Vorgesehene Nutzung siehe Entscheidung G1.
- **Käfigsummen werden auf der Zeichenfläche nicht angezeigt.** `GraphCanvas` zeichnet keine Gruppenbeschriftung und keinen
  `target`-Wert; das Killer-Sudoku ist im Editor deshalb nur über das Eigenschaften-Panel (Zielsumme je Käfig) lesbar.
  Empfehlung für den UI-Schritt: Summe klein in die linke obere Zelle eines `Sum`-Käfigs schreiben. Im Review bestätigt
  (Daten korrekt, nur die Anzeige fehlt); ohne sichtbare Summen ist das Killer-Sudoku auf der Zeichenfläche nicht
  spielbar. Bewusst hier nicht behoben, weil `src/GraphDoku.App` in diesem Worktree tabu ist – **muss** im UI-Schritt
  nach dem Merge erledigt werden.
- Der Kommentar in `SolveIntegrationTests` („alle Beispielrätsel bestehen bisher nur aus AllDifferent“) ist veraltet; die
  dortige allgemeine Prüfung (Lösung als Givens bleibt lösbar) deckt die neuen Regeln trotzdem ab. Nicht geändert, weil
  `tests/GraphDoku.App.Tests` in diesem Worktree tabu war.

**Bekannte Probleme / Hinweise**

- Die fünf Generator-„Tests“ (`ExamplePuzzleGenerator.*`) erscheinen im normalen Testlauf als bestanden, tun dort aber
  nichts (siehe G9).
- Das Dreiecks-Sudoku hat 168 `NotEqual`-Kanten; der Editor zeichnet sie als Linien, das Bild ist dicht, aber lesbar
  (Rendering mit einem nicht eingecheckten Headless-Hilfsprogramm angesehen).

## Entscheidungen Kanten-Generator

- **G1 – Reine Funktion plus Anwenden in einem Schritt.** `EdgeGenerator.Generate(puzzle, request)` verändert das Rätsel
  nicht und liefert ein `EdgeGenerationResult` mit den neuen Kanten (fortlaufende Ids ab `Puzzle.NextEdgeId()`,
  mindestens ab 1 – siehe G17),
  Anzahl passender und übersprungener Paare, Fehlern und einer deutschen Meldung. `result.ApplyTo(puzzle)` fügt Kopien
  ein (wirft, wenn das Ergebnis Fehler hat oder eine Id inzwischen vergeben ist). Grund: Der Editor kann Vorschau und
  Meldung zeigen und die Kanten in **einem** Undo-Befehl einfügen (Execute = `ApplyTo` bzw. Kanten hinzufügen, Undo =
  Kanten mit diesen Ids entfernen), ohne dass Core die Undo-Infrastruktur der App kennen muss.
- **G2 – Abstandsregeln (`DistanceRule`).** `Orthogonal` (gemeinsame Kante: Quadrat 4, Dreieck 3, Sechseck 6),
  `WithDiagonals` (gemeinsame Ecke: 8 / 12 / 6), `KnightMove` (nur Quadratgitter, wie `GridGeometry`, Nr. 29) – alle über
  die vorhandenen Nachbarlisten von `GridGeometry`. **Freier Abstand** = euklidischer Abstand der Zellmittelpunkte in
  Welteinheiten im Bereich **[min, max] einschließlich, mit Toleranz** (Standard 1e-6); `DistanceRule.Exact(d)` ist der
  Bereich [d, d] und entspricht damit `GridGeometry.IsAtDistance` (Nr. 30). Der Bereich ist eine Erweiterung gegenüber
  „frei wählbarer Abstand“: Er erlaubt z. B. „alle Knoten im Umkreis 1,5“ ohne zweite Regelart. Bedingungen: endliche
  Zahlen, min > 0, max ≥ min, 0 ≤ Toleranz < min. Auf dem Dreiecksgitter haben Kantennachbarn den Abstand 1/√3, gleich
  ausgerichtete waagerechte Nachbarn den Abstand 1.
- **G3 – Knotenmenge.** `NodeIds = null` heißt alle Knoten, sonst z. B. die Auswahl; **beide** Endknoten einer Kante
  müssen in der Menge liegen (Auslegung von „auf Auswahl anwenden“). Doppelte Ids in der Menge werden ignoriert,
  unbekannte Ids sind ein Fehler. Knoten an derselben Gitterposition werden nie miteinander verbunden. Enthält das Rätsel
  doppelte Knoten-Ids (Datei ohne Prüfung geöffnet, Nr. 57), verweigert der Generator mit Meldung.
- **G4 – Doppelte Kanten.** Ein Paar wird übersprungen, wenn zwischen beiden Knoten bereits eine Kante **mit demselben
  Regeltyp** besteht oder in diesem Lauf erzeugt wurde – **ungerichtet verglichen und unabhängig von den Parametern**.
  Andere Regeltypen am selben Paar blockieren nicht. Abweichung von der Editor-Regel für einzeln gezogene Kanten (Nr. 54:
  gerichtete Kanten gelten nur in gleicher Richtung als doppelt): Eine vom Generator erzeugte Gegenkante `b → a` neben
  `a → b` („Kleiner als“) wäre sofort ein Widerspruch, und eine zweite `Difference`-Kante mit anderem `k` am selben Paar
  ist fast immer ein Bedienfehler. Ein zweiter Lauf mit derselben Einstellung erzeugt deshalb nichts („Alle n passenden
  Knotenpaare haben bereits eine Kante …“).
- **G5 – Richtung (`EdgeDirectionPolicy`).** „Kleiner als“ wird **nicht** abgelehnt, sondern über eine Richtungsregel
  erzeugt: `ReadingOrder` (Standard) – From ist der Knoten, der in Leserichtung zuerst kommt (kleinere Gitterzeile y,
  dann kleinere waagerechte Weltkoordinate, dann kleinere Id; beim Sechseckgitter zählt die Weltposition x + y/2), die
  Werte steigen also nach rechts bzw. unten; `ReverseReadingOrder` umgekehrt. Einzelne Kanten dreht der Nutzer danach mit
  „Richtung umkehren“ (gibt es seit Paket 3). Ungerichtete Regeln bekommen dieselbe Ordnung (deterministische Dateien).
  Grund: So bleibt der Generator für alle Kantenregeln gleich bedienbar; ein „Größer-als-Sudoku“ lässt sich anlegen,
  indem man alle Nachbarn erzeugt und einzelne Pfeile umdreht.
- **G6 – Regel und Parameter.** Die Regel muss in der Registry stehen und für Kanten gelten (Gruppenregeln wie `Sum` →
  Meldung „gilt für Gruppen, nicht für Kanten“). `Params = null` → `IRule.CreateDefaultParams()` (z. B. `Difference`
  k = 1). Geprüft wird über `IRule.Validate` an einer Probekante (Meldung mit Präfix „Ungültige Parameter für „Differenz“:
  …“). Jede erzeugte Kante bekommt eine eigene Kopie der Parameter.
- **G7 – Fehler statt Ausnahmen.** Eingabefehler (unbekannte Regel, Gruppenregel, ungültige Parameter, Springerzug auf
  Dreieck/Sechseck, ungültiger Abstand, unbekannte Knoten) werden gesammelt und gemeinsam gemeldet; es entstehen dann
  keine Kanten. Nur `null`-Argumente werfen. Grund: wie Solver und Erklär-Modus (Nr. 37, 93) – die Oberfläche zeigt
  Meldungen, statt Ausnahmen abzufangen.
- **G8 – Reihenfolge und Meldungen.** Kanten sind nach dem Knoten des Paars sortiert, der in Leserichtung zuerst kommt,
  dann nach dem zweiten – unabhängig von der Richtungsregel (bei `ReverseReadingOrder` also nach To, dann From).
  Meldungen: „12 Kanten „Ungleich“ erzeugt (direkte Nachbarn (gemeinsame Kante)). 4 Knotenpaare hatten bereits eine
  solche Kante.“, „Keine Kanten erzeugt: Kein Knotenpaar erfüllt die Abstandsregel „Abstand 1,5“.“ (Dezimalkomma wie
  Nr. 45, ohne ICU-Kulturdaten). `DistanceRule.Description` liefert die Texte für die spätere Auswahlliste.

- **G17 – Kanten-Ids mindestens 1 (Review-Befund).** `Puzzle.NextEdgeId()` ist „größte Id + 1“ und kann bei einer
  ungeprüft geöffneten Datei (Nr. 57) mit Kanten-Ids ≤ 0 selbst eine Id ≤ 0 liefern (z. B. −4 bei einer vorhandenen Id
  −5); `PuzzleValidator` behandelt Ids ≤ 0 als Fehler (Nr. 66). Der Generator beginnt deshalb bei
  `Math.Max(1, NextEdgeId())`. Vorhandene ungültige Ids werden **nicht** gemeldet oder verweigert (anders als doppelte
  Knoten-Ids, G3): Die neuen Ids sind auch dann eindeutig und gültig (alle liegen über der größten vorhandenen Id),
  die Prüfmeldungen zeigen den alten Fehler weiterhin an, und der Generator erzeugt
  keinen neuen. Doppelte Kanten-Ids stören den Generator nicht, weil er nur über die Endknoten vergleicht.
  `Puzzle.NextEdgeId()` selbst wurde nicht geändert, weil der Editor (App, in diesem Worktree tabu) es ebenfalls nutzt;
  dort besteht dieselbe Lücke für einzeln gezogene Kanten sowie für `NextNodeId`/`NextGroupId` (Hinweis für den
  Merge-Schritt).

## Entscheidungen Beispielrätsel

- **G9 – Erzeugung als Testcode, statische Dateien.** `ExamplePuzzleGenerator` (Explainer-Tests, weil dort Solver und
  Erklär-Modus verfügbar sind) läuft nur, wenn `GRAPHDOKU_GENERATE_PUZZLES` auf ein Ausgabeverzeichnis zeigt, sonst
  kehren die fünf Facts sofort zurück (im normalen Lauf „bestanden“). Aufruf z. B.
  `GRAPHDOKU_GENERATE_PUZZLES=/tmp/out dotnet test tests/GraphDoku.Explainer.Tests -c Release --filter
  "FullyQualifiedName~ExamplePuzzleGenerator.Killer"`, danach die Datei nach `puzzles/` kopieren. Kein `[Fact(Skip)]`,
  weil sich übersprungene Tests in xUnit 2 nicht gezielt starten lassen; keine eigene Kategorie, weil dafür die CI einen
  Filter bräuchte. Feste Seeds (5001–5005); CP-SAT rechnet mit mehreren Threads, ein neuer Lauf ist deshalb nicht
  zwingend bitgleich. Der normale Testlauf erzeugt nichts, er lädt und prüft nur.
- **G10 – Verfahren.** (1) Zufällige Lösung: Struktur ohne Givens, zufällige Startwerte setzen, solange CP-SAT eine Lösung
  findet, dann lösen. (2) Killer-Käfige bzw. Kropki-Punkte werden aus dieser Lösung abgeleitet. (3) Givens: entweder so
  lange an dem Knoten der ersten Verzweigung des Erklär-Modus den Lösungswert eintragen, bis die Erklärung ohne
  Verzweigung auskommt (Killer), oder vom voll ausgefüllten Gitter ausgehen (übrige). (4) Givens in zufälliger
  Reihenfolge entfernen, solange die Erklärung ohne Verzweigung bleibt; mehrere Durchläufe, das Ergebnis mit den
  wenigsten Givens gewinnt (Killer: 40 Käfigaufteilungen). **Kriterium „ohne Verzweigung erklärbar“ ist strenger als
  „eindeutig“** (die Logik schließt nur sicher, ohne Verzweigung erklärt = eindeutig, Nr. 90). Grund: Die Beispiele sollen
  den Erklär-Modus vorführen, ohne dass er raten muss. Folge: etwas mehr Givens als für bloße Eindeutigkeit nötig wären
  (vor allem beim Dreiecks-Sudoku: 33). Gespeichert wird nur nach erfolgreicher Prüfung (`PuzzleValidator` leer,
  `CountSolutions` eindeutig).
- **G11 – Killer-Sudoku.** Klassisches 9×9 (Zeilen/Spalten/Blöcke `AllDifferent`, Beschriftungen wie die Vorlage) plus
  28 Käfige: zusammenhängend (gemeinsame Kanten), 2–4 Zellen (der Generator erlaubt bis 5), ohne doppelte Ziffer, `Sum` mit `target` und Standard
  `allowRepeats = false` (in der Datei weggelassen). Ein Killer ganz ohne Givens war mit dem Kriterium aus G10 in 40
  Versuchen nicht zu finden (der Erklär-Modus kennt keine Killer-Techniken wie die 45er-Regel); das beste Ergebnis hat
  **3 Givens** („wenige“ laut Auftrag). Beschriftung „Käfig n“ (ohne Summe, damit sie beim Ändern der Zielsumme nicht
  veraltet), eigene Farbe je Käfig aus der Editor-Palette (Blau, Orange, Grün, Rot, Violett, Türkis, Pink), benachbarte
  Käfige verschieden gefärbt (gierige Färbung, Test prüft das). Mit eigener Farbe bekommen Käfige im Editor Fläche und
  farbigen Umriss (Nr. 59/61) – zuerst gewählte Pastellfarben waren im Rendering kaum zu sehen und wurden durch die
  Palettenfarben ersetzt (die Auswahlliste im Panel zeigt sie so auch an).
- **G12 – Kropki-Sudoku.** Klassisches 9×9 mit **allen** Punkten der Lösung: schwarzer Punkt = `Ratio` mit `factor` 2,
  weißer Punkt = `Difference` mit `k` 1, `mode` `exact`. Ein Paar 1–2 erfüllt beides und bekommt den schwarzen Punkt.
  **Keine negative Bedingung** (fehlender Punkt sagt nichts aus; laut Auftrag nicht verlangt und mit dem Katalog nur über
  viele Zusatzkanten ausdrückbar). Punkte nur zwischen Kantennachbarn, höchstens einer je Paar (mit dem Kanten-Generator
  bestimmt). 2 Givens.
- **G13 – Hexadoku.** 16×16, globaler Bereich **0–15** (Anzeige 0–9/A–F, Nr. 32), Zeilen, Spalten und 4×4-Blöcke als
  **`CoverRange`** ohne Parameter (Standard = Rätselbereich) – so kommt `CoverRange` in einem Beispiel vor. 90 Givens.
- **G14 – Dreiecks-Sudoku (Tridoku-ähnlich, eigene Regeln).** Großes, nach oben zeigendes Dreieck der Seitenlänge 9:
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
- **G15 – Zusätzlich: Thermo-Sudoku 6×6** (optional laut Auftrag): Werte 1–6, Zeilen, Spalten und Blöcke 2 × 3
  (`AllDifferent`), 3 Thermometer (je 3 Zellen, orthogonal, steigende Lösungswerte), 4 „Kleiner als“-Kanten zwischen
  Nachbarn außerhalb der Thermometer, 5 Givens. Damit kommen alle acht Katalogregeln in den Beispielen vor.
- **G16 – Namen und Dateinamen** deutsch und ohne Umlaute im Dateinamen: „Killer-Sudoku“, „Kropki-Sudoku“,
  „Hexadoku 16x16“, „Dreiecks-Sudoku“, „Thermo-Sudoku 6x6“.

## Laufzeiten (Entwicklungsrechner, macOS arm64, Release)

Erklär-Modus (`ExplanationEngine.Explain`, ohne Testprüfungen):

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
