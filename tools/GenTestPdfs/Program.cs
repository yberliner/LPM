using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace GenTestPdfs;

public static class Program
{
    // Page sizes in points (1pt = 1/72 in)
    static readonly (string Name, double W, double H)[] Pdf1Pages =
    {
        ("A4 portrait",     595, 842),
        ("Letter portrait", 612, 792),
        ("A4 landscape",    842, 595),
        ("A5 portrait",     420, 595),
    };

    static readonly (string Name, double W, double H)[] Pdf2Pages =
    {
        ("Legal portrait",   612, 1008),
        ("A4 portrait",      595, 842),
        ("Half-letter",      396, 612),
    };

    public static void Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "out");
        Directory.CreateDirectory(outDir);

        var f1 = Path.Combine(outDir, "test-mixed-pages-1.pdf");
        var f2 = Path.Combine(outDir, "test-mixed-pages-2.pdf");

        WritePdf(f1, "Test PDF #1 — Mixed Page Sizes", Pdf1Pages);
        WritePdf(f2, "Test PDF #2 — More Mixed Page Sizes", Pdf2Pages);

        Console.WriteLine($"Wrote: {f1}");
        Console.WriteLine($"Wrote: {f2}");
    }

    static void WritePdf(string path, string title, (string Name, double W, double H)[] pages)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = title;

        for (int i = 0; i < pages.Length; i++)
        {
            var (name, w, h) = pages[i];
            var page = doc.AddPage();
            page.Width  = XUnit.FromPoint(w);
            page.Height = XUnit.FromPoint(h);

            using var gfx = XGraphics.FromPdfPage(page);

            // Big visible text in the middle
            var titleFont = new XFont("Arial", 28, XFontStyle.Bold);
            var bodyFont  = new XFont("Arial", 16, XFontStyle.Regular);
            var smallFont = new XFont("Arial", 12, XFontStyle.Regular);

            var rectFull = new XRect(0, 0, w, h);

            // Background border so the page edges are obvious
            gfx.DrawRectangle(XPens.LightGray, new XRect(10, 10, w - 20, h - 20));

            // Centered title
            gfx.DrawString($"Page {i + 1}", titleFont, XBrushes.Black,
                rectFull, XStringFormats.TopCenter);

            // Move down a bit and write the size info
            var midRect = new XRect(0, h * 0.4, w, h * 0.2);
            gfx.DrawString(name, titleFont, XBrushes.DarkBlue,
                midRect, XStringFormats.Center);

            var infoRect = new XRect(0, h * 0.55, w, 30);
            gfx.DrawString($"{w:F0} × {h:F0} pt   ({w / 72.0:F2} × {h / 72.0:F2} in)",
                bodyFont, XBrushes.DarkRed,
                infoRect, XStringFormats.Center);

            // Foot info
            var footRect = new XRect(0, h - 40, w, 30);
            gfx.DrawString(title, smallFont, XBrushes.Gray,
                footRect, XStringFormats.Center);
        }

        doc.Save(path);
    }
}
