using Microsoft.Data.Sqlite;
using LPM.Services;

namespace FsTest;

public static class Program
{
    public static int Main(string[] args)
    {
        const string dbPath  = @"C:\tmp\lifepower.db";
        const int    pcId    = 156;
        const string pcName  = "Neta Elharar";
        const string outPath = @"C:\tmp\Neta_Elharar_FS_FIXED.pdf";

        if (!File.Exists(dbPath))
        {
            Console.WriteLine($"FATAL: {dbPath} not found");
            return 1;
        }

        // Read summaries directly — bypass DashboardService to avoid RunMigrations side-effects
        // on the user's snapshot DB. SQL mirrors GetSessionSummariesForPc(isSolo: false).
        var summaries = new List<DashboardService.SessionSummaryInfo>();
        using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(s.Name, ''),
                       COALESCE(NULLIF(fs.SessionDate,''), s.SessionDate, SUBSTR(fs.CreatedAt, 1, 10)),
                       fs.SummaryHtml,
                       COALESCE(NULLIF(fs.LengthSeconds,0), s.LengthSeconds, 0),
                       COALESCE(NULLIF(fs.AdminSeconds,0), s.AdminSeconds, 0)
                FROM sess_folder_summary fs
                LEFT JOIN sess_sessions s ON s.SessionId = fs.SessionId
                WHERE fs.PcId = @pcId AND fs.SummaryHtml IS NOT NULL AND fs.SummaryHtml != ''
                  AND fs.AuditorId IS NOT NULL
                ORDER BY COALESCE(NULLIF(fs.SessionDate,''), fs.CreatedAt) DESC";
            cmd.Parameters.AddWithValue("@pcId", pcId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                summaries.Add(new DashboardService.SessionSummaryInfo(
                    r.GetString(0), r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.GetInt32(3), r.GetInt32(4)));
        }

        Console.WriteLine($"[FsTest] Loaded {summaries.Count} summaries for PC {pcId} ({pcName})");
        foreach (var s in summaries)
            Console.WriteLine($"  Name='{s.Name}' Date={s.SessionDate} Len={s.LengthSeconds}s Adm={s.AdminSeconds}s HtmlLen={s.SummaryHtml?.Length ?? 0}");

        // Generate FS-only PDF (originalPageCount=0 → page numbers reflect only the FS pages)
        var pdf = new PdfService();
        var bytes = pdf.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount: 0);

        File.WriteAllBytes(outPath, bytes);
        Console.WriteLine($"[FsTest] Wrote {bytes.Length:N0} bytes → {outPath}");

        // Quick page count via PdfSharpCore
        try
        {
            using var ms = new MemoryStream(bytes);
            using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
            Console.WriteLine($"[FsTest] PDF page count: {doc.PageCount}");
        }
        catch (Exception ex) { Console.WriteLine($"[FsTest] page count failed: {ex.Message}"); }

        return 0;
    }
}
