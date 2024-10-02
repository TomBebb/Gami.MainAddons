using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Gami.Scanner.Epic;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class InstallationData
{
    public string AppName { get; set; } = null!;

    public ImmutableArray<string> BaseUrls { get; set; }
    public bool CanRunOffline { get; set; }
    public string Executable { get; set; } = null!;
    public string InstallPath { get; set; } = null!;
    public long InstallSize { get; set; }
    public bool IsDlc { get; set; }
    public string LaunchParameters { get; set; } = null!;
    public bool NeedsVerification { get; set; }
    public string Platform { get; set; } = null!;
}