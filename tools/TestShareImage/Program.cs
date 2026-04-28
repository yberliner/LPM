using LPM.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace TestShareImage;

public static class Program
{
    public static int Main(string[] args)
    {
        // Run with cwd = LPM_Server so FolderService finds PC-Folders/, ImageInbox/, lifepower.db
        var serverDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "LPM_Server"));
        if (!Directory.Exists(serverDir))
        {
            Console.WriteLine($"FATAL: LPM_Server not found at {serverDir}");
            return 1;
        }
        Directory.SetCurrentDirectory(serverDir);
        Console.WriteLine($"[test] cwd = {Environment.CurrentDirectory}");

        var config = new ConfigurationBuilder()
            .SetBasePath(serverDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var cache  = new MemoryCache(new MemoryCacheOptions { SizeLimit = 200 * 1024 * 1024 });
        var audit  = new FileAuditService(config);
        var txtAnn = new TextAnnotationService(config);
        var folder = new FolderService(config, cache, audit, txtAnn);
        var dash   = new DashboardService(config, new MessageNotifier(), new HtmlSanitizerService());

        // ── 1. Verify LooksLikeImage detects JPG bytes ──
        var jpgBytes = MakeDummyJpg();
        var ext = FolderService.LooksLikeImage(jpgBytes);
        Console.WriteLine($"[test] LooksLikeImage on JPG → {ext ?? "(null)"}");
        if (ext != "jpg") { Console.WriteLine("FATAL: detection failed"); return 2; }

        // Quick negative check: PNG bytes
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
        Console.WriteLine($"[test] LooksLikeImage on PNG → {FolderService.LooksLikeImage(pngBytes) ?? "(null)"}");

        // ── 2. Simulate /share-receive image branch: save blob under a token ──
        const string testUser = "yaniv";   // share-receiver username
        var token = Guid.NewGuid().ToString("N");
        Console.WriteLine($"[test] simulating /share-receive — user='{testUser}' token={token}");
        folder.SaveImageInboxBlob(testUser, token, "jpg", "winnie-test-photo.jpg", jpgBytes);

        // ── 3. Simulate /share-image page load: read blob back ──
        var read = folder.ReadImageInboxBlob(testUser, token);
        if (read == null) { Console.WriteLine("FATAL: ReadImageInboxBlob returned null"); return 3; }
        Console.WriteLine($"[test] ReadImageInboxBlob OK · ext={read.Value.Ext} · originalFileName={read.Value.OriginalFileName} · size={read.Value.Bytes.Length}");

        // ── 4. PC = Winnie Win = PCId 106. Sessions: top 5 by CreatedAt desc ──
        const int pcId = 106;
        var sessions = dash.GetSessionsForPc(pcId, isSolo: false)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.SessionId)
            .Take(5)
            .ToList();
        Console.WriteLine($"[test] Last 5 (regular) sessions for PC {pcId}:");
        foreach (var s in sessions)
            Console.WriteLine($"        {s.SessionId,6} · {s.CreatedAt}  {s.Name}");
        if (sessions.Count == 0) { Console.WriteLine("FATAL: no sessions for PC 106"); return 4; }

        // Pick the most recent (top of list) — same as the Razor page would on tap-1.
        var picked = sessions[0];
        Console.WriteLine($"[test] picked session: SessionId={picked.SessionId} Name='{picked.Name}'");

        // ── 5. Compute next picture_N name (mirrors ShareImage.razor's helper) ──
        var nextName = ComputeNextPictureName(folder, pcId, picked.Name, "jpg", solo: false);
        Console.WriteLine($"[test] next attachment name: {nextName}");

        // ── 6. Real call into the production code path: SaveAttachment ──
        var savedName = folder.SaveAttachment(pcId, picked.Name, nextName, jpgBytes, solo: false);
        Console.WriteLine($"[test] SaveAttachment returned: {savedName ?? "(null)"}");
        if (savedName == null) { Console.WriteLine("FATAL: SaveAttachment returned null (folder lookup failed?)"); return 5; }

        // ── 7. Clean up the temp blob (mirrors /share-image's success path) ──
        folder.DeleteImageInboxBlob(testUser, token);
        Console.WriteLine($"[test] DeleteImageInboxBlob OK");

        // ── 8. Verify the file landed on disk in the WorkSheets folder ──
        var info = folder.GetFolderInfo(pcId, solo: false);
        if (info == null) { Console.WriteLine("FATAL: GetFolderInfo returned null"); return 6; }
        var stem = Path.GetFileNameWithoutExtension(picked.Name);
        var ws = info.WorkSheets.FirstOrDefault(w =>
            Path.GetFileNameWithoutExtension(w.File.FileName).Equals(stem, StringComparison.OrdinalIgnoreCase));
        if (ws == null) { Console.WriteLine($"FATAL: no WorkSheet entry matching '{stem}'"); return 7; }
        var found = ws.Attachments.FirstOrDefault(a => a.FileName == savedName);
        Console.WriteLine($"[test] verify on disk: {(found != null ? "FOUND" : "MISSING")} — {savedName} in WorkSheets/");

        // ── 9. Read it back through the SAME path the /api/pc-file endpoint uses ──
        if (found != null)
        {
            var readBack = folder.ReadFileBytes(pcId, found.RelativePath, solo: false);
            Console.WriteLine($"[test] ReadFileBytes via API path: {(readBack == null ? "NULL" : readBack.Length + " bytes")}");
            if (readBack != null)
            {
                bool magicOk = readBack.Length >= 3 && readBack[0] == 0xFF && readBack[1] == 0xD8 && readBack[2] == 0xFF;
                Console.WriteLine($"[test] readBack JPEG-magic: {(magicOk ? "OK (FF D8 FF)" : "BAD")}");
                bool sameLength = readBack.Length == jpgBytes.Length;
                Console.WriteLine($"[test] readBack length matches input: {sameLength}");
                bool sameContent = sameLength && readBack.SequenceEqual(jpgBytes);
                Console.WriteLine($"[test] readBack bytes-equal input:    {sameContent}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=================== RESULT ===================");
        Console.WriteLine($"PC:                  Winnie win (PcId={pcId})");
        Console.WriteLine($"Session attached to: SessionId={picked.SessionId} · Name='{picked.Name}'");
        Console.WriteLine($"Date of session:     {picked.CreatedAt}");
        Console.WriteLine($"Saved filename:      {savedName}");
        Console.WriteLine($"Folder location:     {info.FolderPath}/WorkSheets/{savedName}");
        Console.WriteLine("==============================================");
        return 0;
    }

    /// <summary>Build a tiny valid 1×1 JPEG via PdfSharpCore-rendered PNG-style approach is unnecessary;
    /// just emit a minimal SOI marker file that LooksLikeImage will accept (since it only checks magic bytes).
    /// Production users obviously share real photos; this just exercises the code path.</summary>
    static byte[] MakeDummyJpg()
    {
        // Minimal valid baseline JPEG (1×1 white pixel) — actually parseable by image viewers.
        return new byte[]
        {
            0xFF,0xD8,0xFF,0xE0,0x00,0x10,0x4A,0x46,0x49,0x46,0x00,0x01,0x01,0x00,0x00,0x01,
            0x00,0x01,0x00,0x00,0xFF,0xDB,0x00,0x43,0x00,0x08,0x06,0x06,0x07,0x06,0x05,0x08,
            0x07,0x07,0x07,0x09,0x09,0x08,0x0A,0x0C,0x14,0x0D,0x0C,0x0B,0x0B,0x0C,0x19,0x12,
            0x13,0x0F,0x14,0x1D,0x1A,0x1F,0x1E,0x1D,0x1A,0x1C,0x1C,0x20,0x24,0x2E,0x27,0x20,
            0x22,0x2C,0x23,0x1C,0x1C,0x28,0x37,0x29,0x2C,0x30,0x31,0x34,0x34,0x34,0x1F,0x27,
            0x39,0x3D,0x38,0x32,0x3C,0x2E,0x33,0x34,0x32,0xFF,0xC0,0x00,0x0B,0x08,0x00,0x01,
            0x00,0x01,0x01,0x01,0x11,0x00,0xFF,0xC4,0x00,0x1F,0x00,0x00,0x01,0x05,0x01,0x01,
            0x01,0x01,0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x02,0x03,0x04,
            0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0xFF,0xC4,0x00,0xB5,0x10,0x00,0x02,0x01,0x03,
            0x03,0x02,0x04,0x03,0x05,0x05,0x04,0x04,0x00,0x00,0x01,0x7D,0x01,0x02,0x03,0x00,
            0x04,0x11,0x05,0x12,0x21,0x31,0x41,0x06,0x13,0x51,0x61,0x07,0x22,0x71,0x14,0x32,
            0x81,0x91,0xA1,0x08,0x23,0x42,0xB1,0xC1,0x15,0x52,0xD1,0xF0,0x24,0x33,0x62,0x72,
            0x82,0x09,0x0A,0x16,0x17,0x18,0x19,0x1A,0x25,0x26,0x27,0x28,0x29,0x2A,0x34,0x35,
            0x36,0x37,0x38,0x39,0x3A,0x43,0x44,0x45,0x46,0x47,0x48,0x49,0x4A,0x53,0x54,0x55,
            0x56,0x57,0x58,0x59,0x5A,0x63,0x64,0x65,0x66,0x67,0x68,0x69,0x6A,0x73,0x74,0x75,
            0x76,0x77,0x78,0x79,0x7A,0x83,0x84,0x85,0x86,0x87,0x88,0x89,0x8A,0x92,0x93,0x94,
            0x95,0x96,0x97,0x98,0x99,0x9A,0xA2,0xA3,0xA4,0xA5,0xA6,0xA7,0xA8,0xA9,0xAA,0xB2,
            0xB3,0xB4,0xB5,0xB6,0xB7,0xB8,0xB9,0xBA,0xC2,0xC3,0xC4,0xC5,0xC6,0xC7,0xC8,0xC9,
            0xCA,0xD2,0xD3,0xD4,0xD5,0xD6,0xD7,0xD8,0xD9,0xDA,0xE1,0xE2,0xE3,0xE4,0xE5,0xE6,
            0xE7,0xE8,0xE9,0xEA,0xF1,0xF2,0xF3,0xF4,0xF5,0xF6,0xF7,0xF8,0xF9,0xFA,0xFF,0xDA,
            0x00,0x08,0x01,0x01,0x00,0x00,0x3F,0x00,0xFB,0xD0,0xFF,0xD9
        };
    }

    static string ComputeNextPictureName(FolderService folder, int pcId, string sessionName, string ext, bool solo)
    {
        try
        {
            var info = folder.GetFolderInfo(pcId, solo);
            var stem = Path.GetFileNameWithoutExtension(sessionName);
            var ws = info?.WorkSheets.FirstOrDefault(w =>
                Path.GetFileNameWithoutExtension(w.File.FileName).Equals(stem, StringComparison.OrdinalIgnoreCase));
            int maxN = 0;
            if (ws != null)
            {
                var rx = new System.Text.RegularExpressions.Regex(
                    @"_att_picture_(\d+)\.(jpg|jpeg|png)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var att in ws.Attachments)
                {
                    var m = rx.Match(att.FileName);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > maxN) maxN = n;
                }
            }
            return $"picture_{maxN + 1}.{ext}";
        }
        catch
        {
            return $"picture_1.{ext}";
        }
    }
}
