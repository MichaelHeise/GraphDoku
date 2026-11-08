#!/usr/bin/env bash
# Baut das Benutzerhandbuch als PDF aus docs/benutzerhandbuch/benutzerhandbuch.md.
#
# Aufruf aus dem Repo-Wurzelverzeichnis:
#   tools/userguide.sh <Version> [Ausgabeordner]
#   tools/userguide.sh 1.1.0                    # → artifacts/release/GraphDoku-1.1.0-Benutzerhandbuch.pdf
#
# Braucht pandoc und weasyprint (Ubuntu/Debian: apt-get install pandoc weasyprint fonts-dejavu-core).
set -euo pipefail
# Metadaten mit Umlauten und Gedankenstrich: Ohne UTF-8-Locale liest pandoc die Argumente falsch.
export LC_ALL=C.UTF-8 LANG=C.UTF-8

if [[ $# -lt 1 ]]; then
  sed -n '2,/^[^#]/{/^#/p;}' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi

version=$1
out=${2:-artifacts/release}

for tool in pandoc weasyprint; do
  command -v "$tool" >/dev/null 2>&1 || { echo "„$tool“ fehlt (apt-get install pandoc weasyprint fonts-dejavu-core)." >&2; exit 1; }
done

cd "$(dirname "$0")/.."
mkdir -p "$out"
target="$(cd "$out" && pwd)/GraphDoku-$version-Benutzerhandbuch.pdf"
# WeasyPrint löst Bildpfade relativ zum Arbeitsverzeichnis auf; pandoc legt dort kurz eine HTML-Zwischendatei an.
cd docs/benutzerhandbuch

# Datum im Format „8. November 2026“, unabhängig von der Sprache des Systems.
months=(Januar Februar März April Mai Juni Juli August September Oktober November Dezember)
date="$(date +%-d). ${months[$(( $(date +%-m) - 1 ))]} $(date +%Y)"

pandoc benutzerhandbuch.md \
  --from markdown+smart \
  --to html5 \
  --standalone \
  --toc --toc-depth=2 \
  --number-sections \
  --css handbuch.css \
  --metadata document-css=false \
  --metadata lang=de \
  --metadata title="GraphDoku" \
  --metadata subtitle="Benutzerhandbuch" \
  --metadata date="Version $version · $date" \
  --metadata toc-title="Inhalt" \
  --pdf-engine=weasyprint \
  --pdf-engine-opt=--presentational-hints \
  -o "$target"

echo "Benutzerhandbuch: $target ($(du -h "$target" | cut -f1))"
