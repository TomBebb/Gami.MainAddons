using System.Collections.Immutable;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Gami.Scanner.Steam;

public sealed class AppDetailsData
{
    public string Type { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DetailedDescription { get; set; } = null!;
    public string ShortDescription { get; set; } = null!;
    public string AboutTheGame { get; set; } = null!;
    public string? Website { get; set; }

    public ImmutableArray<string> Developers { get; set; }
    public ImmutableArray<string> Publishers { get; set; }

    public ImmutableArray<AppGenre>? Genres { get; set; }

    public AppReleaseDate? ReleaseDate { get; set; }
}

public sealed class AppReleaseDate
{
    public bool ComingSoon { get; set; }
    public string? Date { get; set; }
}

public sealed class AppGenre
{
    public string Id { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public sealed class AppDetails
{
    public bool Success { get; set; }
    public AppDetailsData Data { get; set; } = null!;
}