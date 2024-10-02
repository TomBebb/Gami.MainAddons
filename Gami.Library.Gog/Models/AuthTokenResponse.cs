// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Gami.Library.Gog.Models;

public class AuthTokenResponse
{
    public string AccessToken { get; set; } = null!;
    public int ExpiresIn { get; set; }

    public string TokenType { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public string UserId { get; set; } = null!;
}