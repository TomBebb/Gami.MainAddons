namespace Gami.Scanner.Steam;

public static class HttpConsts
{
    public static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        MaxConnectionsPerServer = 8
    });
}