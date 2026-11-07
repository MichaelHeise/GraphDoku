# Hinweise zu Komponenten Dritter

GraphDoku selbst ist proprietär (siehe `LICENSE`). Die folgenden NuGet-Pakete werden verwendet und stehen unter ihren
eigenen Lizenzen. Die Lizenztexte liegen den jeweiligen Paketen bei bzw. sind über die angegebenen Projektseiten
abrufbar.

## Direkt referenzierte Pakete

| Paket | Version | Lizenz | Verwendung | Projekt |
| --- | --- | --- | --- | --- |
| Google.OrTools (inkl. `Google.OrTools.runtime.*`) | 9.15.6755 | Apache-2.0 | Solver (CP-SAT) | https://github.com/google/or-tools |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 12.1.5 | MIT | Benutzeroberfläche | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter | 12.1.5 | MIT (Paket); Schrift Inter: SIL Open Font License 1.1 | Standardschrift | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM | https://github.com/CommunityToolkit/dotnet |

### Native Bibliotheken in `Google.OrTools.runtime.*`

Die Runtime-Pakete von OR-Tools (linux-x64, linux-arm64, osx-arm64, osx-x64, win-x64) bringen neben OR-Tools selbst
weitere native Bibliotheken mit, die mit jeder Ausgabe unter `runtimes/<rid>/native` ausgeliefert werden. Die nuspec
nennt pauschal Apache-2.0; die Komponenten stehen jedoch unter ihren eigenen Lizenzen:

| Komponente | Dateien (Linux-Namen) | Lizenz | Quellcode |
| --- | --- | --- | --- |
| OR-Tools (nativer Kern und C#-Brücke) | `libortools`, `google-ortools-native` | Apache-2.0 | https://github.com/google/or-tools |
| COIN-OR CBC, CLP, CGL, OSI, CoinUtils | `libCbc`, `libCbcSolver`, `libClp`, `libClpSolver`, `libCgl`, `libOsi`, `libOsiCbc`, `libOsiClp`, `libCoinUtils` | **EPL-2.0** | https://github.com/coin-or |
| SCIP 10 | `libscip` | Apache-2.0 | https://github.com/scipopt/scip |
| HiGHS | `libhighs` | MIT | https://github.com/ERGO-Code/HiGHS |
| Abseil | `libabsl_*` (Windows: `abseil_dll`) | Apache-2.0 | https://github.com/abseil/abseil-cpp |
| Protocol Buffers (nativ) | `libprotobuf`, `libutf8_validity` | BSD-3-Clause | https://github.com/protocolbuffers/protobuf |
| RE2 | `libre2` | BSD-3-Clause | https://github.com/google/re2 |
| zlib | `libz` (Windows: `zlib1`) | zlib-Lizenz | https://zlib.net |
| bzip2 | `libbz2` (Windows: `bz2`) | bzip2-Lizenz (BSD-artig) | https://sourceware.org/bzip2/ |

Das win-x64-Paket enthält keine COIN-OR-Bibliotheken. EPL-2.0 verlangt bei der Weitergabe von Binärdateien einen
Hinweis, wo der Quellcode erhältlich ist; das leistet die Spalte „Quellcode“. GraphDoku nutzt nur CP-SAT, die übrigen
Solver werden lediglich mitgeliefert. Die Zuordnung der Lizenzen beruht auf den Angaben der jeweiligen Projekte
(siehe `docs/decisions.md`, Nr. 46).

## Nur für Tests (werden nicht ausgeliefert)

| Paket | Version | Lizenz | Projekt |
| --- | --- | --- | --- |
| xunit | 2.9.3 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT | https://github.com/microsoft/vstest |
| FluentAssertions | 7.2.2 | Apache-2.0 | https://github.com/fluentassertions/fluentassertions |
| Avalonia.Headless, Avalonia.Skia (Rendering-Rauchtest) | 12.1.5 | MIT | https://github.com/AvaloniaUI/Avalonia |

FluentAssertions wird bewusst in Version 7 verwendet: Ab Version 8 steht das Paket unter einer kommerziellen Lizenz
(Xceed), die für dieses proprietäre Projekt nicht vorgesehen ist.

## Wesentliche transitive Abhängigkeiten

| Paket | Lizenz | Herkunft |
| --- | --- | --- |
| Google.Protobuf | BSD-3-Clause | OR-Tools |
| SkiaSharp, SkiaSharp.NativeAssets.* (enthält Skia, BSD-3-Clause) | MIT | Avalonia |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.* (enthält HarfBuzz, „Old MIT“) | MIT | Avalonia |
| Avalonia.Angle.Windows.Natives (ANGLE) | BSD-3-Clause | Avalonia (nur Windows) |
| Avalonia.Native, Avalonia.X11, Avalonia.Win32, Avalonia.FreeDesktop, Avalonia.FreeDesktop.AtSpi, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Remote.Protocol | MIT | Avalonia |
| Avalonia.BuildServices (Build-Telemetrie, im Repo deaktiviert, siehe `docs/decisions.md`) | MIT | Avalonia |
| Tmds.DBus.Protocol | MIT | Avalonia (Linux) |
| MicroCom.Runtime | MIT | Avalonia |
| System.* / Microsoft.* (z. B. System.Drawing.Common, Microsoft.TestPlatform.*) | MIT | .NET / Test-SDK |
| xunit.abstractions, xunit.assert, xunit.core, xunit.extensibility.*, xunit.analyzers | Apache-2.0 | xUnit |

Die vollständige Liste liefert `dotnet list GraphDoku.sln package --include-transitive`.
