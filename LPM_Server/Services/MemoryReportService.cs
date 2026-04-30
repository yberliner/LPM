using System.Collections.Concurrent;
using System.Reflection;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LPM.Services;

/// <summary>
/// Generates a PDF inventory of every field that lives in server RAM per Blazor circuit.
///
/// Two modes:
///   • Curated   — hand-written rows with vetted notes. Accurate explanations of WHY
///                 each field can grow (upload buffers, base64 PDFs, etc.). Stale once
///                 someone adds a new field — needs manual update.
///   • Reflected — walks the compiled LPM assembly at request time. Lists every field
///                 of every IComponent and registered scoped service. Always current,
///                 but the "why it matters" column is best-effort heuristic.
///
/// Both render to A3 landscape PDF, sorted big-to-small by typical bytes.
/// </summary>
public sealed class MemoryReportService
{
    // ── Rough size estimator used by the Reflected report ────────────────────
    // Returns (typical, worst). Heuristics are deliberately conservative; the goal
    // is to spot OUTLIERS, not be byte-perfect.
    private static (long Typ, long Worst) EstimateSize(Type t, string fieldName)
    {
        var nameLower = fieldName.ToLowerInvariant();
        bool nameSuggestsHtml   = nameLower.Contains("html") || nameLower.Contains("quill") || nameLower.Contains("summary") || nameLower.Contains("base64");
        bool nameSuggestsBuffer = nameLower.Contains("upload") || nameLower.Contains("buffer") || nameLower.Contains("pending");
        bool nameSuggestsCache  = nameLower.Contains("cache") || nameLower.Contains("snap");

        // Primitives
        if (t == typeof(bool))                                                   return (1, 1);
        if (t == typeof(byte) || t == typeof(sbyte))                             return (1, 1);
        if (t == typeof(short) || t == typeof(ushort) || t == typeof(char))      return (2, 2);
        if (t == typeof(int) || t == typeof(uint) || t == typeof(float))         return (4, 4);
        if (t == typeof(long) || t == typeof(ulong) || t == typeof(double))      return (8, 8);
        if (t == typeof(decimal))                                                return (16, 16);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))                return (8, 8);
        if (t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan)) return (8, 8);
        if (t == typeof(Guid))                                                   return (16, 16);
        if (t.IsEnum)                                                            return (4, 4);

        // Nullable<T> — same as T, but with a small flag overhead
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var (it, iw) = EstimateSize(t.GetGenericArguments()[0], fieldName);
            return (it + 1, iw + 1);
        }

        // string
        if (t == typeof(string))
        {
            if (nameSuggestsHtml)   return (50_000, 1_000_000);
            if (nameLower.Contains("error") || nameLower.Contains("path")) return (60, 500);
            return (40, 300);
        }

        // byte[] — the headline field type for memory-heavy state
        if (t == typeof(byte[]))
        {
            if (nameSuggestsBuffer || nameSuggestsCache) return (1_000_000, 50_000_000);
            return (40, 1_000_000);
        }

        // Arrays (other than byte[])
        if (t.IsArray)
        {
            var elem = t.GetElementType()!;
            var (et, ew) = EstimateSize(elem, fieldName);
            // Assume a small fixed-size array unless the field name suggests otherwise.
            return (40 + 5 * et, 40 + 100 * ew);
        }

        // Generic collections
        if (t.IsGenericType)
        {
            var def  = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();

            // List<T> / IList<T> / Queue<T> / ConcurrentQueue<T>
            if (def == typeof(List<>) || def == typeof(Queue<>) || def == typeof(ConcurrentQueue<>) ||
                def.Name.StartsWith("IList`") || def.Name.StartsWith("IEnumerable`"))
            {
                var (et, ew) = EstimateSize(args[0], fieldName);
                int typCount = nameSuggestsBuffer ? 5     : 10;
                int wCount   = nameSuggestsBuffer ? 1000  : 1000;
                return (40 + typCount * et, 40 + wCount * ew);
            }

            // HashSet<T>
            if (def == typeof(HashSet<>))
            {
                var (et, ew) = EstimateSize(args[0], fieldName);
                return (40 + 20 * (et + 24), 40 + 1000 * (ew + 24));
            }

            // Dictionary<K,V> / ConcurrentDictionary<K,V>
            if (def == typeof(Dictionary<,>) || def == typeof(ConcurrentDictionary<,>) ||
                def.Name.StartsWith("IDictionary`") || def.Name.StartsWith("IReadOnlyDictionary`"))
            {
                var (kt, kw) = EstimateSize(args[0], fieldName);
                var (vt, vw) = EstimateSize(args[1], fieldName);
                int typEntries = nameSuggestsCache ? 3 : 20;
                int wEntries   = 1000;
                return (40 + typEntries * (kt + vt + 24), 40 + wEntries * (kw + vw + 24));
            }

            // ValueTuple — sum its components
            if (def.Name.StartsWith("ValueTuple`"))
            {
                long typ = 0, w = 0;
                foreach (var a in args) { var (it, iw) = EstimateSize(a, fieldName); typ += it; w += iw; }
                return (typ, w);
            }
        }

        // Reference types: just the 8-byte pointer in this object's layout.
        // The referenced object's footprint is attributed wherever IT is owned.
        return (8, 8);
    }

    // Pretty type names like "List<string>" instead of "System.Collections.Generic.List`1[System.String]"
    private static string FormatType(Type t)
    {
        if (t == typeof(string))  return "string";
        if (t == typeof(int))     return "int";
        if (t == typeof(long))    return "long";
        if (t == typeof(bool))    return "bool";
        if (t == typeof(double))  return "double";
        if (t == typeof(float))   return "float";
        if (t == typeof(byte))    return "byte";
        if (t == typeof(byte[]))  return "byte[]";
        if (t == typeof(object))  return "object";
        if (t == typeof(DateTime))   return "DateTime";
        if (t == typeof(DateOnly))   return "DateOnly";
        if (t == typeof(TimeSpan))   return "TimeSpan";
        if (t == typeof(decimal))    return "decimal";
        if (t == typeof(Guid))       return "Guid";

        if (t.IsArray) return FormatType(t.GetElementType()!) + "[]";

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var name = def.Name;
            int tick = name.IndexOf('`');
            if (tick > 0) name = name[..tick];
            if (def == typeof(Nullable<>)) return FormatType(t.GetGenericArguments()[0]) + "?";
            var argList = string.Join(", ", t.GetGenericArguments().Select(FormatType));
            return $"{name}<{argList}>";
        }
        return t.Name;
    }

    // Where in the source tree the type is declared (best-effort, by namespace).
    private static string ComponentLabel(Type t)
    {
        var ns = t.Namespace ?? "";
        var name = t.Name;
        if (ns.Contains(".Pages") && !ns.Contains(".Pages.")) return $"Pages/{name}.razor";
        if (ns.Contains(".Pages.Admin"))                       return $"Pages/Admin/{name}.razor";
        if (ns.Contains(".Pages."))                            return ns.Replace("LPM.", "") + "/" + name + ".razor";
        if (ns.Contains(".Shared"))                            return $"Shared/{name}.razor";
        if (ns.Contains("._Spk"))                              return $"_Spk/{name}.razor";
        if (ns.Contains(".Services"))                          return $"Services/{name}.cs";
        return name;
    }

    private static string FmtBytes(long b)
    {
        if (b >= 1_000_000_000) return $"{b/1_000_000_000.0:F2} GB";
        if (b >= 1_000_000)     return $"{b/1_000_000.0:F2} MB";
        if (b >= 1_000)         return $"{b/1_000.0:F1} KB";
        return $"{b} B";
    }

    // Row tuple used by both modes
    private record FieldRow(string Component, string Field, string Type, long Typical, long Worst, string Notes);

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CURATED REPORT — hand-written notes, vetted explanations
    // ─────────────────────────────────────────────────────────────────────────
    public byte[] GenerateCuratedReport()
    {
        var rows = new List<FieldRow>
        {
            // Heaviest fields first — these are the ones worth understanding.
            new("MainHeader.razor", "_statCache",                                "Dictionary<int, string>",                        300,        50_000,      "personId → /api/stat-pdf URL. Bytes live in wwwroot/temp/stat_*.pdf and are streamed via auth endpoint, NOT held in C# state. (Previously a Dictionary<int,(string Url, string Base64)> at ~50 MB; the base64 pipeline was removed.)"),
            new("MainHeader.razor", "_ufcFile",                                  "IBrowserFile?",                                  0,          10_000_000,  "Front-cover upload file ref. Browser holds bytes; chunk buffer can be 1–10 MB during upload."),
            new("PcFolder.razor",   "_uploadPendingFiles",                       "List<(string, byte[], bool)>",                   5_000_000,  50_000_000,  "Raw file bytes during upload-confirm dialog. 5 × 1 MB files = 5 MB; 10 × 5 MB = 50 MB. Released on Confirm/Cancel."),
            new("PcFolder.razor",   "_pendingRecoverySaves",                     "List<PendingSaveSnap>",                          2_000_000,  20_000_000,  "LocalStorage recovery snapshots from prior failed saves. Each snap holds a full pane payload."),
            new("PcFolder.razor",   "_excelData",                                "FolderService.ExcelSheetData?",                  100_000,    1_000_000,   "Open .xlsx contents (rows × cells) when an Excel file is loaded into a pane."),
            new("Home.razor",       "_cellDetails",                              "Dictionary<(int,int,string), List<CellSessionInfo>>", 100_000, 500_000, "Weekly grid cell drill-down details. Populated when day-detail modal opens."),
            new("LanguageService (Scoped)", "_translations",                     "Dictionary<string, string>",                     75_000,     500_000,     "All UI translation strings for the active language. ~500–1000 keys."),
            new("PcFolder.razor",   "_editSummaryHtml",                          "string (Quill HTML)",                            50_000,     1_000_000,   "Folder/session summary HTML body. Embedded base64 images can balloon this."),
            new("(Blazor framework)", "Render tree (per circuit)",               "RenderTreeFrame[]",                              50_000,     500_000,     "Diff buffers, render queue, component lifecycle state. Scales with rendered DOM size."),
            new("(Blazor framework)", "SignalR send/recv buffers",               "byte[] + queues",                                30_000,     300_000,     "Per-circuit hub send/receive queues."),
            new("PcFolder.razor",   "_fsEntries",                                "List<SessionSummaryEditInfo>",                   25_000,     100_000,     "Per-session summaries shown in FS edit modal."),
            new("Home.razor",       "_effortCellDetails",                        "Dictionary<(int,int), List<EffortCellEntry>>",   15_000,     100_000,     "Per-PC × week effort drill-down."),
            new("Home.razor",       "_teamCsQueues",                             "Dictionary<int, List<PendingCsSession>>",        12_000,     500_000,     "SeniorCS team queues. Per CS staff: list of pending sessions. Heaviest field for SeniorCS users."),
            new("PcFolder.razor",   "_editSummaryArfJson",                       "string?",                                        10_000,     500_000,     "Quill delta/ops JSON. Big when summary is rich."),
            new("MainHeader.razor", "_smList",                                   "List<SessionListItem>",                          8_000,      100_000,     "Session manager list (paginated)."),
            new("Home.razor",       "_pendingCsSessions",                        "List<PendingCsSession>",                         8_000,      100_000,     "User's own pending CS queue."),
            new("MainHeader.razor", "_completionAllPcs",                         "List<PcItem>",                                   6_000,      100_000,     "All PCs for completion picker."),
            new("Home.razor",       "_grid",                                     "WeekGrid (3 dictionaries)",                      5_600,      100_000,     "Per-PC × per-day × per-role aggregates for the weekly grid."),
            new("(Blazor framework)", "DotNetObjectReference table",             "Dictionary<long, object>",                       5_000,      50_000,      "Tracked .NET refs for JS interop callbacks."),
            new("PcFolder.razor",   "_excelEdits",                               "Dictionary<(int Row, int Col), string>",         4_000,      100_000,     "User's pending Excel cell edits."),
            new("MainHeader.razor", "_completions",                              "List<PcCompletion>",                             4_000,      100_000,     "PC completion history list."),
            new("Home.razor",       "_completionAllPcs",                         "List<PcItem>",                                   4_000,      100_000,     "Full PC list for completion modal."),
            new("Home.razor",       "_audAttachTypes",                           "Dictionary<int, HashSet<string>>",               4_000,      100_000,     "Per-PC attachment-type sets."),
            new("Home.razor",       "_auditorStatuses",                          "List<AuditorSessionStatus>",                     4_000,      50_000,      "Auditor's session-status overview."),
            new("MainHeader.razor", "_apcMyPcs / _apcAllPcs / _apcUnapproved",   "List + List + HashSet",                          4_000,      100_000,     "Assign-PC modal data."),
            new("Home.razor",       "_sessionQuestions",                         "Dictionary<int, QuestionInfo>",                  3_500,      50_000,      "Per-session question status."),
            new("MenuDataService (Scoped)", "MenuData",                          "List<MainMenuItems>",                            3_000,      13_000,      "Static menu structure (~30 items)."),
            new("Home.razor",       "_pendingCs",                                "PendingCsMarkers",                               3_000,      50_000,      "HashSets/Dicts of PC ids with pending CS markers."),
            new("MainHeader.razor", "_existingPcNames",                          "HashSet<string>?",                               3_000,      50_000,      "Existing PC names for dedup check when creating a new PC."),
            new("PcFolder.razor",   "_nameToCreatedAt",                          "Dictionary<string, DateTime>",                   2_800,      50_000,      "Session-name → creation time."),
            new("MainHeader.razor", "_statStaffList",                            "List<StaffMemberWithRole>",                      2_500,      100_000,     "Staff dropdown for statistics."),
            new("PcFolder.razor",   "_wsNavList",                                "List<WorkSheetItem>",                            2_400,      50_000,      "Worksheet navigation list."),
            new("PcFolder.razor",   "_nameToSessionId",                          "Dictionary<string, int>",                        2_400,      50_000,      "Session-name → session-id lookup."),
            new("Home.razor",       "_effortGrid",                               "Dictionary<(int, int), int>",                    2_000,      50_000,      "Per-PC × week effort minutes."),
            new("Home.razor",       "_weeklyTotals",                             "List<WeekTotal>",                                2_000,      100_000,     "Historical weekly aggregates (chart data)."),
            new("MainHeader.razor", "_smSoloList",                               "List<SoloSessionItem>",                          2_000,      100_000,     "Solo session items."),
            new("(Blazor framework)", "Authentication state cache",              "ClaimsPrincipal",                                2_000,      5_000,       "User identity + claims for the circuit."),
            new("ProtectedSessionStorage (Scoped)", "encrypted session keys",    "Dictionary<string, byte[]>",                     2_000,      100_000,     "Encrypted session-state keys held in memory (Blazor built-in)."),
            new("MainHeader.razor", "_xferPcList",                               "List<(int, string, double, int, int)>",          1_600,      50_000,      "Transfer PC list with hours/balance."),
            new("PcFolder.razor",   "_saveErrorPanes",                           "List<SaveErrorDetail>",                          1_500,      50_000,      "Save errors with full details for retry dialog."),
            new("PcFolder.razor",   "_expandedTreeFolders",                      "HashSet<string>",                                1_500,      5_000,       "Front/Back-cover tree folders that are expanded."),
            new("PcFolder.razor",   "_collapsedAttachments",                     "HashSet<string>",                                1_200,      5_000,       "Section keys collapsed in the worksheet UI."),
            new("PcFolder.razor",   "Strings (~30): _renameValue, _moveSelectedFolder, etc.", "string × ~30",                      1_200,      12_000,      "Modal/dialog state strings."),
            new("Home.razor",       "_pcCsNames",                                "Dictionary<int, string>",                        1_100,      10_000,      "Last CS name per PC."),
            new("MainHeader.razor", "_psaList",                                  "List<PsaEntry>",                                 1_000,      100_000,     "Print-staff dropdown."),
            new("StateService (Scoped)", "_currentState",                        "AppState",                                       800,        1_000,       "AppState bag with ~15 string properties (current PC, user, theme, etc.)."),
            new("MainHeader.razor", "Strings (~20): notes, errors, search inputs", "string × ~20",                                 800,        10_000,      "Modal-form strings."),
            new("PcFolder.razor",   "_formulaOverrides",                         "Dictionary<(int, int), string>",                 800,        50_000,      "Excel formula overrides per cell."),
            new("MainHeader.razor", "_completionGrades",                         "List<GradeItem>",                                800,        50_000,      "Grades dropdown."),
            new("Home.razor",       "_myPcs",                                    "List<PcInfo>",                                   800,        50_000,      "Approved PCs for the user."),
            new("PcFolder.razor",   "_normSteps",                                "List<NormStep>",                                 700,        5_000,       "PDF normalization step list."),
            new("Index1Service (Scoped)", "UDPStatusData",                       "List<UDPStatus>",                                600,        10_000,      "Static dashboard sample data."),
            new("MainLayout.razor", "navMenuRef + _activityUsername + _lastNavUrl", "refs + 2 strings",                            600,        1_500,       "Lightweight layout state."),
            new("PcFolder.razor",   "_programInsertFiles / _programRedirectFiles", "List<string> × 2",                             600,        3_000,       "Program file lists for insert / redirect dialogs."),
            new("Home.razor",       "_csStatusLabels",                           "Dictionary<string, string>",                     600,        5_000,       "lkp_cs_status labels (cached)."),
            new("MainHeader.razor", "_completionAuditors",                       "List<AuditorItem>",                              600,        50_000,      "Auditors dropdown in completion modal."),
            new("MainHeader.razor", "_smCsList / _smAddAuditorList",             "List<(int, string)> × 2",                        600,        5_000,       "CS / Auditor dropdowns."),
            new("Home.razor",       "Strings (~15): _addSummary, _editSummary, _weekRemarks, etc.", "string × ~15",                600,        15_000,      "Modal/form input strings."),
            new("Home.razor",       "_chartOptions",                             "ApexChartOptions<WeekTotal>",                    500,        1_000,       "Apex chart configuration."),
            new("MainHeader.razor", "_smPcWallets",                              "List<Wallet>",                                   400,        100_000,     "Wallets for the selected PC in session manager."),
            new("PcFolder.razor",   "_conversionToasts",                         "List<ConversionToast>",                          400,        5_000,       "Doc→PDF conversion toasts in flight."),
            new("PcFolder.razor",   "_dotNetRef + _stopwatch + _stopwatchCts + _dupeCts", "DotNetObjectRef + Timer + 2× CTS",      300,        500,         "JS interop ref + cancel tokens + stopwatch."),
            new("PcFolder.razor",   "_uploadDuplicateNames",                     "List<string>",                                   200,        1_000,       "Duplicate filenames in upload confirm."),
            new("MainHeader.razor", "_smSoloEdits",                              "Dictionary<int, string>",                        200,        10_000,      "Solo session edit drafts."),
            new("PcFolder.razor",   "_csedSessionIds",                           "HashSet<int>",                                   200,        10_000,      "Sessions that already have a cs_review row."),
            new("PcFolder.razor",   "_panes",                                    "PaneState[3]",                                   150,        600,         "3 fixed pane slots (file, name, section)."),
            new("MainHeader.razor", "Integers (~30): _xferFromPcId, _smPcId, _statSelectedId, etc.", "int × ~30",                  120,        120,         "IDs for current modal/wizard step."),
            new("PcFolder.razor",   "_dualInfo",                                 "Dictionary<string, (int, int)>",                 100,        200,         "Per-pane dual-page state."),
            new("PcFolder.razor",   "_dualPanes",                                "HashSet<string>",                                100,        200,         "Pane IDs currently in dual mode."),
            new("PcFolder.razor",   "_uploadRejectedFiles",                      "List<string>",                                   100,        2_000,       "Files rejected by validation."),
            new("MainHeader.razor", "_addPcModal",                               "SpkNewPersonModal",                              100,        1_000,       "Reference to New-Person modal child component."),
            new("Login.razor",      "_username + _password + 9 bools",           "2 strings + bools",                              100,        250,         "Form fields + transient flags."),
            new("Home.razor",       "Integers (~20): _userId, _csQueueDays, _selPcId, etc.", "int × ~20",                          80,         80,          "Counters and IDs."),
            new("PcFolder.razor",   "Integers (~20): _csSessionId, _activeIdx, _zoomPercent, etc.", "int × ~20",                   80,         80,          "Counters and IDs."),
            new("LpmCircuitHandler (Scoped)", "_username + service refs",        "string + 2 refs",                                60,         120,         "Circuit handler with username + service refs."),
            new("PcFolder.razor",   "Booleans (~40): _showCtxMenu, _isRenaming, etc.", "bool × ~40",                                40,         40,          "UI flags. Trivial."),
            new("MainHeader.razor", "Booleans (~35): _smOpen, _showStat, _ufcBusy, etc.", "bool × ~35",                             35,         35,          "UI flags. Trivial."),
            new("Home.razor",       "Booleans (~25): _isAuditor, _showAddSession, etc.", "bool × ~25",                              25,         25,          "UI flags. Trivial."),
            new("NavScrollService (Scoped)", "isMenuType / isVertical / events", "string + bool + actions",                        125,        200,         "Nav UI mode + change events."),
            new("PcFolder.razor",   "_dragNode",                                 "FolderTreeNode?",                                8,          500,         "Reference to currently-dragged folder node."),
            new("PcFolder.razor",   "_questionInfo",                             "QuestionInfo?",                                  8,          500,         "Question status for the current session."),
            new("MainHeader.razor", "_smDetail",                                 "SessionDetailModel?",                            8,          100_000,     "Currently-edited session detail."),
            new("ActionService (Scoped)",          "OnActionTriggered",          "Action<string>?",                                8,          8,           "Single event delegate."),
            new("SessionService (Scoped)",         "_httpContextAccessor",       "ref",                                            8,          8,           "Just an accessor ref."),
            new("SessionManagerLauncher (Scoped)", "OnOpenRequested",            "Action<int>?",                                   8,          8,           "Single event delegate."),
            new("PdfService (Scoped)",             "(stateless)",                "—",                                              0,          0,           "Pure utility — no instance fields."),
        };

        return RenderPdf("Curated", "Per-Circuit Memory Inventory · Curated",
            "Hand-vetted notes explaining WHY each field can grow. Updated manually when fields change. " +
            "Use the 'Reflected' report for the always-current field list.", rows);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. REFLECTED REPORT — walks the compiled assembly at request time
    // ─────────────────────────────────────────────────────────────────────────
    public byte[] GenerateReflectedReport()
    {
        var rows = new List<FieldRow>();

        // Pick any LPM type to find the assembly. PdfService is a safe choice — it lives
        // in the LPM_Server project and never gets renamed.
        var asm = typeof(PdfService).Assembly;

        // 1. Razor components — anything implementing Microsoft.AspNetCore.Components.IComponent.
        var componentInterface = typeof(Microsoft.AspNetCore.Components.IComponent);
        var componentTypes = asm.GetTypes()
            .Where(t => componentInterface.IsAssignableFrom(t)
                     && !t.IsAbstract
                     && !t.IsInterface
                     && t.Name != "_Imports"
                     && !t.Name.StartsWith("<>"))
            .OrderBy(t => t.FullName);

        foreach (var type in componentTypes)
        {
            CollectFieldsFromType(type, rows);
        }

        // 2. Scoped DI services — these are per-circuit. Walk them too.
        // List mirrors AddScoped<...> in Program.cs.
        var scopedTypeNames = new[]
        {
            "StateService","ActionService","MenuDataService","LanguageService","NavScrollService",
            "SessionService","Index1Service","PdfService","LpmCircuitHandler","SessionManagerLauncher",
        };
        var scopedTypes = asm.GetTypes()
            .Where(t => scopedTypeNames.Contains(t.Name) && t.IsClass && !t.IsAbstract);
        foreach (var type in scopedTypes)
        {
            CollectFieldsFromType(type, rows);
        }

        // Sort big-to-small by typical bytes
        rows = rows.OrderByDescending(r => r.Typical).ToList();

        return RenderPdf("Reflected", "Per-Circuit Memory Inventory · Reflected (auto-discovered)",
            "Walked the live compiled assembly. Every row is a real field that exists right now in this build. " +
            "Sizes are heuristic estimates from field type + name; for accurate live numbers, use the heap-snapshot button on Health.",
            rows);
    }

    private static void CollectFieldsFromType(Type type, List<FieldRow> rows)
    {
        // Skip compiler-generated Roslyn helper types
        if (type.Name.StartsWith('<')) return;

        var compLabel = ComponentLabel(type);

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var f in fields)
        {
            // Skip Blazor-generated state / parameters / cascades — we want USER fields
            if (f.Name.StartsWith('<') && f.Name.Contains(">k__BackingField"))
            {
                // Backing field for a property — surface it under the property name.
                var propName = f.Name[1..f.Name.IndexOf('>')];
                if (propName.StartsWith("__")) continue; // framework-internal
                var (typ, w) = EstimateSize(f.FieldType, propName);
                rows.Add(new FieldRow(compLabel, propName, FormatType(f.FieldType), typ, w,
                    AutoNote(f.FieldType, propName)));
                continue;
            }

            // Skip framework-injected ones (CascadingParameter, RenderHandle, etc.)
            if (f.Name == "_renderHandle" || f.Name == "_hasNeverRendered" || f.Name == "_hasPendingQueuedRender") continue;

            var (t, ww) = EstimateSize(f.FieldType, f.Name);
            rows.Add(new FieldRow(compLabel, f.Name, FormatType(f.FieldType), t, ww,
                AutoNote(f.FieldType, f.Name)));
        }
    }

    // Best-effort note generator from type + name. Beats nothing.
    private static string AutoNote(Type t, string name)
    {
        var nl = name.ToLowerInvariant();
        if (t == typeof(byte[]))                   return "byte[] — likely upload buffer or binary blob; can be MB-class.";
        if (t == typeof(string) && nl.Contains("html"))   return "HTML body (Quill / rich text). Embedded base64 images can balloon this.";
        if (t == typeof(string) && nl.Contains("base64")) return "base64-encoded payload — 33% bigger than the binary it represents.";
        if (t == typeof(string) && nl.Contains("error"))  return "Error message string (transient).";
        if (t == typeof(string) && nl.Contains("path"))   return "File system path.";
        if (t == typeof(string))                          return "Text field.";
        if (t == typeof(bool))                            return "Boolean flag (1 byte).";
        if (t.IsEnum)                                     return $"Enum {t.Name}.";

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>))                    return "List — grows with item count.";
            if (def == typeof(HashSet<>))                 return "HashSet — grows with item count.";
            if (def == typeof(Dictionary<,>))             return "Dictionary — grows with entry count.";
            if (def == typeof(ConcurrentDictionary<,>))   return "ConcurrentDictionary (thread-safe) — grows with entry count.";
            if (def == typeof(Queue<>) || def == typeof(ConcurrentQueue<>)) return "Queue — grows with enqueue count.";
            if (def == typeof(Nullable<>))                return "Nullable wrapper.";
        }

        if (t.IsArray)         return "Array.";
        if (!t.IsValueType)    return "Reference to another object (8-byte pointer here).";
        return "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PDF rendering — shared between both modes
    // ─────────────────────────────────────────────────────────────────────────
    private byte[] RenderPdf(string variant, string title, string subtitle, List<FieldRow> rows)
    {
        Settings.License = LicenseType.Community;

        bool MatchAny(string c, params string[] ps) => ps.Any(p => c.Contains(p));
        long SumTyp  (IEnumerable<FieldRow> xs) => xs.Sum(x => x.Typical);
        long SumWorst(IEnumerable<FieldRow> xs) => xs.Sum(x => x.Worst);

        var pcfolderRows = rows.Where(r => MatchAny(r.Component, "PcFolder","MainHeader","MainLayout","Scoped","Blazor framework")).ToList();
        var homeRows     = rows.Where(r => MatchAny(r.Component, "Home","MainHeader","MainLayout","Scoped","Blazor framework")).ToList();
        var loginRows    = rows.Where(r => MatchAny(r.Component, "Login","MainLayout","Scoped","Blazor framework")).ToList();

        long pcfTyp   = SumTyp(pcfolderRows);  long pcfWorst   = SumWorst(pcfolderRows);
        long homeTyp  = SumTyp(homeRows);      long homeWorst  = SumWorst(homeRows);
        long loginTyp = SumTyp(loginRows);     long loginWorst = SumWorst(loginRows);

        return Document.Create(doc =>
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
                        col.Item().Text(title).FontSize(16).Bold().FontColor(Colors.Indigo.Darken3);
                        col.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}    •    {rows.Count} fields    •    Mode: {variant}")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                    });
                });

                p.Content().Column(col =>
                {
                    col.Spacing(8);

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
                            Card("Login (lightest)",   loginTyp, loginWorst, Colors.Green.Darken2);
                            Card("Home (browsing)",    homeTyp,  homeWorst,  Colors.Blue.Darken2);
                            Card("PcFolder (editing)", pcfTyp,   pcfWorst,   Colors.Red.Darken2);
                        });
                    });

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(150);
                            c.ConstantColumn(190);
                            c.ConstantColumn(160);
                            c.ConstantColumn(75);
                            c.ConstantColumn(75);
                            c.RelativeColumn();
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
                            bool huge = r.Typical >= 1_000_000;
                            var typColor = huge ? Colors.Red.Darken2 : Colors.Black;

                            void Cell(Action<IContainer> content) =>
                                t.Cell().Background(bg).Padding(4).Element(content);

                            Cell(c => c.Text(r.Component).FontSize(8).Bold().FontColor(Colors.Indigo.Darken3));
                            Cell(c => c.Text(r.Field).FontSize(8).FontFamily("Consolas"));
                            Cell(c => c.Text(r.Type).FontSize(7.5f).FontColor(Colors.Grey.Darken2).FontFamily("Consolas"));
                            Cell(c => c.AlignRight().Text(FmtBytes(r.Typical)).FontSize(8).Bold().FontColor(typColor));
                            Cell(c => c.AlignRight().Text(FmtBytes(r.Worst)).FontSize(8).FontColor(Colors.Red.Darken1));
                            Cell(c => c.Text(r.Notes).FontSize(8).FontColor(Colors.Grey.Darken3));
                        }
                    });

                    col.Item().PaddingTop(10).Background(Colors.Yellow.Lighten4).Padding(8).Column(n =>
                    {
                        n.Item().Text("Reading notes").Bold().FontSize(10);
                        n.Item().PaddingTop(2).Text("• 'Per circuit' = per browser tab. A user with 2 tabs = 2 circuits = 2× this memory.").FontSize(8.5f);
                        n.Item().Text("• Disconnected circuits are retained for 5 min after the SignalR drops (was 12 h until recently — that was the leak).").FontSize(8.5f);
                        n.Item().Text("• Singleton services (FolderService, DashboardService, etc.) are NOT in this list — they are shared across all circuits.").FontSize(8.5f);
                        n.Item().Text("• PDFs themselves are NOT in this list — PDF.js renders them in the browser.").FontSize(8.5f);
                        if (variant == "Reflected")
                            n.Item().Text("• Reflected mode: every field shown is from the live compiled assembly. Adding a new field anywhere will appear here automatically.").FontSize(8.5f);
                        else
                            n.Item().Text("• Curated mode: notes are hand-vetted. Run 'Reflected' to find new fields added since this list was last updated.").FontSize(8.5f);
                    });
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page "); t.CurrentPageNumber();
                    t.Span(" / ");   t.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
