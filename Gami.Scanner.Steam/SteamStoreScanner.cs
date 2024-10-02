using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Gami.Core;
using Gami.Core.Models;
using Serilog;

namespace Gami.Scanner.Steam;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedType.Global")]
public sealed partial class SteamStoreScanner : IGameMetadataScanner
{
    private static readonly Regex ReleaseDateReg = DateRegex();
    public string Type => SteamCommon.TypeName;

    public async ValueTask<GameMetadata> ScanMetadata(IGameLibraryRef game)
    {
        if (game.LibraryType != "steam")
            return new GameMetadata();
        var res = await HttpConsts.HttpClient.GetFromJsonAsync<ImmutableDictionary<string, AppDetails>>(
            $"https://store.steampowered.com/api/appdetails?appids={game.LibraryId}",
            SteamSerializerOptions.JsonOptions).ConfigureAwait(false);

        Console.WriteLine();
        Log.Information("Game raw metadata: {Data}", res);
        var data = res?.Values.FirstOrDefault();
        return data?.Data == null ? new GameMetadata() : MapMetadata(data.Data);
    }


    public static DateOnly? ParseReleaseDate(string releaseDate)
    {
        var match = ReleaseDateReg.Match(releaseDate);

        if (!match.Success)
            return null;

        var monthDate = match.Groups[1].Value;
        var month = match.Groups[2].Value;
        var year = match.Groups[3].Value;


        return new DateOnly(int.Parse(year), month switch
        {
            "Jan" => 1,
            "Feb" => 2,
            "Mar" => 3,
            "Apr" => 4,
            "May" => 5,
            "Jun" => 6,
            "Jul" => 7,
            "Aug" => 8,
            "Sep" => 9,
            "Oct" => 10,
            "Nov" => 11,
            "Dec" => 12,
            _ => throw new FormatException($"Invalid release date month: {month}")
        }, int.Parse(monthDate));
    }

    private static GameMetadata MapMetadata(AppDetailsData data) =>
        new()
        {
            ReleaseDate = data?.ReleaseDate?.Date != null ? ParseReleaseDate(data.ReleaseDate.Date) : null,
            Description = data.DetailedDescription,
            Developers = data.Developers,
            Publishers = data.Publishers,
            Genres = data.Genres?.Select(g => g.Description).ToImmutableArray()
        };

    [GeneratedRegex(@"^([0-9]+) ([A-Z][a-z]+), ([0-9]{4})$")]
    private static partial Regex DateRegex();
}