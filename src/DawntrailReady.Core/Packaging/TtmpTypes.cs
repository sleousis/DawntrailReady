// Ports: TexTools Mods/DataContainers/ModPackJson.cs, Mods/DataContainers/OriginalModPackJson.cs,
// Mods/FileTypes/TTMP.cs (EModpackType, UpgradesNeeded).
using DawntrailReady.Core.SqPack;

namespace DawntrailReady.Core.Packaging;

public enum EModpackType
{
    Invalid,
    TtmpOriginal,
    TtmpSimple,
    TtmpWizard,
    TtmpBackup,
    Pmp,
}

[Flags]
public enum UpgradesNeeded
{
    None = 0x00,
    NeedsTexFix = 0x01,
    NeedsMdlFix = 0x02,
}

/// <summary>One file inside a TTMP's TTMPD.mpd blob, decompressed on every <see cref="Read"/>.</summary>
public sealed class TtmpFileSource(string mpdPath, long offset, int size) : FileSource
{
    public string MpdPath { get; } = mpdPath;
    public long Offset { get; } = offset;
    public int Size { get; } = size;

    public override byte[] Read()
    {
        using var fs = File.OpenRead(MpdPath);
        return SqPackFile.Decompress(fs, Offset, Size);
    }
}

/// <summary>A TTMP option group as the wizard shows it (needed to write the pack out as a PMP).</summary>
public sealed class TtmpGroupInfo
{
    public string Name { get; init; } = "";

    /// <summary>"Single" or "Multi" (anything but "Single" in the .mpl reads as Multi, like TexTools).</summary>
    public string SelectionType { get; init; } = "Single";

    public int PageIndex { get; init; }
    public List<(ModOption Option, string Description, bool Selected)> Options { get; } = [];
}

/// <summary>A read .ttmp/.ttmp2. Owns a temp folder holding the extracted TTMPD.mpd; dispose to delete it.</summary>
public sealed class TexToolsModpack : IDisposable
{
    public required ModpackData Data { get; init; }
    public required string ExtractFolder { get; init; }
    public EModpackType ModpackType { get; init; }
    public UpgradesNeeded UpgradesNeeded { get; init; }
    public string TTMPVersion { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public string Url { get; init; } = "";
    public List<TtmpGroupInfo> Groups { get; } = [];

    /// <summary>
    /// .meta/.rgsp entries per option. TexTools turns these into Penumbra manipulations
    /// (ItemMetadata.Deserialize + PMPExtensions.MetadataToManipulations); that conversion is NOT PORTED, so they are kept raw.
    /// </summary>
    public Dictionary<ModOption, List<(string GamePath, FileSource Source)>> MetaFiles { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>Same as <see cref="ToPmpMeta"/>: what to pass to <see cref="ModpackFiles.WritePmp"/> for this pack.</summary>
    public PmpMeta Meta => ToPmpMeta();

    /// <summary>Group/option settings for <see cref="ModpackFiles.WritePmp"/>, as TexTools' WizardData.WritePmp would write them.</summary>
    public PmpMeta ToPmpMeta()
    {
        var meta = new PmpMeta { Name = Name, Author = Author, Description = Description, Version = Version, Website = Url };
        foreach (var g in Groups)
        {
            ulong selection = 0;
            if (g.SelectionType == "Single")
            {
                var idx = g.Options.FindIndex(o => o.Selected);
                selection = idx < 0 ? 0UL : (ulong)idx;
            }
            else
            {
                for (var i = 0; i < g.Options.Count; i++)
                {
                    if (g.Options[i].Selected) selection |= 1UL << i;
                }
            }

            var gm = new PmpGroupMeta { Type = g.SelectionType, DefaultSettings = selection, Page = g.PageIndex };
            foreach (var (option, description, _) in g.Options) gm.OptionDescriptions.TryAdd(option.OptionName, description);
            meta.Groups.TryAdd(g.Name, gm);
        }
        return meta;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(ExtractFolder)) Directory.Delete(ExtractFolder, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class ModPackJson
{
    public string? TTMPVersion { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? MinimumFrameworkVersion { get; set; }
    public List<ModPackPageJson>? ModPackPages { get; set; }
    public List<ModsJson>? SimpleModsList { get; set; }
}

internal sealed class ModPackPageJson
{
    public int PageIndex { get; set; }
    public List<ModGroupJson>? ModGroups { get; set; }
}

internal sealed class ModGroupJson
{
    public string? GroupName { get; set; }
    public string? SelectionType { get; set; }
    public List<ModOptionJson>? OptionList { get; set; }
}

internal sealed class ModOptionJson
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public List<ModsJson>? ModsJsons { get; set; }
    public string? GroupName { get; set; }
    public string? SelectionType { get; set; }
    public bool IsChecked { get; set; }
}

internal sealed class ModsJson
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? FullPath { get; set; }
    public long ModOffset { get; set; }
    public int ModSize { get; set; }
    public string? DatFile { get; set; }
    public bool IsDefault { get; set; }
}

internal sealed class OriginalModPackJson
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? FullPath { get; set; }
    public long ModOffset { get; set; }
    public int ModSize { get; set; }
    public string? DatFile { get; set; }
}
