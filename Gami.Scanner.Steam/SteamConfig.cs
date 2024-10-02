// ReSharper disable ClassNeverInstantiated.Global

namespace Gami.Scanner.Steam;

public record SteamConfig(
    string ApiKey = "",
    string SteamId = "");