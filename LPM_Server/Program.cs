using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using LPM.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using ApexCharts;
using Index1;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.IO;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using LPM.Auth;

// Save the original console output
var originalConsoleOut = Console.Out;
var originalConsoleError = Console.Error;

// Use CyclicOutputWriter for logging
var logWriter = new CyclicOutputWriter(originalConsoleOut);
Console.SetOut(logWriter);
Console.SetError(logWriter);

AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    logWriter.WriteLine($"[Unhandled Exception] {DateTime.Now}");
    logWriter.WriteLine(e.ExceptionObject.ToString());
    logWriter.Flush();
};

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.secret.json", optional: true, reloadOnChange: false);

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Configure(context.Configuration.GetSection("Kestrel"));
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 1024 * 1024 * 500; // 500MB
    });
builder.Services.AddSingleton<AppState>();
builder.Services.AddScoped<StateService>();
builder.Services.AddScoped<IActionService, ActionService>();
builder.Services.AddScoped<MenuDataService>();
builder.Services.AddScoped<LPM.Services.LanguageService>();
builder.Services.AddScoped<NavScrollService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<Index1Service>();

builder.Services.AddWMBOS();
builder.Services.AddSession();

//Forsight Scoped classes:
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<PdfService>();

//Forsight Singleton Classes:

builder.Services.AddSingleton<LPM.ServerConfigService>();
builder.Services.AddSingleton<DevTableVisibilityService>();
builder.Services.AddSingleton<AllScriptResServices>(sp =>
{
    AllScriptResServices AllScripts = new AllScriptResServices();
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var ScriptPath = Path.Combine(env.ContentRootPath, "UserFiles", "Scripts");
    var ResultPath = Path.Combine(env.ContentRootPath, "UserFiles", "Results");
    var RwsScriptPath = Path.Combine(env.ContentRootPath, "UserFiles", "RwsScripts");
    var RwsResultPath = Path.Combine(env.ContentRootPath, "UserFiles", "RwsResults");
    var IniPath = Path.Combine(env.ContentRootPath, "UserFiles", "BitConfigs");
    AllScripts.FullScripts = new ScriptResServices(ScriptPath, ResultPath);
    AllScripts.RwsScripts = new ScriptResServices(RwsScriptPath, RwsResultPath);
    AllScripts.IniFileServices = new FileService(IniPath, true);
    return AllScripts;
});


// SQLite user database
builder.Services.AddSingleton<UserDb>();
builder.Services.AddSingleton<LPM.Services.DashboardService>();
builder.Services.AddSingleton<LPM.Services.PcService>();
builder.Services.AddSingleton<LPM.Services.AuditorService>();
builder.Services.AddSingleton<LPM.Services.AcademyService>();
builder.Services.AddSingleton<LPM.Services.StatisticsService>();
builder.Services.AddSingleton<LPM.Services.CourseService>();
builder.Services.AddSingleton<LPM.Services.MessageNotifier>();
builder.Services.AddSingleton<LPM.Services.FolderService>();
builder.Services.AddSingleton<LPM.Services.CsNotificationService>();
builder.Services.AddSingleton<LPM.Services.ShortcutService>();
builder.Services.AddSingleton<LPM.Services.ImportJobService>();

// Add session services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/accessdenied";
        options.Cookie.Name = "YourAppCookieName";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

var app = builder.Build();
//Globals.ServiceProvider = app.Services;

// Configure forwarded headers middleware early in the pipeline
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSession();

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();

// Login endpoint — validates credentials and sets auth cookie
app.MapPost("/loginpost", async (HttpContext ctx, UserDb db) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    if (db.ValidateUser(username, password, out var roles))
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, username) };
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        var loginIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        Console.WriteLine($"[Login] Login success for '{username}' from {loginIp} — roles=[{string.Join(", ", roles)}]");
        return Results.Redirect("/Home");
    }

    var failIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    Console.WriteLine($"[Login] Login failure for '{username}' from {failIp}");
    return Results.Redirect($"/login?error=1&username={Uri.EscapeDataString(username)}");
}).DisableAntiforgery();

// Logout endpoint — clears auth cookie and redirects to login
app.Map("/logout", async (HttpContext ctx) =>
{
    var logoutUser = ctx.User.Identity?.Name ?? "unknown";
    Console.WriteLine($"[Login] Logout by '{logoutUser}'");
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// -----------------------------------------------------------
//  DOWNLOAD END-POINTS
// -----------------------------------------------------------
app.MapGet("/download/Scripts/{fileName}", DownloadHandler("Scripts"));
app.MapGet("/download/Results/{fileName}", DownloadHandler("Results"));
app.MapGet("/download/RwsScripts/{fileName}", DownloadHandler("RwsScripts"));
app.MapGet("/download/RwsResults/{fileName}", DownloadHandler("RwsResults"));
app.MapGet("/download/BitConfigs/{fileName}", DownloadHandler("BitConfigs"));

Func<HttpContext, string, Task<IResult>> DownloadHandler(string bucket) =>
    (HttpContext ctx, string fileName) =>
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Task.FromResult<IResult>(Results.BadRequest("File name is required."));

        var store = ctx.RequestServices.GetRequiredService<AllScriptResServices>();

        var fullPath = bucket switch
        {
            "Scripts"    => Path.Combine(store.FullScripts!.Scripts.DirectoryPath, fileName),
            "Results"    => Path.Combine(store.FullScripts!.Results.DirectoryPath, fileName),
            "RwsScripts" => Path.Combine(store.RwsScripts!.Scripts.DirectoryPath, fileName),
            "RwsResults" => Path.Combine(store.RwsScripts!.Results.DirectoryPath, fileName),
            "BitConfigs" => Path.Combine(store.IniFileServices!.DirectoryPath, fileName),
            _            => null
        };

        if (fullPath is null || !System.IO.File.Exists(fullPath))
            return Task.FromResult<IResult>(Results.NotFound());

        const string mime = "application/octet-stream";
        return Task.FromResult<IResult>(Results.File(fullPath, mime, fileName));
    };

// ── PC Folder file endpoints ──
app.MapGet("/api/pc-file", (int pcId, string path, LPM.Services.FolderService svc) =>
{
    var bytes = svc.ReadFileBytes(pcId, path);
    if (bytes == null) return Results.NotFound();
    return Results.File(bytes, "application/pdf");
});

app.MapPost("/api/pc-file-save", async (HttpContext ctx, LPM.Services.FolderService svc) =>
{
    if (!int.TryParse(ctx.Request.Query["pcId"], out var pcId)) return Results.BadRequest();
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.BadRequest();

    using var ms = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(ms);
    var saveUser = ctx.User?.Identity?.Name ?? "unknown";
    Console.WriteLine($"[PcFile] Saved PC file pcId={pcId}: {path} by '{saveUser}'");
    return svc.SaveFile(pcId, path, ms.ToArray()) ? Results.Ok() : Results.NotFound();
});

app.MapPost("/api/pc-file-save-annotated", async (HttpContext ctx, LPM.Services.FolderService svc) =>
{
    if (!int.TryParse(ctx.Request.Query["pcId"], out var pcId)) return Results.BadRequest();
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.BadRequest();

    var form = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(form["pageCount"], out var pageCount) || pageCount == 0)
        return Results.BadRequest("No pages");

    var widths = System.Text.Json.JsonSerializer.Deserialize<int[]>(form["widths"].ToString()) ?? [];
    var heights = System.Text.Json.JsonSerializer.Deserialize<int[]>(form["heights"].ToString()) ?? [];

    // Build a new PDF from the annotated page images using PdfSharpCore
    using var pdfDoc = new PdfSharpCore.Pdf.PdfDocument();
    for (int i = 0; i < pageCount; i++)
    {
        var file = form.Files[$"page_{i}"];
        if (file == null) continue;

        using var imgStream = new MemoryStream();
        await file.CopyToAsync(imgStream);
        imgStream.Position = 0;

        var page = pdfDoc.AddPage();
        // Scale from rendered pixels (at 1.5x) back to PDF points
        double pxScale = 1.5;
        page.Width = PdfSharpCore.Drawing.XUnit.FromPoint(widths[i] / pxScale);
        page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(heights[i] / pxScale);

        using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
        var img = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(imgStream.ToArray()));
        gfx.DrawImage(img, 0, 0, page.Width, page.Height);
    }

    using var outputMs = new MemoryStream();
    pdfDoc.Save(outputMs);
    var annotUser = ctx.User?.Identity?.Name ?? "unknown";
    Console.WriteLine($"[PcFile] Saved annotated PDF pcId={pcId}: {path} by '{annotUser}'");
    return svc.SaveFile(pcId, path, outputMs.ToArray()) ? Results.Ok() : Results.NotFound();
});

// ── Backup: password verification → one-time token ──
app.MapPost("/api/backup-auth", async (HttpContext ctx, LPM.Auth.UserDb userDb) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var username = ctx.User.Identity?.Name ?? "";

    // Check brute-force lockout
    if (LPM.Services.BackupProgress.IsLockedOut(ip))
    {
        Console.WriteLine($"[Backup] BLOCKED (locked out) attempt by '{username}' from {ip} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Results.Json(new { ok = false, locked = true }, statusCode: 429);
    }

    var form = await ctx.Request.ReadFormAsync();
    var password = form["password"].ToString();

    if (string.IsNullOrEmpty(password) || !userDb.ValidateUser(username, password, out _))
    {
        var remaining = LPM.Services.BackupProgress.RecordFailure(ip);
        var isLocked = remaining == 0;
        Console.WriteLine($"[Backup] FAILED auth attempt by '{username}' from {ip} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({remaining} left)");
        return Results.Json(new { ok = false, locked = isLocked, remaining }, statusCode: isLocked ? 429 : 401);
    }

    // Success — clear failures and generate a one-time token valid for 5 minutes
    LPM.Services.BackupProgress.ClearFailures(ip);
    var token = Guid.NewGuid().ToString("N");
    LPM.Services.BackupProgress.AuthToken = token;
    LPM.Services.BackupProgress.AuthExpiry = DateTime.UtcNow.AddMinutes(5);
    Console.WriteLine($"[Backup] Auth OK for '{username}' from {ip} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    return Results.Ok(new { ok = true, token });
});

// ── Backup: build zip to temp file, then serve it ──
app.MapGet("/api/backup-download", (HttpContext ctx, LPM.Services.FolderService svc) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();

    // Verify one-time token
    var token = ctx.Request.Query["token"].ToString();
    if (string.IsNullOrEmpty(token)
        || token != LPM.Services.BackupProgress.AuthToken
        || DateTime.UtcNow > LPM.Services.BackupProgress.AuthExpiry)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        Console.WriteLine($"[Backup] REJECTED download (bad/expired token) from {ip} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Results.Unauthorized();
    }
    // Invalidate the token (one-time use)
    LPM.Services.BackupProgress.AuthToken = null;

    // Check free space on temp drive
    var tempDir = Path.GetTempPath();
    var tempRoot = Path.GetPathRoot(Path.GetFullPath(tempDir));
    if (!string.IsNullOrEmpty(tempRoot))
    {
        var drive = new DriveInfo(tempRoot);
        var (dbSize, pcSize, _) = svc.GetBackupSizeInfo();
        var needed = dbSize + pcSize;
        if (drive.AvailableFreeSpace < needed)
            return Results.Problem($"Not enough disk space on server. Need {needed / (1024*1024)}MB, have {drive.AvailableFreeSpace / (1024*1024)}MB free.");
    }

    // Clean up any stale backup zips from previous runs
    foreach (var stale in Directory.GetFiles(tempDir, "lpm-backup-*.zip"))
        try { File.Delete(stale); } catch { }

    // Reset progress
    LPM.Services.BackupProgress.Current = 0;
    LPM.Services.BackupProgress.CurrentFile = "";
    LPM.Services.BackupProgress.Running = true;
    LPM.Services.BackupProgress.CancelRequested = false;
    int processed = 0;

    var tempFile = Path.Combine(tempDir, $"lpm-backup-{Guid.NewGuid():N}.zip");
    try
    {
        using (var zip = System.IO.Compression.ZipFile.Open(tempFile, System.IO.Compression.ZipArchiveMode.Create))
        {
            // 1. Add the DB file
            var dbPath = svc.GetDbFilePath();
            if (File.Exists(dbPath))
            {
                LPM.Services.BackupProgress.CurrentFile = "lifepower.db";
                var entry = zip.CreateEntry("lifepower.db", System.IO.Compression.CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var dbStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                dbStream.CopyTo(entryStream);
                processed++;
                LPM.Services.BackupProgress.Current = processed;
            }

            // 2. Add all PC-Folder files (decrypted)
            foreach (var (relPath, fullPath) in svc.EnumerateBackupFiles())
            {
                if (LPM.Services.BackupProgress.CancelRequested) break;
                try
                {
                    LPM.Services.BackupProgress.CurrentFile = relPath;
                    var decrypted = svc.DecryptFileForBackup(fullPath);
                    var entry = zip.CreateEntry(relPath, System.IO.Compression.CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    entryStream.Write(decrypted);
                    processed++;
                    LPM.Services.BackupProgress.Current = processed;
                }
                catch { /* skip unreadable files */ }
            }
        }

        LPM.Services.BackupProgress.Running = false;
        var user = ctx.User.Identity?.Name ?? "unknown";
        var dlIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (LPM.Services.BackupProgress.CancelRequested)
        {
            Console.WriteLine($"[Backup] CANCELLED by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files processed before cancel");
            if (File.Exists(tempFile)) File.Delete(tempFile);
            return Results.StatusCode(499); // Client Closed Request
        }

        Console.WriteLine($"[Backup] Completed by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files");

        var fileName = $"LPM-Backup-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
        // Open as stream that deletes the temp file when disposed
        var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read,
            FileShare.None, 4096, FileOptions.DeleteOnClose);
        return Results.File(stream, "application/zip", fileName);
    }
    catch
    {
        LPM.Services.BackupProgress.Running = false;
        if (File.Exists(tempFile)) File.Delete(tempFile);
        throw;
    }
});

// ── Backup progress polling ──
app.MapGet("/api/backup-progress", (HttpContext ctx) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();
    return Results.Ok(new {
        current = LPM.Services.BackupProgress.Current,
        file = LPM.Services.BackupProgress.CurrentFile,
        running = LPM.Services.BackupProgress.Running
    });
});

// Clean up any leftover backup zips from previous runs (crash recovery)
foreach (var stale in Directory.GetFiles(Path.GetTempPath(), "lpm-backup-*.zip"))
    try { File.Delete(stale); } catch { }

app.Run();
