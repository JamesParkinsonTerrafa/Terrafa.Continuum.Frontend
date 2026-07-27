using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// Type is embedded rather than resolved from the system. The browser head has no system
/// fonts to resolve against at all, and the desktop head previously asked for Verdana and
/// silently accepted whatever fontconfig substituted on Linux.
/// </summary>
public static class AppFonts
{
    internal const string Root = "avares://Terrafa.Continuum.Frontend.Ui/Assets/Fonts";

    /// <summary>Bergoom carries every Latin glyph in the app. Regular and Bold only.</summary>
    public static readonly FontFamily Primary = new($"{Root}#Bergoom");

    /// <summary>
    /// Bergoom has no glyph for 16 of the symbols these views draw — the close cross, the
    /// drag handle, the super/subscripts, and the script capitals in the transfer-function
    /// notation. The desktop OS used to fill those in from its own font stack; a browser
    /// has nothing behind the font, so the coverage has to ship with the app. DejaVu Sans
    /// answers 14 of them and Noto Sans Math the two Mathematical Alphanumeric ones.
    /// Both are subset to the symbol blocks, which is why they cost KB rather than MB.
    ///
    /// tools/check-glyphs.py fails the build if a newly used glyph escapes all three.
    /// </summary>
    public static FontManagerOptions Options => new()
    {
        DefaultFamilyName = $"{Root}#Bergoom",
        FontFallbacks =
        [
            new FontFallback { FontFamily = new FontFamily($"{Root}#DejaVu Sans") },
            new FontFallback { FontFamily = new FontFamily($"{Root}#Noto Sans Math") },
        ],
    };
}
