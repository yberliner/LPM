using PdfSharpCore.Fonts;
using System.Reflection;

namespace LPM.Services;

/// <summary>
/// Serves DejaVu Sans (Regular + Bold) from embedded resources so PdfSharpCore
/// renders text correctly on Linux without relying on system fonts.
/// </summary>
public class EmbeddedFontResolver : IFontResolver
{
    public static readonly EmbeddedFontResolver Instance = new();

    private const string FamilyName = "DejaVu Sans";
    private const string FaceRegular = "DejaVuSans#Regular";
    private const string FaceBold    = "DejaVuSans#Bold";

    private static readonly byte[] _regular = LoadResource("LPM.Fonts.DejaVuSans.ttf");
    private static readonly byte[] _bold    = LoadResource("LPM.Fonts.DejaVuSans-Bold.ttf");

    private static byte[] LoadResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded font not found: {name}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public string DefaultFontName => FamilyName;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // Accept any family name — always serve DejaVu Sans so nothing falls through to squares
        var face = bold ? FaceBold : FaceRegular;
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        FaceBold    => _bold,
        _           => _regular,
    };
}
