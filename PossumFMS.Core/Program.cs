using System.Net.NetworkInformation;
using System.Net.Sockets;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using PossumFMS.Core.FieldHardware;
using PossumFMS.Core.Frontend;
using PossumFMS.Core.Network;

// Check that this is the only instance of PossumFMS.Core running
const string SingleInstanceMutexName = "PossumFMS.Core.Singleton";
using var singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out bool isPrimaryInstance);
if (!isPrimaryInstance)
{
    Console.Error.WriteLine("Another instance of PossumFMS.Core is already running. Exiting.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

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

// Match-state broadcaster
builder.Services.AddSingleton<MatchStateBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchStateBroadcaster>());

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

app.Run();
