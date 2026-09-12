namespace DawntrailReady.Core.Game;

/// <summary>
/// Read-only access to the game's own (vanilla) files, by game path. TexTools reads these through a read-only
/// transaction ("rtx") to copy reference values: the glass and hair sample materials, vanilla hair and iris
/// materials, and the eye base textures. The plugin backs this with Dalamud's IDataManager; tests use a fake.
/// Implementations must be safe to call from a background thread.
/// </summary>
public interface IGameData
{
    /// <summary>True when the game itself ships a file at this path (TexTools: <c>rtx.FileExists(path, true)</c>).</summary>
    bool FileExists(string gamePath);

    /// <summary>The uncompressed bytes of a game file, or null when it does not exist.</summary>
    byte[]? ReadFile(string gamePath);
}
