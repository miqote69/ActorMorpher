using System.Text.Json;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class BulkOutfitFilterPersistenceTests
{
    [Fact]
    public void LegacyConfigurationMigratesBulkFilterDefaults()
    {
        var configuration = JsonSerializer.Deserialize<Configuration>("{\"Version\":5}")!;

        configuration.MigrateAndValidate(false);

        Assert.Equal(6, configuration.Version);
        Assert.False(configuration.BulkIncludeYourself);
        Assert.Equal(0, configuration.BulkActorType);
        Assert.Equal(0, configuration.BulkRace);
        Assert.Equal(0, configuration.BulkGender);
        Assert.Equal(0, configuration.BulkAge);
        Assert.Equal(string.Empty, configuration.BulkNameFilter);
        Assert.False(configuration.BulkExclusionEnabled);
        Assert.Equal(0, configuration.BulkExcludeActorType);
        Assert.Equal(0, configuration.BulkExcludeRace);
        Assert.Equal(0, configuration.BulkExcludeGender);
        Assert.Equal(0, configuration.BulkExcludeAge);
        Assert.Equal(string.Empty, configuration.BulkExcludeNameFilter);
    }

    [Fact]
    public void BulkFilterSelectionsSurviveConfigurationRoundTrip()
    {
        var configuration = new Configuration
        {
            BulkIncludeYourself = true,
            BulkActorType = 2,
            BulkRace = 8,
            BulkGender = 2,
            BulkAge = 2,
            BulkNameFilter = "Target Name",
            BulkExclusionEnabled = true,
            BulkExcludeActorType = 1,
            BulkExcludeRace = 7,
            BulkExcludeGender = 1,
            BulkExcludeAge = 1,
            BulkExcludeNameFilter = "Exclude Name",
        };

        var serialized = JsonSerializer.Serialize(configuration);
        var reloaded = JsonSerializer.Deserialize<Configuration>(serialized)!;
        reloaded.MigrateAndValidate(false);

        Assert.True(reloaded.BulkIncludeYourself);
        Assert.Equal(2, reloaded.BulkActorType);
        Assert.Equal(8, reloaded.BulkRace);
        Assert.Equal(2, reloaded.BulkGender);
        Assert.Equal(2, reloaded.BulkAge);
        Assert.Equal("Target Name", reloaded.BulkNameFilter);
        Assert.True(reloaded.BulkExclusionEnabled);
        Assert.Equal(1, reloaded.BulkExcludeActorType);
        Assert.Equal(7, reloaded.BulkExcludeRace);
        Assert.Equal(1, reloaded.BulkExcludeGender);
        Assert.Equal(1, reloaded.BulkExcludeAge);
        Assert.Equal("Exclude Name", reloaded.BulkExcludeNameFilter);
    }

    [Fact]
    public void InvalidBulkFilterSelectionsAreClampedBeforeUiUse()
    {
        var configuration = new Configuration
        {
            BulkActorType = -1,
            BulkRace = int.MaxValue,
            BulkGender = -1,
            BulkAge = int.MaxValue,
            BulkNameFilter = null!,
            BulkExcludeActorType = int.MaxValue,
            BulkExcludeRace = -1,
            BulkExcludeGender = int.MaxValue,
            BulkExcludeAge = -1,
            BulkExcludeNameFilter = null!,
        };

        configuration.MigrateAndValidate(false);

        Assert.Equal(0, configuration.BulkActorType);
        Assert.Equal(8, configuration.BulkRace);
        Assert.Equal(0, configuration.BulkGender);
        Assert.Equal(2, configuration.BulkAge);
        Assert.Equal(string.Empty, configuration.BulkNameFilter);
        Assert.Equal(2, configuration.BulkExcludeActorType);
        Assert.Equal(0, configuration.BulkExcludeRace);
        Assert.Equal(2, configuration.BulkExcludeGender);
        Assert.Equal(0, configuration.BulkExcludeAge);
        Assert.Equal(string.Empty, configuration.BulkExcludeNameFilter);
    }
}
