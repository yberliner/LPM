// LPM Memory Report Generator
// ─────────────────────────────────────────────────────────────────────────────
// Produces a single PDF (A3 landscape) listing every field stored in server RAM
// per Blazor circuit, sorted big → small by typical bytes. Each row shows:
//   • Razor / Service file the field lives in
//   • Field name + type
//   • Typical and worst-case byte estimates
//   • What it holds and why it can grow
//
// Edit the `rows` list below when the app changes (new pages, new heavy fields,
// retired ones). See README.md for estimation rules.

using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

Settings.License = LicenseType.Community;

// (Component, Field, Type, BytesTypical, BytesWorst, Notes)
var rows = new List<(string Comp, string Field, string Type, long Typ, long Worst, string Notes)>
{
    // ── PcFolder.razor — heaviest @page ────────────────────────────────────────
    ("PcFolder.razor", "_uploadPendingFiles",                        "List<(string, byte[], bool)>",                   5_000_000, 50_000_000, "Raw file bytes during upload-confirm dialog. 5 × 1 MB files = 5 MB; 10 × 5 MB = 50 MB. Released when user clicks Confirm or Cancel."),
    ("PcFolder.razor", "_pendingRecoverySaves",                      "List<PendingSaveSnap>",                          2_000_000, 20_000_000, "LocalStorage recovery snapshots from prior failed saves. Each snap holds a full pane payload (annotations + meta)."),
    ("PcFolder.razor", "_excelData",                                  "FolderService.ExcelSheetData?",                  100_000,    1_000_000,  "Open .xlsx contents (rows × cells) when an Excel file is loaded into a pane."),
    ("PcFolder.razor", "_editSummaryHtml",                            "string (Quill HTML)",                            50_000,     1_000_000,  "Folder/session summary HTML body. Embedded base64 images can balloon this."),
    ("PcFolder.razor", "_fsEntries",                                  "List<SessionSummaryEditInfo>",                   25_000,     100_000,    "Per-session summaries shown in FS edit modal."),
    ("PcFolder.razor", "_editSummaryArfJson",                         "string?",                                        10_000,     500_000,    "Quill delta/ops JSON. Big when summary is rich."),
    ("PcFolder.razor", "_excelEdits",                                 "Dictionary<(int Row, int Col), string>",         4_000,      100_000,    "User's pending Excel cell edits."),
    ("PcFolder.razor", "_nameToCreatedAt",                            "Dictionary<string, DateTime>",                   2_800,      50_000,     "Session-name → creation time."),
    ("PcFolder.razor", "_wsNavList",                                  "List<WorkSheetItem>",                            2_400,      50_000,     "Worksheet navigation list (one entry per worksheet/session)."),
    ("PcFolder.razor", "_nameToSessionId",                            "Dictionary<string, int>",                        2_400,      50_000,     "Session-name → session-id lookup."),
    ("PcFolder.razor", "_saveErrorPanes",                             "List<SaveErrorDetail>",                          1_500,      50_000,     "Save errors with full details for the retry dialog."),
    ("PcFolder.razor", "_expandedTreeFolders",                        "HashSet<string>",                                1_500,      5_000,      "Front/Back-cover tree folders that are expanded."),
    ("PcFolder.razor", "_collapsedAttachments",                       "HashSet<string>",                                1_200,      5_000,      "Section keys collapsed in the worksheet UI."),
    ("PcFolder.razor", "Strings (~30): _renameValue, _moveSelectedFolder, _deleteTargetPath, _excelRelPath, etc.", "string × ~30", 1_200,  12_000,     "Modal/dialog state strings (rename, move, delete, ctx menus, normalization)."),
    ("PcFolder.razor", "_formulaOverrides",                           "Dictionary<(int, int), string>",                 800,        50_000,     "Excel formula overrides per cell."),
    ("PcFolder.razor", "_normSteps",                                  "List<NormStep>",                                 700,        5_000,      "PDF normalization step list."),
    ("PcFolder.razor", "_programInsertFiles / _programRedirectFiles", "List<string> × 2",                               600,        3_000,      "Program file lists for insert / redirect dialogs."),
    ("PcFolder.razor", "_conversionToasts",                           "List<ConversionToast>",                          400,        5_000,      "Doc→PDF conversion toasts in flight."),
    ("PcFolder.razor", "_dotNetRef + _stopwatch + _stopwatchCts + _dupeCts", "DotNetObjectRef + Timer + 2× CTS",        300,        500,        "JS interop ref + cancel tokens + stopwatch (active when CSing)."),
    ("PcFolder.razor", "_csedSessionIds",                             "HashSet<int>",                                   200,        10_000,     "Sessions that already have a cs_review row."),
    ("PcFolder.razor", "_uploadDuplicateNames",                       "List<string>",                                   200,        1_000,      "Duplicate filenames in upload confirm."),
    ("PcFolder.razor", "_panes",                                      "PaneState[3]",                                   150,        600,        "3 fixed pane slots (file, name, section)."),
    ("PcFolder.razor", "_uploadRejectedFiles",                        "List<string>",                                   100,        2_000,      "Files rejected by validation."),
    ("PcFolder.razor", "_dualInfo",                                   "Dictionary<string, (int, int)>",                 100,        200,        "Per-pane dual-page state (current page#, total)."),
    ("PcFolder.razor", "_dualPanes",                                  "HashSet<string>",                                100,        200,        "Pane IDs currently in dual mode."),
    ("PcFolder.razor", "Integers (~20): _csSessionId, _activeIdx, _zoomPercent, etc.", "int × ~20",                     80,         80,         "Counters and IDs."),
    ("PcFolder.razor", "Booleans (~40): _showCtxMenu, _isRenaming, _showMoveModal, etc.", "bool × ~40",                 40,         40,         "UI flags (modals open, expanded, sorting). Trivial."),
    ("PcFolder.razor", "_dragNode",                                   "FolderTreeNode?",                                8,          500,        "Reference to currently-dragged folder node (transient)."),
    ("PcFolder.razor", "_questionInfo",                               "QuestionInfo?",                                  8,          500,        "Question status for the current session."),

    // ── Home.razor — main dashboard ────────────────────────────────────────────
    ("Home.razor",     "_cellDetails",                                "Dictionary<(int,int,string), List<CellSessionInfo>>", 100_000, 500_000, "Weekly grid cell drill-down details. Populated when day-detail modal opens."),
    ("Home.razor",     "_effortCellDetails",                          "Dictionary<(int,int), List<EffortCellEntry>>",   15_000,    100_000,    "Per-PC × week effort drill-down."),
    ("Home.razor",     "_teamCsQueues",                               "Dictionary<int, List<PendingCsSession>>",        12_000,    500_000,    "SeniorCS team queues. Per CS staff: list of pending sessions. Heaviest field for SeniorCS users."),
    ("Home.razor",     "_pendingCsSessions",                          "List<PendingCsSession>",                         8_000,     100_000,    "User's own pending CS queue."),
    ("Home.razor",     "_grid",                                       "WeekGrid (3 dictionaries)",                      5_600,     100_000,    "Per-PC × per-day × per-role aggregates for the weekly grid."),
    ("Home.razor",     "_completionAllPcs",                           "List<PcItem>",                                   4_000,     100_000,    "Full PC list for completion modal."),
    ("Home.razor",     "_audAttachTypes",                             "Dictionary<int, HashSet<string>>",               4_000,     100_000,    "Per-PC attachment-type sets."),
    ("Home.razor",     "_auditorStatuses",                            "List<AuditorSessionStatus>",                     4_000,     50_000,     "Auditor's session-status overview."),
    ("Home.razor",     "_sessionQuestions",                           "Dictionary<int, QuestionInfo>",                  3_500,     50_000,     "Per-session question status."),
    ("Home.razor",     "_pendingCs",                                  "PendingCsMarkers",                               3_000,     50_000,     "HashSets/Dicts of PC ids with pending CS markers."),
    ("Home.razor",     "_effortGrid",                                 "Dictionary<(int, int), int>",                    2_000,     50_000,     "Per-PC × week effort minutes."),
    ("Home.razor",     "_weeklyTotals",                               "List<WeekTotal>",                                2_000,     100_000,    "Historical weekly aggregates (chart data)."),
    ("Home.razor",     "_pcCsNames",                                  "Dictionary<int, string>",                        1_100,     10_000,     "Last CS name per PC."),
    ("Home.razor",     "_myPcs",                                      "List<PcInfo>",                                   800,       50_000,     "Approved PCs for the user."),
    ("Home.razor",     "Strings (~15): _addSummary, _editSummary, _weekRemarks, _addReviewNotes, etc.", "string × ~15", 600,       15_000,     "Modal/form input strings (summaries, notes, errors)."),
    ("Home.razor",     "_csStatusLabels",                             "Dictionary<string, string>",                     600,       5_000,      "lkp_cs_status labels (cached)."),
    ("Home.razor",     "_chartOptions",                               "ApexChartOptions<WeekTotal>",                    500,       1_000,      "Apex chart configuration."),
    ("Home.razor",     "Integers (~20): _userId, _csQueueDays, _selPcId, etc.", "int × ~20",                            80,        80,         "Counters and IDs."),
    ("Home.razor",     "Booleans (~25): _isAuditor, _showAddSession, _showCtxMenu, etc.", "bool × ~25",                 25,        25,         "UI flags. Trivial."),

    // ── MainHeader.razor — loaded on every page ────────────────────────────────
    ("MainHeader.razor", "_statCache",                                "Dictionary<int, (string Url, string Base64)>",   50_000_000, 150_000_000, "Base64 statistics-PDF cache. 1 PDF = 5–50 MB (base64 has 33% bloat). Up to 3 cached at once. THE biggest field in the app."),
    ("MainHeader.razor", "_statPdfBase64",                            "string?",                                        10_000_000, 50_000_000,  "Currently-displayed statistics PDF as a base64 data URL."),
    ("MainHeader.razor", "_ufcFile",                                  "IBrowserFile?",                                  0,          10_000_000,  "Front-cover upload file ref. Browser holds bytes; chunk buffer can be 1–10 MB during upload."),
    ("MainHeader.razor", "_smList",                                   "List<SessionListItem>",                          8_000,      100_000,     "Session manager list (paginated)."),
    ("MainHeader.razor", "_completionAllPcs",                         "List<PcItem>",                                   6_000,      100_000,     "All PCs for completion picker."),
    ("MainHeader.razor", "_completions",                              "List<PcCompletion>",                             4_000,      100_000,     "PC completion history list."),
    ("MainHeader.razor", "_apcMyPcs / _apcAllPcs / _apcUnapproved",   "List + List + HashSet",                          4_000,      100_000,     "Assign-PC modal data."),
    ("MainHeader.razor", "_existingPcNames",                          "HashSet<string>?",                               3_000,      50_000,      "Existing PC names for dedup check when creating a new PC."),
    ("MainHeader.razor", "_statStaffList",                            "List<StaffMemberWithRole>",                      2_500,      100_000,     "Staff dropdown for statistics."),
    ("MainHeader.razor", "_smSoloList",                               "List<SoloSessionItem>",                          2_000,      100_000,     "Solo session items."),
    ("MainHeader.razor", "_xferPcList",                               "List<(int, string, double, int, int)>",          1_600,      50_000,      "Transfer PC list with hours/balance."),
    ("MainHeader.razor", "_psaList",                                  "List<PsaEntry>",                                 1_000,      100_000,     "Print-staff dropdown."),
    ("MainHeader.razor", "Strings (~20): notes, errors, search inputs", "string × ~20",                                 800,        10_000,      "Modal-form strings."),
    ("MainHeader.razor", "_completionGrades",                         "List<GradeItem>",                                800,        50_000,      "Grades dropdown."),
    ("MainHeader.razor", "_completionAuditors",                       "List<AuditorItem>",                              600,        50_000,      "Auditors dropdown in completion modal."),
    ("MainHeader.razor", "_smCsList / _smAddAuditorList",             "List<(int, string)> × 2",                        600,        5_000,       "CS / Auditor dropdowns."),
    ("MainHeader.razor", "_smPcWallets",                              "List<Wallet>",                                   400,        100_000,     "Wallets for the selected PC in session manager."),
    ("MainHeader.razor", "_smSoloEdits",                              "Dictionary<int, string>",                        200,        10_000,      "Solo session edit drafts."),
    ("MainHeader.razor", "Integers (~30): _xferFromPcId, _smPcId, _statSelectedId, etc.", "int × ~30",                  120,        120,         "IDs for current modal/wizard step."),
    ("MainHeader.razor", "_addPcModal",                               "SpkNewPersonModal",                              100,        1_000,       "Reference to New-Person modal child component."),
    ("MainHeader.razor", "Booleans (~35): _smOpen, _showStat, _ufcBusy, etc.", "bool × ~35",                            35,         35,          "UI flags. Trivial."),
    ("MainHeader.razor", "_smDetail",                                 "SessionDetailModel?",                            8,          100_000,     "Currently-edited session detail."),

    // ── Scoped DI services (one instance per circuit) ──────────────────────────
    ("LanguageService (Scoped)",        "_translations",              "Dictionary<string, string>",                     75_000,     500_000,     "All UI translation strings for the active language. ~500–1000 keys × ~150 bytes."),
    ("MenuDataService (Scoped)",        "MenuData",                   "List<MainMenuItems>",                            3_000,      13_000,      "Static menu structure (~30 items)."),
    ("ProtectedSessionStorage (Scoped)","encrypted session keys",     "Dictionary<string, byte[]>",                     2_000,      100_000,     "Encrypted session-state keys held in memory (Blazor built-in)."),
    ("StateService (Scoped)",           "_currentState",              "AppState",                                       800,        1_000,       "AppState bag with ~15 string properties (current PC, user, theme, etc.)."),
    ("Index1Service (Scoped)",          "UDPStatusData",              "List<UDPStatus>",                                600,        10_000,      "Static dashboard sample data."),
    ("NavScrollService (Scoped)",       "isMenuType / isVertical / events", "string + bool + actions",                  125,        200,         "Nav UI mode + change events."),
    ("LpmCircuitHandler (Scoped)",      "_username + service refs",   "string + 2 refs",                                60,         120,         "Circuit handler with username + service refs."),
    ("ActionService (Scoped)",          "OnActionTriggered",          "Action<string>?",                                8,          8,           "Single event delegate."),
    ("SessionService (Scoped)",         "_httpContextAccessor",       "ref",                                            8,          8,           "Just an accessor ref."),
    ("SessionManagerLauncher (Scoped)", "OnOpenRequested",            "Action<int>?",                                   8,          8,           "Single event delegate."),
    ("PdfService (Scoped)",             "(stateless)",                "—",                                              0,          0,           "Pure utility — no instance fields."),

    // ── MainLayout / shared chrome ─────────────────────────────────────────────
    ("MainLayout.razor", "navMenuRef + _scrollVisRef + _activityUsername + _lastNavUrl", "refs + 2 strings",            600,        1_500,       "Lightweight layout state."),

    // ── Login.razor (lightest baseline) ────────────────────────────────────────
    ("Login.razor",      "_username + _password + 9 bools",           "2 strings + bools",                              100,        250,         "Form fields + transient flags."),

    // ── Blazor framework overhead (NOT user code) ──────────────────────────────
    ("(Blazor framework)", "Render tree (per circuit)",               "RenderTreeFrame[]",                              50_000,     500_000,     "Diff buffers, render queue, component lifecycle state. Scales with rendered DOM size."),
    ("(Blazor framework)", "SignalR send/recv buffers",               "byte[] + queues",                                30_000,     300_000,     "Per-circuit hub send/receive queues."),
    ("(Blazor framework)", "DotNetObjectReference table",             "Dictionary<long, object>",                       5_000,      50_000,      "Tracked .NET refs for JS interop callbacks."),
    ("(Blazor framework)", "Authentication state cache",              "ClaimsPrincipal",                                2_000,      5_000,       "User identity + claims for the circuit."),
};

// Sort big → small by typical bytes
rows = rows.OrderByDescending(r => r.Typ).ToList();

// ── Scenario totals ─────────────────────────────────────────────────────────
bool MatchAny(string c, params string[] ps) => ps.Any(p => c.Contains(p));

long SumTyp  (IEnumerable<(string,string,string,long,long,string)> xs) => xs.Sum(x => x.Item4);
long SumWorst(IEnumerable<(string,string,string,long,long,string)> xs) => xs.Sum(x => x.Item5);

var pcfolderRows = rows.Where(r => MatchAny(r.Comp, "PcFolder.razor","MainHeader.razor","MainLayout.razor","Scoped","Blazor framework")).Select(x => (x.Comp, x.Field, x.Type, x.Typ, x.Worst, x.Notes)).ToList();
var homeRows     = rows.Where(r => MatchAny(r.Comp, "Home.razor","MainHeader.razor","MainLayout.razor","Scoped","Blazor framework")).Select(x => (x.Comp, x.Field, x.Type, x.Typ, x.Worst, x.Notes)).ToList();
var loginRows    = rows.Where(r => MatchAny(r.Comp, "Login.razor","MainLayout.razor","Scoped","Blazor framework")).Select(x => (x.Comp, x.Field, x.Type, x.Typ, x.Worst, x.Notes)).ToList();

long pcfTyp   = SumTyp(pcfolderRows);  long pcfWorst   = SumWorst(pcfolderRows);
long homeTyp  = SumTyp(homeRows);      long homeWorst  = SumWorst(homeRows);
long loginTyp = SumTyp(loginRows);     long loginWorst = SumWorst(loginRows);

string FmtBytes(long b)
{
    if (b >= 1_000_000_000) return $"{b/1_000_000_000.0:F2} GB";
    if (b >= 1_000_000)     return $"{b/1_000_000.0:F2} MB";
    if (b >= 1_000)         return $"{b/1_000.0:F1} KB";
    return $"{b} B";
}

// ── Output path: arg[0] override, else default into LPM_Server/ ─────────────
string outPath = args.Length > 0
    ? args[0]
    : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "LPM_Server", "PerCircuitMemoryReport.pdf"));

System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);

Document.Create(doc =>
{
    doc.Page(p =>
    {
        p.Size(PageSizes.A3.Landscape());
        p.Margin(20);
        p.PageColor(Colors.White);
        p.DefaultTextStyle(t => t.FontSize(8.5f).FontFamily("Arial"));

        p.Header().Element(h =>
        {
            h.PaddingBottom(10).Column(col =>
            {
                col.Item().Text("LPM Blazor Server — Per-Circuit Memory Inventory")
                    .FontSize(16).Bold().FontColor(Colors.Indigo.Darken3);
                col.Item().Text("Every field that lives in server RAM per browser tab. Sorted by typical bytes (largest first). " +
                                "Worst case = realistic upper bound under heavy use.")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}    •    {rows.Count} fields catalogued")
                    .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
            });
        });

        p.Content().Column(col =>
        {
            col.Spacing(8);

            // ── Scenario totals at top ──
            col.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(s =>
            {
                s.Item().Text("Total per circuit by scenario").Bold().FontSize(11);
                s.Item().PaddingTop(4).Row(r =>
                {
                    void Card(string label, long t, long w, string color)
                    {
                        r.RelativeItem().Border(1).BorderColor(color).Padding(6).Column(c =>
                        {
                            c.Item().Text(label).Bold().FontColor(color);
                            c.Item().PaddingTop(2).Text($"Typical: {FmtBytes(t)}").FontSize(10);
                            c.Item().Text($"Worst:   {FmtBytes(w)}").FontSize(10).FontColor(Colors.Red.Darken1);
                        });
                        r.ConstantItem(8);
                    }
                    Card("Login (lightest)",        loginTyp, loginWorst, Colors.Green.Darken2);
                    Card("Home (browsing)",         homeTyp,  homeWorst,  Colors.Blue.Darken2);
                    Card("PcFolder (editing)",      pcfTyp,   pcfWorst,   Colors.Red.Darken2);
                });
            });

            // ── Main table ──
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(150);  // Component
                    c.ConstantColumn(190);  // Field
                    c.ConstantColumn(160);  // Type
                    c.ConstantColumn(75);   // Typical
                    c.ConstantColumn(75);   // Worst
                    c.RelativeColumn();     // Notes
                });

                t.Header(h =>
                {
                    string[] cols = { "Razor / Service", "Field", "Type", "Typical", "Worst case", "What it stores · why it matters" };
                    foreach (var label in cols)
                    {
                        h.Cell().Background(Colors.Indigo.Darken2).Padding(5)
                            .Text(label).FontColor(Colors.White).Bold().FontSize(9);
                    }
                });

                int idx = 0;
                foreach (var r in rows)
                {
                    var bg = (idx++ % 2 == 0) ? Colors.Grey.Lighten4 : Colors.White;
                    bool huge = r.Typ >= 1_000_000;
                    var typColor = huge ? Colors.Red.Darken2 : Colors.Black;

                    void Cell(Action<IContainer> content) =>
                        t.Cell().Background(bg).Padding(4).Element(content);

                    Cell(c => c.Text(r.Comp).FontSize(8).Bold().FontColor(Colors.Indigo.Darken3));
                    Cell(c => c.Text(r.Field).FontSize(8).FontFamily("Consolas"));
                    Cell(c => c.Text(r.Type).FontSize(7.5f).FontColor(Colors.Grey.Darken2).FontFamily("Consolas"));
                    Cell(c => c.AlignRight().Text(FmtBytes(r.Typ)).FontSize(8).Bold().FontColor(typColor));
                    Cell(c => c.AlignRight().Text(FmtBytes(r.Worst)).FontSize(8).FontColor(Colors.Red.Darken1));
                    Cell(c => c.Text(r.Notes).FontSize(8).FontColor(Colors.Grey.Darken3));
                }
            });

            // ── Reading notes ──
            col.Item().PaddingTop(10).Background(Colors.Yellow.Lighten4).Padding(8).Column(n =>
            {
                n.Item().Text("Reading notes").Bold().FontSize(10);
                n.Item().PaddingTop(2).Text(
                    "• 'Per circuit' = per browser tab. A user with 2 tabs open = 2 circuits = 2× this memory."
                ).FontSize(8.5f);
                n.Item().Text(
                    "• Disconnected circuits are retained for 5 min after the SignalR drops (was 12 h until recently — that was the leak)."
                ).FontSize(8.5f);
                n.Item().Text(
                    "• Singleton services (FolderService, DashboardService, etc.) are NOT in this list — they are shared across all circuits, not per-circuit."
                ).FontSize(8.5f);
                n.Item().Text(
                    "• PDFs themselves are NOT in this list — PDF.js renders them in the browser. The server only streams encrypted bytes through HTTP."
                ).FontSize(8.5f);
                n.Item().Text(
                    "• The 'Razor / Service' column tells you exactly which file holds each field. Right-click any field name in your IDE to jump to its declaration."
                ).FontSize(8.5f);
                n.Item().Text(
                    "• Largest drivers (>= 1 MB rows highlighted red): _statCache (base64 PDF cache in MainHeader), _uploadPendingFiles (PcFolder upload buffers), and rich-text/Quill HTML bodies."
                ).FontSize(8.5f);
            });
        });

        p.Footer().AlignCenter().Text(t =>
        {
            t.Span("Page ");
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
    });
}).GeneratePdf(outPath);

Console.WriteLine($"WROTE: {outPath}");
Console.WriteLine($"Login total typical={FmtBytes(loginTyp)}  worst={FmtBytes(loginWorst)}");
Console.WriteLine($"Home  total typical={FmtBytes(homeTyp)}   worst={FmtBytes(homeWorst)}");
Console.WriteLine($"PcF   total typical={FmtBytes(pcfTyp)}    worst={FmtBytes(pcfWorst)}");
