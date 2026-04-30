// Generates 50 ARF PDFs using the EXACT same code path as the live app
// (LPM.PdfService.GenerateArfPdf). Each PDF exercises a different mix of
// Hebrew / English / digits / punctuation / hyphens / wraps so the BiDi
// fixes can be visually verified end-to-end.
//
// Usage:
//   dotnet run --project tools/GenArfTestPdfs -- "C:\path\to\out"
// or just:
//   dotnet run --project tools/GenArfTestPdfs
// (defaults to <cwd>/out/arf-test-pdfs/)

using LPM.Services;

namespace GenArfTestPdfs;

public static class Program
{
    public static int Main(string[] args)
    {
        // Same one-time font-resolver setup that Program.cs does in the live app —
        // without it, PdfSharpCore can't find Noto Sans Hebrew or DejaVu Sans.
        PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = EmbeddedFontResolver.Instance;

        var outDir = args.Length > 0
            ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "out", "arf-test-pdfs");
        Directory.CreateDirectory(outDir);

        var svc = new PdfService();
        var cases = BuildCases();

        Console.WriteLine($"Output dir: {outDir}");
        Console.WriteLine($"Generating {cases.Count} ARF PDF(s)…");
        Console.WriteLine();

        int idx = 1;
        foreach (var c in cases)
        {
            byte[] bytes;
            try
            {
                bytes = svc.GenerateArfPdf(
                    pcName:        c.PcName,
                    date:          c.Date,
                    grade:         c.Grade,
                    sessionLength: c.SessionLength,
                    adminTime:     c.AdminTime,
                    totalTa:       c.TotalTa,
                    taRange:       c.TaRange,
                    rowGroups:     c.RowGroups,
                    summaryHtml:   null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {idx,2}. FAILED — {c.Title}");
                Console.WriteLine($"      {ex.GetType().Name}: {ex.Message}");
                idx++;
                continue;
            }

            var safeTitle = SanitizeFileName(c.Title);
            var fileName  = $"{idx:D2}_{safeTitle}.pdf";
            var fullPath  = Path.Combine(outDir, fileName);
            File.WriteAllBytes(fullPath, bytes);
            Console.WriteLine($"  {idx,2}. {fileName}  ({bytes.Length / 1024.0:0.#} KB)");
            idx++;
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {cases.Count} files in: {outDir}");
        return 0;
    }

    static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '-');
    }

    // ── 50 test cases ────────────────────────────────────────────────────────
    static List<TestCase> BuildCases()
    {
        var rgRow = (bool ck, string proc, string time, string tone, string res)
            => new PdfService.ArfRowData(ck, proc, time, tone, res);

        // Helper: single-row group containing one ARF row.
        List<List<PdfService.ArfRowData>> One(PdfService.ArfRowData r) => new() { new() { r } };
        // Helper: single-row group containing many rows.
        List<List<PdfService.ArfRowData>> Many(params PdfService.ArfRowData[] rows) => new() { rows.ToList() };
        // Helper: multiple groups.
        List<List<PdfService.ArfRowData>> Groups(params List<PdfService.ArfRowData>[] gs) => gs.ToList();

        // Default tone-arm value reused across rows.
        const string TN = "1.5";

        var cases = new List<TestCase>
        {
            // ── 1–5: pure English baseline ──
            new("01-pure-english-simple", "John Doe", "01/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "Process check on the meter.", "12:34", TN,
                    "Result: clean reads, no flags."))),

            new("02-pure-english-multiline", "Jane Smith", "02/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false,
                    "First sentence about the process.\nSecond line continues.\nThird line wraps to another concept.",
                    "10:00", TN,
                    "Result line one.\nResult line two with more detail.\nResult line three."))),

            new("03-pure-english-long-wrap", "Bob Jones", "03/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false,
                    "This is a deliberately long English description that will need to wrap across multiple lines because it contains far more content than will fit in a single line of the Process column under normal margins.",
                    "11:11", TN,
                    "Long results paragraph that also wraps several times so we can verify wrap behavior on the Results column with pure ASCII content only."))),

            new("04-pure-english-with-hyphens", "Alex King", "04/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "LF-BD checked - status normal - no issues.",
                    "09:00", TN, "TA-100 stable; LR-flow OK; final-state = pass."))),

            new("05-pure-english-with-digits", "Carl Smith", "05/05/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                One(rgRow(false, "Score: 95/100 over 3 attempts (avg 91.6%).",
                    "08:00", TN, "Final: 95% on the 4th run, 88% on average."))),

            // ── 6–10: pure Hebrew ──
            new("06-pure-hebrew-simple", "כהן דוד", "01/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "בדיקה של התהליך.", "12:34", TN, "התוצאה: ניקוי, ללא דגלים."))),

            new("07-pure-hebrew-multiline", "לוי שרה", "02/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false,
                    "משפט ראשון על התהליך.\nשורה שנייה ממשיכה.\nשורה שלישית עוברת לרעיון נוסף.",
                    "10:00", TN,
                    "תוצאה שורה ראשונה.\nתוצאה שורה שנייה עם פירוט.\nתוצאה שורה שלישית."))),

            new("08-pure-hebrew-long-wrap", "אברהם יצחק", "03/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false,
                    "זהו תיאור עברי ארוך במכוון אשר יצטרך להיגלל על פני מספר שורות מפני שהוא מכיל הרבה יותר תוכן ממה שייכנס בשורה אחת של עמודת התהליך תחת השוליים הרגילים.",
                    "11:11", TN,
                    "פסקת תוצאות ארוכה שגם נגללת מספר פעמים כדי שנוכל לאמת התנהגות גלילה בעמודת התוצאות עם תוכן עברי בלבד."))),

            new("09-pure-hebrew-with-hyphens", "רוזנברג נח", "04/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "אישור-בדיקה - מצב תקין - אין תקלות.",
                    "09:00", TN, "מערכת-תקינה; זרימה-יציבה; מצב-סופי = עבר."))),

            new("10-pure-hebrew-final-letters", "פרידמן ערן", "05/05/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                One(rgRow(false, "שלום עולם – סוף ארוך עם ם ן ך ץ ף.",
                    "08:00", TN, "אמן ופרץ - סופיות במקומן הנכון."))),

            // ── 11–15: Hebrew + English mix in same cell ──
            new("11-hebrew-english-same-cell", "Smith כהן", "06/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "התלמיד עשה Solo טוב מאוד.", "12:34", TN,
                    "Pass — מעולה!"))),

            new("12-multi-english-words-in-rtl", "Cohen David", "07/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false, "החתימה היא Good טובה והתקדמה במהירות.",
                    "10:00", TN, "ניקוד: LF BD - הישג מצוין."))),

            new("13-rtl-with-multi-ltr-cluster", "Levi Sarah", "08/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false, "הישג - LF BD - סיים את כל השלבים.",
                    "11:11", TN, "מסקנה: TA OK ועברנו ל-Phase 2."))),

            new("14-ltr-base-with-hebrew-island", "Avraham Yitzhak", "09/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "Hello שלום עולם World - end of story.",
                    "09:00", TN, "Score 95% מעולה — congrats on the run!"))),

            new("15-alternating-script-words", "Rosenberg Noah", "10/05/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                One(rgRow(false, "שלום World עולם Reach is the goal.",
                    "08:00", TN, "Final - הישג - Excellent - מעולה - DONE"))),

            // ── 16–20: punctuation / colon / digits in mixed text ──
            new("16-colon-between-hebrew", "Friedman Eran", "11/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "זמן: שלום, מצב: עולם — בדיקה.",
                    "12:34", TN, "ניקוד: 95% מצוין!"))),

            new("17-digits-inside-rtl", "Cohen Avi", "12/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false, "התלמיד 95% השלים 3 שלבים.",
                    "10:00", TN, "תאריך: 5/4/2026 - הצלחה."))),

            new("18-leading-digits-rtl", "Mizrachi Yaakov", "13/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false, "5: שלום עולם - בדיקה.",
                    "11:11", TN, "123 שלום ועוד 456 דברים."))),

            new("19-punct-chains-rtl", "Goldberg Tamar", "14/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "שלום, עולם! זה מבחן? כן: בדיוק.",
                    "09:00", TN, "אחת:שתיים, שלוש—ארבע."))),

            new("20-ta-range-with-hebrew", "Levy Asaf", "15/05/2026", "XII", "1:10",
                "00:05", "01:05", "טווח TA 0-1 בעברית",
                One(rgRow(false, "Good בדיקה.", "08:00", TN, "Pass."))),

            // ── 21–25: Hyphens around mixed scripts ──
            new("21-hyphen-LF-BD-hisheg", "Cohen Rachel", "16/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "LF BD - הישג", "12:34", TN, "הישג - LF BD"))),

            new("22-mixed-hyphenated-rtl", "Saban Roni", "17/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false, "שלום-Hello עולם-World", "10:00", TN, "Hello-שלום World-עולם"))),

            new("23-tech-codes-in-rtl", "Yaron Liat", "18/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false, "קוצב TA-100 - לוקח 5 דקות.",
                    "11:11", TN, "מודל ABC-123 עם הישג HF-7."))),

            new("24-dashes-everywhere", "Peretz Maya", "19/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "שלב-ראשון - שלב-שני - תוצאה-סופית.",
                    "09:00", TN, "Pre-test --> Main-test --> Post-test"))),

            new("25-em-dash-mixed", "Stern Joseph", "20/05/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                One(rgRow(false, "Process — שלב חשוב — done.",
                    "08:00", TN, "תוצאה — Excellent — הישג."))),

            // ── 26–30: Multi-row groups ──
            new("26-multi-rows-hebrew", "Berger Daniel", "21/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                Many(
                    rgRow(false, "שורה ראשונה בעברית.", "12:30", TN, "תוצאה ראשונה."),
                    rgRow(true,  "שורה שנייה בעברית.", "12:35", TN, "תוצאה שנייה."),
                    rgRow(false, "שורה שלישית בעברית.", "12:40", TN, "תוצאה שלישית."))),

            new("27-multi-rows-mixed", "Adler Iris", "22/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                Many(
                    rgRow(false, "Row 1: pure English process check.", "10:00", TN, "OK."),
                    rgRow(true,  "שורה 2: בדיקה בעברית בלבד.", "10:15", TN, "מצוין."),
                    rgRow(false, "Row 3: שלום World mixed.", "10:30", TN, "Pass — הצליח."),
                    rgRow(true,  "Row 4: HF-7 - הישג - LF-BD.", "10:45", TN, "תוצאה - 95%"))),

            new("28-multi-rows-different-lengths", "Vidal Tom", "23/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                Many(
                    rgRow(false, "Short.", "11:00", TN, "OK."),
                    rgRow(false,
                        "Medium length English description with a few extra words.",
                        "11:15", TN, "Medium result."),
                    rgRow(false,
                        "תיאור עברי ארוך מאוד שגולל על פני מספר שורות וכולל מילים אנגליות כמו System ו-Module באמצע הטקסט העברי.",
                        "11:30", TN,
                        "תוצאה ארוכה: התלמיד השלים את כל המבחנים עם ציון של 95% ברוב השלבים והגיע למצב מצוין באופן כללי."))),

            new("29-multi-rows-same-length", "Eliyahu Hadar", "24/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                Many(
                    rgRow(true,  "Process A: שלב 1.", "09:00", TN, "Pass."),
                    rgRow(true,  "Process B: שלב 2.", "09:05", TN, "Pass."),
                    rgRow(false, "Process C: שלב 3.", "09:10", TN, "Fail."),
                    rgRow(true,  "Process D: שלב 4.", "09:15", TN, "Pass."),
                    rgRow(true,  "Process E: שלב 5.", "09:20", TN, "Pass."))),

            new("30-checkbox-variants", "Mor Ofer", "25/05/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                Many(
                    rgRow(true,  "Checkbox checked - this row is marked.", "08:00", TN, "✓ done."),
                    rgRow(false, "Checkbox unchecked - this row is open.", "08:05", TN, "open."),
                    rgRow(true,  "סימן ✓ עברית.", "08:10", TN, "סומן."),
                    rgRow(false, "ללא סימן.", "08:15", TN, "פתוח."))),

            // ── 31–35: Multi-group ARF (multiple ARF tables on one PDF) ──
            new("31-two-groups-mixed", "Israel Navon", "26/05/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                Groups(
                    new() {
                        rgRow(false, "ARF 1 / Process 1: Hebrew בדיקה.", "12:00", TN, "OK 1."),
                        rgRow(true,  "ARF 1 / Process 2: שלום World.", "12:15", TN, "Pass 1."),
                    },
                    new() {
                        rgRow(false, "ARF 2 / Process 1: עוד בדיקה.", "13:00", TN, "OK 2."),
                        rgRow(true,  "ARF 2 / Process 2: Hello עולם.", "13:15", TN, "Pass 2."),
                    })),

            new("32-three-groups", "Tal Yossi", "27/05/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                Groups(
                    new() { rgRow(true, "Group 1 row: שלב פתיחה.", "10:00", TN, "Open OK.") },
                    new() { rgRow(true, "Group 2 row: שלב אמצע.", "10:30", TN, "Mid OK.") },
                    new() { rgRow(false, "Group 3 row: שלב סיום.", "11:00", TN, "End OK.") })),

            new("33-groups-different-row-counts", "Avraham Yael", "28/05/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                Groups(
                    new() {
                        rgRow(false, "G1R1.", "10:00", TN, "."),
                        rgRow(false, "G1R2.", "10:05", TN, "."),
                    },
                    new() {
                        rgRow(false, "G2 only row with עברית and English.", "11:00", TN, "Pass."),
                    },
                    new() {
                        rgRow(true, "G3 row 1: זמן: שלום.", "12:00", TN, "1."),
                        rgRow(true, "G3 row 2: עולם World.", "12:05", TN, "2."),
                        rgRow(true, "G3 row 3: TA-100 הישג.", "12:10", TN, "3."),
                    })),

            new("34-groups-with-multiline-cells", "Zilber Edna", "29/05/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                Groups(
                    new() {
                        rgRow(false,
                            "First group, first row.\nMultiple lines.\nשלום עולם.",
                            "09:00", TN,
                            "First group result.\nשורה שנייה.\nLine 3 mixed זה אחד."),
                    },
                    new() {
                        rgRow(true,
                            "Second group: long text שמתפזר על שורות וגם בעברית מסביר את התהליך הקצר.",
                            "10:00", TN,
                            "Result: Pass.\nציון: 95.\nNote: TA-100 used."),
                    })),

            new("35-many-groups-stress", "Maor Ehud", "30/05/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                Groups(
                    new() { rgRow(false, "G1: שלום.", "08:00", TN, ".") },
                    new() { rgRow(false, "G2: עולם.", "08:05", TN, ".") },
                    new() { rgRow(false, "G3: Hello.", "08:10", TN, ".") },
                    new() { rgRow(false, "G4: World.", "08:15", TN, ".") },
                    new() { rgRow(false, "G5: שלום World.", "08:20", TN, ".") },
                    new() { rgRow(false, "G6: Hello עולם.", "08:25", TN, ".") })),

            // ── 36–40: Edge cases (empty, whitespace, special chars) ──
            new("36-empty-cells", "Test Empty", "01/06/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "", "12:34", TN, ""))),

            new("37-whitespace-only-cells", "Test WS", "02/06/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false, "   ", "10:00", TN, "  \n  "))),

            new("38-only-punctuation", "Test Punct", "03/06/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false, "...!?,;:-", "11:11", TN, "()[]{}"))),

            new("39-quotes-and-apostrophes", "Cohen O'Brian", "04/06/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "He said \"שלום\" to me — \"hello\".",
                    "09:00", TN, "עו\"ד דוד אמר 'שלום עולם'."))),

            new("40-curly-quotes-em-dash", "Bridge Pat", "05/06/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                One(rgRow(false, "“smart quotes” — שלום ‘curly’ עולם",
                    "08:00", TN, "אומר ‘שלום’ — said “world”"))),

            // ── 41–45: Long content / page-break stress ──
            new("41-many-rows-page-break", "Cohen Page", "06/06/2026", "VIII", "1:30",
                "00:15", "01:15", "TA range 0-1",
                Many(Enumerable.Range(1, 30).Select(n =>
                    rgRow(n % 2 == 0,
                        $"Row #{n}: שלום World - בדיקה מספר {n}.",
                        $"{8 + n / 60:D2}:{n % 60:D2}", TN,
                        $"תוצאה {n}: הצליח עם ציון {80 + n}%."))
                    .ToArray())),

            new("42-very-long-single-row", "LongRow Single", "07/06/2026", "IX", "2:00",
                "00:20", "01:40", "TA range 0-2",
                One(rgRow(false,
                    string.Join(" ", Enumerable.Repeat("מילה", 100)) + " " +
                    string.Join(" ", Enumerable.Repeat("word", 100)) + " " +
                    string.Join(" ", Enumerable.Repeat("Solo שלום", 50)),
                    "10:00", TN,
                    "תוצאה ארוכה מאוד: " +
                    string.Join(", ", Enumerable.Range(1, 50).Select(n => $"שלב {n}"))))),

            new("43-no-spaces-just-hyphens", "Tight Hyphen", "08/06/2026", "X", "1:15",
                "00:10", "01:05", "TA range 0-1",
                One(rgRow(false, "שלום-Hello-עולם-World-הישג",
                    "11:11", TN, "Solo-שלום-Hello-עולם-Bye"))),

            new("44-adjacent-no-space", "Tight Adj", "09/06/2026", "XI", "1:45",
                "00:15", "01:30", "TA range 0-1",
                One(rgRow(false, "שלוםHelloעולםWorld",
                    "09:00", TN, "Helloשלוםעולםworld"))),

            new("45-mixed-with-numbers-units", "Units Test", "10/06/2026", "XII", "1:10",
                "00:05", "01:05", "TA range 0-1",
                One(rgRow(false, "5kg של חומר ב-3 דקות (95%).",
                    "08:00", TN, "10m × 5cm = 50sqcm על-פי המידה."))),

            // ── 46–50: Final cell / TA / header field-level edges ──
            new("46-hebrew-pcname-english-rest", "כהן דוד הישראלי", "11/06/2026", "VIII",
                "1:30", "00:15", "01:15", "TA range 0-1",
                One(rgRow(false, "Process check.", "12:34", TN, "Result OK."))),

            new("47-english-pcname-hebrew-rest", "John Smith", "12/06/2026", "IX",
                "2:00", "00:20", "01:40", "טווח TA 0-2",
                One(rgRow(false, "בדיקת תהליך.", "10:00", TN, "תוצאה: עבר."))),

            new("48-mixed-pcname", "כהן Smith דוד", "13/06/2026", "X",
                "1:15", "00:10", "01:05", "TA 0-1 mixed",
                One(rgRow(false, "Mixed PC name above + שלום World here.",
                    "11:11", TN, "Pass — הצלחה."))),

            new("49-rtl-only-fields", "כהן דוד", "01-05-2026", "ח'", "שעה וחצי",
                "רבע שעה", "שעה ורבע", "טווח TA אפס עד אחד",
                One(rgRow(false, "תהליך פשוט.", "12:34", TN, "תוצאה תקינה."))),

            new("50-all-fields-stress", "כהן Smith - 4 דוד", "01/05/2026 - יום ב'",
                "ח' (8) - VIII", "שעה ו-30 - 1:30", "ר/ש - 00:15", "1h15m + תוספת",
                "TA 0-1 או טווח אפס",
                Many(
                    rgRow(false, "Stress all: שלום: 5 World - הישג.", "08:00", TN, "Pass: 95% מצוין."),
                    rgRow(true,  "Wrap: " + string.Join(" ", Enumerable.Repeat("מילה word", 30)),
                        "08:30", TN, "Result with — long em-dash and שלום עולם — done."),
                    rgRow(true,  "TA-100 קוצב לוקח 5 דקות בדיוק.",
                        "09:00", TN, "Final: TA-100 OK, score 95%, מעולה!"))),
        };

        return cases;
    }

    sealed class TestCase
    {
        public string Title { get; }
        public string PcName { get; }
        public string Date { get; }
        public string Grade { get; }
        public string SessionLength { get; }
        public string AdminTime { get; }
        public string TotalTa { get; }
        public string TaRange { get; }
        public List<List<PdfService.ArfRowData>> RowGroups { get; }

        public TestCase(string title, string pcName, string date, string grade,
            string sessionLength, string adminTime, string totalTa, string taRange,
            List<List<PdfService.ArfRowData>> rowGroups)
        {
            Title = title; PcName = pcName; Date = date; Grade = grade;
            SessionLength = sessionLength; AdminTime = adminTime;
            TotalTa = totalTa; TaRange = taRange; RowGroups = rowGroups;
        }
    }
}
