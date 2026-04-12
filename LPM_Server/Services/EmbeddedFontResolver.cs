using PdfSharpCore.Fonts;
using System.Reflection;

namespace LPM.Services;

/// <summary>
/// Serves DejaVu Sans (Regular + Bold) and Noto Sans Hebrew (Regular + Bold)
/// from embedded resources so PdfSharpCore renders text correctly on Linux
/// without relying on system fonts.
/// </summary>
public class EmbeddedFontResolver : IFontResolver
{
    public static readonly EmbeddedFontResolver Instance = new();

    private const string DejaVuFamily = "DejaVu Sans";
    private const string HebrewFamily = "Noto Sans Hebrew";

    private const string DejaVuRegular   = "DejaVuSans#Regular";
    private const string DejaVuBold      = "DejaVuSans#Bold";
    private const string HebrewRegular   = "NotoSansHebrew#Regular";
    private const string HebrewBold      = "NotoSansHebrew#Bold";

    private static readonly byte[] _dejaVuRegular = LoadResource("LPM.Fonts.DejaVuSans.ttf");
    private static readonly byte[] _dejaVuBold    = LoadResource("LPM.Fonts.DejaVuSans-Bold.ttf");
    private static readonly byte[] _hebrewRegular = LoadResource("LPM.Fonts.NotoSansHebrew-Regular.ttf");
    private static readonly byte[] _hebrewBold    = LoadResource("LPM.Fonts.NotoSansHebrew-Bold.ttf");

    private static byte[] LoadResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded font not found: {name}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public string DefaultFontName => DejaVuFamily;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (string.Equals(familyName, HebrewFamily, StringComparison.OrdinalIgnoreCase))
        {
            var face = bold ? HebrewBold : HebrewRegular;
            return new FontResolverInfo(face);
        }

        // Default: serve DejaVu Sans for any other family name
        var dejaFace = bold ? DejaVuBold : DejaVuRegular;
        return new FontResolverInfo(dejaFace);
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        DejaVuBold    => _dejaVuBold,
        HebrewRegular => _hebrewRegular,
        HebrewBold    => _hebrewBold,
        _             => _dejaVuRegular,
    };
}
