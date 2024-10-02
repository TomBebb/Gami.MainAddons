namespace Gami.Library.Gog.Models;

public sealed class MyConfig
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime? AccessTokenExpire { get; set; }
}