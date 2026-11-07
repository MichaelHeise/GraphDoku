#!/usr/bin/env bash
# Baut die Download-Pakete von GraphDoku: eigenständig (ohne installiertes .NET) für Windows, Linux und macOS.
#
# Aufruf aus dem Repo-Wurzelverzeichnis:
#   tools/package.sh <Version> [Ausgabeordner] [Ziel ...]
#   tools/package.sh 1.2.0                      # alle Ziele nach artifacts/release
#   tools/package.sh 1.2.0 out win-x64 osx-arm64
#
# Ziele: win-x64, linux-x64, osx-arm64, osx-x64 (Standard: alle).
#
# macOS-Pakete brauchen eine gültige Signatur, sonst meldet macOS die heruntergeladene App als „beschädigt“:
# - auf macOS mit `codesign` (Ad-hoc-Signatur, oder ein Zertifikat über MACOS_SIGN_IDENTITY),
# - auf Linux mit `rcodesign` im PATH (Ad-hoc-Signatur; so macht es der Release-Workflow).
# Fehlen beide, bricht das Skript für macOS-Ziele ab.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi

version=$1
out=${2:-artifacts/release}
shift $(( $# >= 2 ? 2 : 1 ))
targets=("$@")
[[ ${#targets[@]} -eq 0 ]] && targets=(win-x64 linux-x64 osx-arm64 osx-x64)

if ! [[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "Ungültige Version „$version“ (erwartet z. B. 1.2.0 oder 1.2.0-beta.1)." >&2
  exit 2
fi

cd "$(dirname "$0")/.."
root=$PWD
mkdir -p "$out"
out=$(cd "$out" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

publish() { # <rid> <Zielordner>
  # Einzeldatei: Die verwalteten DLLs stecken in der ausführbaren Datei, daneben liegen nur die nativen Bibliotheken
  # (Skia, HarfBuzz, OR-Tools, Avalonia). Unter macOS enthält Contents/MacOS damit nur Mach-O-Dateien, deren Signatur
  # eingebettet ist und das Zippen übersteht (Signaturen anderer Dateien lägen in erweiterten Attributen).
  dotnet publish src/GraphDoku.App/GraphDoku.App.csproj -c Release -r "$1" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:Version="$version" -p:DebugType=none -p:DebugSymbols=false -o "$2" --nologo -v quiet
  # Debug-Symbole der nativen NuGet-Bibliotheken (unter Windows rund 100 MB) gehören nicht in das Paket.
  find "$2" -name '*.pdb' -delete
}

add_docs() { # <Ordner>
  cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$1/"
  # Ordnername ohne Umlaut: ZIP-Programme unter Windows zeigen Umlaute in Dateinamen oft falsch an.
  mkdir -p "$1/Beispiele"
  cp "$root"/puzzles/*.json "$1/Beispiele/"
}

zip_dir() { # <Elternordner> <Name> <Zieldatei>  (Unix-Rechte bleiben erhalten)
  (cd "$1" && rm -f "$3" && zip -qry "$3" "$2")
}

sign_macos() { # <App-Bundle>
  if command -v codesign >/dev/null 2>&1; then
    codesign --force --deep --sign "${MACOS_SIGN_IDENTITY:--}" "$1"
    codesign --verify --deep --strict "$1"
  elif command -v rcodesign >/dev/null 2>&1; then
    rcodesign sign "$1" >/dev/null
  else
    echo "Weder codesign noch rcodesign gefunden – macOS-Pakete können nicht signiert werden." >&2
    exit 1
  fi
}

for rid in "${targets[@]}"; do
  echo "== $rid"
  case $rid in
    win-x64)
      name="GraphDoku-$version-windows-x64"
      publish "$rid" "$work/$name"
      add_docs "$work/$name"
      zip_dir "$work" "$name" "$out/$name.zip"
      ;;
    linux-x64)
      name="GraphDoku-$version-linux-x64"
      publish "$rid" "$work/$name"
      add_docs "$work/$name"
      cp "$root/src/GraphDoku.App/Assets/GraphDoku-256.png" "$work/$name/graphdoku.png"
      sed "s/@VERSION@/$version/" "$root/tools/packaging/graphdoku.desktop" > "$work/$name/graphdoku.desktop"
      tar -C "$work" -czf "$out/$name.tar.gz" "$name"
      ;;
    osx-arm64 | osx-x64)
      arch=${rid#osx-}
      name="GraphDoku-$version-macos-$arch"
      app="$work/$name/GraphDoku.app"
      mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
      publish "$rid" "$app/Contents/MacOS"
      cp "$root/src/GraphDoku.App/Assets/GraphDoku.icns" "$app/Contents/Resources/"
      # macOS verlangt reine Zahlen (1.2.0) in den Bundle-Versionen; ein Vorabzusatz (-beta.1) entfällt dort.
      sed "s/@VERSION@/${version%%-*}/g" "$root/tools/packaging/Info.plist" > "$app/Contents/Info.plist"
      sign_macos "$app"
      add_docs "$work/$name"
      zip_dir "$work" "$name" "$out/$name.zip"
      ;;
    *)
      echo "Unbekanntes Ziel „$rid“ (erlaubt: win-x64, linux-x64, osx-arm64, osx-x64)." >&2
      exit 2
      ;;
  esac
done

sha256() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }
(
  cd "$out"
  files=()
  for f in GraphDoku-"$version"-*.zip GraphDoku-"$version"-*.tar.gz; do [[ -f $f ]] && files+=("$f"); done
  sha256 "${files[@]}" > "SHA256SUMS-$version.txt"
)
echo "Pakete in $out:"
ls -lh "$out" | tail -n +2
