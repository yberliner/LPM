using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.ResponseCompression;
using LPM.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using ApexCharts;
using Index1;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.IO;
using System.Text;
using PdfSharpCore.Pdf.IO;
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
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200 MB — supports large annotated PDF saves
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 1024 * 1024 * 500; // 500MB
    });
// 200 MB decrypted-file cache — shared across all users, keyed by absolute disk path
builder.Services.AddMemoryCache(o => o.SizeLimit = 200L * 1024 * 1024);

// Brotli + Gzip compression for HTML/JS/CSS/JSON (PDFs are already compressed — excluded by default)
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

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
builder.Services.AddSingleton<LPM.Services.PdfShrinkService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LPM.Services.PdfShrinkService>());
builder.Services.AddSingleton<LPM.Services.CsNotificationService>();
builder.Services.AddSingleton<LPM.Services.ShortcutService>();
builder.Services.AddHttpClient("sms");
builder.Services.AddSingleton<LPM.Services.SmsService>();
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

// Log the SQLite version bundled at runtime
{
    using var _sc = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
    _sc.Open();
    using var _sv = _sc.CreateCommand();
    _sv.CommandText = "SELECT sqlite_version()";
    Console.WriteLine($"[Startup] SQLite bundled version = {_sv.ExecuteScalar()}");
}

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

// Only compress in Production — Development tools (Browser Link, Hot Reload) can't inject into compressed responses
if (!app.Environment.IsDevelopment())
    app.UseResponseCompression();

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

static CookieOptions TrustCookieOpts(bool secure) => new()
{
    HttpOnly = true, SameSite = SameSiteMode.Strict,
    Secure = secure,
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
    ctx.Response.Cookies.Append("lpm_trusted", token, TrustCookieOpts(ctx.Request.IsHttps));
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
    var uname = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
    return dashSvc.CanAccessPcFolder(userId, pcId, uname);  // non-solo: must have permission
}

app.MapGet("/api/pc-file", (int pcId, string path, LPM.Services.FolderService svc,
    LPM.Services.DashboardService dashSvc, HttpContext ctx, bool solo = false) =>
{
    var user = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "(unknown)";
    Console.WriteLine($"[api/pc-file] user='{user}' pcId={pcId} solo={solo} path='{path}'");
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc))
    {
        Console.WriteLine($"[api/pc-file] FORBIDDEN for user='{user}' pcId={pcId}");
        return Results.Forbid();
    }
    var bytes = svc.ReadFileBytes(pcId, path, solo);
    Console.WriteLine($"[api/pc-file] bytes={(bytes == null ? "NULL (not found)" : bytes.Length + " bytes")}");
    if (bytes == null) return Results.NotFound();
    return Results.File(bytes, "application/pdf", enableRangeProcessing: true);
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
    return Results.File(combined, "application/pdf", enableRangeProcessing: true);
}).RequireAuthorization();

app.MapGet("/api/pc-session-merged", (int pcId, string session, LPM.Services.FolderService folderSvc,
    LPM.Services.DashboardService dashSvc, HttpContext ctx, bool solo = false) =>
{
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();
    var merged = folderSvc.MergeSessionPdfs(pcId, session, solo);
    if (merged == null) return Results.NotFound();
    var pcName = folderSvc.GetPcName(pcId) ?? $"PC {pcId}";
    var sessionNoExt = System.IO.Path.GetFileNameWithoutExtension(session);
    var downloadName = $"{pcName} - {sessionNoExt}.pdf";
    return Results.File(merged, "application/pdf", downloadName);
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

    var sizeFeat = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (sizeFeat != null) sizeFeat.MaxRequestBodySize = null;

    var form = await ctx.Request.ReadFormAsync(new FormOptions
    {
        MultipartBodyLengthLimit = long.MaxValue,
        ValueLengthLimit = int.MaxValue
    });

    var metaJson = form["meta"].ToString();
    if (string.IsNullOrEmpty(metaJson)) return Results.BadRequest("No meta");

    var jOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var meta = System.Text.Json.JsonSerializer.Deserialize<AnnSaveMeta>(metaJson, jOpts);
    if (meta?.Pages == null) return Results.BadRequest("Invalid meta");

    // Load the original PDF so unchanged pages are preserved as vectors
    var originalBytes = svc.ReadFileBytes(pcId, path, solo);
    if (originalBytes == null) return Results.NotFound();

    using var inputMs = new MemoryStream(originalBytes);
    using var originalDoc = PdfReader.Open(inputMs, PdfDocumentOpenMode.Import);
    using var newDoc = new PdfSharpCore.Pdf.PdfDocument();
    const double pxScale = 1.5;

    foreach (var pg in meta.Pages)
    {
        switch (pg.Action)
        {
            case "original":
                // Copy original page as-is (vectors preserved)
                if (pg.SrcPageIdx >= 0 && pg.SrcPageIdx < originalDoc.PageCount)
                    newDoc.AddPage(originalDoc.Pages[pg.SrcPageIdx]);
                break;

            case "overlay":
            {
                // Copy original page then draw transparent annotation layer on top
                if (pg.SrcPageIdx < 0 || pg.SrcPageIdx >= originalDoc.PageCount) break;
                var page = newDoc.AddPage(originalDoc.Pages[pg.SrcPageIdx]);
                var imgFile = form.Files["img_" + pg.ImgIdx];
                if (imgFile != null)
                {
                    using var imgMs = new MemoryStream();
                    await imgFile.CopyToAsync(imgMs);
                    var imgBytes = imgMs.ToArray();
                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page, PdfSharpCore.Drawing.XGraphicsPdfPageOptions.Append);
                    var xImg = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(imgBytes));
                    gfx.DrawImage(xImg, 0, 0, page.Width, page.Height);
                }
                break;
            }

            case "full_replace":
            case "full_new":
            {
                // Full raster: bg-changed original page or new blank/inserted page
                var imgFile = form.Files["img_" + pg.ImgIdx];
                if (imgFile == null) break;
                using var imgMs = new MemoryStream();
                await imgFile.CopyToAsync(imgMs);
                var imgBytes = imgMs.ToArray();
                var page = newDoc.AddPage();
                page.Width  = PdfSharpCore.Drawing.XUnit.FromPoint(pg.W / pxScale);
                page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(pg.H / pxScale);
                using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
                var xImg = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(imgBytes));
                gfx.DrawImage(xImg, 0, 0, page.Width, page.Height);
                break;
            }
        }
    }

    using var outputMs = new MemoryStream();
    newDoc.Save(outputMs);
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

    // Success — clear failures and generate a token valid for 2 uses (user + server backup) within 10 minutes
    LPM.Services.BackupProgress.ClearFailures(ip);
    var token = Guid.NewGuid().ToString("N");
    LPM.Services.BackupProgress.AuthToken = token;
    LPM.Services.BackupProgress.AuthExpiry = DateTime.UtcNow.AddMinutes(10);
    LPM.Services.BackupProgress.AuthTokenUsesRemaining = 2;
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

// ── Backup: User Backup (DB snapshot + auto-backups + decrypted PC files) ──
app.MapGet("/api/user-backup-download", (HttpContext ctx, LPM.Services.FolderService svc, IConfiguration cfg) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();

    var token = ctx.Request.Query["token"].ToString();
    if (!LPM.Services.BackupProgress.ConsumeToken(token))
    {
        Console.WriteLine($"[Backup] REJECTED user-backup (bad/expired token) from {ctx.Connection.RemoteIpAddress} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Results.Unauthorized();
    }

    var tempDir = Path.GetTempPath();
    var tempRoot = Path.GetPathRoot(Path.GetFullPath(tempDir));
    if (!string.IsNullOrEmpty(tempRoot))
    {
        var drive = new DriveInfo(tempRoot);
        var (dbSize, pcSize, _) = svc.GetBackupSizeInfo();
        if (drive.AvailableFreeSpace < dbSize + pcSize)
            return Results.Problem($"Not enough disk space. Need {(dbSize+pcSize)/(1024*1024)}MB, have {drive.AvailableFreeSpace/(1024*1024)}MB free.");
    }

    foreach (var stale in Directory.GetFiles(tempDir, "lpm-backup-*.zip"))
        try { File.Delete(stale); } catch { }

    var integrity    = svc.CheckIntegrity();
    var integrityTag = integrity == "ok" ? "OK" : "CORRUPT";

    var backupFolder     = svc.GetAutoBackupFolder(cfg["Database:BackupFolder"]);
    var autoBackupFiles  = Directory.Exists(backupFolder)
        ? Directory.GetFiles(backupFolder, "lifepower_*.db").OrderByDescending(f => f).ToArray()
        : [];
    var pcFiles   = svc.EnumerateBackupFiles().ToList();
    var totalFiles = 1 + autoBackupFiles.Length + pcFiles.Count;

    LPM.Services.BackupProgress.Phase          = "user";
    LPM.Services.BackupProgress.TotalFiles     = totalFiles;
    LPM.Services.BackupProgress.Current        = 0;
    LPM.Services.BackupProgress.CurrentFile    = "";
    LPM.Services.BackupProgress.LastError      = null;
    LPM.Services.BackupProgress.WasStarted     = true;
    LPM.Services.BackupProgress.Running        = true;
    LPM.Services.BackupProgress.CancelRequested = false;
    int processed = 0;

    var tempFile = Path.Combine(tempDir, $"lpm-backup-{Guid.NewGuid():N}.zip");
    try
    {
        using (var zip = System.IO.Compression.ZipFile.Open(tempFile, System.IO.Compression.ZipArchiveMode.Create))
        {
            // 1. Live DB snapshot
            var dbPath = svc.GetDbFilePath();
            if (File.Exists(dbPath))
            {
                LPM.Services.BackupProgress.CurrentFile = "db-live/lifepower.db";
                var tempDbCopy = Path.Combine(tempDir, $"db-snap-{Guid.NewGuid():N}.db");
                try
                {
                    svc.BackupDbTo(tempDbCopy);
                    var entry = zip.CreateEntry("db-live/lifepower.db", System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); using var fs = File.OpenRead(tempDbCopy); fs.CopyTo(es);
                }
                finally { try { File.Delete(tempDbCopy); } catch { } }
                LPM.Services.BackupProgress.Current = ++processed;
            }

            // 2. Auto-backup files → db-autobackups/
            foreach (var bf in autoBackupFiles)
            {
                if (LPM.Services.BackupProgress.CancelRequested) break;
                var bfName = Path.GetFileName(bf);
                LPM.Services.BackupProgress.CurrentFile = $"db-autobackups/{bfName}";
                try
                {
                    var entry = zip.CreateEntry($"db-autobackups/{bfName}", System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); using var fs = File.OpenRead(bf); fs.CopyTo(es);
                    LPM.Services.BackupProgress.Current = ++processed;
                }
                catch { }
            }

            // 3. PC files — decrypted for portability
            foreach (var (relPath, fullPath) in pcFiles)
            {
                if (LPM.Services.BackupProgress.CancelRequested) break;
                try
                {
                    LPM.Services.BackupProgress.CurrentFile = relPath;
                    var decrypted = svc.DecryptFileForBackup(fullPath);
                    var entry = zip.CreateEntry(relPath, System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); es.Write(decrypted);
                    LPM.Services.BackupProgress.Current = ++processed;
                }
                catch { }
            }
        }

        LPM.Services.BackupProgress.Running = false;
        var user = ctx.User.Identity?.Name ?? "unknown";
        var dlIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (LPM.Services.BackupProgress.CancelRequested)
        {
            Console.WriteLine($"[Backup] User backup CANCELLED by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files");
            if (File.Exists(tempFile)) File.Delete(tempFile);
            return Results.StatusCode(499);
        }

        Console.WriteLine($"[Backup] User backup complete by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files — {integrityTag}");
        var fileName = $"LPM-UserBackup-{integrityTag}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
        LPM.Services.BackupProgress.CurrentTempFile = tempFile; // Blazor waits for this to disappear before starting phase 2
        var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);
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

// ── Backup: Server Backup (DB + auto-backups + config + raw PC files + avatars) ──
app.MapGet("/api/server-backup-download", (HttpContext ctx, LPM.Services.FolderService svc, IConfiguration cfg) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();

    var token = ctx.Request.Query["token"].ToString();
    if (!LPM.Services.BackupProgress.ConsumeToken(token))
    {
        Console.WriteLine($"[Backup] REJECTED server-backup (bad/expired token) from {ctx.Connection.RemoteIpAddress} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Results.Unauthorized();
    }

    var tempDir = Path.GetTempPath();
    foreach (var stale in Directory.GetFiles(tempDir, "lpm-backup-*.zip"))
        try { File.Delete(stale); } catch { }

    var integrity    = svc.CheckIntegrity();
    var integrityTag = integrity == "ok" ? "OK" : "CORRUPT";

    var backupFolder     = svc.GetAutoBackupFolder(cfg["Database:BackupFolder"]);
    var backupFolderName = Path.GetFileName(backupFolder.TrimEnd('/', '\\'));
    var autoBackupFiles  = Directory.Exists(backupFolder)
        ? Directory.GetFiles(backupFolder, "lifepower_*.db").OrderByDescending(f => f).ToArray()
        : [];
    var extras    = svc.GetServerBackupExtras().ToList();
    var rawPcFiles = svc.EnumerateBackupFiles().ToList();
    var totalFiles = 1 + autoBackupFiles.Length + extras.Count + rawPcFiles.Count;

    LPM.Services.BackupProgress.Phase          = "server";
    LPM.Services.BackupProgress.TotalFiles     = totalFiles;
    LPM.Services.BackupProgress.Current        = 0;
    LPM.Services.BackupProgress.CurrentFile    = "";
    LPM.Services.BackupProgress.LastError      = null;
    LPM.Services.BackupProgress.WasStarted     = true;
    LPM.Services.BackupProgress.Running        = true;
    LPM.Services.BackupProgress.CancelRequested = false;
    int processed = 0;

    var tempFile = Path.Combine(tempDir, $"lpm-backup-{Guid.NewGuid():N}.zip");
    try
    {
        using (var zip = System.IO.Compression.ZipFile.Open(tempFile, System.IO.Compression.ZipArchiveMode.Create))
        {
            // 1. Live DB at root (ready to drop into app folder)
            var dbPath = svc.GetDbFilePath();
            if (File.Exists(dbPath))
            {
                LPM.Services.BackupProgress.CurrentFile = "lifepower.db";
                var tempDbCopy = Path.Combine(tempDir, $"db-snap-{Guid.NewGuid():N}.db");
                try
                {
                    svc.BackupDbTo(tempDbCopy);
                    var entry = zip.CreateEntry("lifepower.db", System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); using var fs = File.OpenRead(tempDbCopy); fs.CopyTo(es);
                }
                finally { try { File.Delete(tempDbCopy); } catch { } }
                LPM.Services.BackupProgress.Current = ++processed;
            }

            // 2. Auto-backup files (under configured folder name, e.g. db-backups/)
            foreach (var bf in autoBackupFiles)
            {
                if (LPM.Services.BackupProgress.CancelRequested) break;
                var bfName = Path.GetFileName(bf);
                LPM.Services.BackupProgress.CurrentFile = $"{backupFolderName}/{bfName}";
                try
                {
                    var entry = zip.CreateEntry($"{backupFolderName}/{bfName}", System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); using var fs = File.OpenRead(bf); fs.CopyTo(es);
                    LPM.Services.BackupProgress.Current = ++processed;
                }
                catch { }
            }

            // 3. Config files + avatars
            foreach (var (zipPath, fullPath) in extras)
            {
                if (LPM.Services.BackupProgress.CancelRequested) break;
                try
                {
                    LPM.Services.BackupProgress.CurrentFile = zipPath;
                    var entry = zip.CreateEntry(zipPath, System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); using var fs = File.OpenRead(fullPath); fs.CopyTo(es);
                    LPM.Services.BackupProgress.Current = ++processed;
                }
                catch { }
            }

            // 4. PC files — raw/encrypted, preserving exact server state
            foreach (var (relPath, fullPath) in rawPcFiles)
            {
                if (LPM.Services.BackupProgress.CancelRequested) break;
                try
                {
                    LPM.Services.BackupProgress.CurrentFile = relPath;
                    var entry = zip.CreateEntry(relPath, System.IO.Compression.CompressionLevel.Fastest);
                    using var es = entry.Open(); using var fs = File.OpenRead(fullPath); fs.CopyTo(es);
                    LPM.Services.BackupProgress.Current = ++processed;
                }
                catch { }
            }
        }

        LPM.Services.BackupProgress.Running = false;
        var user = ctx.User.Identity?.Name ?? "unknown";
        var dlIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (LPM.Services.BackupProgress.CancelRequested)
        {
            Console.WriteLine($"[Backup] Server backup CANCELLED by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files");
            if (File.Exists(tempFile)) File.Delete(tempFile);
            return Results.StatusCode(499);
        }

        Console.WriteLine($"[Backup] Server backup complete by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed} files — {integrityTag}");
        var fileName = $"LPM-ServerBackup-{integrityTag}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
        var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);
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
        current    = LPM.Services.BackupProgress.Current,
        file       = LPM.Services.BackupProgress.CurrentFile,
        running    = LPM.Services.BackupProgress.Running,
        wasStarted = LPM.Services.BackupProgress.WasStarted,
        error      = LPM.Services.BackupProgress.LastError,
        phase      = LPM.Services.BackupProgress.Phase,
        totalFiles = LPM.Services.BackupProgress.TotalFiles
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

// Models for overlay-based annotation save
record AnnPageInfo(string Action, int SrcPageIdx, int W, int H, int ImgIdx);
record AnnSaveMeta(int TotalPages, AnnPageInfo[] Pages);
