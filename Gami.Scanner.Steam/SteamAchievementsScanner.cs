using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl;
using Gami.Core;
using Gami.Core.Models;
using Nito.AsyncEx;
using Serilog;

namespace Gami.Scanner.Steam;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Local")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
public sealed class SteamAchievementsScanner : IGameAchievementScanner
{
    private static readonly JsonSerializerOptions SteamApiJsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AsyncLazy<SteamConfig> _config = new(async () =>
        await PluginJson.LoadOrErrorAsync<SteamConfig>(SteamCommon.TypeName));

    public string Type => SteamCommon.TypeName;

    public async IAsyncEnumerable<Achievement> Scan(IGameLibraryRef game)
    {
        var allAchievements = await GetGameAchievements(game).ConfigureAwait(false);
        if (allAchievements?.Game.AvailableGameStats?.Achievements == null) yield break;
        Log.Debug("Game achievements: {Game}",
            allAchievements.Game.AvailableGameStats.Achievements.Length);

        Log.Debug("Load game percents");
        var globalPercents = await GetPercents(game).ConfigureAwait(false);

        Log.Debug("Loaded game percents");

        var globalPercentsByName =
            globalPercents.AchievementPercentages.Achievements.ToFrozenDictionary(v => v.Name, v => v.Percent);

        foreach (var achievement in allAchievements.Game.AvailableGameStats.Achievements)
            yield return new Achievement
            {
                Id =
                    $"{game.LibraryType}:{game.LibraryId}::{achievement.Name}",
                Name = achievement.DisplayName,
                LibraryId = achievement.Name,
                LockedIconUrl = achievement.Icon,
                UnlockedIconUrl = achievement.IconGray,
                GlobalPercent = globalPercentsByName.GetValueOrDefault(achievement.Name)
            };
    }

    public async IAsyncEnumerable<AchievementProgress> ScanProgress(
        IGameLibraryRef game)
    {
        var playerAchievements =
            await GetPlayerAchievements(game).ConfigureAwait(false);
        Log.Debug("Player Achievements: {Achievements}",
            playerAchievements.PlayerStats.Achievements?.Length ?? 0);
        foreach (var achievement in playerAchievements.PlayerStats.Achievements ??
                                    ImmutableArray<PlayerAchievementItem>.Empty)
            yield return new AchievementProgress
            {
                AchievementId =
                    $"{game.LibraryType}:{game.LibraryId}::{achievement.ApiName}",
                UnlockTime = achievement.UnlockTime == 0
                    ? null
                    : DateTime.UnixEpoch.AddSeconds
                        (achievement.UnlockTime),
                Unlocked = achievement.Achieved == 1
            };
    }

    private async ValueTask<PlayerAchievementsResults> GetPlayerAchievements
        (IGameLibraryRef game)
    {
        var config = await _config.Task;
        var url =
            "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/"
                .AppendQueryParam("appid", game.LibraryId)
                .AppendQueryParam("key", config.ApiKey)
                .AppendQueryParam("steamid", config.SteamId);

        Log.Debug("Fetch playerachievements for {GameId}", url);
        try
        {
            var res = await HttpConsts.HttpClient
                .GetFromJsonAsync<PlayerAchievementsResults>(
                    url, SteamApiJsonSerializerOptions);
            return res!;
        }
        catch (HttpRequestException)
        {
            return new PlayerAchievementsResults
            {
                PlayerStats = new PlayerAchievements { Achievements = ImmutableArray<PlayerAchievementItem>.Empty }
            };
        }
    }

    private static async ValueTask<GlobalPercentAchievementsResults> GetPercents(IGameLibraryRef game)
    {
        var url = "https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/"
            .AppendQueryParam("gameid", game.LibraryId);
        Log.Debug("Fetch global percents for {GameId}", url);

        return (await HttpConsts.HttpClient.GetFromJsonAsync<GlobalPercentAchievementsResults>(url,
            SteamApiJsonSerializerOptions))!;
    }

    private async ValueTask<GameSchemaResult?> GetGameAchievements
        (IGameLibraryRef game)
    {
        var config = await _config.Task;
        var url =
            "https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/"
                .AppendQueryParam("appid", game.LibraryId)
                .AppendQueryParam("key", config.ApiKey);

        Log.Debug("Fetch game achievements for {GameId}", url);

        Log.Information("Fetching {Url}", url);

        var res = await HttpConsts.HttpClient.GetAsync(url);
        if (res.StatusCode == HttpStatusCode.Forbidden &&
            Equals(res.Content.Headers.ContentType, new MediaTypeHeaderValue("application/json")))
            return new GameSchemaResult();
        var steam = await res.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GameSchemaResult>(steam, SteamApiJsonSerializerOptions);
    }

    private sealed class PlayerAchievementItem
    {
        public required string ApiName { get; set; }
        public byte Achieved { get; set; }
        public long UnlockTime { get; set; }
    }

    private sealed class PlayerAchievements
    {
        public ImmutableArray<PlayerAchievementItem>? Achievements { get; set; }
    }

    private sealed class PlayerAchievementsResults
    {
        public required PlayerAchievements PlayerStats { get; set; }
    }

    private sealed class GameSchemaGameStats
    {
        public ImmutableArray<GameSchemaAchievement> Achievements { get; set; }
    }

    private sealed class GameSchema
    {
        public GameSchemaGameStats AvailableGameStats { get; set; } = null!;
    }

    private sealed record GlobalPercentAchievement(string Name, float Percent);

    private sealed record GlobalPercentAchievements(ImmutableArray<GlobalPercentAchievement> Achievements);

    private sealed record GlobalPercentAchievementsResults(
        [property: JsonPropertyName("achievementpercentages")]
        GlobalPercentAchievements AchievementPercentages);

    private sealed class GameSchemaResult
    {
        public GameSchema Game { get; set; } = null!;
    }

    private sealed class GameSchemaAchievement
    {
        // ReSharper disable once UnusedMember.Local
        public int Hidden { get; set; }
        public required string Name { get; set; }
        public required string DisplayName { get; set; }
        public required string Icon { get; set; }
        public required string IconGray { get; set; }
    }
}