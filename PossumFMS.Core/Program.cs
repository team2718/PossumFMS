using System.Net.NetworkInformation;
using System.Net.Sockets;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.FieldHardware;
using PossumFMS.Core.Frontend;
using PossumFMS.Core.Network;

var builder = WebApplication.CreateBuilder(args);

// ── Services ───────────────────────────────────────────────────────────────────

// Central field state machine — injected into DS manager, hub, etc.
builder.Services.AddSingleton<Arena>();

// Game logic — owns per-match scoring state and wires phase-transition rules.
builder.Services.AddSingleton<GameLogic>();

// DS loop — high-freq BackgroundService + injectable singleton for hub commands.
builder.Services.AddSingleton<DriverStationManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriverStationManager>());

// VH-113 access point — configures team SSIDs/WPA keys, polls link status.
builder.Services.AddSingleton<AccessPointManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AccessPointManager>());

// Field hardware — reconnecting TCP manager for ESP32 devices.
builder.Services.AddSingleton<FieldHardwareManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FieldHardwareManager>());

// SignalR for the frontend website.
builder.Services.AddSignalR();

// Periodic match-state broadcaster — keeps the timer live in the browser.
builder.Services.AddSingleton<MatchStateBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchStateBroadcaster>());

// Allow any origin in development; restrict in production via config.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

// ── Application ────────────────────────────────────────────────────────────────

var app = builder.Build();

// Force eager construction so Arena.PhaseChanged is subscribed before any match starts.
app.Services.GetRequiredService<GameLogic>();

// ── Startup checks ─────────────────────────────────────────────────────────────

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

app.Run();
