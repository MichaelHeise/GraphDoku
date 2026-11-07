#!/usr/bin/env bash
# Baut die Icon-Dateien der App neu (nur nötig, wenn sich GenerateIcon.cs ändert).
# Läuft auf macOS (iconutil für .icns); das Ergebnis ist eingecheckt, die CI braucht dieses Skript nicht.
#
# Aufruf aus dem Repo-Wurzelverzeichnis: tools/icon/build-icons.sh
set -euo pipefail

cd "$(dirname "$0")/../.."
assets=src/GraphDoku.App/Assets
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

dotnet run tools/icon/GenerateIcon.cs -- "$work/png"

# macOS: .icns aus einem iconset (Größen und Namen nach Apple-Vorgabe).
iconset="$work/GraphDoku.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  cp "$work/png/icon-$size.png" "$iconset/icon_${size}x${size}.png"
  cp "$work/png/icon-$((size * 2)).png" "$iconset/icon_${size}x${size}@2x.png"
done
iconutil -c icns "$iconset" -o "$assets/GraphDoku.icns"

# Windows: .ico mit eingebetteten PNG-Bildern (seit Windows Vista unterstützt).
python3 - "$work/png" "$assets/GraphDoku.ico" <<'PY'
import struct, sys, pathlib
src, dst = pathlib.Path(sys.argv[1]), sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
images = [(s, (src / f"icon-{s}.png").read_bytes()) for s in sizes]
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, blobs = b"", b""
for size, data in images:
    dim = 0 if size >= 256 else size  # 0 steht im ICO-Format für 256
    entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
    blobs += data
pathlib.Path(dst).write_bytes(header + entries + blobs)
PY

# Linux und Fenstersymbol: PNG in 256 und 512 px.
cp "$work/png/icon-256.png" "$assets/GraphDoku-256.png"
cp "$work/png/icon-512.png" "$assets/GraphDoku-512.png"

echo "Icons aktualisiert in $assets"
