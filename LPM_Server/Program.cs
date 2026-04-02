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
using Microsoft.AspNetCore.DataProtection;
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
    options.AllowSynchronousIO = true; // required for synchronous ZipArchive streaming to response body
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 1024 * 1024 * 500; // 500MB
        // Keep client alive through long GC pauses / backup processing stalls
        options.KeepAliveInterval      = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval  = TimeSpan.FromMinutes(5);
    })
    .AddCircuitOptions(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(12);
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
builder.Services.AddSingleton<LPM.Services.UserActivityService>();
builder.Services.AddScoped<LPM.Services.LpmCircuitHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, LPM.Services.LpmCircuitHandler>();
builder.Services.AddHttpClient("sms");
builder.Services.AddSingleton<LPM.Services.SmsService>();
builder.Services.AddSingleton<LPM.Services.ImportJobService>();
builder.Services.AddSingleton<LPM.Services.CompletionService>();
builder.Services.AddSingleton<LPM.Services.QuestionService>();

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

var autoLoginProtector = app.Services.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("lpm-autologin");

// Register embedded font resolver so PdfSharpCore works on Linux without system fonts
PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = LPM.Services.EmbeddedFontResolver.Instance;

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

// ── Auto-login middleware — on / and /index: redirect to dashboard or auto-login ───
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if ((path == "/" || path.Equals("/index", StringComparison.OrdinalIgnoreCase))
        && ctx.Request.Method == "GET")
    {
        // Already authenticated → go straight to dashboard
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            ctx.Response.Redirect("/Home");
            return;
        }
        var token = ctx.Request.Cookies["lpm_autologin"];
        if (token != null)
        {
            try
            {
                var payload = autoLoginProtector.Unprotect(token);
                var parts   = payload.Split('|');
                if (parts.Length == 3
                    && int.TryParse(parts[0], out var userId)
                    && DateTime.TryParse(parts[1], null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var issuedAt)
                    && (DateTime.UtcNow - issuedAt).TotalHours < 72)
                {
                    var db   = ctx.RequestServices.GetRequiredService<UserDb>();
                    var info = db.GetAutoLoginInfo(userId);
                    if (info is { IsActive: true } && info.Value.PwdHashPrefix == parts[2])
                    {
                        var flags = db.GetLoginFlagsById(userId);
                        if (flags != null)
                        {
                            await SignInUser(ctx, db, flags.Username, flags);
                            SetAutoLoginCookie(ctx, autoLoginProtector, userId, info.Value.PwdHashPrefix);
                            Console.WriteLine($"[AutoLogin] '{flags.Username}' auto-logged in");
                            ctx.Response.Redirect(HomeOrContact(db, userId));
                            return; // short-circuit — don't call next()
                        }
                    }
                }
            }
            catch { /* tampered or expired */ }

            // Invalid cookie — delete it
            ctx.Response.Cookies.Delete("lpm_autologin", new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Strict,
                Secure = ctx.Request.IsHttps, Path = "/"
            });
        }
    }

    await next();
});

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

static CookieOptions AutoLoginCookieOpts(bool secure) => new()
{
    HttpOnly = true, SameSite = SameSiteMode.Strict,
    Secure = secure,
    MaxAge = TimeSpan.FromHours(72)
};

static void SetAutoLoginCookie(HttpContext ctx, IDataProtector dp, int userId, string pwdHashPrefix)
{
    var payload = $"{userId}|{DateTime.UtcNow:O}|{pwdHashPrefix}";
    ctx.Response.Cookies.Append("lpm_autologin", dp.Protect(payload),
        AutoLoginCookieOpts(ctx.Request.IsHttps));
}

static void ApplyAutoLogin(HttpContext ctx, UserDb db, IDataProtector dp, int userId)
{
    var info = db.GetAutoLoginInfo(userId);
    if (info != null)
        SetAutoLoginCookie(ctx, dp, userId, info.Value.PwdHashPrefix);
}

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

    bool require2fa     = flags.Require2FA || flags.TotpEnabled;
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
            ApplyAutoLogin(ctx, db, autoLoginProtector, flags.UserId);
            Console.WriteLine($"[Login] '{username}' signed in via trusted device");
            return Results.Redirect(HomeOrContact(db, flags.UserId));
        }
        ctx.Response.Cookies.Append("lpm_pending", flags.UserId.ToString(), PendingCookieOpts());
        Console.WriteLine($"[Login] '{username}' needs 2FA verification");
        return Results.Redirect("/Login2FA");
    }

    // No security steps required — sign in directly
    await SignInUser(ctx, db, username, flags);
    ApplyAutoLogin(ctx, db, autoLoginProtector, flags.UserId);
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

    bool require2fa = flags2.Require2FA || flags2.TotpEnabled;

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
            ApplyAutoLogin(ctx, db, autoLoginProtector, flags2.UserId);
            return Results.Redirect(HomeOrContact(db, flags2.UserId));
        }
        ctx.Response.Cookies.Append("lpm_pending", userId.ToString(), PendingCookieOpts());
        return Results.Redirect("/Login2FA");
    }

    await SignInUser(ctx, db, flags2.Username, flags2);
    ApplyAutoLogin(ctx, db, autoLoginProtector, flags2.UserId);
    return Results.Redirect(HomeOrContact(db, flags2.UserId));
}).DisableAntiforgery();

// ── /loginpost-setup2fa ───────────────────────────────────────────────────

app.MapPost("/loginpost-setup2fa", async (HttpContext ctx, UserDb db, IConfiguration config) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var code         = form["code"].ToString();
    var rawSecretB64 = form["rawSecret"].ToString();
    bool voluntary   = form["voluntary"].ToString() == "1";

    int userId;
    if (voluntary)
    {
        // User is already authenticated — look up userId from username claim
        var volUsername = ctx.User.Identity?.Name;
        if (string.IsNullOrEmpty(volUsername))
            return Results.Redirect("/UserSettings");
        var volFlags = db.GetLoginFlags(volUsername);
        if (volFlags == null)
            return Results.Redirect("/UserSettings");
        userId = volFlags.UserId;
    }
    else
    {
        if (!int.TryParse(ctx.Request.Cookies["lpm_pending"], out userId))
            return Results.Redirect("/login");
    }

    byte[] rawSecret;
    try { rawSecret = Convert.FromBase64String(rawSecretB64); }
    catch { return Results.Redirect($"/Setup2FA?error=1{(voluntary ? "&voluntary=1" : "")}"); }

    if (!UserDb.VerifyTotpCodeRaw(rawSecret, code))
    {
        Console.WriteLine($"[2FA] Setup verification failed for userId={userId}");
        return Results.Redirect($"/Setup2FA?error=1&rs={Uri.EscapeDataString(rawSecretB64)}{(voluntary ? "&voluntary=1" : "")}");
    }

    var encKey = config["TotpEncryptionKey"] ?? throw new InvalidOperationException("TotpEncryptionKey not configured");
    var encrypted = UserDb.EncryptTotpRaw(rawSecret, encKey);
    db.SaveEncryptedTotpSecret(userId, encrypted);

    Console.WriteLine($"[2FA] Setup complete for userId={userId} (voluntary={voluntary})");

    if (voluntary)
        return Results.Redirect("/UserSettings?2fa=ok");

    var flags = db.GetLoginFlagsById(userId);
    if (flags == null) return Results.Redirect("/login");

    SetTrustCookie(ctx, db, userId);
    await SignInUser(ctx, db, flags.Username, flags);
    ApplyAutoLogin(ctx, db, autoLoginProtector, userId);
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
    ApplyAutoLogin(ctx, db, autoLoginProtector, userId);
    Console.WriteLine($"[2FA] Verify success for userId={userId}");
    return Results.Redirect(HomeOrContact(db, userId));
}).DisableAntiforgery();

// Logout endpoint — clears auth cookie and redirects to login
app.Map("/logout", async (HttpContext ctx) =>
{
    var logoutUser = ctx.User.Identity?.Name ?? "unknown";
    Console.WriteLine($"[Login] Logout by '{logoutUser}'");

    // Log all cookies present BEFORE deletion
    Console.WriteLine($"[Logout] Cookies BEFORE: {string.Join(", ", ctx.Request.Cookies.Select(c => c.Key))}");

    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Cookies.Delete("lpm_autologin", new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict,
        Secure = ctx.Request.IsHttps, Path = "/"
    });

    // Log Set-Cookie headers being sent
    Console.WriteLine($"[Logout] Set-Cookie headers: {string.Join(" | ", ctx.Response.Headers.SetCookie.Select(s => s))}");

    return Results.Redirect("/login");
});

// Temporary debug: shows what cookies the browser is sending
app.MapGet("/debug-cookies", (HttpContext ctx) =>
{
    var cookies = ctx.Request.Cookies.Select(c => $"{c.Key}={c.Value[..Math.Min(20, c.Value.Length)]}...");
    var isAuth = ctx.User.Identity?.IsAuthenticated == true;
    var user = ctx.User.Identity?.Name ?? "(none)";
    return Results.Text($"Authenticated: {isAuth}\nUser: {user}\nCookies:\n{string.Join("\n", cookies)}");
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
    if (staffRole == LPM.StaffRoles.Solo)
        return pcId == userId && solo;   // solo user: own PC, solo folder only
    var uname = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
    return dashSvc.CanAccessPcFolder(userId, pcId, uname);  // non-solo: must have permission
}

app.MapGet("/api/pc-file", (int pcId, string path, LPM.Services.FolderService svc,
    LPM.Services.DashboardService dashSvc, HttpContext ctx, bool solo = false, bool download = false) =>
{
    var user = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "(unknown)";
    Console.WriteLine($"[api/pc-file] user='{user}' pcId={pcId} solo={solo} download={download} path='{path}'");
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc))
    {
        Console.WriteLine($"[api/pc-file] FORBIDDEN for user='{user}' pcId={pcId}");
        return Results.Forbid();
    }
    var bytes = download
        ? svc.ReadFileBytesForDownload(pcId, path, solo)
        : svc.ReadFileBytes(pcId, path, solo);
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

    int originalPageCount = 0;
    int summaryPageCount  = 0;
    byte[]? combined      = null;

    // Count original pages via PdfSharpCore
    try { originalPageCount = pdfSvc.CountPdfPages(originalBytes); }
    catch (Exception ex) { Console.WriteLine($"[FolderSummary] PdfSharpCore CountPdfPages threw for PC {pcId}: {ex.Message}"); }
    Console.WriteLine($"[FolderSummary] originalPageCount for PC {pcId}: {originalPageCount}");

    // ── Step 1: try PdfSharpCore combine ──────────────────────────────────
    try
    {
        var summaryPdf    = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
        summaryPageCount  = pdfSvc.CountPdfPages(summaryPdf);
        var candidate     = pdfSvc.CombinePdfs(summaryPdf, originalBytes);
        int combinedCount = pdfSvc.CountPdfPages(candidate);
        int expectedMin   = summaryPageCount + originalPageCount;
        if (combinedCount >= expectedMin)
        {
            combined = candidate;
        }
        else
        {
            Console.WriteLine($"[FolderSummary] PdfSharpCore produced corrupt output " +
                              $"({combinedCount} pages, expected ≥{expectedMin}) for PC {pcId} — trying Ghostscript");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FolderSummary] PdfSharpCore combine failed for PC {pcId}: {ex.Message} — trying Ghostscript");
    }

    // ── Step 2: Ghostscript fallback ───────────────────────────────────────
    if (combined == null)
    {
        try
        {
            var summaryPdf2 = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
            summaryPageCount = pdfSvc.CountPdfPages(summaryPdf2);
            combined = folderSvc.CombinePdfsViaGhostscript(summaryPdf2, originalBytes);

            // If originalPageCount was 0 (PdfSharpCore couldn't read it), recover from the combined output
            if (combined != null && originalPageCount <= 0)
            {
                int combinedPages = pdfSvc.CountPdfPages(combined); // GS output is clean, PdfSharpCore can read it
                originalPageCount = combinedPages - summaryPageCount;
                Console.WriteLine($"[FolderSummary] Recovered originalPageCount={originalPageCount} from GS combined ({combinedPages} - {summaryPageCount})");
                if (originalPageCount > 0)
                {
                    // Regenerate summary with correct page numbers and recombine
                    var summaryPdf3 = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
                    combined = folderSvc.CombinePdfsViaGhostscript(summaryPdf3, originalBytes);
                }
            }

            if (combined == null)
                Console.WriteLine($"[FolderSummary] Ghostscript returned null for PC {pcId} path={path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderSummary] Ghostscript combine threw for PC {pcId}: {ex.Message}");
        }
    }

    // ── Step 3: all attempts failed ────────────────────────────────────────
    if (combined == null)
    {
        Console.WriteLine($"[FolderSummary] ALL combine attempts failed for PC {pcId} path={path} — returning 500");
        return Results.Problem("Folder summary generation failed", statusCode: 500);
    }

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
    var downloadName = $"{sessionNoExt}.pdf";
    return Results.File(merged, "application/pdf", downloadName);
}).RequireAuthorization();

app.MapGet("/api/pc-file-annotations", (int pcId, string path,
    LPM.Services.FolderService folderSvc, LPM.Services.DashboardService dashSvc,
    HttpContext ctx, bool solo = false) =>
{
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();
    var json = folderSvc.ReadAnnotationSidecar(pcId, path, solo);
    return json != null ? Results.Content(json, "application/json") : Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/pc-file-save-annotations", async (HttpContext ctx,
    LPM.Services.FolderService folderSvc, LPM.Services.DashboardService dashSvc) =>
{
    if (!int.TryParse(ctx.Request.Query["pcId"], out var pcId)) return Results.BadRequest();
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.BadRequest();
    var solo = ctx.Request.Query["solo"].ToString() == "true";
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "[]")
        folderSvc.DeleteAnnotationSidecar(pcId, path, solo);
    else
        folderSvc.WriteAnnotationSidecar(pcId, path, solo, json);
    return Results.Ok();
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
    var saveUser2 = ctx.User?.Identity?.Name ?? "unknown";
    Console.WriteLine($"[AnnSave] pcId={pcId} solo={solo} user='{saveUser2}' path='{path}' pathHex=[{string.Join(" ", System.Text.Encoding.UTF8.GetBytes(path).Select(b => b.ToString("X2")))}]");
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

    // Only load original PDF if any page needs vector data from it
    var needsOriginal = meta.Pages.Any(p => p.Action == "original" || p.Action == "overlay");
    PdfSharpCore.Pdf.PdfDocument? originalDoc = null;
    if (needsOriginal)
    {
        var originalBytes = svc.ReadFileBytes(pcId, path, solo);
        if (originalBytes == null) { Console.WriteLine($"[AnnSave] 404 (needsOriginal) pcId={pcId} solo={solo} path='{path}'"); return Results.NotFound(); }
        try
        {
            using var ms0 = new MemoryStream(originalBytes);
            originalDoc = PdfReader.Open(ms0, PdfDocumentOpenMode.Import);
        }
        catch (PdfSharpCore.Pdf.IO.PdfReaderException ex)
        {
            Console.WriteLine($"[AnnSave] PDF XRef error for PC {pcId} path={path}: {ex.Message}");
            return Results.Json(new { error = "PDF_CORRUPT" }, statusCode: 422);
        }
    }
    else
    {
        // Raster-only save: verify file exists without opening it
        if (svc.ReadFileBytes(pcId, path, solo) == null) { Console.WriteLine($"[AnnSave] 404 (raster) pcId={pcId} solo={solo} path='{path}'"); return Results.NotFound(); }
    }
    using var _disposeOriginalDoc = originalDoc;
    using var newDoc = new PdfSharpCore.Pdf.PdfDocument();
    const double pxScale = 1.5;

    foreach (var pg in meta.Pages)
    {
        switch (pg.Action)
        {
            case "original":
                // Copy original page as-is (vectors preserved)
                if (pg.SrcPageIdx >= 0 && pg.SrcPageIdx < originalDoc!.PageCount)
                    newDoc.AddPage(originalDoc.Pages[pg.SrcPageIdx]!);
                break;

            case "overlay":
            {
                // Copy original page then draw transparent annotation layer on top
                if (pg.SrcPageIdx < 0 || pg.SrcPageIdx >= originalDoc!.PageCount) break;
                var page = newDoc.AddPage(originalDoc.Pages[pg.SrcPageIdx]!);
                var imgFile = form.Files["img_" + pg.ImgIdx];
                if (imgFile != null)
                {
                    using var imgMs = new MemoryStream();
                    await imgFile.CopyToAsync(imgMs);
                    var imgBytes = imgMs.ToArray();
                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page, PdfSharpCore.Drawing.XGraphicsPdfPageOptions.Append);
                    var xImg = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(imgBytes));
                    // PdfSharpCore's page CTM flips Y using page.Height (= MediaBox.Height),
                    // not MediaBox.Y2.  For pages normalised with a non-zero MediaBox origin
                    // (e.g. [0, -21, 595, 821]) page.Height=842 but MediaBox.Y2=821, so the
                    // default DrawImage(0,0,W,H) lands 21 pt above the visible area.
                    // Offset by (page.Height - MediaBox.Y2) to align with the actual MediaBox.
                    var mb    = page.MediaBox;
                    double drawX = mb.X1;
                    double drawY = page.Height.Point - mb.Y2;   // 0 for standard pages
                    Console.WriteLine($"[AnnSave] overlay page {pg.SrcPageIdx}: MB=[{mb.X1:F1},{mb.Y1:F1},{mb.X2:F1},{mb.Y2:F1}] drawY={drawY:F1}");
                    gfx.DrawImage(xImg, drawX, drawY, page.Width, page.Height);
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
    LPM.Services.BackupProgress.AuthExpiry = DateTime.UtcNow.AddHours(24);
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
app.MapGet("/api/user-backup-download", (HttpContext ctx, LPM.Services.FolderService svc, IConfiguration cfg,
    LPM.Services.DashboardService dashSvc, PdfService pdfSvc) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();

    var token = ctx.Request.Query["token"].ToString();
    if (!LPM.Services.BackupProgress.ConsumeToken(token))
    {
        Console.WriteLine($"[Backup] REJECTED user-backup (bad/expired token) from {ctx.Connection.RemoteIpAddress} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Results.Unauthorized();
    }

    var tempDir = Path.GetTempPath();

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
    LPM.Services.BackupProgress.CurrentTempFile = null;
    LPM.Services.BackupProgress.TimedOutFiles.Clear();

    var user = ctx.User.Identity?.Name ?? "unknown";
    var dlIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var fileName = $"LPM-UserBackup-{integrityTag}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";

    // Tell nginx not to buffer this response — stream straight to the browser
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");

    return Results.Stream(responseStream =>
    {
        int processed = 0;
        var phaseSw = System.Diagnostics.Stopwatch.StartNew();
        var errorLog = new List<string>(); // collects per-file errors for summary
        try
        {
            using (var zip = new System.IO.Compression.ZipArchive(responseStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                // 1. Live DB snapshot
                var dbPath = svc.GetDbFilePath();
                if (File.Exists(dbPath))
                {
                    LPM.Services.BackupProgress.CurrentFile = "db-live/lifepower.db";
                    var tempDbCopy = Path.Combine(tempDir, $"db-snap-{Guid.NewGuid():N}.db");
                    var dbSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        svc.BackupDbTo(tempDbCopy);
                        var dbSize = new FileInfo(tempDbCopy).Length;
                        var entry = zip.CreateEntry("db-live/lifepower.db", System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); using var fs = File.OpenRead(tempDbCopy); fs.CopyTo(es);
                        dbSw.Stop();
                        Console.WriteLine($"[Backup][user][{processed + 1}/{totalFiles}] db-live/lifepower.db | {dbSize:N0} bytes | {dbSw.ElapsedMilliseconds}ms");
                    }
                    finally { try { File.Delete(tempDbCopy); } catch { } }
                    LPM.Services.BackupProgress.Current = ++processed;
                    responseStream.Flush();
                }

                // 2. Auto-backup files → db-autobackups/
                foreach (var bf in autoBackupFiles)
                {
                    if (LPM.Services.BackupProgress.CancelRequested) break;
                    var bfName = Path.GetFileName(bf);
                    LPM.Services.BackupProgress.CurrentFile = $"db-autobackups/{bfName}";
                    var bfSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var bfSize = new FileInfo(bf).Length;
                        var entry = zip.CreateEntry($"db-autobackups/{bfName}", System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); using var fs = File.OpenRead(bf); fs.CopyTo(es);
                        bfSw.Stop();
                        Console.WriteLine($"[Backup][user][{processed + 1}/{totalFiles}] db-autobackups/{bfName} | {bfSize:N0} bytes | {bfSw.ElapsedMilliseconds}ms");
                        LPM.Services.BackupProgress.Current = ++processed;
                        responseStream.Flush();
                    }
                    catch (Exception ex) { Console.WriteLine($"[Backup][user] ERROR auto-backup {bfName}: {ex.Message}"); }
                }

                // 3. PC files — decrypted for portability
                foreach (var (relPath, fullPath) in pcFiles)
                {
                    if (LPM.Services.BackupProgress.CancelRequested) break;
                    if (ctx.RequestAborted.IsCancellationRequested)
                    {
                        Console.WriteLine($"[Backup][user] CLIENT DISCONNECTED at file {processed}/{totalFiles} — stopping");
                        break;
                    }

                    // Skip files that hung in a previous backup this session (EBS bad-block protection)
                    lock (LPM.Services.BackupProgress.PermSkipFiles)
                        if (LPM.Services.BackupProgress.PermSkipFiles.Contains(relPath))
                        {
                            Console.WriteLine($"[Backup][user][{processed + 1}/{totalFiles}] PERM-SKIP {relPath}");
                            errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PERM-SKIP (timed out in earlier phase) — {relPath}");
                            lock (LPM.Services.BackupProgress.TimedOutFiles) LPM.Services.BackupProgress.TimedOutFiles.Add(relPath);
                            continue;
                        }

                    try
                    {
                        LPM.Services.BackupProgress.CurrentFile = relPath;
                        Console.WriteLine($"[Backup][user][{processed + 1}/{totalFiles}] reading: {relPath}");

                        // Use Thread.Join timeout — more reliable than Task.Wait when OS read() never returns
                        byte[]? bytes = null;
                        long readBytes = 0;
                        long readMs = 0;
                        var readSw = System.Diagnostics.Stopwatch.StartNew();
                        var readThread = new Thread(() =>
                        {
                            try
                            {
                                var decrypted = svc.DecryptFileForBackup(fullPath);

                                // Folder Summary files: prepend session summaries PDF (same as live viewer)
                                byte[] bytesToZip = decrypted;
                                var (isSummary, fspcId, isSolo) = LPM.Services.FolderService.ParseFolderSummaryBackupPath(relPath);
                                if (isSummary)
                                {
                                    try
                                    {
                                        var summaries = dashSvc.GetSessionSummariesForPc(fspcId, isSolo);
                                        if (summaries.Count > 0)
                                        {
                                            var pcName = dashSvc.GetPersonName(fspcId) ?? $"PC {fspcId}";
                                            var originalPageCount = pdfSvc.CountPdfPages(decrypted);
                                            var summaryPdf = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
                                            bytesToZip = pdfSvc.CombinePdfs(summaryPdf, decrypted);
                                            Console.WriteLine($"[Backup][user] FolderSummary combined for PC {fspcId} ({summaries.Count} sessions)");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[Backup][user] FolderSummary failed for PC {fspcId}: {ex.Message} — using original");
                                    }
                                }
                                bytes = bytesToZip;
                                readBytes = bytesToZip.Length;
                            }
                            catch (Exception ex) { Console.WriteLine($"[Backup][user] read-thread ERROR {relPath}: {ex.Message}"); }
                        }) { IsBackground = true };
                        readThread.Start();

                        if (!readThread.Join(TimeSpan.FromSeconds(30)))
                        {
                            Console.WriteLine($"[Backup][user][{processed + 1}/{totalFiles}] TIMEOUT 30s — skipping permanently: {relPath}");
                            errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TIMEOUT (30s read timeout) — {relPath}");
                            lock (LPM.Services.BackupProgress.PermSkipFiles) LPM.Services.BackupProgress.PermSkipFiles.Add(relPath);
                            lock (LPM.Services.BackupProgress.TimedOutFiles) LPM.Services.BackupProgress.TimedOutFiles.Add(relPath);
                            continue;
                        }
                        readMs = readSw.ElapsedMilliseconds;

                        if (bytes == null) { Console.WriteLine($"[Backup][user] NULL bytes (read error): {relPath}"); errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NULL BYTES (read returned no data) — {relPath}"); continue; }

                        var writeSw = System.Diagnostics.Stopwatch.StartNew();
                        var entry = zip.CreateEntry(relPath, System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); es.Write(bytes);
                        writeSw.Stop();

                        Console.WriteLine($"[Backup][user][{processed + 1}/{totalFiles}] OK {relPath} | {readBytes:N0} bytes | read {readMs}ms | write {writeSw.ElapsedMilliseconds}ms | total {readMs + writeSw.ElapsedMilliseconds}ms");
                        LPM.Services.BackupProgress.Current = ++processed;

                        // Flush after every file to prevent nginx proxy_read_timeout during slow reads
                        responseStream.Flush();

                        // Release memory: clear reference immediately, GC every 200 files or for large files
                        bytes = null;
                        if (processed % 200 == 0)
                            GC.Collect(1, GCCollectionMode.Optimized);
                        else if (readBytes > 50 * 1024 * 1024)
                            GC.Collect(0, GCCollectionMode.Optimized);
                    }
                    catch (Exception ex) { Console.WriteLine($"[Backup][user] OUTER ERROR {relPath}: {ex.Message}"); errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR ({ex.Message}) — {relPath}"); }
                }

                // Write error summary file if there were any problems
                if (errorLog.Count > 0)
                {
                    var summary = new System.Text.StringBuilder();
                    summary.AppendLine($"LPM User Backup — Error Summary");
                    summary.AppendLine($"Backup by: {user} from {dlIp}");
                    summary.AppendLine($"Started:   {DateTime.Now.AddSeconds(-phaseSw.Elapsed.TotalSeconds):yyyy-MM-dd HH:mm:ss}");
                    summary.AppendLine($"Finished:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    summary.AppendLine($"Duration:  {phaseSw.Elapsed.TotalSeconds:F1}s");
                    summary.AppendLine($"Result:    {processed}/{totalFiles} files backed up, {errorLog.Count} error(s)");
                    summary.AppendLine($"Integrity: {integrityTag}");
                    summary.AppendLine();
                    summary.AppendLine("─── Errors ───");
                    foreach (var line in errorLog)
                        summary.AppendLine(line);
                    var summaryText = summary.ToString();
                    Console.Write($"[Backup][user] {summaryText}");
                    var errEntry = zip.CreateEntry("_backup_errors.txt", System.IO.Compression.CompressionLevel.Fastest);
                    using var errStream = errEntry.Open();
                    errStream.Write(System.Text.Encoding.UTF8.GetBytes(summaryText));
                }
            }

            if (LPM.Services.BackupProgress.CancelRequested)
                Console.WriteLine($"[Backup][user] CANCELLED by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed}/{totalFiles} files in {phaseSw.Elapsed.TotalSeconds:F1}s");
            else
                Console.WriteLine($"[Backup][user] COMPLETE by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed}/{totalFiles} files in {phaseSw.Elapsed.TotalSeconds:F1}s — {integrityTag}");
        }
        catch (Exception ex)
        {
            LPM.Services.BackupProgress.LastError = ex.Message;
            LPM.Services.BackupProgress.ActiveUser = "";
            Console.WriteLine($"[Backup][user] EXCEPTION by '{user}': {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            LPM.Services.BackupProgress.Running = false;
        }
        return Task.CompletedTask;
    }, "application/zip", fileName);
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
    LPM.Services.BackupProgress.CurrentTempFile = null;
    LPM.Services.BackupProgress.TimedOutFiles.Clear();

    var user = ctx.User.Identity?.Name ?? "unknown";
    var dlIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var fileName = $"LPM-ServerBackup-{integrityTag}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";

    // Tell nginx not to buffer this response — stream straight to the browser
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");

    return Results.Stream(responseStream =>
    {
        int processed = 0;
        var phaseSw = System.Diagnostics.Stopwatch.StartNew();
        var errorLog = new List<string>(); // collects per-file errors for summary
        try
        {
            using (var zip = new System.IO.Compression.ZipArchive(responseStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                // 1. Live DB at root (ready to drop into app folder)
                var dbPath = svc.GetDbFilePath();
                if (File.Exists(dbPath))
                {
                    LPM.Services.BackupProgress.CurrentFile = "lifepower.db";
                    var tempDbCopy = Path.Combine(tempDir, $"db-snap-{Guid.NewGuid():N}.db");
                    var dbSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        svc.BackupDbTo(tempDbCopy);
                        var dbSize = new FileInfo(tempDbCopy).Length;
                        var entry = zip.CreateEntry("lifepower.db", System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); using var fs = File.OpenRead(tempDbCopy); fs.CopyTo(es);
                        dbSw.Stop();
                        Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] lifepower.db | {dbSize:N0} bytes | {dbSw.ElapsedMilliseconds}ms");
                    }
                    finally { try { File.Delete(tempDbCopy); } catch { } }
                    LPM.Services.BackupProgress.Current = ++processed;
                    responseStream.Flush();
                }

                // 2. Auto-backup files (under configured folder name, e.g. db-backups/)
                foreach (var bf in autoBackupFiles)
                {
                    if (LPM.Services.BackupProgress.CancelRequested) break;
                    var bfName = Path.GetFileName(bf);
                    LPM.Services.BackupProgress.CurrentFile = $"{backupFolderName}/{bfName}";
                    var bfSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var bfSize = new FileInfo(bf).Length;
                        var entry = zip.CreateEntry($"{backupFolderName}/{bfName}", System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); using var fs = File.OpenRead(bf); fs.CopyTo(es);
                        bfSw.Stop();
                        Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] {backupFolderName}/{bfName} | {bfSize:N0} bytes | {bfSw.ElapsedMilliseconds}ms");
                        LPM.Services.BackupProgress.Current = ++processed;
                        responseStream.Flush();
                    }
                    catch (Exception ex) { Console.WriteLine($"[Backup][server] ERROR auto-backup {bfName}: {ex.Message}"); }
                }

                // 3. Config files + avatars
                foreach (var (zipPath, fullPath) in extras)
                {
                    if (LPM.Services.BackupProgress.CancelRequested) break;
                    var extSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        LPM.Services.BackupProgress.CurrentFile = zipPath;
                        var extSize = new FileInfo(fullPath).Length;
                        var entry = zip.CreateEntry(zipPath, System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); using var fs = File.OpenRead(fullPath); fs.CopyTo(es);
                        extSw.Stop();
                        Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] {zipPath} | {extSize:N0} bytes | {extSw.ElapsedMilliseconds}ms");
                        LPM.Services.BackupProgress.Current = ++processed;
                        responseStream.Flush();
                    }
                    catch (Exception ex) { Console.WriteLine($"[Backup][server] ERROR extra {zipPath}: {ex.Message}"); }
                }

                // 4. PC files — raw/encrypted, preserving exact server state
                // No File.ReadAllBytes — stream directly disk→zip inside the thread to avoid loading entire file into RAM.
                // Thread.Join(30s) stays safely under nginx proxy_read_timeout (default 60s).
                foreach (var (relPath, fullPath) in rawPcFiles)
                {
                    if (LPM.Services.BackupProgress.CancelRequested) break;
                    if (ctx.RequestAborted.IsCancellationRequested)
                    {
                        Console.WriteLine($"[Backup][server] CLIENT DISCONNECTED at file {processed}/{totalFiles} — stopping");
                        break;
                    }

                    // Skip files that hung in a previous backup this session (EBS bad-block protection)
                    lock (LPM.Services.BackupProgress.PermSkipFiles)
                        if (LPM.Services.BackupProgress.PermSkipFiles.Contains(relPath))
                        {
                            Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] PERM-SKIP {relPath}");
                            errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PERM-SKIP (timed out in earlier phase) — {relPath}");
                            lock (LPM.Services.BackupProgress.TimedOutFiles) LPM.Services.BackupProgress.TimedOutFiles.Add(relPath);
                            continue;
                        }

                    try
                    {
                        LPM.Services.BackupProgress.CurrentFile = relPath;
                        Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] reading: {relPath}");

                        long fileSize = 0;
                        Exception? readEx = null;
                        var readSw = System.Diagnostics.Stopwatch.StartNew();

                        // Read raw bytes in a separate thread for reliable OS-level timeout
                        byte[]? rawBytes = null;
                        var readThread = new Thread(() =>
                        {
                            try
                            {
                                fileSize = new FileInfo(fullPath).Length;
                                rawBytes = File.ReadAllBytes(fullPath);
                            }
                            catch (Exception ex) { readEx = ex; }
                        }) { IsBackground = true };
                        readThread.Start();

                        if (!readThread.Join(TimeSpan.FromSeconds(30)))
                        {
                            Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] TIMEOUT 30s — skipping permanently: {relPath}");
                            errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TIMEOUT (30s read timeout) — {relPath}");
                            lock (LPM.Services.BackupProgress.PermSkipFiles) LPM.Services.BackupProgress.PermSkipFiles.Add(relPath);
                            lock (LPM.Services.BackupProgress.TimedOutFiles) LPM.Services.BackupProgress.TimedOutFiles.Add(relPath);
                            continue;
                        }
                        var readMs = readSw.ElapsedMilliseconds;

                        if (readEx != null) { Console.WriteLine($"[Backup][server] read-thread ERROR {relPath}: {readEx.Message}"); errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] READ ERROR ({readEx.Message}) — {relPath}"); continue; }
                        if (rawBytes == null) { Console.WriteLine($"[Backup][server] NULL bytes: {relPath}"); errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NULL BYTES (read returned no data) — {relPath}"); continue; }

                        var writeSw = System.Diagnostics.Stopwatch.StartNew();
                        var entry = zip.CreateEntry(relPath, System.IO.Compression.CompressionLevel.Fastest);
                        using var es = entry.Open(); es.Write(rawBytes);
                        writeSw.Stop();

                        Console.WriteLine($"[Backup][server][{processed + 1}/{totalFiles}] OK {relPath} | {fileSize:N0} bytes | read {readMs}ms | write {writeSw.ElapsedMilliseconds}ms | total {readMs + writeSw.ElapsedMilliseconds}ms");
                        LPM.Services.BackupProgress.Current = ++processed;

                        // Flush after every file to prevent nginx proxy_read_timeout during slow reads
                        responseStream.Flush();

                        // Release memory immediately; GC every 200 files or for large files
                        rawBytes = null;
                        if (processed % 200 == 0)
                            GC.Collect(1, GCCollectionMode.Optimized);
                        else if (fileSize > 50 * 1024 * 1024)
                            GC.Collect(0, GCCollectionMode.Optimized);
                    }
                    catch (Exception ex) { Console.WriteLine($"[Backup][server] OUTER ERROR {relPath}: {ex.Message}"); errorLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR ({ex.Message}) — {relPath}"); }
                }

                // Write error summary file if there were any problems
                if (errorLog.Count > 0)
                {
                    var summary = new System.Text.StringBuilder();
                    summary.AppendLine($"LPM Server Backup — Error Summary");
                    summary.AppendLine($"Backup by: {user} from {dlIp}");
                    summary.AppendLine($"Started:   {DateTime.Now.AddSeconds(-phaseSw.Elapsed.TotalSeconds):yyyy-MM-dd HH:mm:ss}");
                    summary.AppendLine($"Finished:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    summary.AppendLine($"Duration:  {phaseSw.Elapsed.TotalSeconds:F1}s");
                    summary.AppendLine($"Result:    {processed}/{totalFiles} files backed up, {errorLog.Count} error(s)");
                    summary.AppendLine($"Integrity: {integrityTag}");
                    summary.AppendLine();
                    summary.AppendLine("─── Errors ───");
                    foreach (var line in errorLog)
                        summary.AppendLine(line);
                    var summaryText = summary.ToString();
                    Console.Write($"[Backup][server] {summaryText}");
                    var errEntry = zip.CreateEntry("_backup_errors.txt", System.IO.Compression.CompressionLevel.Fastest);
                    using var errStream = errEntry.Open();
                    errStream.Write(System.Text.Encoding.UTF8.GetBytes(summaryText));
                }
            }

            if (LPM.Services.BackupProgress.CancelRequested)
                Console.WriteLine($"[Backup][server] CANCELLED by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed}/{totalFiles} files in {phaseSw.Elapsed.TotalSeconds:F1}s");
            else
                Console.WriteLine($"[Backup][server] COMPLETE by '{user}' from {dlIp} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {processed}/{totalFiles} files in {phaseSw.Elapsed.TotalSeconds:F1}s — {integrityTag}");
        }
        catch (Exception ex)
        {
            LPM.Services.BackupProgress.LastError = ex.Message;
            LPM.Services.BackupProgress.ActiveUser = "";
            Console.WriteLine($"[Backup][server] EXCEPTION by '{user}': {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            LPM.Services.BackupProgress.Running = false;
        }
        return Task.CompletedTask;
    }, "application/zip", fileName);
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

// ── File-by-file backup: file list ──
app.MapGet("/api/backup-file-list", (HttpContext ctx, LPM.Services.FolderService svc, IConfiguration cfg) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();
    var token = ctx.Request.Query["token"].ToString();
    if (!LPM.Services.BackupProgress.ValidateToken(token)) return Results.Unauthorized();

    var phase = ctx.Request.Query["phase"].ToString();
    if (phase is not "user" and not "server") return Results.BadRequest("phase must be 'user' or 'server'");

    var backupFolder = svc.GetAutoBackupFolder(cfg["Database:BackupFolder"]);
    var autoBackupFiles = Directory.Exists(backupFolder)
        ? Directory.GetFiles(backupFolder, "lifepower_*.db").OrderByDescending(f => f).ToArray()
        : Array.Empty<string>();

    var files = new List<object>();

    // DB snapshot
    var dbPath = svc.GetDbFilePath();
    if (File.Exists(dbPath))
    {
        var dbTarget = phase == "user" ? "db-live/lifepower.db" : "lifepower.db";
        files.Add(new { path = dbTarget, modified = File.GetLastWriteTimeUtc(dbPath).ToString("o"), alwaysDownload = true });
    }

    // Auto-backup DBs
    var backupFolderName = phase == "server" ? Path.GetFileName(backupFolder.TrimEnd('/', '\\')) : "db-autobackups";
    foreach (var bf in autoBackupFiles)
    {
        var bfName = Path.GetFileName(bf);
        files.Add(new { path = $"{backupFolderName}/{bfName}", modified = File.GetLastWriteTimeUtc(bf).ToString("o"), alwaysDownload = true });
    }

    // Server extras (config + avatars)
    if (phase == "server")
    {
        foreach (var (zipPath, fullPath) in svc.GetServerBackupExtras())
            files.Add(new { path = zipPath, modified = File.GetLastWriteTimeUtc(fullPath).ToString("o"), alwaysDownload = false });
    }

    // PC files
    foreach (var (relPath, fullPath) in svc.EnumerateBackupFiles())
    {
        var isFolderSummary = LPM.Services.FolderService.ParseFolderSummaryBackupPath(relPath).IsSummary;
        files.Add(new { path = relPath, modified = File.GetLastWriteTimeUtc(fullPath).ToString("o"), alwaysDownload = isFolderSummary });
    }

    Console.WriteLine($"[BackupFileList] {phase} phase: {files.Count} files listed for '{ctx.User.Identity?.Name}'");
    return Results.Ok(files);
});

// ── File-by-file backup: single file download ──
app.MapGet("/api/backup-file", (HttpContext ctx, LPM.Services.FolderService svc, IConfiguration cfg,
    LPM.Services.DashboardService dashSvc, PdfService pdfSvc) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();
    var token = ctx.Request.Query["token"].ToString();
    if (!LPM.Services.BackupProgress.ValidateToken(token)) return Results.Unauthorized();

    var phase = ctx.Request.Query["phase"].ToString();
    var reqPath = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(reqPath) || phase is not "user" and not "server")
        return Results.BadRequest("phase and path required");

    // Path traversal protection
    if (reqPath.Contains("..") || reqPath.Contains('\0'))
        return Results.BadRequest("invalid path");

    byte[]? bytes = null;

    // Route to correct file source
    if (reqPath.StartsWith("db-live/") || reqPath == "lifepower.db")
    {
        // Live DB snapshot
        var tempCopy = Path.Combine(Path.GetTempPath(), $"db-snap-{Guid.NewGuid():N}.db");
        try
        {
            svc.BackupDbTo(tempCopy);
            bytes = File.ReadAllBytes(tempCopy);
        }
        finally { try { File.Delete(tempCopy); } catch { } }
    }
    else if (reqPath.StartsWith("db-autobackups/") || reqPath.StartsWith(Path.GetFileName(svc.GetAutoBackupFolder(cfg["Database:BackupFolder"]).TrimEnd('/', '\\')) + "/"))
    {
        // Auto-backup DB file
        var backupFolder = svc.GetAutoBackupFolder(cfg["Database:BackupFolder"]);
        var fileName = Path.GetFileName(reqPath);
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return Results.BadRequest("invalid path");
        var fullPath = Path.Combine(backupFolder, fileName);
        if (!File.Exists(fullPath)) return Results.NotFound();
        bytes = File.ReadAllBytes(fullPath);
    }
    else if (reqPath.StartsWith("PC-Folders/"))
    {
        // PC file — find full path via EnumerateBackupFiles match
        var match = svc.EnumerateBackupFiles().FirstOrDefault(f => f.RelativePath == reqPath);
        if (match.FullPath == null) return Results.NotFound();

        if (phase == "user")
        {
            // Decrypt for user backup — fall back to raw bytes if decryption fails (unencrypted legacy file)
            byte[] decrypted;
            try { decrypted = svc.DecryptFileForBackup(match.FullPath); }
            catch (System.Security.Cryptography.CryptographicException)
            {
                Console.WriteLine($"[BackupFile] Decrypt failed (serving raw): {reqPath}");
                decrypted = File.ReadAllBytes(match.FullPath);
            }

            // Folder Summary: prepend session summaries
            var (isSummary, fspcId, isSolo) = LPM.Services.FolderService.ParseFolderSummaryBackupPath(reqPath);
            if (isSummary)
            {
                try
                {
                    var summaries = dashSvc.GetSessionSummariesForPc(fspcId, isSolo);
                    if (summaries.Count > 0)
                    {
                        var pcName = dashSvc.GetPersonName(fspcId) ?? $"PC {fspcId}";
                        var originalPageCount = pdfSvc.CountPdfPages(decrypted);
                        var summaryPdf = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
                        decrypted = pdfSvc.CombinePdfs(summaryPdf, decrypted);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[BackupFile] FolderSummary combine failed for PC {fspcId}: {ex.Message}"); }
            }
            bytes = decrypted;
        }
        else
        {
            // Raw for server backup
            bytes = File.ReadAllBytes(match.FullPath);
        }
    }
    else if (phase == "server")
    {
        // Server extras (config, avatars)
        var match = svc.GetServerBackupExtras().FirstOrDefault(f => f.ZipPath == reqPath);
        if (match.FullPath == null) return Results.NotFound();
        bytes = File.ReadAllBytes(match.FullPath);
    }
    else
    {
        return Results.NotFound();
    }

    if (bytes == null) return Results.NotFound();
    return Results.File(bytes, "application/octet-stream");
});

// Enable WAL mode for crash-safe writes (runs once; setting persists in the DB file)
app.Services.GetRequiredService<LPM.Services.FolderService>().InitializeDb();
app.Services.GetRequiredService<LPM.Services.UserActivityService>().Initialize();

// Clean up any leftover backup zips from previous runs (crash recovery)
foreach (var stale in Directory.GetFiles(Path.GetTempPath(), "lpm-backup-*.zip"))
    try { File.Delete(stale); } catch { }

// Ensure dummy program insert PDFs exist
LPM.Services.FolderService.EnsureDummyProgramInserts();

app.Run();

// Models for overlay-based annotation save
record AnnPageInfo(string Action, int SrcPageIdx, int W, int H, int ImgIdx);
record AnnSaveMeta(int TotalPages, AnnPageInfo[] Pages);
