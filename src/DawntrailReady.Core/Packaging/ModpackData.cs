namespace DawntrailReady.Core.Packaging;

/// <summary>
/// A mod as the upgrader sees it: TexTools' WizardData reduced to what the Dawntrail upgrade touches.
/// Every option, the default option included, is its own file set. TexTools upgrades each option on its own
/// and never merges the default option's files into another option, so neither do we.
/// </summary>
public sealed class ModpackData
{
    public string Name { get; set; } = "";

    /// <summary>
    /// The default option (Penumbra's DefaultData / default_mod.json, a simple TTMP's file list) first,
    /// then every option of every group in file order.
    /// </summary>
    public List<ModOption> Options { get; } = [];

    /// <summary>Options that carry files. TexTools' loops skip options without StandardData the same way.</summary>
    public IEnumerable<ModOption> FileOptions => Options.Where(o => o.Files is not null);
}

/// <summary>One option (or the default container): game path to file.</summary>
public sealed class ModOption
{
    public string GroupName { get; init; } = "";
    public string OptionName { get; init; } = "";

    /// <summary>
    /// Opaque handle the reader/writer uses to find this option again in the source JSON
    /// (e.g. "default", or "group:3/option:1"). The upgrader never reads it.
    /// </summary>
    public string Key { get; init; } = "";

    /// <summary>
    /// Game path (lower-case, forward slashes) to its file. Null for options TexTools would give no
    /// StandardData (e.g. IMC groups). Keyed with ordinal comparison, like TexTools' dictionaries.
    /// </summary>
    public Dictionary<string, FileSource>? Files { get; set; } = new(StringComparer.Ordinal);
}
