// ReSharper disable UnusedMember.Global
// ReSharper disable ClassNeverInstantiated.Global

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Gami.Library.Gog.Models;

public sealed record Download
{
    public string ManualUrl { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string Date { get; set; } = null!;
    public string Size { get; set; } = null!;
}