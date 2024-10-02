using System.Collections.Immutable;

// ReSharper disable ClassNeverInstantiated.Global

namespace Gami.Library.Gog.Models;

public sealed record OwnedGames(ImmutableArray<long> Owned);