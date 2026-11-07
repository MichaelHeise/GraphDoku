using GraphDoku.App.Solving;
using GraphDoku.App.ViewModels;

namespace GraphDoku.App.Tests;

/// <summary>Meldung bei fehlender nativer OR-Tools-Bibliothek (unter Windows meist die Visual-C++-Laufzeit).</summary>
public sealed class NativeLibraryHintTests
{
    private static readonly Exception TypeInitFailure =
        new TypeInitializationException("Google.OrTools.Sat.operations_research_satPINVOKE", new DllNotFoundException("google-ortools-native"));

    [Fact]
    public void Windows_hint_names_the_visual_cpp_runtime_with_download_link()
    {
        var text = NativeLibraryHint.Describe(TypeInitFailure, isWindows: true);

        text.Should().Contain("Visual C++ Redistributable").And.Contain(NativeLibraryHint.WindowsRuntimeUrl);
    }

    [Fact]
    public void Other_systems_get_the_loader_message()
    {
        NativeLibraryHint.Describe(TypeInitFailure, isWindows: false).Should().Contain("google-ortools-native").And.NotContain("Visual C++");
    }

    [Fact]
    public void Other_exceptions_are_not_described() =>
        NativeLibraryHint.Describe(new InvalidOperationException("x"), isWindows: true).Should().BeNull();
}
