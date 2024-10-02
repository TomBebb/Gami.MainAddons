using System.Collections.Immutable;

// ReSharper disable UnusedAutoPropertyAccessor.Global

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Gami.Library.Gog.Models;

public sealed class GameDetails
{
    public string Title { get; set; } = null!;
    public string BackgroundImage { get; set; } = null!;
    public ImmutableArray<(string, ImmutableDictionary<string, ImmutableArray<Download>>)> Downloads { get; set; }
}