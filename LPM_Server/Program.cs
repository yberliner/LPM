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
using MSGS;
using FSMSGS;
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

builder.Services.Configure<FSMSGS.TCPSettings>(builder.Configuration.GetSection("TCPSettings"));

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
builder.Services.AddScoped<NavScrollService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<Index1Service>();

builder.Services.AddWMBOS();
builder.Services.AddSession();

//Forsight Scoped classes:
builder.Services.AddScoped<ClientSessionData>();
builder.Services.AddScoped<ClientManager>();
builder.Services.AddScoped<AgentList>();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<PdfService>();

//Forsight Singleton Classes:
builder.Services.AddSingleton<TCPEngine>();
builder.Services.AddSingleton<CommRepository>();
builder.Services.AddSingleton<OutgoingMsgsManager>();
builder.Services.AddSingleton<AgentsRepository>();
builder.Services.AddSingleton<LPM.ServerConfigService>();
builder.Services.AddSingleton<DevTableVisibilityService>();
builder.Services.AddSingleton<EMBVersionStorage>();
builder.Services.AddSingleton<OrdersExcelToJson>();
builder.Services.AddSingleton<CustomerReservationsProvider>();
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

builder.Services.AddSingleton<IFileStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new FileStore(env.ContentRootPath);
});

// SQLite user database
builder.Services.AddSingleton<UserDb>();
builder.Services.AddSingleton<LPM.Services.DashboardService>();

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
Globals.ServiceProvider = app.Services;

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

if (true)
{
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    });
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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

        Console.WriteLine($"[Login] '{username}' signed in as [{string.Join(", ", roles)}]");
        return roles.Contains("Admin")
            ? Results.Redirect("/Admin")
            : Results.Redirect("/Home");
    }

    Console.WriteLine($"[Login] Failed attempt for '{username}'");
    return Results.Redirect($"/login?error=1&username={Uri.EscapeDataString(username)}");
}).DisableAntiforgery();

// Logout endpoint — clears auth cookie and redirects to login
app.Map("/logout", async (HttpContext ctx) =>
{
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

app.Run();
