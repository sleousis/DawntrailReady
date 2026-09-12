namespace DawntrailReady.Core.Packaging;

/// <summary>
/// Where the uncompressed bytes of one mod file come from. TexTools calls this FileStorageInformation.
/// The upgrader never edits a source in place: a changed file replaces the option's entry with a new
/// <see cref="MemoryFileSource"/>. So "did this option change" is reference inequality, exactly like
/// TexTools' <c>ModpackUpgrader.AnyChanges</c>.
/// </summary>
public abstract class FileSource
{
    /// <summary>The uncompressed file bytes (a .mdl, .mtrl, .tex as the game reads them).</summary>
    public abstract byte[] Read();
}

/// <summary>A file inside an extracted mod folder, addressed by its path relative to that folder.</summary>
public sealed class DiskFileSource(string modFolder, string relativePath) : FileSource
{
    public string ModFolder { get; } = modFolder;

    /// <summary>As written in the mod's JSON (either slash), e.g. <c>hoodie\chara\...\x.mtrl</c>.</summary>
    public string RelativePath { get; } = relativePath;

    public string FullPath => ModPaths.Resolve(ModFolder, RelativePath);

    public override byte[] Read() => File.ReadAllBytes(FullPath);
}

/// <summary>Bytes produced by the upgrade (or decompressed from a .ttmp2 blob).</summary>
public sealed class MemoryFileSource(byte[] data) : FileSource
{
    public byte[] Data { get; } = data;

    /// <summary>
    /// A fresh copy each time, like TexTools re-reading its temp file: the upgrade edits arrays in place (models,
    /// materials) and may throw half-way, which must never change the source.
    /// </summary>
    public override byte[] Read() => (byte[])Data.Clone();
}
