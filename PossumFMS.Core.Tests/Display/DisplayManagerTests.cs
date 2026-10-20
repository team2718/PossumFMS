using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PossumFMS.Core.Database;
using PossumFMS.Core.Display;
using Xunit;

namespace PossumFMS.Core.Tests.Display;

public sealed class DisplayManagerTests : IDisposable
{
    private readonly string _tempDir;

    public DisplayManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PossumFMS_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in temp dir
            }
        }
    }

    private DatabaseService CreateDatabaseService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DataPath"] = _tempDir
            })
            .Build();

        return new DatabaseService(config, NullLogger<DatabaseService>.Instance);
    }

    [Fact]
    public void DisplayManager_Default_CarterAudioIsFalse()
    {
        var display = new DisplayManager();
        Assert.False(display.CarterAudio);
    }

    [Fact]
    public void DisplayManager_LoadsCarterAudioFromDatabaseService()
    {
        var db = CreateDatabaseService();
        db.SaveCarterAudio(true);

        var display = new DisplayManager(db);
        Assert.True(display.CarterAudio);
    }

    [Fact]
    public void DisplayManager_SetCarterAudio_UpdatesAndPersistsAcrossReboot()
    {
        var db1 = CreateDatabaseService();
        var display1 = new DisplayManager(db1);
        Assert.False(display1.CarterAudio);

        // Turn on Carter audio
        display1.SetCarterAudio(true);
        Assert.True(display1.CarterAudio);

        // Simulate server reboot: new DatabaseService and DisplayManager instance pointing to same storage
        var db2 = CreateDatabaseService();
        var display2 = new DisplayManager(db2);
        Assert.True(display2.CarterAudio);

        // Turn off Carter audio
        display2.SetCarterAudio(false);
        Assert.False(display2.CarterAudio);

        // Simulate another server reboot
        var db3 = CreateDatabaseService();
        var display3 = new DisplayManager(db3);
        Assert.False(display3.CarterAudio);
    }

    [Fact]
    public void DatabaseService_HandlesMalformedSettingsFileGracefully()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, "{ malformed json ... }");

        var db = CreateDatabaseService();
        Assert.False(db.GetCarterAudio());

        // Saving new value should safely overwrite malformed file
        db.SaveCarterAudio(true);
        Assert.True(db.GetCarterAudio());

        var dbReboot = CreateDatabaseService();
        Assert.True(dbReboot.GetCarterAudio());
    }
}

