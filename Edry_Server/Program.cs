using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using ForsightTester.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using ApexCharts;
using Index1;
using Microsoft.AspNetCore.HttpOverrides;  // Add this namespace
using MSGS;
using FSMSGS;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.IO;
using System.Text;

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

// ... rest of your Program.cs ...
var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddControllers();

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

//builder.Services.AddFilePond();

//Forsight Scoped classes:
builder.Services.AddScoped<ClientSessionData>();
builder.Services.AddScoped<ClientManager>();
builder.Services.AddScoped<AgentList>();
builder.Services.AddScoped<ProtectedSessionStorage>();

//Forsight Singleton Classes:
builder.Services.AddSingleton<TCPEngine>();
builder.Services.AddSingleton<CommRepository>();
builder.Services.AddSingleton<OutgoingMsgsManager>();
builder.Services.AddSingleton<AgentsRepository>();
builder.Services.AddSingleton<ForsightTester.ServerConfigService>(); // Register as Singleton
builder.Services.AddSingleton<DevTableVisibilityService>();
builder.Services.AddSingleton<EMBVersionStorage>();
builder.Services.AddSingleton<OrdersExcelToJson>();
builder.Services.AddSingleton<CustomerReservationsProvider>();
builder.Services.AddSingleton<AllScriptResServices>(sp =>
{
    AllScriptResServices AllScripts = new AllScriptResServices();
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var ScriptPath = Path.Combine(env.ContentRootPath, "UserFiles","Scripts");
    var ResultPath = Path.Combine(env.ContentRootPath, "UserFiles","Results");
    var RwsScriptPath = Path.Combine(env.ContentRootPath, "UserFiles","RwsScripts");
    var RwsResultPath = Path.Combine(env.ContentRootPath, "UserFiles","RwsResults");
    var IniPath = Path.Combine(env.ContentRootPath, "UserFiles", "BitConfigs");
    AllScripts.FullScripts = new ScriptResServices(ScriptPath, ResultPath);
    AllScripts.RwsScripts = new ScriptResServices(RwsScriptPath, RwsResultPath);
    AllScripts.IniFileServices = new FileService(IniPath,true);
    return AllScripts;
});

builder.Services.AddSingleton<IFileStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();   // available here
    return new FileStore(env.ContentRootPath);
});
// choose lifetime that fits your engine (no mutable shared state → Transient)
//builder.Services.AddTransient<MotionScriptEngine>();

// Add session services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Adjust timeout as needed
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
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

builder.Services.AddScoped<PageCommandService>();

var app = builder.Build();
Globals.ServiceProvider = app.Services;  // ✅ Set global access here

// Configure forwarded headers middleware early in the pipeline
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSession();


//always use devepolment mode until we finish the whole project
if (true) //app.Environment.IsDevelopment())
{
    // Serve static assets with "no-cache" headers in DEV
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] =
                "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    });
}
//else
//{
//    // normal caching for production
//    app.UseStaticFiles();
//}


app.UseRouting();
app.MapControllers();

// Forsight - start TcpEngine and outgoingMsgs
var tcpEngine = app.Services.GetRequiredService<TCPEngine>();
tcpEngine.Start();
var outgoingMsgs = app.Services.GetRequiredService<OutgoingMsgsManager>();
outgoingMsgs.Start();

app.MapBlazorHub();
app.UseAuthentication();
app.UseAuthorization();
app.MapFallbackToPage("/_Host");

// -----------------------------------------------------------
//  DOWNLOAD END-POINTS  (Minimal-API style)
//  /download/Scripts/{fileName}
//  /download/Results/{fileName}
//  /download/RwsScripts/{fileName}
//  /download/RwsResults/{fileName}
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
            "Scripts" => Path.Combine(store.FullScripts!.Scripts.DirectoryPath, fileName),
            "Results" => Path.Combine(store.FullScripts!.Results.DirectoryPath, fileName),
            "RwsScripts" => Path.Combine(store.RwsScripts!.Scripts.DirectoryPath, fileName),
            "RwsResults" => Path.Combine(store.RwsScripts!.Results.DirectoryPath, fileName),
            "BitConfigs" => Path.Combine(store.IniFileServices!.DirectoryPath, fileName),
            _ => null
        };

        if (fullPath is null || !System.IO.File.Exists(fullPath))
            return Task.FromResult<IResult>(Results.NotFound());

        const string mime = "application/octet-stream";
        return Task.FromResult<IResult>(Results.File(fullPath, mime, fileName));
    };

app.Run();

