#!/usr/bin/env bash
# Gibt den Text eines Releases als Markdown aus (Downloads, Hinweise je Plattform, Handbuch, Prüfsummen).
#
# Aufruf: tools/release-notes.sh <Version>   (z. B. 1.1.0)
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Aufruf: tools/release-notes.sh <Version>" >&2
  exit 2
fi
v=$1

cat <<EOF
GraphDoku $v

## Downloads

- **Windows (x64):** \`GraphDoku-$v-windows-x64.zip\` entpacken, \`GraphDoku.App.exe\` starten. Braucht die Microsoft Visual C++ Redistributable 2015–2022 (x64), auf den meisten PCs schon vorhanden: https://aka.ms/vs/17/release/vc_redist.x64.exe
- **macOS (Apple Silicon):** \`GraphDoku-$v-macos-arm64.zip\`, **Intel-Mac:** \`GraphDoku-$v-macos-x64.zip\`. \`GraphDoku.app\` in „Programme“ ziehen. Beim ersten Start meldet macOS einen nicht verifizierten Entwickler: Systemeinstellungen → Datenschutz & Sicherheit → „Dennoch öffnen“.
- **Linux (x64):** \`GraphDoku-$v-linux-x64.tar.gz\` entpacken, \`GraphDoku.App\` starten.

**Benutzerhandbuch:** \`GraphDoku-$v-Benutzerhandbuch.pdf\` – Installation je Plattform, Konzept und Bedienung.

Ein installiertes .NET ist nicht nötig. Beispielrätsel liegen im Ordner \`Beispiele\`. Prüfsummen: \`SHA256SUMS-$v.txt\`.
EOF
