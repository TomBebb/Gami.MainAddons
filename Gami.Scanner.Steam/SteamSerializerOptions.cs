using System.Text.Json;

namespace Gami.Scanner.Steam;

public static class SteamSerializerOptions
{
    public static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerOptions.Default)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
}