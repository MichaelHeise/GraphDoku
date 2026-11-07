# Paket 6 – Erklär-Modus (Bibliotheksteil): Notizen zum Zusammenführen

Diese Datei entstand im Worktree `feature/wp6-explainer` parallel zur Editor-Arbeit. Der Lead übernimmt die Abschnitte
nach `docs/progress.md` (Fortschritt) und `docs/decisions.md` (Entscheidungen, fortlaufend nummeriert ab Nr. 47).

## Fortschritt

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

- Review-Befunde (zweite Runde) behoben, siehe Entscheidungen 14–19: Stufe 0 „feststehende Werte streichen“ vor den
  Singles (behebt auch Singles, die einen schon vorhandenen Wert ein zweites Mal setzten), Widerspruch statt Ausnahme
  bei einer eigenen Regel, die einen gestrichenen Wert setzt, Solver-Bestätigung bei eigenen Regeln ohne Verzweigung,
  kürzere Texte für hidden singles und Verzweigungen, Zwischenspeicher für `Propagate`, Abbruchprüfung zwischen den
  Constraints.

**Offen**

- UI des Erklär-Modus (Schrittleiste mit Vor/Zurück, Anfang/Ende, Schieberegler, Hervorhebung, Kandidatenanzeige
  ein/aus) – kommt nach dem Zusammenführen mit dem Editor. Die Bibliothek liefert dafür alles Nötige:
  `GetSnapshot(n)`, `step.NodeIds` (aktuelle Zellen), `step.InvolvedNodeIds` und `step.Constraint` (Regel zum
  Hervorheben), `step.Text`/`Summary`, `snapshot.IsSet` (Wert oder Kandidaten anzeigen).
- Weitere Techniken (naked/hidden pairs, Pointing usw.) sind nicht Teil der Spezifikation; „mittel“ braucht deshalb
  drei Verzweigungen.

**Bekannte Probleme**

- `Sum` prüft in einem `Propagate`-Aufruf jeden Kandidaten per Tiefensuche (Budget 200 000 Suchschritte pro Aufruf);
  ist das Budget erschöpft, werden die restlichen Kandidaten nicht geprüft (weiterhin nur sichere Schlüsse, aber
  unvollständig). Seit dem Zwischenspeicher (Entscheidung 18) wird die Suche nur wiederholt, wenn sich Kandidaten der
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
  zu melden. Die Oberfläche sollte diese Ausnahme abfangen.

## Entscheidungen

1. **Kandidatenmengen als unveränderliche Bitmengen (`CandidateSet`).** Grund: Der Erklär-Modus braucht billige
   Schnappschüsse. Mit unveränderlichen Mengen teilen Kopien alle unveränderten Knoten; `Clone()` ist O(Knotenanzahl).
   Die Bitmenge liegt über dem eigenen Wertebereich des Knotens (Versatz = Minimum), damit Knoten mit abweichendem
   Bereich funktionieren. `CandidateState.GetCandidates` liefert jetzt `CandidateSet` statt `IReadOnlySet<int>`
   (quellkompatibel, `CandidateSet` implementiert `IReadOnlySet<int>`); eine zurückgegebene Menge ändert sich später
   nicht mehr (vorher war sie eine Live-Ansicht). Minimum/Maximum heißen `MinValue`/`MaxValue`, damit sie nicht mit
   LINQ `Min()`/`Max()` kollidieren.
2. **`Reason.Constraint` ist nullable.** Grund: „einziger Kandidat“ und „Verzweigung“ hängen an keiner Regel; der
   Schritt soll trotzdem dieselbe `Reason`-Struktur (Text, beteiligte Knoten) tragen. Regeln setzen den Constraint
   weiterhin immer.
3. **Überarbeitung von `Propagate` (Korrektheit und Vollständigkeit):**
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
4. **Begründungstexte.** Streichungen festgelegter Werte in Gruppen haben einen vom Zielknoten unabhängigen Text,
   z. B. „Gruppe 3 „Zeile 3“ enthält bereits die 5 (Knoten 21). Alle Werte sind verschieden. Also ist die 5 in den
   übrigen Knoten der Gruppe nicht mehr möglich.“ (`CoverRange`: „Jede Zahl von 1 bis 9 kommt genau einmal vor.“,
   `Sum`: „In der Summe wiederholen sich keine Werte.“). `LessThan` nennt die Schranke des anderen Knotens,
   `Difference`/`Ratio` dessen Kandidaten (ab 13 Kandidaten gekürzt), `Sum` „genau x“ statt „x bis x“, `Thermometer`
   „muss … den Wert x haben“ statt „zwischen x und x“. Der Text bei `NotEqual` ist unverändert.
5. **Ein bestehender Test geändert:** `Sum_uses_bounds_of_other_nodes` prüft jetzt mit `allowRepeats = true` (dort
   bleiben 1, 2, 3); ohne Wiederholung streicht die stärkere Propagation zusätzlich die 2 (eigener neuer Test).
6. **Schritte und ihre Körnung:**
   - Ein gesetzter Wert ist immer ein eigener Schritt („gesetzter Wert“). Die Folgen für die Nachbarn sind **nicht**
     Teil dieses Schritts, sondern eigene Streichschritte (seit Entscheidung 14 in Stufe 0, sonst Stufe 3).
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
7. **Feste Reihenfolge der Schlüsse**, nach jedem Schritt wieder von vorn (Stufe 0 siehe Entscheidung 14):
   0. Feststehende Werte streichen (seit Entscheidung 14).
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
8. **Widersprüche** werden vor jedem Schritt geprüft: Knoten ohne Kandidaten (Text nennt den letzten Schritt, der
   ihn verändert hat; Constraint und beteiligte Knoten stammen von dort), Zahl ohne Platz in einer `CoverRange`-Gruppe,
   `AllDifferent`-Gruppe mit weniger möglichen Werten als Knoten. Ohne vorherige Verzweigung ist das ein Beweis der
   Unlösbarkeit (Status `Unsolvable`, `Contradiction` gesetzt); der Solver wird dafür nicht gefragt.
9. **Verzweigung und Solver-Nutzung:**
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
10. **Navigation über Zwischenstände statt Schnappschuss pro Schritt:** Alle 16 Schritte wird ein Zwischenstand
    gespeichert, `GetSnapshot(n)` spielt ab dem nächstgelegenen höchstens 15 Schritte nach. Das Anwenden eines Schritts
    ist für Schleife und Nachspielen derselbe Code (deterministisch). Jeder Snapshot ist eine eigene Kopie.
11. **Abbruch** wie beim Solver ohne `OperationCanceledException`: Status `Cancelled`, die bis dahin erzeugten Schritte
    bleiben erhalten (navigierbar). Das Token wird vor jedem Schritt geprüft und an den Solver weitergereicht.
    `ExplainAsync` läuft per `Task.Run` auf einer Kopie des Rätsels (wie `SolverExtensions`).
12. **Ungültige Rätsel** (Prüfung mit `PuzzleValidator` und der Registry der Engine) liefern `Invalid` mit den
    deutschen Fehlern und ohne Zustände (`HasSnapshots = false`, `GetSnapshot` wirft).
13. **Testhilfen:** `GraphDoku.Explainer.Tests` bindet den unabhängigen `SolutionVerifier` aus den Solver-Tests als
    verlinkte Datei ein (keine Kopie) und kopiert `puzzles/*.json` ins Ausgabeverzeichnis.
14. **Stufe 0 „Feststehenden Wert streichen“ vor den Singles (Abweichung von der Reihenfolge der Spezifikation).**
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
15. **Eigene Regel setzt einen gestrichenen Wert → Widerspruch statt Ausnahme.** Liefert eine Regel `Assign(v)` für
    einen Knoten, bei dem `v` kein Kandidat mehr ist, ist das vor der ersten Verzweigung ein Beweis der Unlösbarkeit:
    Status `Unsolvable`, `Contradiction` mit Constraint und Knoten der Regel und dem Text der Regel plus „Die 4 ist für
    Knoten 2 aber nicht mehr möglich.“ Nach einer Verzweigung bleibt es eine `InvalidOperationException` („Interner
    Fehler …“), weil dann eine Regel falsch geschlossen hat. Grund: Eine korrekte eigene Regel darf so einen Widerspruch
    zeigen; vorher warf `Explain` bzw. lieferte `ExplainAsync` eine fehlerhafte Task.
16. **Eigene Regeln: Solver bestätigt eine ohne Raten erreichte Belegung.** Der Status „eindeutig ohne Solver“ setzt
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
17. **Kürzere Begründungstexte.**
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
18. **Zwischenspeicher für `Propagate` (nur Katalogregeln).** Pro Constraint werden das letzte Ergebnis und die
    Kandidatenmengen seiner Knoten gemerkt; sind alle Mengen noch dieselben Objekte (unveränderliche `CandidateSet`),
    wird das Ergebnis wiederverwendet. Grund: Die teure, budgetierte Prüfung großer `Sum`-Gruppen lief sonst nach jedem
    Schritt erneut. Eigene Regeln werden nicht zwischengespeichert, weil ihr Ergebnis laut Vertrag nicht nur von den
    eigenen Knoten abhängen muss.
19. **Abbruch zwischen den Constraints.** Stufe 3 prüft das Token vor jedem `Propagate`-Aufruf; ein Abbruch dort führt
    direkt zu `Cancelled`, ohne den Solver für eine Verzweigung zu fragen.
