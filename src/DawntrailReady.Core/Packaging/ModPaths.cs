namespace DawntrailReady.Core.Packaging;

/// <summary>
/// Path rules for files inside a mod. Every relative path read from a mod's JSON or archive is untrusted:
/// it must resolve inside the mod folder, never above it.
/// </summary>
public static class ModPaths
{
    /// <summary>
    /// Joins a mod-relative path onto the mod folder and refuses anything that escapes it
    /// (<c>..\..\x</c>, rooted paths, drive letters).
    /// </summary>
    public static string Resolve(string modFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("Empty file path in mod.");
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(rel)) throw new InvalidDataException($"Rooted file path in mod: {relativePath}");

        var root = Path.GetFullPath(modFolder);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"File path escapes the mod folder: {relativePath}");
        return full;
    }

    /// <summary>Game paths are lower-case with forward slashes (TexTools and Penumbra both key on that form).</summary>
    public static string NormalizeGamePath(string gamePath) => gamePath.Replace('\\', '/').Trim().ToLowerInvariant();
}
