using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Gami.Core;
using Gami.Core.Models;

namespace Gami.Scanner.Steam;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class SteamCommon : IGameLibraryLauncher, IGameLibraryManagement
{
    public const string TypeName = "steam";
    public string Type => "steam";

    public void Launch(IGameLibraryRef gameRef) =>
        RunGameCmd("rungameid", gameRef.LibraryId);

    public ValueTask<Process?> GetMatchingProcess(IGameLibraryRef gameRef)
    {
        var meta = SteamScanner.ScanInstalledGame(gameRef.LibraryId);
        if (meta == null)
            return ValueTask.FromResult<Process?>(null);
        var appDir = Path.Join(SteamScanner.AppsPath, "common", meta.InstallDir);
        return ValueTask.FromResult(appDir.ResolveMatchingProcess());
    }

    public async ValueTask Install(IGameLibraryRef gameRef) =>
        await Task.Run(() => RunGameCmd("install", gameRef.LibraryId));

    public void Uninstall(IGameLibraryRef gameRef) =>
        RunGameCmd("uninstall", gameRef.LibraryId);

    public ValueTask<GameInstallStatus> CheckInstallStatus(IGameLibraryRef game) =>
        SteamScanner.CheckStatus(game.LibraryId);


    private static void RunGameCmd(string cmd, string id)
    {
        var info = new ProcessStartInfo
            { FileName = $"steam://{cmd}/{id}", UseShellExecute = true };
        new Process { StartInfo = info }.Start();
    }
}