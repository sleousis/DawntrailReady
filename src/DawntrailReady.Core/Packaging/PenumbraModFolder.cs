// Ports: TexTools Mods/WizardData.cs (FromPmp, WizardGroupEntry.FromPMPGroup) and Mods/FileTypes/PMP.cs
// (UnpackPmpOption with includeData == false, as FromPMPGroup calls it).
using DawntrailReady.Core.Game;

namespace DawntrailReady.Core.Packaging;

/// <summary>Reads and writes back an extracted Penumbra mod folder (FileVersion 3 or 4).</summary>
public static partial class PenumbraModFolder
{
    /// <summary>
    /// Every container as a <see cref="ModOption"/>: default (when not empty) first, then every group's options,
    /// groups ordered by Page. Imc and Combining options get <c>Files == null</c> (TexTools gives them no StandardData).
    /// </summary>
    /// <param name="game">
    /// Optional. When given, FileSwaps are mirrored the way TexTools' UnpackPmpOption(includeData: false) does:
    /// keyed by the swap's SOURCE path, only if that game file exists, as a <see cref="PenumbraFileSwapSource"/>.
    /// When null, FileSwaps are not listed.
    /// </param>
    public static ModpackData Read(string modFolder, IGameData? game = null)
    {
        var json = PenumbraModJson.Load(modFolder);
        var data = new ModpackData { Name = json.Name };
        var sources = new Dictionary<string, DiskFileSource>(StringComparer.Ordinal);

        foreach (var c in json.Containers)
        {
            data.Options.Add(new ModOption
            {
                GroupName = c.GroupName,
                OptionName = c.OptionName,
                Key = c.Key,
                Files = c.HasStandardData ? ReadFiles(modFolder, c, sources, game) : null,
            });
        }
        return data;
    }

    private static Dictionary<string, FileSource> ReadFiles(string modFolder, PenumbraContainer c,
        Dictionary<string, DiskFileSource> sources, IGameData? game)
    {
        var files = new Dictionary<string, FileSource>(StringComparer.Ordinal);
        foreach (var (gamePath, rel) in PenumbraModJson.FileEntries(c.Option))
        {
            if (!PenumbraModJson.CanImport(gamePath)) continue;

            // Refuse traversal now rather than when the file is first read.
            ModPaths.Resolve(modFolder, rel);
            var norm = NormalizeRelative(rel);
            if (!sources.TryGetValue(norm, out var source))
            {
                source = new DiskFileSource(modFolder, rel);
                sources.Add(norm, source);
            }
            files.Add(gamePath, source);
        }

        if (game is null || c.Option["FileSwaps"] is not System.Text.Json.Nodes.JsonObject swaps) return files;

        foreach (var kv in swaps)
        {
            // "For some reason the destination value is backslashed instead of forward-slashed."
            var src = PenumbraModJson.Str(kv.Value).Replace("\\", "/");
            var dest = kv.Key.Replace("\\", "/");
            if (!PenumbraModJson.CanImport(src) || !PenumbraModJson.CanImport(dest)) continue;
            if (!game.FileExists(src)) continue;

            // TexTools quirk kept: keyed by src, not dest.
            if (files.ContainsKey(src)) continue;
            files.Add(src, new PenumbraFileSwapSource(src));
        }
        return files;
    }

    /// <summary>Comparison form of a mod-relative path: backslashes, lower case (Windows file names).</summary>
    internal static string NormalizeRelative(string rel) =>
        rel.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
}

/// <summary>
/// A FileSwaps entry as TexTools lists it: a blank FileStorageInformation. Reading it fails exactly like
/// TexTools' GetUncompressedFile does (File.OpenRead(null)). Never written back as a file.
/// </summary>
public sealed class PenumbraFileSwapSource(string sourceGamePath) : FileSource
{
    public string SourceGamePath { get; } = sourceGamePath;

    public override byte[] Read() => throw new ArgumentNullException("path", "TexTools lists file swaps with no file behind them.");
}
