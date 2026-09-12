using DawntrailReady.Core.Game;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Upgrade;

namespace DawntrailReady.Core.Service;

/// <summary>A mod the upgrade would give a texture too large to load safely. It is left untouched.</summary>
public sealed class TextureTooLargeException(string message) : Exception(message);

/// <summary>
/// Keeps the upgrade from producing textures bigger than <see cref="MaxSide"/> on a side. TexTools would make them
/// (uncompressed, so an 8192x8192 texture is 256 MB of video memory and far more memory while it is built), and
/// loading them can overload the graphics card. Such mods are left untouched instead; everything else is unchanged.
/// </summary>
public static class TextureSizeGuard
{
    public const int MaxSide = 4096;

    /// <summary>
    /// Before the upgrade runs: TexTools' iris conversion paints an old mask onto a canvas four times its size, so
    /// catch those here, before that canvas is ever allocated.
    /// </summary>
    public static void CheckBeforeUpgrade(ModpackData data, IGameData game)
    {
        foreach (var (path, source) in PreDawntrailDetector.EyeMasksToConvert(data, game))
        {
            var (w, h) = Dimensions(source);
            if (w * 4 > MaxSide || h * 4 > MaxSide)
                throw new TextureTooLargeException(
                    $"Left untouched: updating it would create a {w * 4}x{h * 4} eye texture (from {path}), larger than {MaxSide}x{MaxSide}, which can overload the graphics card.");
        }
    }

    /// <summary>After the upgrade ran in memory, before anything is written: every texture it created or changed.</summary>
    public static void CheckAfterUpgrade(UpgradeResult result)
    {
        foreach (var change in result.Changes)
        {
            if (!(change.Added || change.ContentChanged) || !change.GamePath.EndsWith(".tex", StringComparison.Ordinal)) continue;
            if (change.Option.Files is null || !change.Option.Files.TryGetValue(change.GamePath, out var source)) continue;
            if (source is PenumbraFileSwapSource) continue;
            var (w, h) = Dimensions(source);
            if (w > MaxSide || h > MaxSide)
                throw new TextureTooLargeException(
                    $"Left untouched: updating it would create a {w}x{h} texture ({change.GamePath}), larger than {MaxSide}x{MaxSide}, which can overload the graphics card.");
        }
    }

    /// <summary>Width and height from the 80-byte .tex header (bytes 8 and 10); only the header is read.</summary>
    private static (int W, int H) Dimensions(FileSource source)
    {
        byte[] head;
        switch (source)
        {
            case MemoryFileSource memory:
                head = memory.Data;
                break;
            case DiskFileSource disk:
                using (var fs = File.OpenRead(disk.FullPath))
                {
                    head = new byte[12];
                    if (fs.ReadAtLeast(head, 12, throwOnEndOfStream: false) < 12) return (0, 0);
                }
                break;
            default:
                head = source.Read();
                break;
        }
        return head.Length < 12 ? (0, 0) : (BitConverter.ToUInt16(head, 8), BitConverter.ToUInt16(head, 10));
    }
}
