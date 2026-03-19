using Microsoft.Extensions.Logging;
using PossumFMS.Core.Frontend;

namespace PossumFMS.Core.Tests.Frontend;

public sealed class RecentLogStoreTests
{
    [Fact]
    public void Add_RetainsOnlyMostRecentHundredEntries()
    {
        var store = new RecentLogStore();

        for (var index = 1; index <= 105; index++)
        {
            store.Add(LogLevel.Warning, "Tests.Category", $"Message {index}");
        }

        var entries = store.GetEntries();

        Assert.Equal(RecentLogStore.Capacity, entries.Count);
        Assert.Equal("Message 6", entries.First().Message);
        Assert.Equal("Message 105", entries.Last().Message);
    }

    [Fact]
    public void Add_AssignsIncreasingIdsInInsertionOrder()
    {
        var store = new RecentLogStore();

        store.Add(LogLevel.Information, "Tests.Category", "First");
        store.Add(LogLevel.Error, "Tests.Category", "Second");

        var entries = store.GetEntries();

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].Id < entries[1].Id);
        Assert.Equal("Information", entries[0].Level);
        Assert.Equal("Error", entries[1].Level);
    }
}