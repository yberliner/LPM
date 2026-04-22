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
using Fido2NetLib;
using Fido2NetLib.Objects;

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
        // Server pings client every 15s (must be < client's 30s default serverTimeout)
        options.KeepAliveInterval      = TimeSpan.FromSeconds(15);
        // Server tolerates client silence for 12 min (long backups / GC pauses)
        options.ClientTimeoutInterval  = TimeSpan.FromMinutes(12);
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
builder.Services.AddSingleton<LPM.Services.FileAuditService>();
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
builder.Services.AddSingleton<LPM.Services.EmailService>();

// Fido2 (WebAuthn / Passkeys)
builder.Services.AddFido2(options =>
{
    options.ServerDomain = builder.Configuration["Fido2:ServerDomain"] ?? "localhost";
    options.ServerName = builder.Configuration["Fido2:ServerName"] ?? "LPM System";
    options.Origins = builder.Configuration.GetSection("Fido2:Origins").Get<HashSet<string>>()
        ?? new HashSet<string> { "http://localhost:5000" };
});
builder.Services.AddSingleton<LPM.Services.ImportJobService>();
builder.Services.AddSingleton<LPM.Services.CompletionService>();
builder.Services.AddSingleton<LPM.Services.QuestionService>();
builder.Services.AddSingleton<LPM.Services.EffortService>();
builder.Services.AddSingleton<LPM.Services.WalletService>();
builder.Services.AddScoped<LPM.Services.SessionManagerLauncher>();
builder.Services.AddHostedService<LPM.Services.MaintenanceService>();

// Add session services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(12);
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
        options.ExpireTimeSpan = TimeSpan.FromDays(36500);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(username))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                    return;
                }
                // Throttle: only check DB every 60 seconds (avoid per-request overhead on SignalR)
                var lastCheck = context.Properties.GetString("LastActiveCheck");
                if (lastCheck != null && DateTime.TryParse(lastCheck, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var lc) && (DateTime.UtcNow - lc).TotalSeconds < 60)
                    return;
                var db = context.HttpContext.RequestServices.GetRequiredService<UserDb>();
                var flags = db.GetLoginFlags(username);
                if (flags == null)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                    Console.WriteLine($"[Auth] Rejected cookie for '{username}' — user inactive or not found");
                    return;
                }
                context.Properties.SetString("LastActiveCheck", DateTime.UtcNow.ToString("O"));
                context.ShouldRenew = true;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DiagnosisAccess", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Admin") ||
            context.User.HasClaim(c => c.Type == "OriginalUser")));
});

var app = builder.Build();
//Globals.ServiceProvider = app.Services;

// In-memory store for Fido2 challenges (avoids cookie size limits)
var fido2Store = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

var autoLoginProtector = app.Services.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("lpm-autologin");

// Register embedded font resolver so PdfSharpCore works on Linux without system fonts
PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = LPM.Services.EmbeddedFontResolver.Instance;

// Register DejaVu Sans + Noto Sans Hebrew with QuestPDF for Latin + Hebrew glyph coverage
{
    var asm = System.Reflection.Assembly.GetExecutingAssembly();
    using var regular = asm.GetManifestResourceStream("LPM.Fonts.DejaVuSans.ttf")!;
    using var bold = asm.GetManifestResourceStream("LPM.Fonts.DejaVuSans-Bold.ttf")!;
    using var hebrewRegular = asm.GetManifestResourceStream("LPM.Fonts.NotoSansHebrew-Regular.ttf")!;
    using var hebrewBold = asm.GetManifestResourceStream("LPM.Fonts.NotoSansHebrew-Bold.ttf")!;
    QuestPDF.Drawing.FontManager.RegisterFont(regular);
    QuestPDF.Drawing.FontManager.RegisterFont(bold);
    QuestPDF.Drawing.FontManager.RegisterFont(hebrewRegular);
    QuestPDF.Drawing.FontManager.RegisterFont(hebrewBold);
}

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

// Force browsers to revalidate sw.js and manifest on every load (prevents stale PWA)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path != null && (path.EndsWith("/sw.js") || path.EndsWith("/manifest.webmanifest")))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }
    await next();
});
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── Auto-login middleware — on / and /index: redirect to dashboard or auto-login ───
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if ((path == "/" || path.Equals("/index", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/login", StringComparison.OrdinalIgnoreCase))
        && ctx.Request.Method == "GET")
    {
        // Already authenticated → check security flags before going to dashboard
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var gateDb = ctx.RequestServices.GetRequiredService<UserDb>();
            var gateUsername = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
            var gateFlags = gateDb.GetLoginFlags(gateUsername);
            if (gateFlags != null)
            {
                if (gateFlags.MustChangePassword)
                { ctx.Response.Redirect("/ChangePassword"); return; }
            }
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
                    )
                {
                    var db   = ctx.RequestServices.GetRequiredService<UserDb>();
                    var info = db.GetAutoLoginInfo(userId);
                    if (info is { IsActive: true } && info.Value.PwdHashPrefix == parts[2])
                    {
                        var flags = db.GetLoginFlagsById(userId);
                        if (flags != null)
                        {
                            // Block auto-login if user must change password
                            if (flags.MustChangePassword)
                            {
                                Console.WriteLine($"[AutoLogin] '{flags.Username}' blocked — needs password change");
                            }
                            else
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
    MaxAge = TimeSpan.FromHours(1)
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
    Expires = DateTimeOffset.UtcNow.AddYears(100)
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

static string HomeOrContact(UserDb db, int userId) => "/Home";

/// <summary>Resolve UserId from claim (new cookies) or fall back to username lookup (old cookies).</summary>
static int ResolveUserId(HttpContext ctx, UserDb db)
{
    if (int.TryParse(ctx.User.FindFirst("UserId")?.Value, out var uid) && uid > 0)
        return uid;
    var username = ctx.User.Identity?.Name ?? "";
    return db.GetLoginFlags(username)?.UserId ?? 0;
}

static async Task SignInUser(HttpContext ctx, UserDb db, string username,
    LPM.Auth.UserDb.LoginFlags flags)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, username),
        new("StaffRole", flags.StaffRole),
        new("PersonId", flags.PersonId.ToString()),
        new("UserId", flags.UserId.ToString()),
    };
    foreach (var r in flags.Roles)
        claims.Add(new Claim(ClaimTypes.Role, r));
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var authProps = new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(36500)
    };
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProps);
    ctx.Response.Cookies.Delete("lpm_pending");

    // Record the actual login activity
    var activitySvc = ctx.RequestServices.GetRequiredService<LPM.Services.UserActivityService>();
    activitySvc.RecordLogin(username);
}

// ── Impersonation (Yaniv-only debug feature) ─────────────────────────────

app.MapPost("/admin/impersonate", async (HttpContext ctx, UserDb db) =>
{
    var originalUser = ctx.User.FindFirst("OriginalUser")?.Value;
    var currentUser  = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
    var realUser     = originalUser ?? currentUser;
    if (!string.Equals(realUser, "yaniv", StringComparison.OrdinalIgnoreCase))
        return Results.Redirect("/Admin/Diagnosis");

    var form = await ctx.Request.ReadFormAsync();
    var target = form["targetUsername"].ToString().Trim();
    if (string.IsNullOrEmpty(target))
        return Results.Redirect("/Admin/Diagnosis");

    var flags = db.GetLoginFlags(target);
    if (flags == null)
        return Results.Redirect("/Admin/Diagnosis");

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, flags.Username),
        new("StaffRole", flags.StaffRole),
        new("PersonId", flags.PersonId.ToString()),
        new("UserId", flags.UserId.ToString()),
        new("OriginalUser", "yaniv"),
    };
    foreach (var r in flags.Roles)
        claims.Add(new Claim(ClaimTypes.Role, r));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var authProps = new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(36500)
    };
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProps);
    Console.WriteLine($"[Impersonate] yaniv → {flags.Username}");
    return Results.Redirect("/Home");
});

app.MapPost("/admin/stop-impersonate", async (HttpContext ctx, UserDb db) =>
{
    var originalUser = ctx.User.FindFirst("OriginalUser")?.Value;
    if (!string.Equals(originalUser, "yaniv", StringComparison.OrdinalIgnoreCase))
        return Results.Redirect("/Home");

    var flags = db.GetLoginFlags("yaniv");
    if (flags == null)
        return Results.Redirect("/login");

    await SignInUser(ctx, db, "yaniv", flags);
    Console.WriteLine("[Impersonate] Returned to yaniv");
    return Results.Redirect("/Admin/Diagnosis");
});

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

    // Step 1: force password change?
    if (flags.MustChangePassword)
    {
        ctx.Response.Cookies.Append("lpm_pending", flags.UserId.ToString(), PendingCookieOpts());
        Console.WriteLine($"[Login] '{username}' must change password");
        return Results.Redirect("/ChangePassword");
    }

    // Step 2: check trusted device
    var trustedToken = ctx.Request.Cookies["lpm_trusted"];
    if (trustedToken != null && db.GetTrustedDeviceUserId(trustedToken) == flags.UserId)
    {
        await SignInUser(ctx, db, username, flags);
        ApplyAutoLogin(ctx, db, autoLoginProtector, flags.UserId);
        Console.WriteLine($"[Login] '{username}' signed in via trusted device");
        return Results.Redirect("/Home");
    }

    // Step 3: new/untrusted device — send magic link email
    var email = db.GetPersonEmail(flags.PersonId);
    if (string.IsNullOrWhiteSpace(email))
    {
        Console.WriteLine($"[Login] '{username}' has no email — cannot verify device");
        return Results.Redirect("/login?error=noemail");
    }

    var emailSvc = ctx.RequestServices.GetRequiredService<LPM.Services.EmailService>();
    var code = db.CreateMagicLink(flags.PersonId, flags.UserId);
    var displayName = db.GetPersonDisplayName(flags.PersonId) ?? username;
    var emailSent = await emailSvc.SendVerificationCodeAsync(email, code, displayName);
    if (!emailSent)
    {
        Console.WriteLine($"[Login] FAILED to send verification email to '{email}' for '{username}'");
        return Results.Redirect("/login?error=emailfail");
    }
    ctx.Response.Cookies.Append("lpm_pending", flags.UserId.ToString(), PendingCookieOpts());
    Console.WriteLine($"[Login] Verification code sent to '{email}' for '{username}'");
    return Results.Redirect($"/VerifyDevice?email={Uri.EscapeDataString(email)}");
}).DisableAntiforgery();

// ── /loginpost-changepwd ──────────────────────────────────────────────────

app.MapPost("/loginpost-changepwd", async (HttpContext ctx, UserDb db, IConfiguration config) =>
{
    int userId;
    if (int.TryParse(ctx.Request.Cookies["lpm_pending"], out var pendingId))
    {
        userId = pendingId;
    }
    else if (ctx.User.Identity?.IsAuthenticated == true)
    {
        // Already signed in (redirected from Home guard) — resolve userId from auth cookie
        var authUsername = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        var authFlags = db.GetLoginFlags(authUsername);
        if (authFlags == null) return Results.Redirect("/login");
        userId = authFlags.UserId;
    }
    else
    {
        return Results.Redirect("/login");
    }

    var form = await ctx.Request.ReadFormAsync();
    var newPwd = form["newPassword"].ToString();
    if (string.IsNullOrWhiteSpace(newPwd))
        return Results.Redirect("/ChangePassword?error=empty");

    db.ForceSetPassword(userId, newPwd);
    Console.WriteLine($"[Login] userId={userId} changed password");

    // Re-fetch flags (MustChangePassword now 0)
    var flags2 = db.GetLoginFlagsById(userId);
    if (flags2 == null) return Results.Redirect("/login");

    // Check trusted device — if trusted, sign in directly
    var trustedToken = ctx.Request.Cookies["lpm_trusted"];
    if (trustedToken != null && db.GetTrustedDeviceUserId(trustedToken) == userId)
    {
        await SignInUser(ctx, db, flags2.Username, flags2);
        ApplyAutoLogin(ctx, db, autoLoginProtector, flags2.UserId);
        return Results.Redirect("/Home");
    }

    // Not trusted — send magic link
    var email = db.GetPersonEmail(flags2.PersonId);
    if (string.IsNullOrWhiteSpace(email))
    {
        // No email — sign in directly (password change was the security step)
        await SignInUser(ctx, db, flags2.Username, flags2);
        ApplyAutoLogin(ctx, db, autoLoginProtector, flags2.UserId);
        return Results.Redirect("/Home");
    }

    var emailSvc = ctx.RequestServices.GetRequiredService<LPM.Services.EmailService>();
    var mlCode = db.CreateMagicLink(flags2.PersonId, flags2.UserId);
    var dispName = db.GetPersonDisplayName(flags2.PersonId) ?? flags2.Username;
    var mlSent = await emailSvc.SendVerificationCodeAsync(email, mlCode, dispName);
    if (!mlSent)
        return Results.Redirect("/login?error=emailfail");
    ctx.Response.Cookies.Append("lpm_pending", userId.ToString(), PendingCookieOpts());
    return Results.Redirect($"/VerifyDevice?email={Uri.EscapeDataString(email)}");
}).DisableAntiforgery();

// ── /verify-code ─────────────────────────────────────────────────────────

app.MapPost("/verify-code", async (HttpContext ctx, UserDb db) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var code = form["code"].ToString().Trim();
    // Recover email for error redirects
    var pendingEmail = "";
    if (int.TryParse(ctx.Request.Cookies["lpm_pending"], out var pendingUid))
    {
        var pendingFlags = db.GetLoginFlagsById(pendingUid);
        if (pendingFlags != null)
            pendingEmail = db.GetPersonEmail(pendingFlags.PersonId) ?? "";
    }
    var emailParam = string.IsNullOrEmpty(pendingEmail) ? "" : $"&email={Uri.EscapeDataString(pendingEmail)}";

    if (string.IsNullOrEmpty(code))
        return Results.Redirect($"/VerifyDevice?error=empty{emailParam}");

    var userId = db.ValidateMagicLink(code);
    if (userId == null)
    {
        Console.WriteLine($"[Auth] Invalid or expired code: '{code}'");
        return Results.Redirect($"/VerifyDevice?error=invalid{emailParam}");
    }

    var flags = db.GetLoginFlagsById(userId.Value);
    if (flags == null)
        return Results.Redirect("/login");

    SetTrustCookie(ctx, db, flags.UserId);
    await SignInUser(ctx, db, flags.Username, flags);
    ApplyAutoLogin(ctx, db, autoLoginProtector, flags.UserId);
    Console.WriteLine($"[Auth] Code verified — '{flags.Username}' signed in, device trusted");
    return Results.Redirect("/Home");
}).DisableAntiforgery();

// ── Passkey endpoints ────────────────────────────────────────────────────

app.MapPost("/api/passkey/register-options", (HttpContext ctx, UserDb db, IFido2 fido2) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var userId = ResolveUserId(ctx, db);
    if (userId == 0) return Results.Unauthorized();
    if (!int.TryParse(ctx.User.FindFirst("PersonId")?.Value, out var personId)) return Results.Unauthorized();

    var displayName = db.GetPersonDisplayName(personId) ?? "User";
    var username = ctx.User.Identity.Name ?? "";

    var existingKeys = db.GetPasskeysByUser(userId)
        .Select(p => new PublicKeyCredentialDescriptor(PublicKeyCredentialType.PublicKey, p.CredentialId, null))
        .ToList();

    var fidoUser = new Fido2User
    {
        Id = Encoding.UTF8.GetBytes(userId.ToString()),
        Name = username,
        DisplayName = displayName
    };

    var authSelection = new AuthenticatorSelection
    {
        UserVerification = UserVerificationRequirement.Preferred,
        ResidentKey = ResidentKeyRequirement.Preferred
    };

    var options = fido2.RequestNewCredential(new RequestNewCredentialParams
    {
        User = fidoUser,
        ExcludeCredentials = existingKeys,
        AuthenticatorSelection = authSelection,
        AttestationPreference = AttestationConveyancePreference.None
    });

    fido2Store[$"reg_{userId}"] = options.ToJson();

    return Results.Text(options.ToJson(), "application/json");
}).DisableAntiforgery();

app.MapPost("/api/passkey/register", async (HttpContext ctx, UserDb db, IFido2 fido2) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var userId = ResolveUserId(ctx, db);
    if (userId == 0) return Results.Unauthorized();
    if (!int.TryParse(ctx.User.FindFirst("PersonId")?.Value, out var personId)) return Results.Unauthorized();

    if (!fido2Store.TryRemove($"reg_{userId}", out var optionsJson))
        return Results.BadRequest("No pending registration");

    var options = CredentialCreateOptions.FromJson(optionsJson);
    using var regReader = new StreamReader(ctx.Request.Body);
    var body = await regReader.ReadToEndAsync();
    var response = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(body);
    if (response == null) return Results.BadRequest("Invalid response");

    try
    {
        var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = (args, ct) => Task.FromResult(db.GetPasskeyByCredentialId(args.CredentialId) == null)
        });

        db.AddPasskey(personId, userId, credential.Id, credential.PublicKey,
            (int)credential.SignCount, "Passkey");

        Console.WriteLine($"[Auth] Passkey registered for UserId={userId}");
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Auth] Passkey registration failed: {ex.Message}");
        return Results.BadRequest(ex.Message);
    }
}).DisableAntiforgery();

app.MapPost("/api/passkey/login-options", async (HttpContext ctx, UserDb db, IFido2 fido2) =>
{
    using var optReader = new StreamReader(ctx.Request.Body);
    var body = await optReader.ReadToEndAsync();
    var req = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
    var username = req.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
    if (string.IsNullOrEmpty(username)) return Results.BadRequest("Username required");

    var loginFlags = db.GetLoginFlags(username);
    if (loginFlags == null) return Results.Json(new { hasPasskeys = false });

    var passkeys = db.GetPasskeysByUser(loginFlags.UserId);
    if (passkeys.Count == 0) return Results.Json(new { hasPasskeys = false });

    var allowedCredentials = passkeys
        .Select(p => new PublicKeyCredentialDescriptor(PublicKeyCredentialType.PublicKey, p.CredentialId, null))
        .ToList();

    var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
    {
        AllowedCredentials = allowedCredentials,
        UserVerification = UserVerificationRequirement.Preferred
    });

    var loginKey = $"login_{loginFlags.UserId}";
    fido2Store[loginKey] = options.ToJson();

    return Results.Text(options.ToJson(), "application/json");
}).DisableAntiforgery();

app.MapPost("/api/passkey/login", async (HttpContext ctx, UserDb db, IFido2 fido2) =>
{
    // We need to find which user this assertion belongs to from the credential
    using var loginReader = new StreamReader(ctx.Request.Body);
    var body = await loginReader.ReadToEndAsync();
    var response = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(body);
    if (response == null) return Results.BadRequest("Invalid response");

    var passkey = db.GetPasskeyByCredentialId(response.RawId);
    if (passkey == null) return Results.BadRequest("Unknown credential");

    var loginKey = $"login_{passkey.UserId}";
    if (!fido2Store.TryRemove(loginKey, out var optionsJson))
        return Results.BadRequest("No pending challenge");

    var options = AssertionOptions.FromJson(optionsJson);

    try
    {
        var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = response,
            OriginalOptions = options,
            StoredPublicKey = passkey.PublicKey,
            StoredSignatureCounter = (uint)passkey.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, ct) => Task.FromResult(db.GetPasskeyByCredentialId(args.CredentialId) != null)
        });

        db.UpdatePasskeySignCount(passkey.Id, (int)result.SignCount);

        var flags = db.GetLoginFlagsById(passkey.UserId);
        if (flags == null) return Results.BadRequest("User not found");

        SetTrustCookie(ctx, db, flags.UserId);
        await SignInUser(ctx, db, flags.Username, flags);
        ApplyAutoLogin(ctx, db, autoLoginProtector, flags.UserId);

        Console.WriteLine($"[Auth] Passkey login for '{flags.Username}'");
        return Results.Json(new { success = true, redirect = "/Home" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Auth] Passkey login failed: {ex.Message}");
        return Results.BadRequest("Verification failed");
    }
}).DisableAntiforgery();

app.MapPost("/api/passkey/remove", (HttpContext ctx, UserDb db) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var userId = ResolveUserId(ctx, db);
    if (userId == 0) return Results.Unauthorized();

    var form = ctx.Request.ReadFormAsync().GetAwaiter().GetResult();
    if (!int.TryParse(form["id"].ToString(), out var passkeyId)) return Results.BadRequest("Invalid id");

    var removed = db.RemovePasskey(passkeyId, userId);
    Console.WriteLine($"[Auth] Passkey {passkeyId} removed for UserId={userId}: {removed}");
    return Results.Json(new { success = removed });
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

// ── PWA Share Target: receive scanned PDF ──
app.MapPost("/share-receive", async (HttpContext ctx, LPM.Services.FolderService svc,
    LPM.Services.UserActivityService activitySvc) =>
{
    var username = ctx.User.Identity?.Name;
    if (string.IsNullOrEmpty(username))
        return Results.Redirect("/login");

    var form = await ctx.Request.ReadFormAsync();
    // Try "files" (manifest field name), then any file
    var file = form.Files.GetFile("files") ?? form.Files.FirstOrDefault();
    if (file == null || file.Length == 0)
        return Results.Redirect("/scan-received?status=nofile");

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    svc.SaveToScanInbox(username, file.FileName ?? "scan.pdf", ms.ToArray());

    var savedName = file.FileName ?? "scan.pdf";
    activitySvc.RecordActivity(username, $"Shared '{savedName}' to scan inbox", "scan");
    activitySvc.RecordInteraction(username);

    Console.WriteLine($"[ShareTarget] Received '{file.FileName}' ({file.Length} bytes) from '{username}'");
    return Results.Redirect($"/scan-received?status=ok&name={Uri.EscapeDataString(savedName)}");
});

app.MapGet("/api/pc-file", (int pcId, string path, LPM.Services.FolderService svc,
    LPM.Services.DashboardService dashSvc, HttpContext ctx, bool solo = false, bool download = false) =>
{
    var user = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "(unknown)";
    // PDF.js issues many range requests per file; suppress the per-chunk noise and log
    // only the initial non-range GET. The Range header is the reliable signal.
    var isRange = ctx.Request.Headers.ContainsKey("Range");
    if (!isRange)
        Console.WriteLine($"[api/pc-file] user='{user}' pcId={pcId} solo={solo} download={download} path='{path}'");
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc))
    {
        Console.WriteLine($"[api/pc-file] FORBIDDEN for user='{user}' pcId={pcId}");
        return Results.Forbid();
    }
    var bytes = download
        ? svc.ReadFileBytesForDownload(pcId, path, solo)
        : svc.ReadFileBytes(pcId, path, solo);
    if (!isRange)
        Console.WriteLine($"[api/pc-file] bytes={(bytes == null ? "NULL (not found)" : bytes.Length + " bytes")}");
    if (bytes == null) return Results.NotFound();
    var ext = Path.GetExtension(path).ToLowerInvariant();
    var mime = ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/pdf"
    };
    return Results.File(bytes, mime, enableRangeProcessing: true);
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
    PdfService pdfSvc, HttpContext ctx, bool solo = false, bool summaryOnly = false) =>
{
    if (!CanAccessPcFile(ctx, pcId, solo, dashSvc)) return Results.Forbid();
    var originalBytes = folderSvc.ReadFileBytes(pcId, path, solo);
    if (originalBytes == null) return Results.NotFound();

    var pcName = dashSvc.GetPersonName(pcId) ?? $"PC {pcId}";
    var summaries = dashSvc.GetSessionSummariesForPc(pcId, isSolo: solo);
    Console.WriteLine($"[FolderSummary] PC {pcId} ({pcName}) solo={solo} summaryOnly={summaryOnly}: {summaries.Count} summaries found");
    if (summaries.Count == 0)
        return Results.File(originalBytes, "application/pdf");

    // Phase 1: return just the QuestPDF summary table (no combine with original)
    if (summaryOnly)
    {
        int origPages = 0;
        try { origPages = pdfSvc.CountPdfPages(originalBytes); } catch { }
        var summaryPdf = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, origPages);
        return Results.File(summaryPdf, "application/pdf");
    }

    int originalPageCount = 0;
    int summaryPageCount  = 0;
    byte[]? combined      = null;

    // Count original pages via PdfSharpCore; fall back to Ghostscript for PDFs
    // PdfSharpCore can't parse (e.g. malformed XRef). Getting this right up front
    // avoids the fragile post-combine recovery path that can produce mismatched
    // page numbers in the baked summary when page counts don't agree.
    try { originalPageCount = pdfSvc.CountPdfPages(originalBytes); }
    catch (Exception ex) { Console.WriteLine($"[FolderSummary] PdfSharpCore CountPdfPages threw for PC {pcId}: {ex.Message}"); }
    if (originalPageCount <= 0)
    {
        var gsCount = folderSvc.CountPdfPagesViaGhostscript(originalBytes);
        if (gsCount.HasValue && gsCount.Value > 0)
        {
            originalPageCount = gsCount.Value;
            Console.WriteLine($"[FolderSummary] Recovered originalPageCount={originalPageCount} via Ghostscript for PC {pcId}");
        }
    }
    Console.WriteLine($"[FolderSummary] originalPageCount for PC {pcId}: {originalPageCount}");

    // ── Step 1: Ghostscript combine (primary — authoritative PDF impl, handles
    // complex/older PDFs that PdfSharpCore silently damages). Returns null when
    // Ghostscript is not configured — falls through to PdfSharpCore below.
    try
    {
        var summaryPdf = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
        summaryPageCount = pdfSvc.CountPdfPages(summaryPdf);
        combined = folderSvc.CombinePdfsViaGhostscript(summaryPdf, originalBytes);

        // If originalPageCount was 0 (PdfSharpCore couldn't read it), recover from the combined output
        if (combined != null && originalPageCount <= 0)
        {
            int combinedPages = pdfSvc.CountPdfPages(combined); // GS output is clean, PdfSharpCore can read it
            originalPageCount = combinedPages - summaryPageCount;
            Console.WriteLine($"[FolderSummary] Recovered originalPageCount={originalPageCount} from GS combined ({combinedPages} - {summaryPageCount})");
            if (originalPageCount > 0)
            {
                // Regenerate summary with correct page numbers and recombine
                var summaryPdf2 = pdfSvc.GenerateSessionSummariesPdf(pcName, summaries, originalPageCount);
                combined = folderSvc.CombinePdfsViaGhostscript(summaryPdf2, originalBytes);
            }
        }

        if (combined == null)
            Console.WriteLine($"[FolderSummary] Ghostscript returned null for PC {pcId} path={path} — trying PdfSharpCore");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FolderSummary] Ghostscript combine threw for PC {pcId}: {ex.Message} — trying PdfSharpCore");
    }

    // ── Step 2: PdfSharpCore fallback (when Ghostscript unavailable or failed) ──
    if (combined == null)
    {
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
                                  $"({combinedCount} pages, expected ≥{expectedMin}) for PC {pcId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderSummary] PdfSharpCore combine failed for PC {pcId}: {ex.Message}");
        }
    }

    // Do NOT normalize page sizes here: the normalizer re-draws each page via
    // PdfSharpCore XPdfForm, which rasterizes the content stream but DROPS the
    // page's /Annots array — so user-drawn ink/pen annotations on the original
    // disappear. The viewer can handle per-page size differences; preserving
    // annotation content matters more than uniform canvas sizing.

    // ── Step 3: all attempts failed ────────────────────────────────────────
    if (combined == null)
    {
        Console.WriteLine($"[FolderSummary] ALL combine attempts failed for PC {pcId} path={path} — returning 500");
        return Results.Problem("Folder summary generation failed", statusCode: 500);
    }

    return Results.File(combined, "application/pdf", enableRangeProcessing: true);
}).RequireAuthorization();

// Per-cell breakdown for the Statistics page hover tooltip.
// metric: "audit" | "csolo" | "effort"
// staffId: PersonId of auditor / CS / effort-performer
// start, end: "yyyy-MM-dd" (inclusive). For week view these are the 7-day range;
//             month view is the full month; day view is the single day (start==end).
app.MapGet("/api/stats-cell-detail", (string metric, int staffId, string start, string end,
    LPM.Services.StatisticsService svc) =>
{
    if (!DateOnly.TryParse(start, out var s) || !DateOnly.TryParse(end, out var e))
        return Results.BadRequest("Invalid start/end date");
    return metric switch
    {
        "audit"  => Results.Json(svc.GetAuditDetail(staffId, s, e)),
        "csolo"  => Results.Json(svc.GetSoloCsDetail(staffId, s, e)),
        "cs"     => Results.Json(svc.GetCsDetail(staffId, s, e)),
        "effort" => Results.Json(svc.GetEffortDetail(staffId, s, e)),
        _        => Results.BadRequest("Unknown metric"),
    };
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
    // Folder Summary is read-only — reject sidecar writes too (defense in depth)
    if (LPM.Services.FolderService.IsFolderSummaryRelPath(path))
    {
        Console.WriteLine($"[AnnSidecar] REJECTED — Folder Summary is read-only: pcId={pcId} path='{path}'");
        return Results.BadRequest("Folder Summary is read-only.");
    }
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
    // Folder Summary is read-only — reject generic file saves too (defense in depth)
    if (LPM.Services.FolderService.IsFolderSummaryRelPath(path))
    {
        Console.WriteLine($"[PcFile] REJECTED — Folder Summary is read-only: pcId={pcId} path='{path}'");
        return Results.BadRequest("Folder Summary is read-only.");
    }

    using var ms = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(ms);
    var saveUser = ctx.User?.Identity?.Name ?? "unknown";
    Console.WriteLine($"[PcFile] Saved PC file pcId={pcId}: {path} by '{saveUser}'");
    return svc.SaveFile(pcId, path, ms.ToArray(), solo, auditUser: saveUser) ? Results.Ok() : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/purchase-receipt/pdf", async (HttpContext ctx, PdfService pdfSvc) =>
{
    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();
    PurchaseReceiptRequest? req;
    try
    {
        req = System.Text.Json.JsonSerializer.Deserialize<PurchaseReceiptRequest>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch { return Results.BadRequest("Invalid JSON."); }
    if (req is null) return Results.BadRequest("Missing body.");

    byte[]? sig = null;
    if (!string.IsNullOrEmpty(req.SignatureDataUrl))
    {
        var comma = req.SignatureDataUrl.IndexOf(',');
        var b64 = comma > 0 ? req.SignatureDataUrl[(comma + 1)..] : req.SignatureDataUrl;
        try { sig = Convert.FromBase64String(b64); } catch { sig = null; }
    }

    var items = (req.Items ?? new()).Select(i => new PdfService.PurchaseReceiptItem(
        i.ItemType ?? "",
        i.CourseName ?? "",
        i.HoursBought,
        i.AmountPaid,
        i.RegistrarName ?? "",
        i.ReferralName ?? ""
    )).ToList();

    byte[] bytes;
    try
    {
        bytes = pdfSvc.GeneratePurchaseReceiptPdf(req.PcName ?? "", req.DateStr ?? "", items, req.Notes, sig);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PurchaseReceipt] PDF generation failed: {ex.Message}");
        return Results.Problem("Failed to generate PDF.");
    }

    var invalid = System.IO.Path.GetInvalidFileNameChars();
    var safePc = new string((req.PcName ?? "PC").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    var safeDate = (req.DateStr ?? "").Replace('/', '-');
    var fileName = $"Purchase Receipt - {safePc} - {safeDate}.pdf";
    return Results.File(bytes, "application/pdf", fileName);
}).RequireAuthorization();

// ── Backup errors PDF (Admin only) ──
app.MapPost("/api/backup-errors/pdf", async (HttpContext ctx, PdfService pdfSvc) =>
{
    if (ctx.User?.IsInRole("Admin") != true) return Results.Forbid();

    using var reader = new System.IO.StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();
    BackupErrorsPdfRequest? req;
    try
    {
        req = System.Text.Json.JsonSerializer.Deserialize<BackupErrorsPdfRequest>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch { return Results.BadRequest("Invalid JSON."); }
    if (req is null) return Results.BadRequest("Missing body.");

    var rows = (req.Errors ?? new()).Select(e =>
    {
        DateTime when;
        if (!DateTime.TryParse(e.When, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out when))
            when = DateTime.Now;
        return new PdfService.BackupErrorPdfRow(
            e.Type ?? "",
            e.RelPath ?? "",
            e.FullPath ?? "",
            string.IsNullOrWhiteSpace(e.PcName) ? null : e.PcName,
            e.Detail ?? "",
            when.ToLocalTime());
    }).ToList();

    byte[] bytes;
    try
    {
        bytes = pdfSvc.GenerateBackupErrorsPdf(rows, req.UserName ?? (ctx.User.Identity?.Name ?? ""));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BackupErrorsPdf] PDF generation failed: {ex.Message}");
        return Results.Problem("Failed to generate PDF.");
    }

    var fileName = $"LPM-BackupErrors-{DateTime.Now:yyyyMMdd-HHmm}.pdf";
    return Results.File(bytes, "application/pdf", fileName);
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

    // Folder Summary files are read-only. The pane displays a combined PDF (session
    // summaries from sess_folder_summary DB + canvas on disk); saving client-side
    // annotations would corrupt page mapping, so reject the write unconditionally.
    if (LPM.Services.FolderService.IsFolderSummaryRelPath(path))
    {
        Console.WriteLine($"[AnnSave] REJECTED — Folder Summary is read-only: pcId={pcId} user='{saveUser2}' path='{path}'");
        return Results.BadRequest("Folder Summary is read-only.");
    }

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
    var needsOriginal = meta.Pages.Any(p => p.Action is "original" or "overlay" or "bg_color" or "bg_color_overlay");
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
                // Full raster: new blank/inserted page (no original to reference)
                var imgFile = form.Files["img_" + pg.ImgIdx];
                if (imgFile == null) break;
                using var imgMs = new MemoryStream();
                await imgFile.CopyToAsync(imgMs);
                var imgBytes = imgMs.ToArray();
                var page = newDoc.AddPage();
                // Use natural PDF page dimensions (points) if provided, otherwise fall back to pixel-based calculation
                if (pg.PdfPtW is > 0 && pg.PdfPtH is > 0)
                {
                    page.Width  = PdfSharpCore.Drawing.XUnit.FromPoint(pg.PdfPtW.Value);
                    page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(pg.PdfPtH.Value);
                }
                else
                {
                    page.Width  = PdfSharpCore.Drawing.XUnit.FromPoint(pg.W / pxScale);
                    page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(pg.H / pxScale);
                }
                using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
                var xImg = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(imgBytes));
                gfx.DrawImage(xImg, 0, 0, page.Width, page.Height);
                break;
            }

            case "bg_color":
            {
                // Vector-preserving background color: copy original page + append multiply rectangle
                if (pg.SrcPageIdx < 0 || pg.SrcPageIdx >= originalDoc!.PageCount) break;
                var page = newDoc.AddPage(originalDoc.Pages[pg.SrcPageIdx]!);
                if (!string.IsNullOrEmpty(pg.Color) && pg.Color != "#ffffff" && pg.Color != "#FFFFFF")
                    PdfBgHelper.AppendMultiplyBackground(newDoc, page, pg.Color);
                break;
            }

            case "bg_color_overlay":
            {
                // Vector-preserving background color + draw stroke overlay
                if (pg.SrcPageIdx < 0 || pg.SrcPageIdx >= originalDoc!.PageCount) break;
                var page = newDoc.AddPage(originalDoc.Pages[pg.SrcPageIdx]!);
                if (!string.IsNullOrEmpty(pg.Color) && pg.Color != "#ffffff" && pg.Color != "#FFFFFF")
                    PdfBgHelper.AppendMultiplyBackground(newDoc, page, pg.Color);
                // Append draw overlay (same as overlay case)
                var imgFile = form.Files["img_" + pg.ImgIdx];
                if (imgFile != null)
                {
                    using var imgMs = new MemoryStream();
                    await imgFile.CopyToAsync(imgMs);
                    var imgBytes = imgMs.ToArray();
                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page, PdfSharpCore.Drawing.XGraphicsPdfPageOptions.Append);
                    var xImg = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(imgBytes));
                    var mb = page.MediaBox;
                    double drawX = mb.X1;
                    double drawY = page.Height.Point - mb.Y2;
                    gfx.DrawImage(xImg, drawX, drawY, page.Width, page.Height);
                }
                break;
            }
        }
    }

    using var outputMs = new MemoryStream();
    newDoc.Save(outputMs);
    var annotUser = ctx.User?.Identity?.Name ?? "unknown";
    Console.WriteLine($"[PcFile] Saved annotated PDF pcId={pcId}: {path} solo={solo} by '{annotUser}'");
    var saved = svc.SaveFile(pcId, path, outputMs.ToArray(), solo, auditUser: annotUser);
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
        files.Add(new { path = dbTarget, modified = File.GetLastWriteTimeUtc(dbPath).ToString("o"), alwaysDownload = true, fullPath = dbPath, pcName = (string?)null });
    }

    // Auto-backup DBs
    var backupFolderName = phase == "server" ? Path.GetFileName(backupFolder.TrimEnd('/', '\\')) : "db-autobackups";
    foreach (var bf in autoBackupFiles)
    {
        var bfName = Path.GetFileName(bf);
        files.Add(new { path = $"{backupFolderName}/{bfName}", modified = File.GetLastWriteTimeUtc(bf).ToString("o"), alwaysDownload = true, fullPath = bf, pcName = (string?)null });
    }

    // Server extras (config + avatars)
    if (phase == "server")
    {
        foreach (var (zipPath, extraFull) in svc.GetServerBackupExtras())
            files.Add(new { path = zipPath, modified = File.GetLastWriteTimeUtc(extraFull).ToString("o"), alwaysDownload = false, fullPath = extraFull, pcName = (string?)null });
    }

    // PC files
    foreach (var (relPath, pcFullPath) in svc.EnumerateBackupFiles())
    {
        var isFolderSummary = LPM.Services.FolderService.ParseFolderSummaryBackupPath(relPath).IsSummary;
        // Extract PC name: relPath looks like "PC-Folders/<PcName>/..."
        string? pcName = null;
        if (relPath.StartsWith("PC-Folders/", StringComparison.Ordinal))
        {
            var tail = relPath.Substring("PC-Folders/".Length);
            var slash = tail.IndexOf('/');
            pcName = slash > 0 ? tail.Substring(0, slash) : tail;
        }
        files.Add(new { path = relPath, modified = File.GetLastWriteTimeUtc(pcFullPath).ToString("o"), alwaysDownload = isFolderSummary, fullPath = pcFullPath, pcName });
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

    // Path traversal protection — reject ".." only as a full path segment
    // (filenames like "241217..pdf" with embedded dots are legitimate)
    if (reqPath.Contains('\0')) return Results.BadRequest("invalid path");
    {
        var _segs = reqPath.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        if (_segs.Any(s => s == ".." || s == "."))
            return Results.BadRequest("invalid path");
    }

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
        if (fileName == ".." || fileName == "." || fileName.Contains('/') || fileName.Contains('\\'))
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
record AnnPageInfo(string Action, int SrcPageIdx, int W, int H, int ImgIdx, string? Color = null, double? PdfPtW = null, double? PdfPtH = null);
record AnnSaveMeta(int TotalPages, AnnPageInfo[] Pages);

static class PdfBgHelper
{
    public static (double r, double g, double b) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return (1, 1, 1);
        return (
            int.Parse(hex[0..2], System.Globalization.NumberStyles.HexNumber) / 255.0,
            int.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber) / 255.0,
            int.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber) / 255.0
        );
    }

    /// <summary>Appends a full-page colored rectangle with PDF Multiply blend mode to preserve vectors.</summary>
    public static void AppendMultiplyBackground(PdfSharpCore.Pdf.PdfDocument doc, PdfSharpCore.Pdf.PdfPage page, string hexColor)
    {
        var (r, g, b) = ParseHexColor(hexColor);

        // 1. Create ExtGState with Multiply blend mode
        var gsDict = new PdfSharpCore.Pdf.PdfDictionary(doc);
        gsDict.Elements.SetName("/Type", "/ExtGState");
        gsDict.Elements.SetName("/BM", "/Multiply");
        doc.Internals.AddObject(gsDict);

        // 2. Register in page Resources → ExtGState
        var resources = page.Resources;
        var extGStates = resources.Elements.GetDictionary("/ExtGState");
        if (extGStates == null)
        {
            extGStates = new PdfSharpCore.Pdf.PdfDictionary(doc);
            resources.Elements["/ExtGState"] = extGStates;
        }
        extGStates.Elements["/GSBgLPM"] = gsDict.Reference;

        // 3. Build raw content stream: colored rectangle with multiply blend
        double w = page.MediaBox.Width;
        double h = page.MediaBox.Height;
        var ops = System.Text.Encoding.ASCII.GetBytes(
            $"q /GSBgLPM gs {r:F3} {g:F3} {b:F3} rg 0 0 {w:F2} {h:F2} re f Q\n");

        // 4. Create stream object and register it
        var streamObj = new PdfSharpCore.Pdf.PdfDictionary(doc);
        streamObj.CreateStream(ops);
        doc.Internals.AddObject(streamObj);

        // 5. Append to page's Contents
        var contentsItem = page.Elements["/Contents"];
        if (contentsItem is PdfSharpCore.Pdf.PdfArray arr)
        {
            arr.Elements.Add(streamObj.Reference);
        }
        else
        {
            // Single content stream or reference — wrap in array
            var newArr = new PdfSharpCore.Pdf.PdfArray(doc);
            if (contentsItem != null)
                newArr.Elements.Add(contentsItem);
            newArr.Elements.Add(streamObj.Reference);
            page.Elements["/Contents"] = newArr;
        }

        Console.WriteLine($"[AnnSave] Appended multiply bg color={hexColor} to page (w={w:F0} h={h:F0})");
    }
}

public record PurchaseReceiptRequest(
    string? PcName,
    string? DateStr,
    List<PurchaseReceiptItemDto>? Items,
    string? Notes,
    string? SignatureDataUrl);

public record PurchaseReceiptItemDto(
    string? ItemType,
    string? CourseName,
    double HoursBought,
    int AmountPaid,
    string? RegistrarName,
    string? ReferralName);

public record BackupErrorsPdfRequest(
    string? UserName,
    List<BackupErrorDto>? Errors);

public record BackupErrorDto(
    string? Type,
    string? RelPath,
    string? FullPath,
    string? PcName,
    string? Detail,
    string? When);
