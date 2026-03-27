using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.FileProviders;
using PossumFMS.Core.Arena;
using PossumFMS.Core.Database;
using PossumFMS.Core.Display;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.FieldHardware;
using PossumFMS.Core.Frontend;
using PossumFMS.Core.Network;
using PossumFMS.Core.TheBlueAlliance;

// Check that this is the only instance of PossumFMS.Core running
const string SingleInstanceMutexName = "PossumFMS.Core.Singleton";
using var singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out bool isPrimaryInstance);
if (!isPrimaryInstance)
{
    Console.Error.WriteLine("Another instance of PossumFMS.Core is already running. Exiting.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(serverOptions => 
{
    serverOptions.Listen(System.Net.IPAddress.Any, 80);
});

// Central field state machine
builder.Services.AddSingleton<Arena>();

// Game logic
builder.Services.AddSingleton<GameLogic>();

// DS loop
builder.Services.AddSingleton<DriverStationManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriverStationManager>());

// VH-113 access point configuration manager
builder.Services.AddSingleton<AccessPointManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AccessPointManager>());

// Field hardware
builder.Services.AddSingleton<FieldHardwareManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FieldHardwareManager>());

// Frontend SignalR hub
builder.Services.AddSignalR();

builder.Services.AddSingleton<RecentLogStore>();
builder.Services.AddSingleton<ILoggerProvider, RecentLogBufferLoggerProvider>();
builder.Services.AddHostedService<RecentLogBroadcaster>();

// Match-state broadcaster
builder.Services.AddSingleton<MatchStateBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchStateBroadcaster>());

// JSON file database (loaded into memory on startup)
builder.Services.AddSingleton<DatabaseService>();

// Audience display state manager
builder.Services.AddSingleton<DisplayManager>();

// The Blue Alliance HTTP client
builder.Services.AddHttpClient("TBA", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["TheBlueAlliance:ApiKey"] ?? "";
    client.BaseAddress = new Uri("https://www.thebluealliance.com/api/v3/");
    client.DefaultRequestHeaders.Add("X-TBA-Auth-Key", apiKey);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<TbaClient>();

// Allow any origin since this will be only ever be on private local networks
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();
app.Services.GetRequiredService<GameLogic>();

// Check that the host has the required FMS IP address
const string RequiredIp = "10.0.100.5";
var logger = app.Services.GetRequiredService<ILogger<Program>>();

bool hasRequiredIp = NetworkInterface.GetAllNetworkInterfaces()
    .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
    .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
    .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
    .Any(addr => addr.Address.ToString() == RequiredIp);

if (!hasRequiredIp)
    logger.LogWarning(
        "Host does not have IP {RequiredIp}. DS and field hardware communication will likely fail. " +
        "Set the FMS network adapter to {RequiredIp} before running a match.",
        RequiredIp, RequiredIp);

app.UseCors();
app.MapHub<FmsHub>("/fmshub");

var webBuildPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "PossumFMS.Web", "build"));
if (Directory.Exists(webBuildPath))
{
    var webBuildProvider = new PhysicalFileProvider(webBuildPath);

    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = webBuildProvider,
        RequestPath = string.Empty,
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = webBuildProvider,
        RequestPath = string.Empty,
    });

    app.MapFallback(async context =>
    {
        var requestedPath = context.Request.Path.Value ?? string.Empty;
        if (Path.HasExtension(requestedPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var normalizedPath = requestedPath.Trim('/');
        string[] candidates =
        [
            normalizedPath.Length == 0 ? "index.html" : $"{normalizedPath}.html",
            normalizedPath.Length == 0 ? "index.html" : Path.Combine(normalizedPath, "index.html"),
            "index.html",
        ];

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(Path.Combine(webBuildPath, candidate));
            if (!fullPath.StartsWith(webBuildPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (File.Exists(fullPath))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(fullPath);
                return;
            }
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    });

    logger.LogInformation("Serving frontend static files from {WebBuildPath}.", webBuildPath);
}
else
{
    logger.LogWarning(
        "Frontend build directory not found at {WebBuildPath}. Run 'pnpm build' in PossumFMS.Web to serve the UI from PossumFMS.Core.",
        webBuildPath);
}

app.Run();
