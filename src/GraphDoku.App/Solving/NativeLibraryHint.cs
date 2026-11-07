namespace GraphDoku.App.Solving;

/// <summary>
/// Verständliche Meldung, wenn die native OR-Tools-Bibliothek nicht geladen werden kann. Unter Windows fehlt dann fast
/// immer die Visual-C++-Laufzeit, die die Bibliotheken von OR-Tools voraussetzen und die das Paket nicht mitbringt.
/// </summary>
internal static class NativeLibraryHint
{
    public const string WindowsRuntimeUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    /// <summary>Meldung für einen Ladefehler der nativen Bibliothek, sonst <see langword="null"/>.</summary>
    public static string? Describe(Exception exception, bool isWindows)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DllNotFoundException or BadImageFormatException)
            {
                return isWindows
                    ? "Die Rechenbibliothek (OR-Tools) konnte nicht geladen werden. Unter Windows braucht sie die "
                      + $"„Microsoft Visual C++ Redistributable 2015–2022 (x64)“: {WindowsRuntimeUrl}"
                    : $"Die Rechenbibliothek (OR-Tools) konnte nicht geladen werden: {current.Message}";
            }
        }

        return null;
    }
}
