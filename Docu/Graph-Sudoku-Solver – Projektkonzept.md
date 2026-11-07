# Graph-Sudoku-Solver – Projektkonzept

Eine Avalonia-App (.NET), in der man beliebige Sudoku-Varianten als Graph zeichnet, mit OR-Tools löst und den Lösungsweg Schritt für Schritt nachvollzieht. Anlass: die Rätsel der Langen Nacht der Mathematik (Tridoku, Hexadoku, Sonderregeln).

## Idee in einem Satz

Zellen sind **Knoten** mit Wertebereich, Beziehungen zwischen zwei Zellen sind **Kanten**, Regeln für Gruppen von Zellen hängen an **Knotenmengen** – der Solver kennt keine Sudoku-Typen, nur Variablen und Constraints.

## Entscheidungen

| Thema | Entscheidung |
| --- | --- |
| Plattform | .NET, Avalonia, MVVM |
| Solver | Google OR-Tools (CP-SAT); `ISolver`-Interface bleibt für einen eigenen Lern-Solver, OR-Tools dient dann als Baseline |
| Regeln | Fester, erweiterbarer Katalog im Code; Ausdruckssprache eventuell später |
| Darstellung | Kreise auf Gitter; Dreieck, Quadrat, Sechseck nur optional, nichts darüber hinaus |
| Lösungsweg | Eigener Erklär-Modus: Schritt für Schritt mit der Regel, die den Wert erzwungen hat |
| Speicherformat | JSON mit `formatVersion`, Lösungen werden nicht gespeichert |
| Umsetzung | Claude bzw. ein Agent implementiert nach der Agenten-Anleitung (zweiter Tab) |

## Regelkatalog (Version 1)

| Regel | Gilt für | Beispiel |
| --- | --- | --- |
| Alle verschieden | Knotenmenge | Zeilen, Spalten, Blöcke |
| Bereich abdecken | Knotenmenge | Menge enthält genau 1–9 |
| Summe | Knotenmenge | Killer-Käfige |
| Thermometer | geordnete Knotenmenge | Werte steigen entlang des Pfads |
| Ungleich | Kante | Nachbarn, Springerzug |
| Differenz | Kante | Abstand genau k oder mindestens k |
| Verhältnis | Kante | schwarze Punkte (Faktor 2) |
| Kleiner als | gerichtete Kante | Pfeil zwischen Zellen |

Dazu ein **Generator**, der aus einer Abstandsregel automatisch Kanten erzeugt.

## Funktionen

- **Editor:** Knoten auf Gitter setzen und verschieben, Kanten ziehen, Mengen aus Auswahl bilden, Givens eintragen, Gitter-Vorlagen, Undo/Redo, Speichern/Laden.
- **Lösen:** Lösung finden, Eindeutigkeit prüfen, Widersprüche melden.
- **Lösungsweg:** vor- und zurückspringen; gesetzte Zelle und verantwortliche Regel werden hervorgehoben; Raten wird als Verzweigung markiert.

## Meilensteine

1. Core-Datenmodell und JSON-Format
2. OR-Tools-Anbindung; klassisches Sudoku lösbar
3. Avalonia-Editor mit Knoten, Kanten, Mengen
4. Vollständiger Regelkatalog und Generator
5. Erklär-Modus und Schritt-Ansicht
6. Optional: Zellformen Dreieck, Quadrat, Sechseck
