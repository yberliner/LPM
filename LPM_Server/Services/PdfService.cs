using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LPM.Services;   // PcInfo lives here in your project

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
                page.Margin(18, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    // ── Header ──────────────────────────────────────────────
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Statistics Report").SemiBold().FontSize(20).FontColor("#1a1a2e");
                            c.Item().Text($"Week: {weekStart:ddd dd/MM/yyyy} – {weekEnd:ddd dd/MM/yyyy}")
                                .FontSize(11).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    col.Item().PaddingTop(10).LineHorizontal(1.5f).LineColor("#667eea");
                    col.Item().PaddingTop(12);

                    // ── Week Summary 3-box row ───────────────────────────────
                    col.Item().Row(r =>
                    {
                        // Box 1: Total Audit+CS
                        r.RelativeItem().Border(1).BorderColor("#e0e4f0")
                            .Background("#f0f4ff").CornerRadius(6).Padding(10)
                            .Column(c =>
                            {
                                c.Item().AlignCenter().Text("Total Aud+CS Sold").FontSize(8)
                                    .FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(4).AlignCenter()
                                    .Text(FmtSec(currentWeekSummary?.TotalAuditCsSec ?? 0))
                                    .FontSize(22).Bold().FontColor("#6366f1");
                            });

                        r.ConstantItem(10);

                        // Box 2: Academy Students
                        r.RelativeItem().Border(1).BorderColor("#d1fae5")
                            .Background("#f0fdf4").CornerRadius(6).Padding(10)
                            .Column(c =>
                            {
                                c.Item().AlignCenter().Text("Academy Students").FontSize(8)
                                    .FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(4).AlignCenter()
                                    .Text((currentWeekSummary?.AcademyCount ?? 0).ToString())
                                    .FontSize(22).Bold().FontColor("#16a34a");
                            });

                        r.ConstantItem(10);

                        // Box 3: Body in Shop
                        r.RelativeItem().Border(1).BorderColor("#fde8d8")
                            .Background("#fff7ed").CornerRadius(6).Padding(10)
                            .Column(c =>
                            {
                                c.Item().AlignCenter().Text("Body in Shop").FontSize(8)
                                    .FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(4).AlignCenter()
                                    .Text((currentWeekSummary?.BodyInShop ?? 0).ToString())
                                    .FontSize(22).Bold().FontColor("#ea580c");
                            });
                    });

                    col.Item().PaddingTop(16);

                    // ── Day-by-day summary ───────────────────────────────────
                    col.Item().Text("Day-by-Day Breakdown").SemiBold().FontSize(12).FontColor("#1a1a2e");
                    col.Item().PaddingTop(6);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2);   // Day
                            cols.RelativeColumn(2);   // Total Time
                            cols.RelativeColumn(1.2f); // Academy
                            cols.RelativeColumn(1.2f); // Body/Shop
                        });

                        // Header
                        table.Header(h =>
                        {
                            h.Cell().Background("#1a1a2e").Padding(5)
                                .Text("Day").FontSize(9).SemiBold().FontColor(Colors.White);
                            h.Cell().Background("#1a1a2e").Padding(5).AlignCenter()
                                .Text("Total Time").FontSize(9).SemiBold().FontColor(Colors.White);
                            h.Cell().Background("#1a1a2e").Padding(5).AlignCenter()
                                .Text("Academy").FontSize(9).SemiBold().FontColor(Colors.White);
                            h.Cell().Background("#1a1a2e").Padding(5).AlignCenter()
                                .Text("Body/Shop").FontSize(9).SemiBold().FontColor(Colors.White);
                        });

                        for (int d = 0; d < 7; d++)
                        {
                            var ds     = dayStats[d];
                            var total  = ds.Staff.Sum(s => s.AuditSec + s.SoloCsSec);
                            var bg     = d % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;
                            bool today = ds.Date == DateOnly.FromDateTime(DateTime.Today);

                            table.Cell().Background(bg).Padding(5)
                                .Text(t =>
                                {
                                    t.Span(ds.Date.ToString("ddd dd/MM")).FontSize(9);
                                    if (today) t.Span(" ●").FontColor("#6366f1").FontSize(7);
                                });
                            table.Cell().Background(bg).AlignCenter().Padding(5)
                                .Text(total > 0 ? FmtSec(total) : "–").FontSize(9)
                                .FontColor(total > 0 ? "#6366f1" : Colors.Grey.Medium);
                            table.Cell().Background(bg).AlignCenter().Padding(5)
                                .Text(ds.AcademyCount > 0 ? ds.AcademyCount.ToString() : "–").FontSize(9)
                                .FontColor(ds.AcademyCount > 0 ? "#16a34a" : Colors.Grey.Medium);
                            table.Cell().Background(bg).AlignCenter().Padding(5)
                                .Text(ds.BodyInShop > 0 ? ds.BodyInShop.ToString() : "–").FontSize(9)
                                .FontColor(ds.BodyInShop > 0 ? "#ea580c" : Colors.Grey.Medium);
                        }
                    });

                    col.Item().PaddingTop(16);

                    // ── Staff Leaderboard (full week) ────────────────────────
                    col.Item().Text("Staff Leaderboard — Full Week").SemiBold().FontSize(12).FontColor("#1a1a2e");
                    col.Item().PaddingTop(6);

                    if (weekStaff.Count == 0)
                    {
                        col.Item().Text("No sessions recorded this week.").FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(26);  // Rank
                                cols.RelativeColumn(3);   // Name
                                cols.RelativeColumn(2);   // Auditing
                                cols.RelativeColumn(2);   // CS Solo
                                cols.RelativeColumn(2);   // Total
                            });

                            table.Header(h =>
                            {
                                h.Cell().Background("#667eea").Padding(5).AlignCenter().Text("#").FontSize(9).SemiBold().FontColor(Colors.White);
                                h.Cell().Background("#667eea").Padding(5).Text("Staff Member").FontSize(9).SemiBold().FontColor(Colors.White);
                                h.Cell().Background("#667eea").Padding(5).AlignCenter().Text("Auditing").FontSize(9).SemiBold().FontColor(Colors.White);
                                h.Cell().Background("#667eea").Padding(5).AlignCenter().Text("CS Solo").FontSize(9).SemiBold().FontColor(Colors.White);
                                h.Cell().Background("#667eea").Padding(5).AlignCenter().Text("Total").FontSize(9).SemiBold().FontColor(Colors.White);
                            });

                            int rank = 1;
                            foreach (var s in weekStaff)
                            {
                                var bg = rank % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                                table.Cell().Background(bg).AlignCenter().Padding(4)
                                    .Text(rank.ToString()).FontSize(9).FontColor(Colors.Grey.Darken1);
                                table.Cell().Background(bg).Padding(4)
                                    .Text(s.Name).FontSize(9).SemiBold();
                                table.Cell().Background(bg).AlignCenter().Padding(4)
                                    .Text(s.AuditSec > 0 ? FmtSec(s.AuditSec) : "–").FontSize(9)
                                    .FontColor(s.AuditSec > 0 ? "#1a73e8" : Colors.Grey.Medium);
                                table.Cell().Background(bg).AlignCenter().Padding(4)
                                    .Text(s.SoloCsSec > 0 ? FmtSec(s.SoloCsSec) : "–").FontSize(9)
                                    .FontColor(s.SoloCsSec > 0 ? "#c5221f" : Colors.Grey.Medium);
                                table.Cell().Background(bg).AlignCenter().Padding(4)
                                    .Text(FmtSec(s.TotalSec)).FontSize(9).SemiBold()
                                    .FontColor("#6366f1");
                                rank++;
                            }
                        });
                    }

                    col.Item().PaddingTop(16);

                    // ── Weekly History Table ─────────────────────────────────
                    col.Item().Text("Weekly History (last 20 weeks)").SemiBold().FontSize(12).FontColor("#1a1a2e");
                    col.Item().PaddingTop(6);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2.5f);  // Week
                            cols.RelativeColumn(2);      // Aud+CS
                            cols.RelativeColumn(1.5f);   // Academy
                            cols.RelativeColumn(1.5f);   // Body/Shop
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background("#1a1a2e").Padding(5).Text("Week").FontSize(9).SemiBold().FontColor(Colors.White);
                            h.Cell().Background("#1a1a2e").Padding(5).AlignCenter().Text("Aud+CS Sold").FontSize(9).SemiBold().FontColor(Colors.White);
                            h.Cell().Background("#1a1a2e").Padding(5).AlignCenter().Text("Academy").FontSize(9).SemiBold().FontColor(Colors.White);
                            h.Cell().Background("#1a1a2e").Padding(5).AlignCenter().Text("Body/Shop").FontSize(9).SemiBold().FontColor(Colors.White);
                        });

                        foreach (var w in Enumerable.Reverse(weekHistory))
                        {
                            bool   isCur = w.WeekStart == weekStart;
                            string bg    = isCur ? "#e8f0fe" : (string)Colors.White;
                            table.Cell().Background(bg).Padding(4)
                                .Text(t =>
                                {
                                    t.Span(w.WeekRangeLabel).FontSize(9);
                                    if (isCur) t.Span(" ◄").FontColor("#6366f1").FontSize(8);
                                });
                            table.Cell().Background(bg).AlignCenter().Padding(4)
                                .Text(w.TotalAuditCsSec > 0 ? FmtSec(w.TotalAuditCsSec) : "–")
                                .FontSize(9).FontColor(w.TotalAuditCsSec > 0 ? "#1a73e8" : Colors.Grey.Medium);
                            table.Cell().Background(bg).AlignCenter().Padding(4)
                                .Text(w.AcademyCount > 0 ? w.AcademyCount.ToString() : "–")
                                .FontSize(9).FontColor(w.AcademyCount > 0 ? "#1e8e3e" : Colors.Grey.Medium);
                            table.Cell().Background(bg).AlignCenter().Padding(4)
                                .Text(w.BodyInShop > 0 ? w.BodyInShop.ToString() : "–")
                                .FontSize(9).FontColor(w.BodyInShop > 0 ? "#c5221f" : Colors.Grey.Medium);
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    static string FmtSec(int s) =>
        s <= 0 ? "–" : $"{s / 3600}:{(s % 3600) / 60:D2}";

}
