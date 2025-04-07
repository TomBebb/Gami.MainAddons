using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flurl;
using Gami.Core;
using Gami.Core.Models;
using Nito.AsyncEx;
using Serilog;
using ValveKeyValue;

// ReSharper disable ClassNeverInstantiated.Global

namespace Gami.Scanner.Steam;

public class SteamLocalLibraryMetadata : ScannedGameLibraryMetadata
{
    public string InstallDir { get; init; } = null!;
}

[Flags]
public enum AppStateFlags
{
    Uninstalled = 1,
    UpdateRequired = 2,
    FullyInstalled = 4,
    Encrypted = 8,
    Locked = 16,
    FilesMissing = 32,
    AppRunning = 64,
    FilesCorrupt = 128,
    UpdateRunning = 256,
    UpdatePaused = 512,
    UpdateStarted = 1024,
    Uninstalling = 2048,
    BackupRunning = 4096,
    Reconfiguring = 65536,
    Validating = 131072,
    AddingFiles = 262144,
    Preallocating = 524288,
    Downloading = 1048576,
    Staging = 2097152,
    Committing = 4194304,
    UpdateStopping = 8388608
}

public sealed class SteamScanner : IGameLibraryScanner
{
    private static readonly string BasePath = OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS()
        ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Application Support/Steam")
        : OperatingSystem.IsLinux()
            ? Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Steam")
            : @"C:\Program Files (x86)\Steam";

    public static readonly string UsersConfPath = Path.Join(BasePath, "config/loginusers.vdf");

    public static readonly string AppsPath = Path.Join(BasePath, "steamapps");

    private static readonly string AppsImageCachePath = Path.Join(BasePath, "appcache/librarycache");

    public static Lazy<string> SteamId = new Lazy<string>(() =>
    {
        var content = File.ReadAllText(UsersConfPath);
        Log.Debug("MapGame Done reading {Path}", UsersConfPath);
        return content.Split('"')[3];
    });

    private readonly AsyncLazy<SteamConfig> _config = new(() =>
        AddonJson.LoadOrErrorAsync<SteamConfig>(SteamCommon.TypeName).AsTask());

    private ImmutableArray<OwnedGame>? _cachedGames;


    public string Type => "steam";

    public async IAsyncEnumerable<IGameLibraryMetadata> Scan()
    {
        var ownedGames = await ScanOwned().ConfigureAwait(false);

        Log.Debug("Got owned games: {Total}", ownedGames.Length);
        var installed = ScanInstalled()
            .ToFrozenDictionary(lib => long.Parse(lib.LibraryId), lib => lib.InstallStatus);

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var game in ownedGames)
        {
            var gameRef = new ScannedGameLibraryMetadata
            {
                LastPlayed = game.RtimeLastPlayed == 0 ? null : DateTime.UnixEpoch.AddSeconds(game.RtimeLastPlayed),
                Playtime = TimeSpan.FromMinutes(game.PlaytimeForever),
                InstallStatus = installed.GetValueOrDefault(game.AppId, GameInstallStatus.InLibrary),
                LibraryId = game.AppId.ToString(),
                LibraryType = Type,
                Name = game.Name,
                IconUrl = AutoMapPathUrl($"{game.AppId}_icon.jpg"),
                LogoUrl = AutoMapPathUrl($"{game.AppId}_logo.png"),
                HeaderUrl = AutoMapPathUrl($"{game.AppId}_header.jpg"),
                HeroUrl = AutoMapPathUrl($"{game.AppId}_library_hero.jpg")
            };
#if DEBUG
            Log.Debug("Yield {Game}", JsonSerializer.Serialize(game));
#endif

            yield return gameRef;
        }

        yield break;

        Uri? AutoMapPathUrl(string path)
        {
            path = Path.Join(AppsImageCachePath, "/" + path);
            Log.Information("Check path: {Path}; exists: {Exists}", path, File.Exists(path));
            return File.Exists(path) ? new Uri(path) : null;
        }
    }

    private async ValueTask<ImmutableArray<OwnedGame>> ScanOwned(bool forceRefresh = false)
    {
        if (_cachedGames.HasValue && !forceRefresh)
            return _cachedGames.Value;
        Log.Debug("Scan owned steam games: get conf");
        var config = await _config.Task.ConfigureAwait(false);
        Log.Debug("Scan owned steam games: got cnof");
        var client = HttpConsts.HttpClient;
        var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/"
            .SetQueryParam("key", config.ApiKey)
            .SetQueryParam("steamid", SteamId.Value)
            .SetQueryParam("include_appinfo", 1)
            .SetQueryParam("format", "json");
        Log.Debug("Steam scanning player owned games: {Url}", url);

        var res = await client.GetFromJsonAsync<OwnedGamesResults>(url,
            new JsonSerializerOptions(JsonSerializerDefaults.General)
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }).ConfigureAwait
            (false);
        Log.Debug("Steam scanned player owned games: {Total}",
            res?.Response.Games.Length ?? 0);
        _cachedGames = res!.Response.Games;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            _cachedGames = null;
        });
        Log.Debug("Scanned owned steam games");
        return _cachedGames.Value;
    }

    public static ValueTask<GameInstallStatus> CheckStatus(string id) =>
        ValueTask.FromResult(ScanInstalledGame(id)?.InstallStatus ?? GameInstallStatus.InLibrary);

    private static IEnumerable<SteamLocalLibraryMetadata> ScanInstalled()
    {
        Log.Debug("Scan installed steam games");
        var path = AppsPath;

        Log.Debug("Scan steam path {Path}", path);
        if (!Path.Exists(path))
        {
            Log.Error("Non-existent scan path: " + path);
            yield break;
        }

        Log.Debug("Scanning steam games in {Dir}", path);
        foreach (var partialPath in Directory.EnumerateFiles(path, "appmanifest*"))
        {
            var manifestPath = Path.Combine(path, partialPath);
            Log.Debug("Mapping game manifest at {Path}", manifestPath);
            var mapped = MapGameManifest(manifestPath);
            Log.Debug("Mapped game manifest at {Path}", manifestPath);
            var name = mapped.Name;
            if (name == "Steam Controller Configs" || name.StartsWith("Steam Linux") ||
                name.StartsWith("Proton") ||
                name.StartsWith("Steamworks"))
                continue;
            yield return mapped;
        }

        Log.Debug("Scanned installed steam games");
    }

    public static SteamLocalLibraryMetadata? ScanInstalledGame(string id)
    {
        var path = Path.Join(AppsPath, $"appmanifest_{id}.acf");
        if (!Path.Exists(path))
            return null;
        Log.Debug("ScanInstalledGame {Path} {Exists}", path, File.Exists(path));
        return MapGameManifest(path);
    }

    private static SteamLocalLibraryMetadata MapGameManifest(string path)
    {
        Log.Debug("MapGameMan {Path}", path);
        var stream = File.OpenRead(path);
        Log.Debug("MapGame Opened stream {Path}", path);
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        Log.Debug("MapGame created deserializer {Path}", path);
        KVObject data = kv.Deserialize(stream);
        Log.Debug("MapGame deserialized {Path}", path);
        var appId = data["appid"].ToString(CultureInfo.CurrentCulture);
        Log.Debug("Raw appId: {AppId}", appId);
        var name = data["name"].ToString(CultureInfo.CurrentCulture);
        var installDir = data["installdir"].ToString(CultureInfo.CurrentCulture);

        Log.Debug("Raw name: {AppId}", name);
        var bytesToDl = data["BytesToDownload"]?.ToString(CultureInfo.InvariantCulture);
        Log.Debug("Raw BytesToDownload: {AppId}", bytesToDl);

        var bytesDl = data["BytesDownloaded"]?.ToString(CultureInfo.InvariantCulture);
        Log.Debug("Raw BytesDownloaded: {AppId}", bytesDl);

        var lastPlayedRaw = data["LastPlayed"]?.ToString(CultureInfo.InvariantCulture);
        var lastPlayedInt = int.Parse(lastPlayedRaw ?? "0", CultureInfo.InvariantCulture);

        var rawState = data["StateFlags"]?.ToInt32(CultureInfo.CurrentCulture);
        var state = (AppStateFlags)rawState;

        var mapped = new SteamLocalLibraryMetadata
        {
            LibraryType = SteamCommon.TypeName,
            LibraryId = appId,
            Name = name,
            InstallDir = installDir,
            LastPlayed = lastPlayedInt == 0 ? null : DateTime.UnixEpoch.AddSeconds(lastPlayedInt),

            InstallStatus = (state & AppStateFlags.Uninstalling) != 0
                ? GameInstallStatus.Uninstalling
                : ((state & (AppStateFlags.Downloading | AppStateFlags.Committing | AppStateFlags.Preallocating |
                             AppStateFlags.Staging | AppStateFlags.Reconfiguring | AppStateFlags.Validating)) != 0
                    ? GameInstallStatus.Installing
                    : GameInstallStatus.Installed)
        };

        Log.Debug("Mapped bytes: {Mapped}", JsonSerializer.Serialize(mapped));
        return mapped;
    }
}