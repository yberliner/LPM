using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LPM.Services;   // PcInfo lives here in your project
using SkiaSharp;
using System.Text.RegularExpressions;

public class PdfService
{
    public byte[] GenerateWeeklyTablePdf(
        string userDisplayName,
        DateOnly weekStart,
        List<PcInfo> pcs,
        Dictionary<(int pcId, int dayIdx), int> grid,
        Dictionary<int, string> pcCsNames,
        string? weeklyRemarks = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        bool hasData(PcInfo pc) => Enumerable.Range(0, 7).Any(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d)) > 0);
        var csSoloPcs  = pcs.Where(pc => pc.WorkCapacity == "CSSolo" && hasData(pc)).ToList();
        var csPcs      = pcs.Where(pc => pc.WorkCapacity == "CS" && hasData(pc)).ToList();
        var audPcs     = pcs.Where(pc => !csSoloPcs.Contains(pc) && !csPcs.Contains(pc) && hasData(pc)).ToList();

        var weekEnd = weekStart.AddDays(6);

        int audTotal     = audPcs.Sum(pc => Enumerable.Range(0, 7).Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d))));
        int csSoloTotal  = csSoloPcs.Sum(pc => Enumerable.Range(0, 7).Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d))));
        int csTotal      = csPcs.Sum(pc => Enumerable.Range(0, 7).Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d))));
        int combinedTotal = audTotal + csSoloTotal;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().ScaleToFit().Column(col =>
                {
                    // ── Top row ──
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

                    // ══════ AUDITOR TABLE ══════
                    if (audPcs.Count > 0)
                    {
                        col.Item().PaddingTop(10).Text("Auditor").SemiBold().FontSize(11).FontColor("#1a237e");
                        col.Item().PaddingTop(4);
                        RenderPdfTable(col, audPcs, grid, pcCsNames, weekStart, tableType: "Auditor");

                        col.Item().PaddingTop(6);
                        col.Item().Background("#1a237e").Padding(6).Row(r =>
                        {
                            r.RelativeItem().Text("Auditor Total").SemiBold().FontSize(12).FontColor("#ffffff");
                            r.ConstantItem(150).AlignRight()
                                .Text(DashboardService.FmtOrBlank(audTotal))
                                .SemiBold().FontSize(16).FontColor("#ffffff");
                        });
                    }

                    // ══════ CS SOLO TABLE ══════
                    if (csSoloPcs.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("CS Solo").SemiBold().FontSize(10).FontColor("#6a1b9a");
                        col.Item().PaddingTop(4);
                        RenderPdfTable(col, csSoloPcs, grid, pcCsNames, weekStart, tableType: "CSSolo");

                        col.Item().PaddingTop(6);
                        col.Item().Background("#6a1b9a").Padding(6).Row(r =>
                        {
                            r.RelativeItem().Text("CS Solo Total").SemiBold().FontSize(11).FontColor("#ffffff");
                            r.ConstantItem(150).AlignRight()
                                .Text(DashboardService.FmtOrBlank(csSoloTotal))
                                .SemiBold().FontSize(14).FontColor("#ffffff");
                        });
                    }

                    // ══════ COMBINED GRAND TOTAL (Auditor + CS Solo) ══════
                    col.Item().PaddingTop(10);
                    col.Item().Background("#0d47a1").Padding(10).Row(r =>
                    {
                        r.RelativeItem().Text("Grand Total (Auditor + CS Solo)").SemiBold().FontSize(16).FontColor("#ffffff");
                        r.ConstantItem(160).AlignRight()
                            .Text(DashboardService.Fmt(combinedTotal))
                            .SemiBold().FontSize(22).FontColor("#ffffff");
                    });

                    // ══════ CS TABLE ══════
                    if (csPcs.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("CS").SemiBold().FontSize(10).FontColor("#546e7a");
                        col.Item().PaddingTop(4);
                        RenderPdfTable(col, csPcs, grid, pcCsNames, weekStart, tableType: "CS");

                        col.Item().PaddingTop(6);
                        col.Item().Background("#546e7a").Padding(6).Row(r =>
                        {
                            r.RelativeItem().Text("CS Total").SemiBold().FontSize(11).FontColor("#ffffff");
                            r.ConstantItem(150).AlignRight()
                                .Text(DashboardService.FmtOrBlank(csTotal))
                                .SemiBold().FontSize(14).FontColor("#ffffff");
                        });
                    }

                    // ══════ WEEKLY REMARKS ══════
                    if (!string.IsNullOrWhiteSpace(weeklyRemarks))
                    {
                        col.Item().PaddingTop(14);
                        col.Item().Text("Weekly Remarks / הערות שבועיות").SemiBold().FontSize(11).FontColor("#1a237e");
                        col.Item().PaddingTop(4);
                        RenderHtmlBlock(col, weeklyRemarks);
                    }
                });
            });
        }).GeneratePdf();
    }

    private void RenderPdfTable(
        ColumnDescriptor col,
        List<PcInfo> pcsList,
        Dictionary<(int pcId, int dayIdx), int> grid,
        Dictionary<int, string> pcCsNames,
        DateOnly weekStart,
        string tableType)   // "Auditor", "CSSolo", or "CS"
    {
        bool isCs     = tableType == "CS";
        bool isCSSolo = tableType == "CSSolo";
        bool isAud    = tableType == "Auditor";

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(90);
                for (int i = 0; i < Math.Max(1, pcsList.Count); i++)
                    columns.RelativeColumn();
            });

            table.Header(header =>
            {
                if (isAud)
                {
                    // Row 1: CS name row (auditor table only)
                    header.Cell().Element(CsHeaderCell).AlignLeft().Text("CS:").SemiBold().FontSize(9);
                    foreach (var pc in pcsList)
                    {
                        var csFullName = pcCsNames.TryGetValue(pc.PcId, out var cn) ? cn : "";
                        var csDisplay = csFullName.Length > 0 ? csFullName.Split(' ')[0] : "NA";
                        header.Cell().Element(CsHeaderCell).AlignCenter()
                            .Text(t => t.Span(csDisplay).FontSize(9).FontColor(Colors.Grey.Darken2));
                    }

                    // Row 2-3: Role + PC name
                    header.Cell().RowSpan(2).Element(HeaderCell).AlignMiddle().Text("Day").SemiBold();
                    foreach (var pc in pcsList)
                        header.Cell().Element(RoleHeaderCell).AlignCenter().PaddingVertical(2)
                            .Text(RoleLabel(pc)).FontSize(9).FontColor(Colors.Grey.Darken1);
                    foreach (var pc in pcsList)
                        header.Cell().Element(NameHeaderCell).AlignCenter()
                            .Text(pc.FullName).SemiBold().FontSize(11);
                }
                else if (isCSSolo)
                {
                    // CS Solo table: Day + PC names in purple tone
                    header.Cell().Element(HeaderCell).AlignMiddle().Text("Day").SemiBold().FontSize(9);
                    foreach (var pc in pcsList)
                        header.Cell().Element(NameHeaderCell).AlignCenter()
                            .Text(pc.FullName).SemiBold().FontSize(9).FontColor("#6a1b9a");
                }
                else
                {
                    // CS table: Day + PC names
                    header.Cell().Element(HeaderCell).AlignMiddle().Text("Day").SemiBold().FontSize(9);
                    foreach (var pc in pcsList)
                        header.Cell().Element(NameHeaderCell).AlignCenter()
                            .Text(pc.FullName).SemiBold().FontSize(9).FontColor("#546e7a");
                }
            });

            bool compact = isCs || isCSSolo;
            string textColor = isCSSolo ? "#6a1b9a" : isCs ? "#888888" : "#000000";

            // Day rows
            for (int d = 0; d < 7; d++)
            {
                var date = weekStart.AddDays(d);
                if (compact)
                    table.Cell().Element(CellStyle).Text(t => t.Span(date.ToString("ddd dd/MM")).FontSize(8).FontColor(textColor));
                else
                    table.Cell().Element(CellStyle).Text(date.ToString("ddd dd/MM"));

                foreach (var pc in pcsList)
                {
                    int secs = grid.GetValueOrDefault((DashboardService.GKey(pc), d));
                    if (compact)
                        table.Cell().Element(CellStyle).AlignCenter()
                            .Text(t => t.Span(DashboardService.FmtOrBlank(secs)).FontSize(8).FontColor(textColor));
                    else
                        table.Cell().Element(CellStyle).AlignCenter()
                            .Text(DashboardService.FmtOrBlank(secs));
                }
            }

            // Σ Week row
            if (compact)
                table.Cell().Element(WeekTotalCell).Text(t => t.Span("Σ Week").FontSize(8).SemiBold().FontColor(textColor));
            else
                table.Cell().Element(WeekTotalCell).Text("Σ Week").SemiBold();

            foreach (var pc in pcsList)
            {
                int total = Enumerable.Range(0, 7)
                    .Sum(d => grid.GetValueOrDefault((DashboardService.GKey(pc), d)));
                if (compact)
                    table.Cell().Element(WeekTotalCell).AlignCenter()
                        .Text(t => t.Span(DashboardService.FmtOrBlank(total)).FontSize(8).FontColor(textColor).SemiBold());
                else
                    table.Cell().Element(WeekTotalCell).AlignCenter()
                        .Text(DashboardService.FmtOrBlank(total)).SemiBold();
            }
        });
    }

    public byte[] GenerateAcademyWeekPdf(DateOnly weekStart,
        List<(int PersonId, string FullName, int VisitCount, int TotalSessions, string Referral, string Org, string Nick)> students,
        Dictionary<string, int>? byReferral = null,
        Dictionary<string, int>? byOrg = null,
        Dictionary<int, string>? personCourses = null,
        List<(int PersonId, string FullName, int VisitCount, int TotalSessions, string Referral, string Org, string Nick)>? monthStudents = null,
        string? monthLabel = null,
        int monthWeekCount = 0,
        DateOnly? monthStart = null,
        DateOnly? monthEnd = null,
        List<WeekVisitCount>? weeklyTotals = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var weekEnd  = weekStart.AddDays(6);
        int count    = students.Count;
        int numCols  = count <= 25 ? 2 : count <= 55 ? 3 : count <= 100 ? 4 : 6;
        int rows     = (int)Math.Ceiling((double)count / numCols);

        // Build monthly chart data by aggregating weekly totals per month
        var monthlyChartData = new List<(string Label, int Don, int Friend, int Social, int Haifa, int Other, int Total)>();
        if (weeklyTotals != null && weeklyTotals.Count > 0)
        {
            var byMonth = new Dictionary<string, (int Don, int Friend, int Social, int Haifa, int Other, int Total)>();
            var monthOrder = new List<string>();
            foreach (var w in weeklyTotals)
            {
                // Parse week label "dd/MM" to get month
                var parts = w.WeekLabel.Split('/');
                if (parts.Length == 2)
                {
                    var mLabel = $"{parts[1]}/{(DateTime.Now.Year % 100):D2}"; // MM/YY
                    if (!byMonth.ContainsKey(mLabel))
                    {
                        byMonth[mLabel] = (0, 0, 0, 0, 0, 0);
                        monthOrder.Add(mLabel);
                    }
                    var cur = byMonth[mLabel];
                    byMonth[mLabel] = (cur.Don + w.DonCount, cur.Friend + w.FriendCount,
                        cur.Social + w.SocialCount, cur.Haifa + w.HaifaCount,
                        cur.Other + w.OtherCount, cur.Total + w.TotalVisits);
                }
            }
            foreach (var ml in monthOrder)
            {
                var d = byMonth[ml];
                monthlyChartData.Add((ml, d.Don, d.Friend, d.Social, d.Haifa, d.Other, d.Total));
            }
        }

        return Document.Create(container =>
        {
            // ══════════ PAGE 1: Weekly ══════════
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
                            c.Item().AlignRight()
                                .Text(t =>
                                {
                                    t.Span("Visits: ").FontSize(10);
                                    t.Span(students.Sum(s => s.VisitCount).ToString()).FontSize(13).Bold().FontColor("#1b5e20");
                                    t.Span("  Total Visits: ").FontSize(10);
                                    t.Span(students.Sum(s => s.TotalSessions).ToString()).FontSize(13).Bold().FontColor("#d97706");
                                });
                        });
                    });

                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#2e7d32");
                    col.Item().PaddingTop(6);

                    // ── Weekly breakdown summary ──
                    bool hasBreakdown = (byReferral != null && byReferral.Count > 0) ||
                                        (byOrg      != null && byOrg.Count > 0);
                    if (hasBreakdown)
                    {
                        col.Item().Background("#f0fdf4").CornerRadius(4).Padding(6).Row(br =>
                        {
                            if (byReferral != null && byReferral.Count > 0)
                            {
                                br.AutoItem().Column(rc =>
                                {
                                    rc.Item().Text("By Referral").FontSize(7).FontColor("#065f46").SemiBold();
                                    rc.Item().PaddingTop(2).Row(rr =>
                                    {
                                        foreach (var kv in byReferral.OrderByDescending(x => x.Value))
                                        {
                                            var refColor = kv.Key switch
                                            {
                                                "Friend"          => "#f59e0b",
                                                "Social Network"  => "#3b82f6",
                                                "Haifa"           => "#7c3aed",
                                                "Other"           => "#94a3b8",
                                                _                 => "#16a34a",
                                            };
                                            rr.AutoItem().PaddingRight(10).Column(bc =>
                                            {
                                                bc.Item().Text(t =>
                                                {
                                                    t.Span("● ").FontSize(9).FontColor(refColor);
                                                    t.Span($"{kv.Key}: ").FontSize(8).FontColor("#374151");
                                                    t.Span(kv.Value.ToString()).FontSize(9).Bold().FontColor("#1b5e20");
                                                });
                                            });
                                        }
                                    });
                                });
                            }

                            if (byOrg != null && byOrg.Count > 0)
                            {
                                if (byReferral != null && byReferral.Count > 0)
                                    br.ConstantItem(1).Background("#6ee7b7");
                                br.AutoItem().PaddingLeft(byReferral?.Count > 0 ? 10 : 0).Column(oc =>
                                {
                                    oc.Item().Text("By Organization").FontSize(7).FontColor("#065f46").SemiBold();
                                    oc.Item().PaddingTop(2).Row(or =>
                                    {
                                        foreach (var kv in byOrg.OrderByDescending(x => x.Value))
                                        {
                                            or.AutoItem().PaddingRight(10).Column(bc =>
                                            {
                                                bc.Item().Text(t =>
                                                {
                                                    t.Span("■ ").FontSize(8).FontColor("#059669");
                                                    t.Span($"{kv.Key}: ").FontSize(8).FontColor("#374151");
                                                    t.Span(kv.Value.ToString()).FontSize(9).Bold().FontColor("#1b5e20");
                                                });
                                            });
                                        }
                                    });
                                });
                            }
                        });
                        col.Item().PaddingTop(6);
                    }

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
                                innerCol.Item()
                                    .Background("#e8f5e9")
                                    .PaddingVertical(3).PaddingHorizontal(5)
                                    .Row(hr =>
                                    {
                                        hr.RelativeItem()
                                            .Text("Student")
                                            .FontSize(8).FontColor("#555").SemiBold();
                                        hr.ConstantItem(24).AlignRight()
                                            .Text("Vis.")
                                            .FontSize(7).FontColor("#555").SemiBold();
                                        hr.ConstantItem(24).AlignRight()
                                            .Text("Total")
                                            .FontSize(7).FontColor("#555").SemiBold();
                                    });

                                int rank = colIdx * rows + 1;
                                foreach (var (pid, name, visits, totalSessions, referral, org, nick) in slice)
                                {
                                    int r2 = rank++;
                                    var rowBg = r2 % 2 == 0 ? "#f6fef6" : "#ffffff";
                                    innerCol.Item()
                                        .BorderBottom(0.5f)
                                        .BorderColor(Colors.Grey.Lighten3)
                                        .Background(rowBg)
                                        .PaddingVertical(2).PaddingHorizontal(5)
                                        .Row(rr =>
                                        {
                                            rr.ConstantItem(18)
                                                .Text($"{r2}.")
                                                .FontSize(8).FontColor(Colors.Grey.Medium);
                                            rr.RelativeItem()
                                                .Text(string.IsNullOrEmpty(nick) ? name : $"{name} ({nick})")
                                                .FontSize(9);
                                            rr.ConstantItem(24).AlignRight()
                                                .Text(visits.ToString())
                                                .FontSize(9).FontColor("#2e7d32").SemiBold();
                                            rr.ConstantItem(24).AlignRight()
                                                .Text(totalSessions.ToString())
                                                .FontSize(9).FontColor("#d97706").SemiBold();
                                        });
                                }
                            });

                            if (c < numCols - 1)
                                row.ConstantItem(8).Column(_ => { });
                        }
                    });

                    // ── Per-course student count table ──
                    if (personCourses != null)
                    {
                        var byCourse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (pid, _, _, _, _, _, _) in students)
                        {
                            if (!personCourses.TryGetValue(pid, out var cs) || string.IsNullOrEmpty(cs))
                                continue;
                            foreach (var part in cs.Split(','))
                            {
                                var p = part.Trim();
                                var lastParen = p.LastIndexOf('(');
                                var courseName = lastParen > 0 ? p[..lastParen].Trim() : p;
                                if (!string.IsNullOrEmpty(courseName))
                                    byCourse[courseName] = byCourse.GetValueOrDefault(courseName) + 1;
                            }
                        }

                        if (byCourse.Count > 0)
                        {
                            col.Item().PaddingTop(8).LineHorizontal(1f).LineColor("#a7f3d0");
                            col.Item().PaddingTop(6)
                                .Text("Students per Course — this week")
                                .FontSize(8).SemiBold().FontColor("#065f46");
                            col.Item().PaddingTop(3).Table(t =>
                            {
                                t.ColumnsDefinition(cd =>
                                {
                                    cd.RelativeColumn(4);
                                    cd.RelativeColumn(1);
                                });
                                t.Header(h =>
                                {
                                    h.Cell().Background("#d1fae5").Padding(3)
                                        .Text("Course").FontSize(7.5f).SemiBold().FontColor("#065f46");
                                    h.Cell().Background("#d1fae5").Padding(3).AlignCenter()
                                        .Text("Students").FontSize(7.5f).SemiBold().FontColor("#065f46");
                                });
                                int ci = 0;
                                foreach (var kv in byCourse.OrderByDescending(x => x.Value))
                                {
                                    string bg = ci++ % 2 == 0 ? "#ffffff" : "#f0fdf4";
                                    t.Cell().Background(bg).Padding(3)
                                        .Text(kv.Key).FontSize(8).FontColor("#1a1a2e");
                                    t.Cell().Background(bg).AlignCenter().Padding(3)
                                        .Text(kv.Value.ToString()).FontSize(9).SemiBold().FontColor("#059669");
                                }
                            });
                        }
                    }

                    // ── Weekly visit chart ──
                    if (weeklyTotals != null && weeklyTotals.Count > 0)
                    {
                        col.Item().PaddingTop(10).LineHorizontal(1f).LineColor("#a7f3d0");
                        col.Item().PaddingTop(4)
                            .Text("Weekly Visit History")
                            .FontSize(8).SemiBold().FontColor("#065f46");
                        col.Item().PaddingTop(4).Height(140).Svg(size =>
                        {
                            using var stream = new System.IO.MemoryStream();
                            using (var skCanvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
                                DrawAcademyChart(skCanvas, new SKSize(size.Width, size.Height), weeklyTotals, weekStart);
                            stream.Position = 0;
                            return new System.IO.StreamReader(stream).ReadToEnd();
                        });
                    }
                });
            });

            // ══════════ PAGE 2: Monthly ══════════
            if (monthStudents != null && monthStudents.Count > 0)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(12, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Content().ScaleToFit().Column(col =>
                    {
                        // Header
                        col.Item().Row(r =>
                        {
                            r.RelativeItem()
                                .Text($"Monthly Student List — {monthLabel} ({monthStart:dd/MM} – {monthEnd:dd/MM})")
                                .SemiBold().FontSize(18).FontColor("#4338ca");

                            r.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().AlignRight()
                                    .Text(t =>
                                    {
                                        t.Span("Total students: ").FontSize(14);
                                        t.Span(monthStudents.Count.ToString()).FontSize(17).Bold().FontColor("#4338ca");
                                    });
                                c.Item().AlignRight()
                                    .Text(t =>
                                    {
                                        t.Span("Visits: ").FontSize(10);
                                        t.Span(monthStudents.Sum(s => s.VisitCount).ToString()).FontSize(13).Bold().FontColor("#4338ca");
                                        t.Span("  Total Visits: ").FontSize(10);
                                        t.Span(monthStudents.Sum(s => s.TotalSessions).ToString()).FontSize(13).Bold().FontColor("#d97706");
                                    });
                                c.Item().AlignRight()
                                    .Text($"{monthWeekCount} weeks")
                                    .FontSize(10).FontColor("#6b7280");
                            });
                        });

                        col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#4338ca");
                        col.Item().PaddingTop(6);

                        int mCount   = monthStudents.Count;
                        int mNumCols = mCount <= 25 ? 2 : mCount <= 55 ? 3 : mCount <= 100 ? 4 : 6;
                        int mRows    = (int)Math.Ceiling((double)mCount / mNumCols);

                        col.Item().Row(row =>
                        {
                            for (int c = 0; c < mNumCols; c++)
                            {
                                var slice = monthStudents.Skip(c * mRows).Take(mRows).ToList();
                                int colIdx = c;

                                row.RelativeItem().Column(innerCol =>
                                {
                                    innerCol.Item()
                                        .Background("#e0e7ff")
                                        .PaddingVertical(3).PaddingHorizontal(5)
                                        .Row(hr =>
                                        {
                                            hr.RelativeItem()
                                                .Text("Student")
                                                .FontSize(8).FontColor("#555").SemiBold();
                                            hr.ConstantItem(24).AlignRight()
                                                .Text("Vis.")
                                                .FontSize(7).FontColor("#555").SemiBold();
                                            hr.ConstantItem(24).AlignRight()
                                                .Text("Total")
                                                .FontSize(7).FontColor("#555").SemiBold();
                                        });

                                    int rank = colIdx * mRows + 1;
                                    foreach (var (pid, name, visits, totalSessions, referral, org, nick) in slice)
                                    {
                                        int r2 = rank++;
                                        var rowBg = r2 % 2 == 0 ? "#f5f3ff" : "#ffffff";
                                        innerCol.Item()
                                            .BorderBottom(0.5f)
                                            .BorderColor(Colors.Grey.Lighten3)
                                            .Background(rowBg)
                                            .PaddingVertical(2).PaddingHorizontal(5)
                                            .Row(rr =>
                                            {
                                                rr.ConstantItem(18)
                                                    .Text($"{r2}.")
                                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                                rr.RelativeItem()
                                                    .Text(string.IsNullOrEmpty(nick) ? name : $"{name} ({nick})")
                                                    .FontSize(9);
                                                rr.ConstantItem(24).AlignRight()
                                                    .Text(visits.ToString())
                                                    .FontSize(9).FontColor("#4338ca").SemiBold();
                                                rr.ConstantItem(24).AlignRight()
                                                    .Text(totalSessions.ToString())
                                                    .FontSize(9).FontColor("#d97706").SemiBold();
                                            });
                                    }
                                });

                                if (c < mNumCols - 1)
                                    row.ConstantItem(8).Column(_ => { });
                            }
                        });

                        // ── Monthly students per course ──
                        if (personCourses != null)
                        {
                            var byMonthCourse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            foreach (var (pid, _, _, _, _, _, _) in monthStudents)
                            {
                                if (!personCourses.TryGetValue(pid, out var cs) || string.IsNullOrEmpty(cs))
                                    continue;
                                foreach (var part in cs.Split(','))
                                {
                                    var p = part.Trim();
                                    var lastParen = p.LastIndexOf('(');
                                    var courseName = lastParen > 0 ? p[..lastParen].Trim() : p;
                                    byMonthCourse[courseName] = byMonthCourse.GetValueOrDefault(courseName) + 1;
                                }
                            }
                            if (byMonthCourse.Count > 0)
                            {
                                col.Item().PaddingTop(8).LineHorizontal(1f).LineColor("#c7d2fe");
                                col.Item().PaddingTop(6)
                                    .Text("Students per Course — this month")
                                    .FontSize(8).SemiBold().FontColor("#312e81");
                                col.Item().PaddingTop(3).Table(t =>
                                {
                                    t.ColumnsDefinition(cd =>
                                    {
                                        cd.RelativeColumn(4);
                                        cd.RelativeColumn(1);
                                    });
                                    t.Header(h =>
                                    {
                                        h.Cell().Background("#e0e7ff").Padding(3)
                                            .Text("Course").FontSize(7.5f).SemiBold().FontColor("#312e81");
                                        h.Cell().Background("#e0e7ff").Padding(3).AlignCenter()
                                            .Text("Students").FontSize(7.5f).SemiBold().FontColor("#312e81");
                                    });
                                    int ci = 0;
                                    foreach (var kv in byMonthCourse.OrderByDescending(x => x.Value))
                                    {
                                        string bg = ci++ % 2 == 0 ? "#ffffff" : "#f5f3ff";
                                        t.Cell().Background(bg).Padding(3)
                                            .Text(kv.Key).FontSize(8).FontColor("#1a1a2e");
                                        t.Cell().Background(bg).AlignCenter().Padding(3)
                                            .Text(kv.Value.ToString()).FontSize(9).SemiBold().FontColor("#4338ca");
                                    }
                                });
                            }
                        }

                        // ── Monthly visit chart ──
                        if (monthlyChartData.Count > 0)
                        {
                            col.Item().PaddingTop(10).LineHorizontal(1f).LineColor("#c7d2fe");
                            col.Item().PaddingTop(4)
                                .Text("Monthly Visit History")
                                .FontSize(8).SemiBold().FontColor("#312e81");
                            col.Item().PaddingTop(4).Height(140).Svg(size =>
                            {
                                using var stream = new System.IO.MemoryStream();
                                using (var skCanvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
                                    DrawAcademyMonthlyChart(skCanvas, new SKSize(size.Width, size.Height), monthlyChartData);
                                stream.Position = 0;
                                return new System.IO.StreamReader(stream).ReadToEnd();
                            });
                        }
                    });
                });
            }
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

    static string RoleLabel(PcInfo pc) => pc.WorkCapacity switch
    {
        "CSSolo"        => "Solo",
        "CS"            => "CS",
        "Miscellaneous" => "Other",
        "SoloAuditor"   => "Solo",
        _               => "Auditor",
    };

    // ════════════════════════════════════════════════════════════════════════
    // Statistics PDF
    // ════════════════════════════════════════════════════════════════════════

    public byte[] GenerateStatisticsPdf(
        DateOnly weekStart,
        List<StaffStatRow> weekStaff,
        List<DayStat> dayStats,
        List<WeekStatSummary> weekHistory,
        WeekStatSummary? currentWeekSummary,
        List<OriginHours>? originHours = null,
        List<StaffStatRow>? monthStaff = null,
        WeekStatSummary? monthSummary = null,
        List<OriginHours>? monthOriginHours = null,
        string? monthLabel = null,
        int monthWeekCount = 0,
        DateOnly? monthStart = null,
        DateOnly? monthEnd = null,
        List<MonthStatSummary>? monthHistory = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var weekEnd = weekStart.AddDays(6);
        bool hasMonthData = monthSummary != null && monthStaff != null && monthStaff.Count > 0;

        return Document.Create(container =>
        {
            // ── PAGE 1: Weekly Report ──────────────────────────────────────
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
                                    foreach (var (lbl, align) in new[] { ("Day","L"), ("Aud+CS","C"), ("PCs","C"), ("Acad","C"), ("BIS","C") })
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

                            // ── Hours by Organization (below day-by-day table) ───────
                            if (originHours != null && originHours.Count > 0)
                            {
                                left.Item().PaddingTop(8);
                                left.Item().Text("Hours by Organization").SemiBold().FontSize(9).FontColor("#1a1a2e");
                                left.Item().PaddingTop(3);
                                left.Item().Table(ot =>
                                {
                                    ot.ColumnsDefinition(oc =>
                                    {
                                        oc.RelativeColumn(3);
                                        oc.RelativeColumn(2);
                                    });
                                    ot.Header(h =>
                                    {
                                        h.Cell().Background("#4c1d95").Padding(3).Text("Origin").FontSize(7).SemiBold().FontColor(Colors.White);
                                        h.Cell().Background("#4c1d95").Padding(3).AlignCenter().Text("Hours").FontSize(7).SemiBold().FontColor(Colors.White);
                                    });
                                    int orank = 0;
                                    foreach (var oh in originHours)
                                    {
                                        var (oBg, oColor) = oh.Origin switch
                                        {
                                            "Haifa"       => ("#f5f3ff", "#7c3aed"),
                                            "Riga"        => ("#f0fdfa", "#0d9488"),
                                            "from Abroad" => ("#fff7ed", "#ea580c"),
                                            _             => ("#f9fafb", "#6b7280"),
                                        };
                                        ot.Cell().Background(oBg).Padding(3).Text(oh.Origin).FontSize(8).FontColor(oColor).SemiBold();
                                        ot.Cell().Background(oBg).AlignCenter().Padding(3).Text(FmtSec(oh.Seconds)).FontSize(8).FontColor(oColor).SemiBold();
                                        orank++;
                                    }
                                });
                            }
                        });

                        mainRow.ConstantItem(12);

                        mainRow.RelativeItem(4).Column(right =>
                        {
                            right.Item().Text("Week Staff Leaderboard").SemiBold().FontSize(9).FontColor("#1a1a2e");
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
                    col.Item().Text("Weekly History — PCs / Academy / BIS").SemiBold().FontSize(9).FontColor("#1a1a2e");
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

            // ── PAGE 2: Monthly Report ─────────────────────────────────────
            if (hasMonthData)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(10, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Content().ScaleToFit().Column(col =>
                    {
                        // Monthly header
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Monthly Report — {monthLabel ?? ""}").SemiBold().FontSize(16).FontColor("#4338ca");
                                c.Item().Text($"{monthStart?.ToString("ddd dd/MM/yyyy") ?? ""} – {monthEnd?.ToString("ddd dd/MM/yyyy") ?? ""} · {monthWeekCount} weeks")
                                    .FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                        });

                        col.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor("#4338ca");
                        col.Item().PaddingTop(8);

                        // Month 4-box summary (indigo themed)
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Border(1).BorderColor("#c7d2fe").Background("#eef2ff").CornerRadius(4).Padding(7).Column(c =>
                            {
                                c.Item().AlignCenter().Text("Aud+CS Sold").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(3).AlignCenter().Text(FmtSec(monthSummary!.TotalAuditCsSec)).FontSize(16).Bold().FontColor("#4338ca");
                            });
                            r.ConstantItem(6);
                            r.RelativeItem().Border(1).BorderColor("#dbeafe").Background("#eff6ff").CornerRadius(4).Padding(7).Column(c =>
                            {
                                c.Item().AlignCenter().Text("PCs").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(3).AlignCenter().Text(monthSummary!.PcCount.ToString()).FontSize(16).Bold().FontColor("#2563eb");
                            });
                            r.ConstantItem(6);
                            r.RelativeItem().Border(1).BorderColor("#d1fae5").Background("#f0fdf4").CornerRadius(4).Padding(7).Column(c =>
                            {
                                c.Item().AlignCenter().Text("Academy").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(3).AlignCenter().Text(monthSummary!.AcademyCount.ToString()).FontSize(16).Bold().FontColor("#16a34a");
                            });
                            r.ConstantItem(6);
                            r.RelativeItem().Border(1).BorderColor("#fde8d8").Background("#fff7ed").CornerRadius(4).Padding(7).Column(c =>
                            {
                                c.Item().AlignCenter().Text("Body in Shop").FontSize(7).FontColor(Colors.Grey.Darken2).SemiBold();
                                c.Item().PaddingTop(3).AlignCenter().Text(monthSummary!.BodyInShop.ToString()).FontSize(16).Bold().FontColor("#ea580c");
                            });
                        });

                        col.Item().PaddingTop(10);

                        // Month staff leaderboard + origin hours side by side
                        col.Item().Row(mainRow =>
                        {
                            mainRow.RelativeItem(5).Column(left =>
                            {
                                left.Item().Text("Month Staff Leaderboard").SemiBold().FontSize(9).FontColor("#4338ca");
                                left.Item().PaddingTop(3);
                                if (monthStaff!.Count == 0)
                                {
                                    left.Item().Text("No sessions this month.").FontSize(8).FontColor(Colors.Grey.Medium);
                                }
                                else
                                {
                                    left.Item().Table(table =>
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
                                            h.Cell().Background("#4338ca").Padding(3).AlignCenter().Text("#").FontSize(7).SemiBold().FontColor(Colors.White);
                                            h.Cell().Background("#4338ca").Padding(3).Text("Name").FontSize(7).SemiBold().FontColor(Colors.White);
                                            h.Cell().Background("#4338ca").Padding(3).AlignCenter().Text("Auditing").FontSize(7).SemiBold().FontColor(Colors.White);
                                            h.Cell().Background("#4338ca").Padding(3).AlignCenter().Text("CS Solo").FontSize(7).SemiBold().FontColor(Colors.White);
                                            h.Cell().Background("#4338ca").Padding(3).AlignCenter().Text("Total").FontSize(7).SemiBold().FontColor(Colors.White);
                                        });
                                        int mrank = 1;
                                        foreach (var s in monthStaff)
                                        {
                                            var bg = mrank % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                                            table.Cell().Background(bg).AlignCenter().Padding(3).Text(mrank.ToString()).FontSize(8).FontColor(Colors.Grey.Darken1);
                                            table.Cell().Background(bg).Padding(3).Text(s.Name).FontSize(8).SemiBold();
                                            table.Cell().Background(bg).AlignCenter().Padding(3).Text(s.AuditSec > 0 ? FmtSec(s.AuditSec) : "–").FontSize(8).FontColor(s.AuditSec > 0 ? "#1a73e8" : Colors.Grey.Medium);
                                            table.Cell().Background(bg).AlignCenter().Padding(3).Text(s.SoloCsSec > 0 ? FmtSec(s.SoloCsSec) : "–").FontSize(8).FontColor(s.SoloCsSec > 0 ? "#c5221f" : Colors.Grey.Medium);
                                            table.Cell().Background(bg).AlignCenter().Padding(3).Text(FmtSec(s.TotalSec)).FontSize(8).SemiBold().FontColor("#4338ca");
                                            mrank++;
                                        }
                                    });
                                }
                            });

                            mainRow.ConstantItem(12);

                            mainRow.RelativeItem(4).Column(right =>
                            {
                                // Month origin hours
                                if (monthOriginHours != null && monthOriginHours.Count > 0)
                                {
                                    right.Item().Text("Hours by Organization — Month").SemiBold().FontSize(9).FontColor("#4338ca");
                                    right.Item().PaddingTop(3);
                                    right.Item().Table(ot =>
                                    {
                                        ot.ColumnsDefinition(oc =>
                                        {
                                            oc.RelativeColumn(3);
                                            oc.RelativeColumn(2);
                                        });
                                        ot.Header(h =>
                                        {
                                            h.Cell().Background("#4338ca").Padding(3).Text("Origin").FontSize(7).SemiBold().FontColor(Colors.White);
                                            h.Cell().Background("#4338ca").Padding(3).AlignCenter().Text("Hours").FontSize(7).SemiBold().FontColor(Colors.White);
                                        });
                                        foreach (var oh in monthOriginHours)
                                        {
                                            var (oBg, oColor) = oh.Origin switch
                                            {
                                                "Haifa"       => ("#f5f3ff", "#7c3aed"),
                                                "Riga"        => ("#f0fdfa", "#0d9488"),
                                                "from Abroad" => ("#fff7ed", "#ea580c"),
                                                _             => ("#f9fafb", "#6b7280"),
                                            };
                                            ot.Cell().Background(oBg).Padding(3).Text(oh.Origin).FontSize(8).FontColor(oColor).SemiBold();
                                            ot.Cell().Background(oBg).AlignCenter().Padding(3).Text(FmtSec(oh.Seconds)).FontSize(8).FontColor(oColor).SemiBold();
                                        }
                                    });
                                }
                            });
                        });

                        // ── Monthly History Chart ─────────────────────────────
                        if (monthHistory != null && monthHistory.Count > 0)
                        {
                            col.Item().PaddingTop(10);
                            col.Item().Text("Monthly History — PCs / Academy / BIS").SemiBold().FontSize(9).FontColor("#4338ca");
                            col.Item().PaddingTop(4);
                            col.Item().Height(160).Svg(size =>
                            {
                                using var stream = new MemoryStream();
                                using (var skCanvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
                                    DrawMonthlyChart(skCanvas, new SKSize(size.Width, size.Height), monthHistory, monthStart ?? default);
                                stream.Position = 0;
                                return new StreamReader(stream).ReadToEnd();
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    static void DrawAcademyChart(SKCanvas canvas, SKSize size, IList<WeekVisitCount> data, DateOnly currentWeekStart)
    {
        if (data.Count == 0) return;

        float W = size.Width, H = size.Height;
        const float leftM = 28f, rightM = 6f, topM = 18f, bottomM = 26f;
        float cX = leftM, cY = topM, cW = W - leftM - rightM, cH = H - topM - bottomM;

        using var bgPaint   = new SKPaint { Color = new SKColor(248, 250, 252), Style = SKPaintStyle.Fill };
        using var gridPaint = new SKPaint { Color = new SKColor(218, 222, 232), Style = SKPaintStyle.Stroke, StrokeWidth = 0.4f, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0) };
        using var axisPaint = new SKPaint { Color = new SKColor(160, 165, 175), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f };
        using var hlPaint   = new SKPaint { Color = new SKColor(5, 150, 105, 30), Style = SKPaintStyle.Fill };

        // Referral colors matching the UI chart
        using var donPaint    = new SKPaint { Color = SKColor.Parse("#16a34a"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var friendPaint = new SKPaint { Color = SKColor.Parse("#f59e0b"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var socialPaint = new SKPaint { Color = SKColor.Parse("#3b82f6"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var haifaPaint  = new SKPaint { Color = SKColor.Parse("#7c3aed"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var otherPaint  = new SKPaint { Color = SKColor.Parse("#94a3b8"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var yPaint      = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 7f, IsAntialias = true };
        using var xPaint      = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 6.5f, IsAntialias = true };

        canvas.DrawRect(cX, cY, cW, cH, bgPaint);

        // Max stacked value
        int maxVal = 1;
        foreach (var w in data)
            maxVal = Math.Max(maxVal, w.TotalVisits);

        const int gridLines = 4;
        int step = (int)Math.Ceiling((float)maxVal / gridLines);
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

        int n = data.Count;
        float groupW = cW / n;
        float barW = groupW * 0.7f;
        float gapW = (groupW - barW) / 2f;
        float baseY = cY + cH;

        // Find current week label for highlighting
        string curLabel = currentWeekStart.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);

        for (int i = 0; i < n; i++)
        {
            var w = data[i];
            float gx = cX + i * groupW;

            if (w.WeekLabel == curLabel)
                canvas.DrawRect(gx, cY, groupW, cH, hlPaint);

            float bx = gx + gapW;
            float cumH = 0;

            void DrawStack(int val, SKPaint p)
            {
                if (val <= 0) return;
                float segH = cH * val / roundedMax;
                canvas.DrawRect(SKRect.Create(bx, baseY - cumH - segH, barW, segH), p);
                cumH += segH;
            }

            DrawStack(w.DonCount, donPaint);
            DrawStack(w.FriendCount, friendPaint);
            DrawStack(w.SocialCount, socialPaint);
            DrawStack(w.HaifaCount, haifaPaint);
            DrawStack(w.OtherCount, otherPaint);

            // X label
            float lx = gx + groupW / 2f;
            canvas.Save();
            canvas.Translate(lx, baseY + 4f);
            canvas.RotateDegrees(-50f);
            canvas.DrawText(w.WeekLabel, 0, 0, xPaint);
            canvas.Restore();
        }

        // Legend
        float legY = cY + 11f;
        DrawLegendItem(canvas, cX + 4f,   legY, SKColor.Parse("#16a34a"), "Don",     7f);
        DrawLegendItem(canvas, cX + 38f,  legY, SKColor.Parse("#f59e0b"), "Friend",  7f);
        DrawLegendItem(canvas, cX + 82f,  legY, SKColor.Parse("#3b82f6"), "Social",  7f);
        DrawLegendItem(canvas, cX + 122f, legY, SKColor.Parse("#7c3aed"), "Haifa",   7f);
        DrawLegendItem(canvas, cX + 162f, legY, SKColor.Parse("#94a3b8"), "Other",   7f);
    }

    static void DrawAcademyMonthlyChart(SKCanvas canvas, SKSize size,
        IList<(string Label, int Don, int Friend, int Social, int Haifa, int Other, int Total)> data)
    {
        if (data.Count == 0) return;

        float W = size.Width, H = size.Height;
        const float leftM = 28f, rightM = 6f, topM = 18f, bottomM = 26f;
        float cX = leftM, cY = topM, cW = W - leftM - rightM, cH = H - topM - bottomM;

        using var bgPaint   = new SKPaint { Color = new SKColor(248, 250, 252), Style = SKPaintStyle.Fill };
        using var gridPaint = new SKPaint { Color = new SKColor(218, 222, 232), Style = SKPaintStyle.Stroke, StrokeWidth = 0.4f, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0) };
        using var axisPaint = new SKPaint { Color = new SKColor(160, 165, 175), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f };

        using var donPaint    = new SKPaint { Color = SKColor.Parse("#16a34a"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var friendPaint = new SKPaint { Color = SKColor.Parse("#f59e0b"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var socialPaint = new SKPaint { Color = SKColor.Parse("#3b82f6"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var haifaPaint  = new SKPaint { Color = SKColor.Parse("#7c3aed"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var otherPaint  = new SKPaint { Color = SKColor.Parse("#94a3b8"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var yPaint      = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 7f, IsAntialias = true };
        using var xPaint      = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 7f, IsAntialias = true };

        canvas.DrawRect(cX, cY, cW, cH, bgPaint);

        int maxVal = 1;
        foreach (var m in data) maxVal = Math.Max(maxVal, m.Total);

        const int gridLines = 4;
        int step = (int)Math.Ceiling((float)maxVal / gridLines);
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

        int n = data.Count;
        float groupW = cW / n;
        float barW = groupW * 0.7f;
        float gapW = (groupW - barW) / 2f;
        float baseY = cY + cH;

        for (int i = 0; i < n; i++)
        {
            var m = data[i];
            float gx = cX + i * groupW;
            float bx = gx + gapW;
            float cumH = 0;

            void DrawStack(int val, SKPaint p)
            {
                if (val <= 0) return;
                float segH = cH * val / roundedMax;
                canvas.DrawRect(SKRect.Create(bx, baseY - cumH - segH, barW, segH), p);
                cumH += segH;
            }

            DrawStack(m.Don, donPaint);
            DrawStack(m.Friend, friendPaint);
            DrawStack(m.Social, socialPaint);
            DrawStack(m.Haifa, haifaPaint);
            DrawStack(m.Other, otherPaint);

            float lx = gx + groupW / 2f;
            float tw = xPaint.MeasureText(m.Label);
            canvas.DrawText(m.Label, lx - tw / 2f, baseY + 12f, xPaint);
        }

        float legY = cY + 11f;
        DrawLegendItem(canvas, cX + 4f,   legY, SKColor.Parse("#16a34a"), "Don",     7f);
        DrawLegendItem(canvas, cX + 38f,  legY, SKColor.Parse("#f59e0b"), "Friend",  7f);
        DrawLegendItem(canvas, cX + 82f,  legY, SKColor.Parse("#3b82f6"), "Social",  7f);
        DrawLegendItem(canvas, cX + 122f, legY, SKColor.Parse("#7c3aed"), "Haifa",   7f);
        DrawLegendItem(canvas, cX + 162f, legY, SKColor.Parse("#94a3b8"), "Other",   7f);
    }

    static void DrawMonthlyChart(SKCanvas canvas, SKSize size, IList<MonthStatSummary> history, DateOnly currentMonthStart)
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
        using var hlPaint    = new SKPaint { Color = new SKColor(67, 56, 202, 30), Style = SKPaintStyle.Fill };
        using var auditPaint = new SKPaint { Color = SKColor.Parse("#4338ca"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var pcPaint    = new SKPaint { Color = SKColor.Parse("#3b82f6"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var acPaint    = new SKPaint { Color = SKColor.Parse("#16a34a"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var bpPaint    = new SKPaint { Color = SKColor.Parse("#ea580c"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var yPaint     = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 7f,   IsAntialias = true };
        using var xPaint     = new SKPaint { Color = new SKColor(90, 95, 110), TextSize = 6.5f, IsAntialias = true };

        canvas.DrawRect(cX, cY, cW, cH, bgPaint);

        float maxF = 1f;
        foreach (var m in history)
            maxF = Math.Max(maxF, Math.Max(
                (float)(m.TotalAuditCsSec / 3600.0),
                Math.Max(m.PcCount, Math.Max(m.AcademyCount, m.BodyInShop))));

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
        const float minH = 2f;

        for (int i = 0; i < n; i++)
        {
            var   m  = history[i];
            float gx = cX + i * groupW;

            if (m.MonthStart == currentMonthStart)
                canvas.DrawRect(gx, cY, groupW, cH, hlPaint);

            float bx = gx + gapW;

            void DrawBar(float x, float val, SKPaint p)
            {
                float bh = val > 0 ? cH * val / roundedMax : minH;
                canvas.DrawRoundRect(SKRect.Create(x, base_ - bh, barW, bh), 1.2f, 1.2f, p);
            }

            DrawBar(bx,            (float)(m.TotalAuditCsSec / 3600.0), auditPaint);
            DrawBar(bx + barW,     m.PcCount,                           pcPaint);
            DrawBar(bx + barW * 2f, m.AcademyCount,                    acPaint);
            DrawBar(bx + barW * 3f, m.BodyInShop,                      bpPaint);

            string xl = m.MonthStart.ToString("MMM yy", System.Globalization.CultureInfo.InvariantCulture);
            float  lx = gx + groupW / 2f;
            canvas.Save();
            canvas.Translate(lx, base_ + 4f);
            canvas.RotateDegrees(-50f);
            canvas.DrawText(xl, 0, 0, xPaint);
            canvas.Restore();
        }

        // Legend
        float legY = cY + 11f;
        DrawLegendItem(canvas, cX + 4f,   legY, SKColor.Parse("#4338ca"), "Aud+CS (h)", 7f);
        DrawLegendItem(canvas, cX + 66f,  legY, SKColor.Parse("#3b82f6"), "PCs",        7f);
        DrawLegendItem(canvas, cX + 100f, legY, SKColor.Parse("#16a34a"), "Academy",    7f);
        DrawLegendItem(canvas, cX + 153f, legY, SKColor.Parse("#ea580c"), "BIS",        7f);
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
        DrawLegendItem(canvas, cX + 153f, legY, SKColor.Parse("#ea580c"), "BIS",        7f);
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

    static string ReferralPdfBg(string referral, int rowNum) => referral switch
    {
        "Friend"          => rowNum % 2 == 0 ? "#fef9e7" : "#fef3c7",
        "Social Network"  => rowNum % 2 == 0 ? "#eff8ff" : "#dbeafe",
        "Haifa"           => rowNum % 2 == 0 ? "#f5f3ff" : "#ede9fe",
        "Other"           => rowNum % 2 == 0 ? "#f9fafb" : "#f3f4f6",
        _                 => rowNum % 2 == 0 ? "#f9fbe7" : Colors.White,  // Don / default
    };

    // ── HTML → QuestPDF rich text rendering ──────────────────────────

    // ── Auditor Report Form PDF ──

    public record ArfRowData(bool Checked, string Process, string Time, string ToneArm, string Results);

    public class ArfData
    {
        public string PcName        { get; set; } = "";
        public string Date          { get; set; } = "";
        public string Grade         { get; set; } = "";
        public string SessionLength { get; set; } = "";
        public string AdminTime     { get; set; } = "";
        public string TotalTa       { get; set; } = "";
        public string TaRange       { get; set; } = "";
        public List<ArfRowData> Rows { get; set; } = [];
    }

    public byte[] GenerateArfPdf(
        string pcName, string date, string grade, string sessionLength,
        string adminTime, string totalTa, string taRange,
        List<ArfRowData> rows, string? summaryHtml)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#1a1a1a"));

                page.Content().Column(col =>
                {
                    // Title
                    col.Item().AlignCenter().PaddingBottom(14)
                        .Text("Auditor Report Form").FontSize(18).Bold().FontColor("#1a1a1a");

                    // Row 1: PC's Name + Date
                    col.Item().PaddingBottom(6).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("PC's Name:  ").FontSize(10).FontColor("#666");
                            t.Span(pcName).FontSize(12).Bold().FontColor("#000");
                        });
                        row.ConstantItem(200).Text(t =>
                        {
                            t.Span("Date:  ").FontSize(10).FontColor("#666");
                            t.Span(date).FontSize(12).Bold().FontColor("#000");
                        });
                    });

                    // Row 2: PC's Grade + Session Length
                    col.Item().PaddingBottom(6).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("PC's Grade:  ").FontSize(10).FontColor("#666");
                            t.Span(grade).FontSize(12).Bold().FontColor("#000");
                        });
                        row.ConstantItem(200).Text(t =>
                        {
                            t.Span("Session Length:  ").FontSize(10).FontColor("#666");
                            t.Span(sessionLength).FontSize(12).Bold().FontColor("#000");
                        });
                    });

                    // Row 3: Admin Time + Total TA
                    col.Item().PaddingBottom(12).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Admin Time:  ").FontSize(10).FontColor("#666");
                            t.Span(adminTime).FontSize(12).Bold().FontColor("#000");
                        });
                        row.ConstantItem(200).Text(t =>
                        {
                            t.Span("Total TA:  ").FontSize(10).FontColor("#666");
                            t.Span(totalTa).FontSize(12).Bold().FontColor("#000");
                        });
                    });

                    // Table header
                    col.Item().Background("#2c3e50").Border(0.5f).BorderColor("#2c3e50").Row(row =>
                    {
                        row.RelativeItem(4).Padding(6).Text("Process").FontSize(10).Bold().FontColor("#fff");
                        row.ConstantItem(65).Padding(6).AlignCenter().Text("Time").FontSize(10).Bold().FontColor("#fff");
                        row.ConstantItem(75).Padding(6).AlignCenter().Text("Tone Arm\nReads").FontSize(9).Bold().FontColor("#fff");
                        row.RelativeItem(4).Padding(6).Text("Results and Comments").FontSize(10).Bold().FontColor("#fff");
                    });

                    // Table rows
                    bool alt = false;
                    foreach (var r in rows)
                    {
                        var rowBg = alt ? "#f4f6f8" : "#ffffff";
                        alt = !alt;
                        col.Item().Background(rowBg).BorderBottom(0.5f).BorderColor("#ddd").Row(row =>
                        {
                            row.RelativeItem(4).Padding(6).Text(r.Process).FontSize(11).FontColor("#1a1a1a");
                            row.ConstantItem(65).Padding(6).AlignCenter().Text(r.Time).FontSize(11).FontColor("#1a1a1a");
                            row.ConstantItem(75).Padding(6).AlignCenter().Text(r.ToneArm).FontSize(11).FontColor("#1a1a1a");
                            row.RelativeItem(4).Padding(6).Text(r.Results).FontSize(11).FontColor("#1a1a1a");
                        });
                    }

                    // TA Range
                    col.Item().PaddingTop(12).Text(t =>
                    {
                        t.Span("TA Range:  ").FontSize(10).FontColor("#666");
                        t.Span(taRange).FontSize(12).Bold().FontColor("#000");
                    });

                    // Summary
                    if (!string.IsNullOrWhiteSpace(summaryHtml))
                    {
                        col.Item().PaddingTop(16).PaddingBottom(4)
                            .Text("Summary").FontSize(12).Bold().FontColor("#1a1a1a");
                        col.Item().Background("#f8f9fa").Border(0.5f).BorderColor("#ddd")
                            .Padding(10).Column(sumCol =>
                        {
                            RenderHtmlBlock(sumCol, summaryHtml);
                        });
                    }
                });
            });
        }).GeneratePdf();
    }

    // ── Session Summaries PDF (prepended to Folder Summary) ──

    static string SecsToHMM(int secs) => secs <= 0 ? "" : $"{secs / 3600}:{(secs % 3600) / 60:D2}";

    static string FormatDateDDMMYY(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var dt))
            return dt.ToString("dd-MM-yy");
        return dateStr;
    }

    public byte[] GenerateSessionSummariesPdf(string pcName,
        List<DashboardService.SessionSummaryInfo> summaries, int originalPageCount)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Two-pass: first generate to count summary pages, then regenerate with correct total
        int summaryPageCount = 1;
        int totalPages = summaryPageCount + originalPageCount;

        // Pass 1: render to count pages
        var pass1 = BuildFolderSummaryDoc(pcName, summaries, 999).GeneratePdf();
        using (var ms1 = new System.IO.MemoryStream(pass1))
        using (var doc1 = PdfSharpCore.Pdf.IO.PdfReader.Open(ms1, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            summaryPageCount = doc1.PageCount;
        }
        totalPages = summaryPageCount + originalPageCount;

        // Pass 2: render with correct total
        return BuildFolderSummaryDoc(pcName, summaries, totalPages).GeneratePdf();
    }

    // Estimate entry height for page layout
    private static float EstimateEntryHeight(DashboardService.SessionSummaryInfo s)
    {
        float h = 18; // date line
        if (s.LengthSeconds > 0) h += 16;
        if (s.AdminSeconds > 0) h += 16;
        if (!string.IsNullOrWhiteSpace(s.Name)) h += 16;
        if (!string.IsNullOrWhiteSpace(s.SummaryHtml))
        {
            var text = Regex.Replace(s.SummaryHtml!, "<[^>]+>", "");
            var lines = Math.Max(1, (int)Math.Ceiling(text.Length / 40.0));
            h += lines * 15;
        }
        return h + 12; // padding
    }

    private const float PageUsableHeight = 700f; // A4 minus margins and header

    // Pre-compute which entries go on which page and column
    private static List<(List<DashboardService.SessionSummaryInfo> left, List<DashboardService.SessionSummaryInfo> right)>
        PackIntoPages(List<DashboardService.SessionSummaryInfo> summaries)
    {
        var pages = new List<(List<DashboardService.SessionSummaryInfo>, List<DashboardService.SessionSummaryInfo>)>();
        var curLeft = new List<DashboardService.SessionSummaryInfo>();
        var curRight = new List<DashboardService.SessionSummaryInfo>();
        float leftH = 0, rightH = 0;
        bool fillingLeft = true;

        foreach (var s in summaries)
        {
            float h = EstimateEntryHeight(s);
            if (fillingLeft)
            {
                if (leftH + h <= PageUsableHeight)
                {
                    curLeft.Add(s); leftH += h;
                }
                else
                {
                    fillingLeft = false;
                    curRight.Add(s); rightH = h;
                }
            }
            else
            {
                if (rightH + h <= PageUsableHeight)
                {
                    curRight.Add(s); rightH += h;
                }
                else
                {
                    pages.Add((curLeft, curRight));
                    curLeft = new List<DashboardService.SessionSummaryInfo> { s };
                    curRight = new List<DashboardService.SessionSummaryInfo>();
                    leftH = h; rightH = 0; fillingLeft = true;
                }
            }
        }
        if (curLeft.Count > 0 || curRight.Count > 0)
            pages.Add((curLeft, curRight));
        if (pages.Count == 0)
            pages.Add((new(), new()));
        return pages;
    }

    private Document BuildFolderSummaryDoc(string pcName,
        List<DashboardService.SessionSummaryInfo> summaries, int totalPages)
    {
        var pages = PackIntoPages(summaries);
        int summaryPageCount = pages.Count;

        return Document.Create(container =>
        {
            for (int p = 0; p < pages.Count; p++)
            {
                var pageIdx = p;
                // Backward page number: first summary page = totalPages, last = totalPages - summaryPageCount + 1
                var displayPageNum = totalPages - pageIdx;
                var (left, right) = pages[pageIdx];
                int maxRows = Math.Max(left.Count, right.Count);

                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor("#1a1a1a"));

                    page.Header().Column(hdr =>
                    {
                        hdr.Item().AlignCenter().PaddingBottom(8)
                            .Text("FOLDER SUMMARY").FontSize(24).Bold();
                        hdr.Item().PaddingBottom(8).Row(row =>
                        {
                            row.RelativeItem().Text(t =>
                            {
                                t.Span("PC Name: ").FontSize(16).FontColor("#333");
                                t.Span(pcName).FontSize(16).Bold();
                            });
                            row.ConstantItem(140).AlignRight().Text(t =>
                            {
                                t.Span("Page: ").FontSize(16).Bold();
                                t.Span($"{displayPageNum}").FontSize(16).Bold();
                                t.Span($" / {totalPages}").FontSize(16).Bold();
                            });
                        });
                        // Column headers with full border
                        hdr.Item().BorderTop(1).BorderLeft(1).BorderRight(1).BorderColor("#000").Row(row =>
                        {
                            row.ConstantItem(65).Padding(4).AlignCenter().AlignMiddle().Text("Date & Time").FontSize(11).Bold().FontColor("#000");
                            row.RelativeItem(4).BorderLeft(1).BorderColor("#000").Padding(4).AlignCenter().AlignMiddle()
                                .Text("What & Results").FontSize(11).Bold().FontColor("#000");
                            row.ConstantItem(3).Background("#000");
                            row.ConstantItem(65).Padding(4).AlignCenter().AlignMiddle().Text("Date & Time").FontSize(11).Bold().FontColor("#000");
                            row.RelativeItem(4).BorderLeft(1).BorderColor("#000").Padding(4).AlignCenter().AlignMiddle()
                                .Text("What & Results").FontSize(11).Bold().FontColor("#000");
                        });
                    });

                    page.Content().Border(1).BorderColor("#000").Column(tableCol =>
                    {
                        for (int i = 0; i < maxRows; i++)
                        {
                            var rowIdx = i;
                            tableCol.Item().BorderBottom(1).BorderColor("#000").MinHeight(30).Row(row =>
                            {
                                // Left: Date & Time
                                if (rowIdx < left.Count)
                                {
                                    var entry = left[rowIdx];
                                    row.ConstantItem(65).Padding(4).Column(dtCol =>
                                    {
                                        dtCol.Item().Text(FormatDateDDMMYY(entry.SessionDate)).FontSize(11).Bold();
                                        if (entry.LengthSeconds > 0)
                                            dtCol.Item().Text($"S: {SecsToHMM(entry.LengthSeconds)}").FontSize(11).FontColor("#333");
                                        if (entry.AdminSeconds > 0)
                                            dtCol.Item().Text($"A: {SecsToHMM(entry.AdminSeconds)}").FontSize(11).FontColor("#333");
                                    });

                                    // Left: What & Results
                                    row.RelativeItem(4).BorderLeft(1).BorderColor("#000")
                                        .Padding(4).Column(sumCol =>
                                    {
                                        if (!string.IsNullOrWhiteSpace(entry.SummaryHtml))
                                            RenderHtmlBlock(sumCol, entry.SummaryHtml!);
                                    });
                                }
                                else
                                {
                                    row.ConstantItem(65);
                                    row.RelativeItem(4).BorderLeft(1).BorderColor("#000");
                                }

                                // Center divider
                                row.ConstantItem(3).Background("#000");

                                // Right: Date & Time
                                if (rowIdx < right.Count)
                                {
                                    var entry = right[rowIdx];
                                    row.ConstantItem(65).Padding(4).Column(dtCol =>
                                    {
                                        dtCol.Item().Text(FormatDateDDMMYY(entry.SessionDate)).FontSize(11).Bold();
                                        if (entry.LengthSeconds > 0)
                                            dtCol.Item().Text($"S: {SecsToHMM(entry.LengthSeconds)}").FontSize(11).FontColor("#333");
                                        if (entry.AdminSeconds > 0)
                                            dtCol.Item().Text($"A: {SecsToHMM(entry.AdminSeconds)}").FontSize(11).FontColor("#333");
                                    });

                                    // Right: What & Results
                                    row.RelativeItem(4).BorderLeft(1).BorderColor("#000")
                                        .Padding(4).Column(sumCol =>
                                    {
                                        if (!string.IsNullOrWhiteSpace(entry.SummaryHtml))
                                            RenderHtmlBlock(sumCol, entry.SummaryHtml!);
                                    });
                                }
                                else
                                {
                                    row.ConstantItem(65);
                                    row.RelativeItem(4).BorderLeft(1).BorderColor("#000");
                                }
                            });
                        }
                        // Fill remaining page height with vertical lines
                        tableCol.Item().Extend().Row(row =>
                        {
                            row.ConstantItem(65);
                            row.RelativeItem(4).BorderLeft(1).BorderColor("#000");
                            row.ConstantItem(3).Background("#000");
                            row.ConstantItem(65);
                            row.RelativeItem(4).BorderLeft(1).BorderColor("#000");
                        });
                    }); // end tableCol / page.Content
                }); // end container.Page
            } // end pages loop
        });
    }

    public byte[] CombinePdfs(byte[] first, byte[] second)
    {
        using var output = new PdfSharpCore.Pdf.PdfDocument();

        void AddPages(byte[] src)
        {
            using var ms = new System.IO.MemoryStream(src);
            using var input = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
            for (int i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        AddPages(first);
        AddPages(second);

        using var result = new System.IO.MemoryStream();
        output.Save(result);
        return result.ToArray();
    }

    public int CountPdfPages(byte[] pdfBytes)
    {
        using var ms = new System.IO.MemoryStream(pdfBytes);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static void ExtractInlineStyles(string tag, ref string? color, ref string? bgColor, ref float fontSize)
    {
        var styleMatch = Regex.Match(tag, @"style\s*=\s*""([^""]*)""");
        if (!styleMatch.Success) return;
        var style = styleMatch.Groups[1].Value;
        var colorMatch = Regex.Match(style, @"(?<![a-z-])color\s*:\s*([^;""]+)");
        if (colorMatch.Success) color = colorMatch.Groups[1].Value.Trim();
        var bgMatch = Regex.Match(style, @"background-color\s*:\s*([^;""]+)");
        if (bgMatch.Success) bgColor = bgMatch.Groups[1].Value.Trim();
        var sizeMatch = Regex.Match(style, @"font-size\s*:\s*(\d+)");
        if (sizeMatch.Success) fontSize = float.Parse(sizeMatch.Groups[1].Value);
    }

    private static void RenderHtmlBlock(ColumnDescriptor col, string html)
    {
        Console.WriteLine($"[RenderHtmlBlock] Input HTML: {html}");
        // Split into paragraphs by <p>, <div>, <br>, <li> boundaries
        // Quill output: <p>text</p>, <p><br></p> for empty lines, <p class="ql-direction-rtl">...</p>
        var blocks = Regex.Split(html, @"(?=<p[\s>])|(?=<li[\s>])|(?=<div[\s>])");

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;

            // Detect RTL: explicit Quill class/attribute, or auto-detect Hebrew/Arabic characters
            var blockText = Regex.Replace(block, "<[^>]+>", "");
            blockText = System.Net.WebUtility.HtmlDecode(blockText);
            bool isRtl = block.Contains("ql-direction-rtl", StringComparison.OrdinalIgnoreCase)
                      || Regex.IsMatch(block, @"dir\s*=\s*[""']rtl[""']", RegexOptions.IgnoreCase)
                      || Regex.IsMatch(blockText, @"[\u0590-\u05FF\u0600-\u06FF\uFB50-\uFDFF\uFE70-\uFEFF]");

            // Check for empty paragraph (<p><br></p>)
            var innerHtml = Regex.Replace(block, @"^<[^>]+>|</[^>]+>$", "").Trim();
            if (string.IsNullOrEmpty(innerHtml) || innerHtml == "<br>" || innerHtml == "<br/>")
            {
                col.Item().PaddingTop(4);
                continue;
            }

            var item = col.Item().PaddingHorizontal(4).PaddingVertical(1);
            if (isRtl)
                item = item.AlignRight();

            item.Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(10));

                // Parse inline elements: <strong>, <em>, <u>, <s>, <span style="...">, plain text
                var parts = Regex.Split(innerHtml, @"(<(?:strong|em|u|s|span|br)\b[^>]*>|</(?:strong|em|u|s|span)>)");
                Console.WriteLine($"  [RenderHtml] innerHtml: {innerHtml}");
                for (int pi = 0; pi < parts.Length; pi++)
                    if (!string.IsNullOrEmpty(parts[pi]))
                        Console.WriteLine($"    part[{pi}]: [{parts[pi]}]");

                bool bold = false, italic = false, underline = false, strike = false;
                string? color = null;
                string? bgColor = null;
                float fontSize = 10f;

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    // Check for opening tags (may have style attributes)
                    if (part.StartsWith("<strong") || part.StartsWith("<b>") || part.StartsWith("<b "))
                    { bold = true; ExtractInlineStyles(part, ref color, ref bgColor, ref fontSize); continue; }
                    if (part == "</strong>" || part == "</b>") { bold = false; continue; }
                    if (part.StartsWith("<em") || part.StartsWith("<i>") || part.StartsWith("<i "))
                    { italic = true; ExtractInlineStyles(part, ref color, ref bgColor, ref fontSize); continue; }
                    if (part == "</em>" || part == "</i>") { italic = false; continue; }
                    if (part.StartsWith("<u")) { underline = true; ExtractInlineStyles(part, ref color, ref bgColor, ref fontSize); continue; }
                    if (part == "</u>") { underline = false; continue; }
                    if (part.StartsWith("<s>") || part.StartsWith("<s ")) { strike = true; ExtractInlineStyles(part, ref color, ref bgColor, ref fontSize); continue; }
                    if (part == "</s>") { strike = false; continue; }
                    if (part.StartsWith("<br")) { text.Span("\n"); continue; }

                    if (part.StartsWith("<span"))
                    {
                        ExtractInlineStyles(part, ref color, ref bgColor, ref fontSize);
                        continue;
                    }
                    if (part == "</span>") { color = null; bgColor = null; fontSize = 10f; continue; }

                    // Skip other tags
                    if (part.StartsWith("<")) continue;

                    // Render text span with accumulated styles
                    var decoded = System.Net.WebUtility.HtmlDecode(part);
                    var span = text.Span(decoded).FontSize(fontSize);
                    if (bold) span = span.SemiBold();
                    if (italic) span = span.Italic();
                    if (underline) span = span.Underline();
                    if (strike) span = span.Strikethrough();
                    if (color != null) span = span.FontColor(ParseColor(color));
                    if (bgColor != null) span = span.BackgroundColor(ParseColor(bgColor));
                }
            });
        }
    }

    private static string ParseColor(string cssColor)
    {
        cssColor = cssColor.Trim().TrimEnd(';').Trim();
        if (cssColor.StartsWith("#")) return cssColor;
        // Handle rgb(r,g,b) and rgba(r,g,b,a)
        var rgbMatch = Regex.Match(cssColor, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
        if (rgbMatch.Success)
        {
            int r = int.Parse(rgbMatch.Groups[1].Value);
            int g = int.Parse(rgbMatch.Groups[2].Value);
            int b = int.Parse(rgbMatch.Groups[3].Value);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        // Named colors fallback
        return cssColor.ToLowerInvariant() switch
        {
            "red" => "#FF0000", "blue" => "#0000FF", "green" => "#008000",
            "orange" => "#FF8C00", "purple" => "#800080", "magenta" => "#FF00FF",
            "yellow" => "#FFFF00", "white" => "#FFFFFF", "black" => "#000000",
            "gray" or "grey" => "#808080",
            _ => "#000000"
        };
    }
}

