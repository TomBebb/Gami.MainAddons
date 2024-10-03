using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Flurl;
using Gami.Core;
using Gami.Core.Ext;
using Gami.Core.Models;
using Gami.Library.Gog.Models;
using Serilog;
using TupleAsJsonArray;

namespace Gami.Library.Gog;

// ReSharper disable once UnusedType.Global
public sealed class GogLibrary : IGameLibraryAuth, IGameLibraryScanner, IGameLibraryManagement, IGameLibraryLauncher
{
    private const string ClientId = "46899977096215655";
    private const string ClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
    private const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";
    private const string GamesRoot = "C:/GOG Games";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new TupleConverterFactory() }
    };

    private static readonly JsonSerializerOptions AuthSerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly HttpClient HttpClient = new();

    private readonly MyConfig _config = AddonJson.Load<MyConfig>("gog") ??
                                        new MyConfig();

    public string Type => "gog";

    public bool NeedsAuth => true;

    public async ValueTask<bool> CurrUrlChange(string url)
    {
        Log.Debug("GOG URL changed {Code}", url);

        var isAuth = url.StartsWith("https://embed.gog.com/on_login_success");
        if (!isAuth)
            return false;

        var parsed = new Uri(url);

        var ps = HttpUtility.ParseQueryString(parsed.Query);
        var code = ps.Get("code") ?? throw new NullReferenceException("No code param!");
        Log.Debug("Got gog code {Code}", code);
        await ProcessLoginCode(code);
        return true;
    }

    public string AuthUrl() =>
        "https://auth.gog.com/auth?client_id=46899977096215655&redirect_uri=https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client2";

    public void Launch(IGameLibraryRef gameRef)
    {
        var data = Directory.EnumerateFiles(GetInstallDir(gameRef), "*.lnk").Select(Lnk.Lnk.LoadFile).FirstOrDefault();
        if (data == null)
            throw new ApplicationException("Unable to launch GOG game " + gameRef.Name + "; no lnk shortcut found");

        data.LocalPath.AutoRun(gameRef);
    }

    public ValueTask<Process?> GetMatchingProcess(IGameLibraryRef gameRef) => throw new NotImplementedException();

    public async ValueTask Install(IGameLibraryRef gameLibraryRef)
    {
        var details = await GetGameDetails(gameLibraryRef.LibraryId);
        Log.Debug("Game details {Json}", JsonSerializer.Serialize(details, SerializerOptions));
        var dlDir = Path.Join(Consts.AppDir, "gog/dls");
        Directory.CreateDirectory(dlDir);

        var osMap = details.Downloads.FirstOrDefault(v => v.Item1 == "English").Item2;

        Log.Debug("OS Map {Json}", JsonSerializer.Serialize(osMap, SerializerOptions));
        var dls = osMap["windows"];
        Log.Debug("Mapped URLs {Json}", JsonSerializer.Serialize(dls, SerializerOptions));

        var baseUri = new Uri("https://embed.gog.com");
        var paths = new List<string>();

        foreach (var currDl in dls)
        {
            Log.Debug("Get url {Url}", currDl.ManualUrl);

            var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

            var uri = new Uri(baseUri, currDl.ManualUrl);
            uri = await ResolveAuthFinalLocation(httpClient, baseUri, uri);
            var outPath = Path.Join(dlDir, Path.GetFileName(uri.LocalPath));
            paths.Add(outPath);
            if (File.Exists(outPath))
                continue;
            Log.Debug("Saving to {Out}", outPath);
            var res = await SendAuth(httpClient, uri).ConfigureAwait(false);
            await using var dlStream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
            Log.Debug("Opened dl stream {Out}", outPath);
            await using var outStream = File.OpenWrite(outPath);
            await dlStream.CopyToAsync(outStream);
            outStream.Close();

            Log.Debug("Downloaded file to {Path}", outPath);
        }

        await paths[0].AutoRunExeAsync(gameLibraryRef);
    }

    public void Uninstall(IGameLibraryRef game)
    {
        Path.Join(GetInstallDir(game), "unins000.exe").AutoRun(game);
    }

    public ValueTask<GameInstallStatus> CheckInstallStatus(IGameLibraryRef game)
    {
        var checkDir = GetInstallDir(game);
        return ValueTask.FromResult(Directory
            .Exists(checkDir)
            ? GameInstallStatus.Installed
            : GameInstallStatus.InLibrary);
    }

    public async IAsyncEnumerable<IGameLibraryMetadata> Scan()
    {
        if (string.IsNullOrEmpty(_config.AccessToken))
        {
            Log.Error("Not authenticated; returning none");

            yield break;
        }

        Log.Debug("GetOwnedGams");

        var ownedGames = await GetOwnedGames().ConfigureAwait(false);
        Log.Debug("GotOwnedGams");
        foreach (var gameIdLong in ownedGames.Owned)
        {
            Log.Debug("Scan game ID {Id}", gameIdLong);
            var gameId = gameIdLong.ToString();
            var game = await GetGameDetails(gameId);
            Log.Debug("Scanned game ID {Id}", gameIdLong);

            yield return new ScannedGameLibraryMetadata
            {
                LibraryType = Type,
                Name = game.Title,
                Playtime = TimeSpan.Zero,
                LibraryId = gameId,
                InstallStatus = await CheckInstallStatus(new GameLibraryRef
                {
                    LibraryType = Type, LibraryId =
                        gameId,
                    Name = game.Title
                })
            };
        }
    }

    private ValueTask<T> GetAuthJson<T>(string uri) => GetAuthJson<T>(new Uri(uri));

    private async ValueTask<HttpResponseMessage> SendAuth(HttpClient client, Uri uri, HttpMethod? method = null)
    {
        method ??= HttpMethod.Get;
        await AutoRefreshToken().ConfigureAwait(false);
        var request = new HttpRequestMessage
        {
            RequestUri = uri,
            Method = method
        };
        request.Headers.Add("Authorization", $"Bearer {_config.AccessToken}");
        Log.Debug("GOG {Method} fetching {Uri}", method.Method, uri);
        var res = await client.SendAsync(request).ConfigureAwait(false);
        Log.Debug("GOG {Method} fetched {Uri}", method.Method, uri);
        return res;
    }

    private async ValueTask<Uri> ResolveAuthFinalLocation(HttpClient httpClient, Uri baseUri, Uri uri)
    {
        var res = await SendAuth(httpClient, uri, HttpMethod.Head).ConfigureAwait(false);

        while (res.StatusCode == HttpStatusCode.Found)
        {
            var oldUri = uri;
            uri = new Uri(baseUri, res.Headers.Location!);

            Log.Debug("Redirect: {From} => {To}", oldUri, uri);
            res = await SendAuth(httpClient, uri, HttpMethod.Head).ConfigureAwait(false);
        }

        return uri;
    }

    private async ValueTask<Stream> GetAuthStream(HttpClient client, Uri uri)
    {
        var res = await SendAuth(client, uri).ConfigureAwait(false);
        Log.Debug("GOG fetches response {Uri}", uri);
        return await res.Content.ReadAsStreamAsync();
    }

    private async ValueTask<T> GetAuthJson<T>(Uri uri)
    {
        var stream = await GetAuthStream(HttpClient, uri).ConfigureAwait(false);
        Log.Debug("GOG fetched as stream {Uri}", uri);

        var data = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions);
        Log.Debug("GOG deserialized {Uri}", uri);
        return data!;
    }

    private ValueTask<OwnedGames> GetOwnedGames() => GetAuthJson<OwnedGames>("https://embed.gog.com/user/data/games");

    private ValueTask<GameDetails> GetGameDetails(string id) =>
        GetAuthJson<GameDetails>("https://embed.gog.com/account/gameDetails/".AppendPathSegment($"{id}.json"));

    private ValueTask SaveConfig() => AddonJson.Save(_config, "gog");

    private async ValueTask ProcessTokenUrl(string url)
    {
        Log.Debug("Get gog token");

        var auth = await HttpClient.GetFromJsonAsync<AuthTokenResponse>(url, AuthSerializerOptions)
            .ConfigureAwait(false);
        Log.Debug("Got gog token {Data}", JsonSerializer.Serialize(auth));
        _config.AccessToken = auth!.AccessToken;
        _config.RefreshToken = auth.RefreshToken;
        _config.AccessTokenExpire = DateTime.UtcNow + TimeSpan.FromSeconds(auth.ExpiresIn);

        Log.Debug("Saving config");
        await SaveConfig();
        Log.Debug("Saved config");
    }

    private async ValueTask ProcessLoginCode(string code)
    {
        var url = "https://auth.gog.com/token"
            .SetQueryParam("client_id", ClientId)
            .SetQueryParam("client_secret", ClientSecret)
            .SetQueryParam("redirect_uri", RedirectUri)
            .SetQueryParam("grant_type", "authorization_code")
            .SetQueryParam("code", code);
        await ProcessTokenUrl(url);
    }

    private async ValueTask RefreshToken()
    {
        var url = "https://auth.gog.com/token"
            .SetQueryParam("client_id", ClientId)
            .SetQueryParam("client_secret", ClientSecret)
            .SetQueryParam("redirect_uri", RedirectUri)
            .SetQueryParam("grant_type", "refresh_token")
            .SetQueryParam("refresh_token", _config.RefreshToken);
        await ProcessTokenUrl(url);
    }

    private async ValueTask AutoRefreshToken()
    {
        Log.Debug("Expire time: {Expire} Now: {Now}", _config.AccessTokenExpire, DateTime.UtcNow);
        if (_config.AccessTokenExpire.HasValue && DateTime.UtcNow > _config.AccessTokenExpire.Value)
            await RefreshToken().ConfigureAwait(false);
    }

    private static string GetInstallDir(IGameLibraryRef game) =>
        Path.Join
            (GamesRoot, game.Name.Replace(":", ""));
}