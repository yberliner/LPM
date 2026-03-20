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
builder.Services.AddSingleton<LPM.Services.MeetingService>();
builder.Services.AddHostedService<LPM.Services.DbBackupService>();
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

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

app.UseSession();

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();

// ── Auth helpers ──────────────────────────────────────────────────────────

static CookieOptions PendingCookieOpts() => new()
{
    HttpOnly = true, SameSite = SameSiteMode.Strict,
    MaxAge = TimeSpan.FromMinutes(10)
};

static CookieOptions TrustCookieOpts() => new()
{
    HttpOnly = true, SameSite = SameSiteMode.Strict,
    Expires = DateTimeOffset.UtcNow.AddYears(100)
};

static string HomeOrContact(UserDb db, int userId)
    => db.NeedsContactConfirm(userId) ? "/WelcomeContact" : "/Home";

static async Task SignInUser(HttpContext ctx, UserDb db, string username,
    LPM.Auth.UserDb.LoginFlags flags)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, username),
        new("StaffRole", flags.StaffRole),
        new("PersonId", flags.PersonId.ToString()),
    };
    foreach (var r in flags.Roles)
        claims.Add(new Claim(ClaimTypes.Role, r));
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    ctx.Response.Cookies.Delete("lpm_pending");
}

static void SetTrustCookie(HttpContext ctx, UserDb db, int userId)
{
    var token = Guid.NewGuid().ToString("N");
    db.AddTrustedDevice(userId, token);
    ctx.Response.Cookies.Append("lpm_trusted", token, TrustCookieOpts());
}

// ── /loginpost ────────────────────────────────────────────────────────────

app.MapPost("/loginpost", async (HttpContext ctx, UserDb db, IConfiguration config) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (LPM.Services.LoginRateLimit.IsLockedOut(ip))
    {
        Console.WriteLine($"[Login] BLOCKED from {ip}");
        return Results.Redirect("/login?error=2");
    }

    var form = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    if (!db.ValidateUser(username, password, out _, out _))
    {
        var remaining = LPM.Services.LoginRateLimit.RecordFailure(ip);
        Console.WriteLine($"[Login] Failure for '{username}' from {ip} ({remaining} remaining)");
        return Results.Redirect(remaining == 0
            ? "/login?error=2"
            : $"/login?error=1&username={Uri.EscapeDataString(username)}");
    }

    LPM.Services.LoginRateLimit.ClearFailures(ip);
    var flags = db.GetLoginFlags(username)!;

    bool require2fa     = config.GetValue<bool>("Security:Require2FA");
    bool requirePwdChg  = config.GetValue<bool>("Security:RequirePasswordChangeOnFirstLogin");

    // Step 1: force password change?
    if (requirePwdChg && flags.MustChangePassword)
    {
        ctx.Response.Cookies.Append("lpm_pending", flags.UserId.ToString(), PendingCookieOpts());
        Console.WriteLine($"[Login] '{username}' must change password");
        return Results.Redirect("/ChangePassword");
    }

    // Step 2: 2FA not yet set up?
    if (require2fa && !flags.TotpEnabled)
    {
        ctx.Response.Cookies.Append("lpm_pending", flags.UserId.ToString(), PendingCookieOpts());
        Console.WriteLine($"[Login] '{username}' must set up 2FA");
        return Results.Redirect("/Setup2FA");
    }

    // Step 3: 2FA enabled — check trusted device
    if (require2fa && flags.TotpEnabled)
    {
        var trustedToken = ctx.Request.Cookies["lpm_trusted"];
        if (trustedToken != null && db.GetTrustedDeviceUserId(trustedToken) == flags.UserId)
        {
            await SignInUser(ctx, db, username, flags);
            Console.WriteLine($"[Login] '{username}' signed in via trusted device");
            return Results.Redirect(HomeOrContact(db, flags.UserId));
        }
        ctx.Response.Cookies.Append("lpm_pending", flags.UserId.ToString(), PendingCookieOpts());
        Console.WriteLine($"[Login] '{username}' needs 2FA verification");
        return Results.Redirect("/Login2FA");
    }

    // No security steps required — sign in directly
    await SignInUser(ctx, db, username, flags);
    Console.WriteLine($"[Login] '{username}' signed in (no 2FA/pwd-chg required)");
    return Results.Redirect(HomeOrContact(db, flags.UserId));
}).DisableAntiforgery();

// ── /loginpost-changepwd ──────────────────────────────────────────────────

app.MapPost("/loginpost-changepwd", async (HttpContext ctx, UserDb db, IConfiguration config) =>
{
    if (!int.TryParse(ctx.Request.Cookies["lpm_pending"], out var userId))
        return Results.Redirect("/login");

    var form = await ctx.Request.ReadFormAsync();
    var newPwd = form["newPassword"].ToString();
    if (string.IsNullOrWhiteSpace(newPwd))
        return Results.Redirect("/ChangePassword?error=empty");

    db.ForceSetPassword(userId, newPwd);
    Console.WriteLine($"[Login] userId={userId} changed password");

    // Re-fetch flags (MustChangePassword now 0)
    var username = ctx.User.Identity?.Name ?? "";
    // Need to find username by userId
    var flags2 = db.GetLoginFlagsById(userId);
    if (flags2 == null) return Results.Redirect("/login");

    bool require2fa = config.GetValue<bool>("Security:Require2FA");

    if (require2fa && !flags2.TotpEnabled)
    {
        ctx.Response.Cookies.Append("lpm_pending", userId.ToString(), PendingCookieOpts());
        return Results.Redirect("/Setup2FA");
    }
    if (require2fa && flags2.TotpEnabled)
    {
        var trustedToken = ctx.Request.Cookies["lpm_trusted"];
        if (trustedToken != null && db.GetTrustedDeviceUserId(trustedToken) == userId)
        {
            await SignInUser(ctx, db, flags2.Username, flags2);
            return Results.Redirect(HomeOrContact(db, flags2.UserId));
        }
        ctx.Response.Cookies.Append("lpm_pending", userId.ToString(), PendingCookieOpts());
        return Results.Redirect("/Login2FA");
    }

    await SignInUser(ctx, db, flags2.Username, flags2);
    return Results.Redirect(HomeOrContact(db, flags2.UserId));
}).DisableAntiforgery();

// ── /loginpost-setup2fa ───────────────────────────────────────────────────

app.MapPost("/loginpost-setup2fa", async (HttpContext ctx, UserDb db, IConfiguration config) =>
{
    if (!int.TryParse(ctx.Request.Cookies["lpm_pending"], out var userId))
        return Results.Redirect("/login");

    var form = await ctx.Request.ReadFormAsync();
    var code         = form["code"].ToString();
    var rawSecretB64 = form["rawSecret"].ToString();

    byte[] rawSecret;
    try { rawSecret = Convert.FromBase64String(rawSecretB64); }
    catch { return Results.Redirect("/Setup2FA?error=1"); }

    if (!UserDb.VerifyTotpCodeRaw(rawSecret, code))
    {
        Console.WriteLine($"[2FA] Setup verification failed for userId={userId}");
        return Results.Redirect($"/Setup2FA?error=1&rs={Uri.EscapeDataString(rawSecretB64)}");
    }

    var encKey = config["TotpEncryptionKey"] ?? throw new InvalidOperationException("TotpEncryptionKey not configured");
    var encrypted = UserDb.EncryptTotpRaw(rawSecret, encKey);
    db.SaveEncryptedTotpSecret(userId, encrypted);

    var flags = db.GetLoginFlagsById(userId);
    if (flags == null) return Results.Redirect("/login");

    SetTrustCookie(ctx, db, userId);
    await SignInUser(ctx, db, flags.Username, flags);
    Console.WriteLine($"[2FA] Setup complete for userId={userId}");
    return Results.Redirect(HomeOrContact(db, userId));
}).DisableAntiforgery();

// ── /loginpost-verify2fa ──────────────────────────────────────────────────

app.MapPost("/loginpost-verify2fa", async (HttpContext ctx, UserDb db, IConfiguration config) =>
{
    if (!int.TryParse(ctx.Request.Cookies["lpm_pending"], out var userId))
        return Results.Redirect("/login");

    var form = await ctx.Request.ReadFormAsync();
    var code = form["code"].ToString();

    var flags = db.GetLoginFlagsById(userId);
    if (flags == null || flags.EncryptedTotpSecret == null)
        return Results.Redirect("/login");

    var encKey = config["TotpEncryptionKey"] ?? throw new InvalidOperationException("TotpEncryptionKey not configured");

    if (!db.VerifyTotpCode(flags.EncryptedTotpSecret, code, encKey))
    {
        Console.WriteLine($"[2FA] Verify failed for userId={userId}");
        return Results.Redirect("/Login2FA?error=1");
    }

    SetTrustCookie(ctx, db, userId);
    await SignInUser(ctx, db, flags.Username, flags);
    Console.WriteLine($"[2FA] Verify success for userId={userId}");
    return Results.Redirect(HomeOrContact(db, userId));
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

// ── PC Folder file endpoints ──

// Returns true if the authenticated user may access the given pcId/solo combination.
static bool CanAccessPcFile(HttpContext ctx, int pcId, bool solo, LPM.Services.DashboardService dashSvc)
{
    if (!int.TryParse(ctx.User.FindFirst("PersonId")?.Value, out var userId) || userId == 0)
    {
        // Fallback for session cookies predating the PersonId claim — look up by username
        var username = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        userId = dashSvc.GetUserIdByUsername(username) ?? 0;
        if (userId == 0) return false;
    }
    var staffRole = ctx.User.FindFirst("StaffRole")?.Value ?? "";
    if (staffRole == "Solo")
        return pcId == userId && solo;   // solo user: own PC, solo folder only
    return dashSvc.CanAccessPcFolder(userId, pcId);  // non-solo: must have permission
}

app.MapGet("/api/pc-file", (int pcId, string path, LPM.Services.FolderService svc,
    LPM.Services.DashboardService dashSvc, HttpContext ctx, bool solo = false) =>
{
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();
    var bytes = svc.ReadFileBytes(pcId, path, solo);
    if (bytes == null) return Results.NotFound();
    return Results.File(bytes, "application/pdf");
}).RequireAuthorization();

app.MapGet("/api/program-insert", (string name, LPM.Services.FolderService svc) =>
{
    var bytes = svc.GetProgramInsertFileBytes(name);
    if (bytes == null) return Results.NotFound();
    return Results.File(bytes, "application/pdf");
}).RequireAuthorization();

app.MapGet("/api/pc-file-folder-summary", (int pcId, string path,
    LPM.Services.FolderService folderSvc,
    LPM.Services.DashboardService dashSvc,
    PdfService pdfSvc, HttpContext ctx, bool solo = false) =>
{
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();
    var originalBytes = folderSvc.ReadFileBytes(pcId, path, solo);
    if (originalBytes == null) return Results.NotFound();

    var pcName = dashSvc.GetPersonName(pcId) ?? $"PC {pcId}";
    var summaries = dashSvc.GetSessionSummariesForPc(pcId, isSolo: solo);
    Console.WriteLine($"[FolderSummary] PC {pcId} ({pcName}) solo={solo}: {summaries.Count} summaries found");
    if (summaries.Count == 0)
        return Results.File(originalBytes, "application/pdf");

    var originalPageCount = pdfSvc.CountPdfPages(originalBytes);
    var summaryPdf = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
    var combined = pdfSvc.CombinePdfs(summaryPdf, originalBytes);
    return Results.File(combined, "application/pdf");
}).RequireAuthorization();

app.MapPost("/api/pc-file-save", async (HttpContext ctx, LPM.Services.FolderService svc,
    LPM.Services.DashboardService dashSvc) =>
{
    if (!int.TryParse(ctx.Request.Query["pcId"], out var pcId)) return Results.BadRequest();
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.BadRequest();
    var solo = ctx.Request.Query["solo"].ToString() == "true";
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();

    using var ms = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(ms);
    var saveUser = ctx.User?.Identity?.Name ?? "unknown";
    Console.WriteLine($"[PcFile] Saved PC file pcId={pcId}: {path} by '{saveUser}'");
    return svc.SaveFile(pcId, path, ms.ToArray(), solo) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/pc-file-save-annotated", async (HttpContext ctx, LPM.Services.FolderService svc,
    LPM.Services.DashboardService dashSvc) =>
{
    if (!int.TryParse(ctx.Request.Query["pcId"], out var pcId)) return Results.BadRequest();
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.BadRequest();
    var solo = ctx.Request.Query["solo"].ToString() == "true";
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();

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
    Console.WriteLine($"[PcFile] Saved annotated PDF pcId={pcId}: {path} solo={solo} by '{annotUser}'");
    var saved = svc.SaveFile(pcId, path, outputMs.ToArray(), solo);
    return saved ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

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

    if (string.IsNullOrEmpty(password) || !userDb.ValidateUser(username, password, out _, out _))
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

// ── Backup: integrity pre-check (called before triggering download) ──
app.MapGet("/api/backup-integrity", (HttpContext ctx, LPM.Services.FolderService svc) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();
    var result = svc.CheckIntegrity();
    return Results.Ok(new { ok = result == "ok", detail = result });
});

// ── Backup: build zip to temp file, then serve it ──
app.MapGet("/api/backup-download", (HttpContext ctx, LPM.Services.FolderService svc, IConfiguration cfg) =>
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

    // Integrity check — always proceed, embed result in filename
    var integrity = svc.CheckIntegrity();
    var integrityTag = integrity == "ok" ? "OK" : "CORRUPT";

    // Reset progress
    LPM.Services.BackupProgress.Current = 0;
    LPM.Services.BackupProgress.CurrentFile = "";
    LPM.Services.BackupProgress.LastError = null;
    LPM.Services.BackupProgress.WasStarted = true;
    LPM.Services.BackupProgress.Running = true;
    LPM.Services.BackupProgress.CancelRequested = false;
    int processed = 0;

    var tempFile = Path.Combine(tempDir, $"lpm-backup-{Guid.NewGuid():N}.zip");
    try
    {
        using (var zip = System.IO.Compression.ZipFile.Open(tempFile, System.IO.Compression.ZipArchiveMode.Create))
        {
            // 1. Live DB snapshot → db-live/lifepower.db
            var dbPath = svc.GetDbFilePath();
            if (File.Exists(dbPath))
            {
                LPM.Services.BackupProgress.CurrentFile = "db-live/lifepower.db";
                var tempDbCopy = Path.Combine(tempDir, $"db-snap-{Guid.NewGuid():N}.db");
                try
                {
                    svc.BackupDbTo(tempDbCopy);
                    var entry = zip.CreateEntry("db-live/lifepower.db", System.IO.Compression.CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    using var dbStream = File.OpenRead(tempDbCopy);
                    dbStream.CopyTo(entryStream);
                }
                finally { try { File.Delete(tempDbCopy); } catch { } }
                processed++;
                LPM.Services.BackupProgress.Current = processed;
            }

            // 2. Auto-backup files → db-autobackups/
            var backupFolder = svc.GetAutoBackupFolder(cfg["Database:BackupFolder"]);
            if (Directory.Exists(backupFolder))
            {
                foreach (var bf in Directory.GetFiles(backupFolder, "lifepower_*.db").OrderByDescending(f => f))
                {
                    if (LPM.Services.BackupProgress.CancelRequested) break;
                    var bfName = Path.GetFileName(bf);
                    LPM.Services.BackupProgress.CurrentFile = $"db-autobackups/{bfName}";
                    try
                    {
                        var entry = zip.CreateEntry($"db-autobackups/{bfName}", System.IO.Compression.CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        using var bfStream = File.OpenRead(bf);
                        bfStream.CopyTo(entryStream);
                        processed++;
                        LPM.Services.BackupProgress.Current = processed;
                    }
                    catch { /* skip unreadable */ }
                }
            }

            // 3. PC-Folder files
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

        Console.WriteLine($"[Backup] Completed by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files — integrity={integrityTag}");

        var fileName = $"LPM-Backup-{integrityTag}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
        var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read,
            FileShare.None, 4096, FileOptions.DeleteOnClose);
        return Results.File(stream, "application/zip", fileName);
    }
    catch (Exception ex)
    {
        LPM.Services.BackupProgress.LastError = ex.Message;
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
        running = LPM.Services.BackupProgress.Running,
        wasStarted = LPM.Services.BackupProgress.WasStarted,
        error = LPM.Services.BackupProgress.LastError
    });
});

// Enable WAL mode for crash-safe writes (runs once; setting persists in the DB file)
app.Services.GetRequiredService<LPM.Services.FolderService>().InitializeDb();

// Clean up any leftover backup zips from previous runs (crash recovery)
foreach (var stale in Directory.GetFiles(Path.GetTempPath(), "lpm-backup-*.zip"))
    try { File.Delete(stale); } catch { }

// Ensure dummy program insert PDFs exist
LPM.Services.FolderService.EnsureDummyProgramInserts();

app.Run();
