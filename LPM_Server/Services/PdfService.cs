using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LPM.Services;   // PcInfo lives here in your project
using SkiaSharp;

public class PdfService
{
    public byte[] GenerateWeeklyTablePdf(
        string userDisplayName,
        DateOnly weekStart,
        List<PcInfo> pcs,
        Dictionary<(int pcId, int dayIdx), int> grid,
        Dictionary<int, string> pcCsNames)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Remove columns that are all-zero for the week
        var pcsToPrint = pcs
            .Where(pc => Enumerable.Range(0, 7)
                .Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d))) > 0)
            .ToList();

        var weekEnd = weekStart.AddDays(6);

        // Solo CS columns count as regular time, not CS time
        int nonCsTotal = pcsToPrint
            .Where(pc => pc.WorkCapacity != "CS" || pc.IsSolo)
            .Sum(pc => Enumerable.Range(0, 7).Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d))));
        int csTotal = pcsToPrint
            .Where(pc => pc.WorkCapacity == "CS" && !pc.IsSolo)
            .Sum(pc => Enumerable.Range(0, 7).Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d))));

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    // ── Top row ──
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(""); // left spacer
                        r.RelativeItem().AlignCenter()
                            .Text(userDisplayName)
                            .SemiBold().FontSize(16);
                        r.RelativeItem().AlignRight()
                            .Text($"Week: {weekStart:ddd dd/MM} – {weekEnd:ddd dd/MM}")
                            .FontSize(11);
                    });

                    col.Item().PaddingTop(10);

                    // ── Table ──
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(90); // Day
                            for (int i = 0; i < Math.Max(1, pcsToPrint.Count); i++)
                                columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            // Row 1: CS row
                            header.Cell()
                                .Element(CsHeaderCell)
                                .AlignLeft()
                                .Text("CS:")
                                .SemiBold()
                                .FontSize(9);

                            if (pcsToPrint.Count == 0)
                            {
                                header.Cell().Element(CsHeaderCell).Text("");
                                // Day spans rows 2-3
                                header.Cell().RowSpan(2).Element(HeaderCell).AlignMiddle().Text("Day").SemiBold();
                                header.Cell().Element(RoleHeaderCell).AlignCenter().Text("Role");
                                header.Cell().Element(NameHeaderCell).AlignCenter().Text("No data");
                                return;
                            }

                            foreach (var pc in pcsToPrint)
                            {
                                string csDisplay;
                                if (pc.IsSolo)
                                    csDisplay = "Solo";
                                else if (pc.WorkCapacity == "CS")
                                    csDisplay = "CS";
                                else
                                {
                                    var csFullName = pcCsNames.TryGetValue(pc.PcId, out var cn) ? cn : "";
                                    csDisplay = csFullName.Length > 0 ? csFullName.Split(' ')[0]
                                              : pc.WorkCapacity == "Auditor" ? "NA" : "";
                                }

                                header.Cell()
                                    .Element(CsHeaderCell)
                                    .AlignCenter()
                                    .Text(t =>
                                    {
                                        t.Span(csDisplay).FontSize(9).FontColor(Colors.Grey.Darken2);
                                    });
                            }

                            // Day spans rows 2-3
                            header.Cell().RowSpan(2)
                                .Element(HeaderCell)
                                .AlignMiddle()
                                .Text("Day")
                                .SemiBold();

                            // Row 2: Role
                            foreach (var pc in pcsToPrint)
                            {
                                header.Cell()
                                    .Element(RoleHeaderCell)
                                    .AlignCenter()
                                    .PaddingVertical(2)
                                    .Text(RoleLabel(pc))
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken1);
                            }

                            // Row 3: PC Names (bold)
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

                        // Day rows
                        for (int d = 0; d < 7; d++)
                        {
                            var date = weekStart.AddDays(d);

                            table.Cell().Element(CellStyle)
                                .Text(date.ToString("ddd dd/MM"));

                            if (pcsToPrint.Count == 0)
                            {
                                table.Cell().Element(CellStyle).Text("");
                            }
                            else
                            {
                                foreach (var pc in pcsToPrint)
                                {
                                    int secs = grid.GetValueOrDefault((DashboardService.GKey(pc), d));
                                    // Regular CS columns: grayed small; everything else: normal
                                    if (pc.WorkCapacity == "CS" && !pc.IsSolo)
                                        table.Cell().Element(CellStyle).AlignCenter()
                                            .Text(t => t.Span(DashboardService.FmtOrBlank(secs)).FontSize(8).FontColor("#aaaaaa"));
                                    else
                                        table.Cell().Element(CellStyle).AlignCenter()
                                            .Text(DashboardService.FmtOrBlank(secs));
                                }
                            }
                        }

                        // ── Σ Week row (BOLD) ──
                        table.Cell().Element(WeekTotalCell)
                            .Text("Σ Week")
                            .SemiBold();

                        if (pcsToPrint.Count == 0)
                        {
                            table.Cell().Element(WeekTotalCell)
                                .AlignCenter()
                                .Text("")
                                .SemiBold();
                        }
                        else
                        {
                            foreach (var pc in pcsToPrint)
                            {
                                int total = Enumerable.Range(0, 7)
                                    .Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d)));

                                if (pc.WorkCapacity == "CS" && !pc.IsSolo)
                                    table.Cell().Element(WeekTotalCell).AlignCenter()
                                        .Text(t => t.Span(DashboardService.FmtOrBlank(total)).FontSize(8).FontColor("#aaaaaa"));
                                else
                                    table.Cell().Element(WeekTotalCell).AlignCenter()
                                        .Text(DashboardService.FmtOrBlank(total)).SemiBold();
                            }
                        }
                    });

                    // ── Grand Total ──
                    col.Item().PaddingTop(12);

                    col.Item().Background(Colors.Grey.Lighten4)
                        .Padding(10)
                        .Row(r =>
                        {
                            r.RelativeItem()
                                .Text("Grand Total")
                                .SemiBold().FontSize(16);

                            r.ConstantItem(200).AlignRight().Column(c =>
                            {
                                c.Item().AlignRight()
                                    .Text(DashboardService.FmtOrBlank(nonCsTotal))
                                    .SemiBold().FontSize(22);
                                if (csTotal > 0)
                                    c.Item().AlignRight()
                                        .Text($"CS time: ({DashboardService.FmtOrBlank(csTotal)})")
                                        .FontSize(8).FontColor("#aaaaaa");
                            });
                        });
                });
            });
        }).GeneratePdf();
    }

    public byte[] GenerateAcademyWeekPdf(DateOnly weekStart, List<(string FullName, int VisitCount)> students)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var weekEnd  = weekStart.AddDays(6);
        int count    = students.Count;
        int numCols  = count <= 25 ? 2 : count <= 55 ? 3 : count <= 100 ? 4 : 6;
        int rows     = (int)Math.Ceiling((double)count / numCols);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(12, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().ScaleToFit().Column(col =>
                {
                    // ── Header ──
                    col.Item().Row(r =>
                    {
                        r.RelativeItem()
                            .Text("Academy")
                            .SemiBold().FontSize(20).FontColor("#1b5e20");

                        r.RelativeItem().AlignCenter()
                            .Text($"Week: {weekStart:dd/MM} – {weekEnd:dd/MM}")
                            .FontSize(13);

                        r.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight()
                                .Text(t =>
                                {
                                    t.Span("Total students: ").FontSize(14);
                                    t.Span(count.ToString()).FontSize(17).Bold().FontColor("#1b5e20");
                                });
                        });
                    });

                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#2e7d32");
                    col.Item().PaddingTop(8);

                    if (count == 0)
                    {
                        col.Item().AlignCenter()
                            .Text("No visits recorded for this week.")
                            .FontColor(Colors.Grey.Medium);
                        return;
                    }

                    // ── Student columns ──
                    col.Item().Row(row =>
                    {
                        for (int c = 0; c < numCols; c++)
                        {
                            var slice = students.Skip(c * rows).Take(rows).ToList();
                            int colIdx = c;

                            row.RelativeItem().Column(innerCol =>
                            {
                                // column header
                                innerCol.Item()
                                    .Background("#e8f5e9")
                                    .PaddingVertical(3).PaddingHorizontal(5)
                                    .Row(hr =>
                                    {
                                        hr.RelativeItem()
                                            .Text("Student")
                                            .FontSize(8).FontColor("#555").SemiBold();
                                        hr.ConstantItem(22).AlignRight()
                                            .Text("Visits")
                                            .FontSize(8).FontColor("#555").SemiBold();
                                    });

                                int rank = colIdx * rows + 1;
                                foreach (var (name, visits) in slice)
                                {
                                    int r2 = rank++;
                                    innerCol.Item()
                                        .BorderBottom(0.5f)
                                        .BorderColor(Colors.Grey.Lighten3)
                                        .Background(r2 % 2 == 0 ? "#f9fbe7" : Colors.White)
                                        .PaddingVertical(2).PaddingHorizontal(5)
                                        .Row(rr =>
                                        {
                                            rr.ConstantItem(18)
                                                .Text($"{r2}.")
                                                .FontSize(8).FontColor(Colors.Grey.Medium);
                                            rr.RelativeItem()
                                                .Text(name)
                                                .FontSize(9);
                                            rr.ConstantItem(22).AlignRight()
                                                .Text(visits.ToString())
                                                .FontSize(9).FontColor("#2e7d32").SemiBold();
                                        });
                                }
                            });

                            // column divider (except after last)
                            if (c < numCols - 1)
                                row.ConstantItem(8).Column(_ => { });
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    // ── Styles ──

    static IContainer CellStyle(IContainer c) =>
        c.Border(1)
         .BorderColor(Colors.Grey.Lighten2)
         .Padding(4);

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

    static IContainer CsHeaderCell(IContainer c) =>
        c.Border(1)
         .BorderColor(Colors.Grey.Lighten2)
         .Background(Colors.Blue.Lighten5)
         .PaddingVertical(3)
         .PaddingHorizontal(4);

    static IContainer RoleHeaderCell(IContainer c) =>
        c.Border(1)
         .BorderColor(Colors.Grey.Lighten2)
         .Background(Colors.Grey.Lighten4)
         .PaddingHorizontal(4)
         .PaddingLeft(6);   // small indent effect

    static IContainer WeekTotalCell(IContainer c) =>
        c.Border(1)
         .BorderColor(Colors.Grey.Lighten2)
         .Background(Colors.Grey.Lighten2)  // slightly darker summary row
         .PaddingVertical(5)
         .PaddingHorizontal(4);

    // ── Helpers ──

    static string RoleLabel(PcInfo pc)
    {
        if (pc.IsSolo)            return "Solo";
        if (pc.WorkCapacity == "CS")            return "CS";
        if (pc.WorkCapacity == "Miscellaneous") return "Other";
        if (pc.WorkCapacity == "SoloAuditor")   return "Solo";
        return "Auditor";
    }

    // ════════════════════════════════════════════════════════════════════════
    // Statistics PDF
    // ════════════════════════════════════════════════════════════════════════

    public byte[] GenerateStatisticsPdf(
        DateOnly weekStart,
        List<StaffStatRow> weekStaff,
        List<DayStat> dayStats,
        List<WeekStatSummary> weekHistory,
        WeekStatSummary? currentWeekSummary)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var weekEnd = weekStart.AddDays(6);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(10, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Content().ScaleToFit().Column(col =>
                {
                    // ── Header ──────────────────────────────────────────────
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Statistics Report").SemiBold().FontSize(16).FontColor("#1a1a2e");
                            c.Item().Text($"Week: {weekStart:ddd dd/MM/yyyy} – {weekEnd:ddd dd/MM/yyyy}")
                                .FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    col.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor("#667eea");
                    col.Item().PaddingTop(8);

                    // ── 4-box summary ────────────────────────────────────────
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Border(1).BorderColor("#e0e4f0").Background("#f0f4ff").CornerRadius(4).Padding(7).Column(c =>
                        {
                            c.Item().AlignCenter().Text("Aud+CS Sold").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                            c.Item().PaddingTop(3).AlignCenter().Text(FmtSec(currentWeekSummary?.TotalAuditCsSec ?? 0)).FontSize(16).Bold().FontColor("#6366f1");
                        });
                        r.ConstantItem(6);
                        r.RelativeItem().Border(1).BorderColor("#dbeafe").Background("#eff6ff").CornerRadius(4).Padding(7).Column(c =>
                        {
                            c.Item().AlignCenter().Text("PCs").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                            c.Item().PaddingTop(3).AlignCenter().Text((currentWeekSummary?.PcCount ?? 0).ToString()).FontSize(16).Bold().FontColor("#2563eb");
                        });
                        r.ConstantItem(6);
                        r.RelativeItem().Border(1).BorderColor("#d1fae5").Background("#f0fdf4").CornerRadius(4).Padding(7).Column(c =>
                        {
                            c.Item().AlignCenter().Text("Academy").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                            c.Item().PaddingTop(3).AlignCenter().Text((currentWeekSummary?.AcademyCount ?? 0).ToString()).FontSize(16).Bold().FontColor("#16a34a");
                        });
                        r.ConstantItem(6);
                        r.RelativeItem().Border(1).BorderColor("#fde8d8").Background("#fff7ed").CornerRadius(4).Padding(7).Column(c =>
                        {
                            c.Item().AlignCenter().Text("Body in Shop").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                            c.Item().PaddingTop(3).AlignCenter().Text((currentWeekSummary?.BodyInShop ?? 0).ToString()).FontSize(16).Bold().FontColor("#ea580c");
                        });
                    });

                    col.Item().PaddingTop(10);

                    // ── Two-column: Day-by-day | Leaderboard ─────────────────
                    col.Item().Row(mainRow =>
                    {
                        mainRow.RelativeItem(5).Column(left =>
                        {
                            left.Item().Text("Day-by-Day").SemiBold().FontSize(9).FontColor("#1a1a2e");
                            left.Item().PaddingTop(3);
                            left.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2.2f);
                                    cols.RelativeColumn(1.8f);
                                    cols.RelativeColumn(1f);
                                    cols.RelativeColumn(1f);
                                    cols.RelativeColumn(1f);
                                });
                                table.Header(h =>
                                {
                                    foreach (var (lbl, align) in new[] { ("Day","L"), ("Aud+CS","C"), ("PCs","C"), ("Acad","C"), ("BITS","C") })
                                    {
                                        var cell = h.Cell().Background("#1a1a2e").Padding(3);
                                        if (align == "C") cell.AlignCenter().Text(lbl).FontSize(7).SemiBold().FontColor(Colors.White);
                                        else              cell.Text(lbl).FontSize(7).SemiBold().FontColor(Colors.White);
                                    }
                                });
                                for (int d = 0; d < 7; d++)
                                {
                                    var ds    = dayStats[d];
                                    var total = ds.Staff.Sum(s => s.AuditSec + s.SoloCsSec);
                                    var bg    = d % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;
                                    bool tod  = ds.Date == DateOnly.FromDateTime(DateTime.Today);
                                    table.Cell().Background(bg).Padding(3).Text(t =>
                                    {
                                        t.Span(ds.Date.ToString("ddd dd/MM")).FontSize(8);
                                        if (tod) t.Span(" ●").FontColor("#6366f1").FontSize(6);
                                    });
                                    table.Cell().Background(bg).AlignCenter().Padding(3).Text(total > 0 ? FmtSec(total) : "–").FontSize(8).FontColor(total > 0 ? "#6366f1" : Colors.Grey.Medium);
                                    table.Cell().Background(bg).AlignCenter().Padding(3).Text(ds.PcCount > 0 ? ds.PcCount.ToString() : "–").FontSize(8).FontColor(ds.PcCount > 0 ? "#2563eb" : Colors.Grey.Medium);
                                    table.Cell().Background(bg).AlignCenter().Padding(3).Text(ds.AcademyCount > 0 ? ds.AcademyCount.ToString() : "–").FontSize(8).FontColor(ds.AcademyCount > 0 ? "#16a34a" : Colors.Grey.Medium);
                                    table.Cell().Background(bg).AlignCenter().Padding(3).Text(ds.BodyInShop > 0 ? ds.BodyInShop.ToString() : "–").FontSize(8).FontColor(ds.BodyInShop > 0 ? "#ea580c" : Colors.Grey.Medium);
                                }
                            });
                        });

                        mainRow.ConstantItem(12);

                        mainRow.RelativeItem(4).Column(right =>
                        {
                            right.Item().Text("Staff Leaderboard").SemiBold().FontSize(9).FontColor("#1a1a2e");
                            right.Item().PaddingTop(3);
                            if (weekStaff.Count == 0)
                            {
                                right.Item().Text("No sessions this week.").FontSize(8).FontColor(Colors.Grey.Medium);
                            }
                            else
                            {
                                right.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.ConstantColumn(18);
                                        cols.RelativeColumn(3);
                                        cols.RelativeColumn(2);
                                        cols.RelativeColumn(2);
                                        cols.RelativeColumn(2);
                                    });
                                    table.Header(h =>
                                    {
                                        h.Cell().Background("#667eea").Padding(3).AlignCenter().Text("#").FontSize(7).SemiBold().FontColor(Colors.White);
                                        h.Cell().Background("#667eea").Padding(3).Text("Name").FontSize(7).SemiBold().FontColor(Colors.White);
                                        h.Cell().Background("#667eea").Padding(3).AlignCenter().Text("Auditing").FontSize(7).SemiBold().FontColor(Colors.White);
                                        h.Cell().Background("#667eea").Padding(3).AlignCenter().Text("CS Solo").FontSize(7).SemiBold().FontColor(Colors.White);
                                        h.Cell().Background("#667eea").Padding(3).AlignCenter().Text("Total").FontSize(7).SemiBold().FontColor(Colors.White);
                                    });
                                    int rank = 1;
                                    foreach (var s in weekStaff)
                                    {
                                        var bg = rank % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                                        table.Cell().Background(bg).AlignCenter().Padding(3).Text(rank.ToString()).FontSize(8).FontColor(Colors.Grey.Darken1);
                                        table.Cell().Background(bg).Padding(3).Text(s.Name).FontSize(8).SemiBold();
                                        table.Cell().Background(bg).AlignCenter().Padding(3).Text(s.AuditSec > 0 ? FmtSec(s.AuditSec) : "–").FontSize(8).FontColor(s.AuditSec > 0 ? "#1a73e8" : Colors.Grey.Medium);
                                        table.Cell().Background(bg).AlignCenter().Padding(3).Text(s.SoloCsSec > 0 ? FmtSec(s.SoloCsSec) : "–").FontSize(8).FontColor(s.SoloCsSec > 0 ? "#c5221f" : Colors.Grey.Medium);
                                        table.Cell().Background(bg).AlignCenter().Padding(3).Text(FmtSec(s.TotalSec)).FontSize(8).SemiBold().FontColor("#6366f1");
                                        rank++;
                                    }
                                });
                            }
                        });
                    });

                    col.Item().PaddingTop(10);

                    // ── Weekly History Chart (full width) ────────────────────
                    col.Item().Text("Weekly History — PCs / Academy / BITS").SemiBold().FontSize(9).FontColor("#1a1a2e");
                    col.Item().PaddingTop(4);
                    col.Item().Height(160).Svg(size =>
                    {
                        using var stream = new MemoryStream();
                        using (var skCanvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
                            DrawWeeklyChart(skCanvas, new SKSize(size.Width, size.Height), weekHistory, weekStart);
                        stream.Position = 0;
                        return new StreamReader(stream).ReadToEnd();
                    });
                });
            });
        }).GeneratePdf();
    }

    static void DrawWeeklyChart(SKCanvas canvas, SKSize size, IList<WeekStatSummary> history, DateOnly currentWeekStart)
    {
        if (history.Count == 0) return;

        float W = size.Width;
        float H = size.Height;
        const float leftM   = 28f;
        const float rightM  = 6f;
        const float topM    = 18f;
        const float bottomM = 26f;

        float cX = leftM;
        float cY = topM;
        float cW = W - leftM - rightM;
        float cH = H - topM - bottomM;

        using var bgPaint    = new SKPaint { Color = new SKColor(248, 250, 252), Style = SKPaintStyle.Fill };
        using var gridPaint  = new SKPaint { Color = new SKColor(218, 222, 232), Style = SKPaintStyle.Stroke, StrokeWidth = 0.4f, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0) };
        using var axisPaint  = new SKPaint { Color = new SKColor(160, 165, 175), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f };
        using var hlPaint    = new SKPaint { Color = new SKColor(99, 102, 241, 30), Style = SKPaintStyle.Fill };
        using var auditPaint = new SKPaint { Color = SKColor.Parse("#6366f1"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var pcPaint    = new SKPaint { Color = SKColor.Parse("#3b82f6"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var acPaint    = new SKPaint { Color = SKColor.Parse("#16a34a"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var bpPaint    = new SKPaint { Color = SKColor.Parse("#ea580c"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var yPaint     = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 7f,   IsAntialias = true };
        using var xPaint     = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 6.5f, IsAntialias = true };

        canvas.DrawRect(cX, cY, cW, cH, bgPaint);

        // Max value — audit converted to hours, others as counts
        float maxF = 1f;
        foreach (var w in history)
            maxF = Math.Max(maxF, Math.Max(
                (float)(w.TotalAuditCsSec / 3600.0),
                Math.Max(w.PcCount, Math.Max(w.AcademyCount, w.BodyInShop))));

        const int gridLines = 4;
        int step = (int)Math.Ceiling(maxF / gridLines);
        if (step < 1) step = 1;
        int roundedMax = step * gridLines;

        for (int i = 0; i <= gridLines; i++)
        {
            float gy = cY + cH - i * cH / gridLines;
            canvas.DrawLine(cX, gy, cX + cW, gy, gridPaint);
            string lbl = (i * step).ToString();
            float tw = yPaint.MeasureText(lbl);
            canvas.DrawText(lbl, cX - tw - 3f, gy + 2.5f, yPaint);
        }

        canvas.DrawLine(cX, cY, cX, cY + cH, axisPaint);
        canvas.DrawLine(cX, cY + cH, cX + cW, cY + cH, axisPaint);

        int   n      = history.Count;
        float groupW = cW / n;
        float barsW  = groupW * 0.84f;
        float barW   = barsW / 4f;
        float gapW   = (groupW - barsW) / 2f;
        float base_  = cY + cH;
        const float minH = 2f;   // stub height for zero values

        for (int i = 0; i < n; i++)
        {
            var   w  = history[i];
            float gx = cX + i * groupW;

            if (w.WeekStart == currentWeekStart)
                canvas.DrawRect(gx, cY, groupW, cH, hlPaint);

            float bx = gx + gapW;

            void DrawBar(float x, float val, SKPaint p)
            {
                float bh = val > 0 ? cH * val / roundedMax : minH;
                canvas.DrawRoundRect(SKRect.Create(x, base_ - bh, barW, bh), 1.2f, 1.2f, p);
            }

            DrawBar(bx,            (float)(w.TotalAuditCsSec / 3600.0), auditPaint);
            DrawBar(bx + barW,     w.PcCount,                           pcPaint);
            DrawBar(bx + barW * 2f, w.AcademyCount,                    acPaint);
            DrawBar(bx + barW * 3f, w.BodyInShop,                      bpPaint);

            string xl = w.WeekStart.ToString("dd/MM");
            float  lx = gx + groupW / 2f;
            canvas.Save();
            canvas.Translate(lx, base_ + 4f);
            canvas.RotateDegrees(-50f);
            canvas.DrawText(xl, 0, 0, xPaint);
            canvas.Restore();
        }

        // Legend
        float legY = cY + 11f;
        DrawLegendItem(canvas, cX + 4f,   legY, SKColor.Parse("#6366f1"), "Aud+CS (h)", 7f);
        DrawLegendItem(canvas, cX + 66f,  legY, SKColor.Parse("#3b82f6"), "PCs",        7f);
        DrawLegendItem(canvas, cX + 100f, legY, SKColor.Parse("#16a34a"), "Academy",    7f);
        DrawLegendItem(canvas, cX + 153f, legY, SKColor.Parse("#ea580c"), "BITS",       7f);
    }

    static void DrawLegendItem(SKCanvas canvas, float x, float y, SKColor color, string label, float fs)
    {
        using var rp = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(SKRect.Create(x, y - fs + 1f, fs - 1f, fs - 1f), 1.5f, 1.5f, rp);
        using var tp = new SKPaint { Color = new SKColor(55, 65, 81), TextSize = fs, IsAntialias = true };
        canvas.DrawText(label, x + fs + 2f, y, tp);
    }

    static string FmtSec(int s) =>
        s <= 0 ? "–" : $"{s / 3600}:{(s % 3600) / 60:D2}";

}
