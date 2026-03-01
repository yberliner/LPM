using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LPM.Services;   // <-- CHANGE if PcInfo is elsewhere

public class PdfService
{
    public byte[] GenerateWeeklyTablePdf(
        string userDisplayName,
        DateOnly weekStart,
        List<PcInfo> pcs,
        Dictionary<(int pcId, int dayIdx), int> grid)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pcsToPrint = pcs
            .Where(pc => Enumerable.Range(0, 7)
            .Sum(d => grid.GetValueOrDefault((pc.PcId, d))) > 0)
            .ToList();

        var weekEnd = weekStart.AddDays(6);

        int grandTotal = pcsToPrint.Sum(pc =>
            Enumerable.Range(0, 7)
            .Sum(d => grid.GetValueOrDefault((pc.PcId, d))));

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("");
                        r.RelativeItem().AlignCenter()
                            .Text(userDisplayName)
                            .SemiBold().FontSize(16);
                        r.RelativeItem().AlignRight()
                            .Text($"Week: {weekStart:ddd dd/MM} – {weekEnd:ddd dd/MM}")
                            .FontSize(11);
                    });

                    col.Item().PaddingTop(10);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(90);
                            for (int i = 0; i < Math.Max(1, pcsToPrint.Count); i++)
                                columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            // Day spans 2 header rows
                            header.Cell().RowSpan(2)
                                .Element(HeaderCell)
                                .AlignMiddle()
                                .Text("Day")
                                .SemiBold();

                            if (pcsToPrint.Count == 0)
                            {
                                header.Cell().Element(RoleHeaderCell).AlignCenter().Text("Role");
                                header.Cell().Element(NameHeaderCell).AlignCenter().Text("No data");
                                return;
                            }

                            // ── Row 1: Role (subtle styling + slight indent look) ──
                            foreach (var pc in pcsToPrint)
                            {
                                header.Cell()
                                    .Element(RoleHeaderCell)
                                    .AlignCenter()
                                    .PaddingVertical(2)
                                    .Text(RoleLabel(pc.Role))
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken1);
                            }

                            // ── Row 2: PC Names (bold & stronger) ──
                            foreach (var pc in pcsToPrint)
                            {
                                header.Cell()
                                    .Element(NameHeaderCell)
                                    .AlignCenter()
                                    .Text(pc.FullName)
                                    .SemiBold()
                                    .FontSize(11);
                            }
                        });

                        for (int d = 0; d < 7; d++)
                        {
                            var date = weekStart.AddDays(d);

                            table.Cell().Border(1).Padding(4)
                                .Text(date.ToString("ddd dd/MM"));

                            if (pcsToPrint.Count == 0)
                            {
                                table.Cell().Border(1).Padding(4).Text("");
                            }
                            else
                            {
                                foreach (var pc in pcsToPrint)
                                {
                                    int secs = grid.GetValueOrDefault((pc.PcId, d));
                                    table.Cell().Border(1).Padding(4)
                                        .AlignCenter()
                                        .Text(FmtOrBlank(secs));
                                }
                            }
                        }

                        table.Cell().Border(1).Padding(4)
                            .Text("Σ Week");

                        if (pcsToPrint.Count == 0)
                        {
                            table.Cell().Border(1).Padding(4).Text("");
                        }
                        else
                        {
                            foreach (var pc in pcsToPrint)
                            {
                                int total = Enumerable.Range(0, 7)
                                    .Sum(d => grid.GetValueOrDefault((pc.PcId, d)));

                                table.Cell().Border(1).Padding(4)
                                    .AlignCenter()
                                    .Text(FmtOrBlank(total));
                            }
                        }
                    });

                    col.Item().PaddingTop(12);

                    col.Item().Background(Colors.Grey.Lighten4)
                        .Padding(10)
                        .Row(r =>
                        {
                            r.RelativeItem()
                                .Text("Grand Total")
                                .SemiBold().FontSize(16);

                            r.ConstantItem(200)
                                .AlignRight()
                                .Text(FmtOrBlank(grandTotal))
                                .SemiBold().FontSize(22);
                        });
                });
            });
        }).GeneratePdf();
    }

    static IContainer HeaderCell(IContainer c) =>
    c.Border(1)
     .BorderColor(Colors.Grey.Lighten2)
     .Background(Colors.Grey.Lighten3)
     .PaddingVertical(4)
     .PaddingHorizontal(4);

    static IContainer NameHeaderCell(IContainer c) =>
    c.Border(1)
     .BorderColor(Colors.Grey.Lighten2)
     .Background(Colors.Grey.Lighten3)
     .PaddingVertical(5)
     .PaddingHorizontal(4);

    static IContainer RoleHeaderCell(IContainer c) =>
        c.Border(1)
         .BorderColor(Colors.Grey.Lighten2)
         .Background(Colors.Grey.Lighten4)
         .PaddingHorizontal(4)
         .PaddingLeft(6);   // small indent effect

    static string RoleLabel(string? role)
    {
        return role == "CS"
            ? "CS"
            : role == "Miscellaneous"
                ? "Other"
                : "Auditor";
    }

    static string FmtOrBlank(int sec)
    {
        if (sec <= 0) return "";
        var ts = TimeSpan.FromSeconds(sec);
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}";
    }
}