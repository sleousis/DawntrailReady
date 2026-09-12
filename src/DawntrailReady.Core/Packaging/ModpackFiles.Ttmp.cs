// Ports: TexTools Mods/FileTypes/TTMP.cs (GetModpackType, GetModpackList, GetLegacyModpackMpl, UnzipTtmp,
// DoesModpackNeedFix) and Mods/WizardData.cs (FromModpack, FromWizardTtmp, FromSimpleTtmp, WizardGroupEntry.FromWizardGroup).
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DawntrailReady.Core.Packaging;

public static partial class ModpackFiles
{
    // Newtonsoft (TexTools) matches property names case-insensitively and reads numbers written as strings.
    private static readonly JsonSerializerOptions MplOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Reads a .ttmp2 (simple, wizard or backup) or legacy .ttmp into option file sets, exactly the options and files
    /// TexTools' WizardData.FromModpack builds. Dispose the result to delete the extracted .mpd.
    /// </summary>
    public static TexToolsModpack ReadTtmp(string ttmpPath)
    {
        var type = GetModpackType(ttmpPath);
        if (type is EModpackType.Invalid or EModpackType.Pmp)
            throw new InvalidDataException($"Not a TexTools modpack: {ttmpPath}");

        var mpl = GetModpackList(ttmpPath);
        var upgrades = DoesModpackNeedFix(mpl.TTMPVersion);

        var extract = Path.Combine(Path.GetTempPath(), "DawntrailReady", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extract);
        var pack = new TexToolsModpack
        {
            Data = new ModpackData { Name = mpl.Name ?? "" },
            ExtractFolder = extract,
            ModpackType = type,
            UpgradesNeeded = upgrades,
            TTMPVersion = mpl.TTMPVersion ?? "",
            Name = mpl.Name ?? "",
            Author = mpl.Author ?? "",
            Version = mpl.Version ?? "",
            Description = mpl.Description ?? "",
            Url = mpl.Url ?? "",
        };

        try
        {
            // WizardGroupEntry.FromWizardGroup reads Path.Combine(unzipPath, "TTMPD.mpd").
            var mpdPath = Path.Combine(extract, "TTMPD.mpd");
            ExtractMpd(ttmpPath, mpdPath);

            if (type == EModpackType.TtmpWizard)
            {
                var pages = mpl.ModPackPages ?? throw new InvalidDataException("Wizard modpack has no ModPackPages.");
                for (var pi = 0; pi < pages.Count; pi++)
                {
                    var groups = pages[pi].ModGroups ?? throw new InvalidDataException("Modpack page has no ModGroups.");
                    for (var gi = 0; gi < groups.Count; gi++)
                        FromWizardGroup(pack, groups[gi], pi, $"page:{pi}/group:{gi}", mpdPath, upgrades);
                }
            }
            else
            {
                // FromSimpleTtmp: one fake single-select group holding SimpleModsList.
                var group = new ModGroupJson
                {
                    GroupName = "Default Group",
                    SelectionType = "Single",
                    OptionList =
                    [
                        new ModOptionJson
                        {
                            Name = "Default Option", IsChecked = true, SelectionType = "Single", GroupName = "Default Group",
                            ModsJsons = mpl.SimpleModsList ?? throw new InvalidDataException("Simple modpack has no SimpleModsList."),
                        },
                    ],
                };
                FromWizardGroup(pack, group, 0, "default", mpdPath, upgrades);
            }

            var metaCount = pack.MetaFiles.Values.Sum(l => l.Count);
            if (metaCount > 0)
                pack.Warnings.Add($"{metaCount} .meta/.rgsp files are kept as files; Penumbra turns them into its own settings when it imports the pack.");
            return pack;
        }
        catch
        {
            pack.Dispose();
            throw;
        }
    }

    /// <summary>WizardGroupEntry.FromWizardGroup. Empty groups (no options) are skipped.</summary>
    private static void FromWizardGroup(TexToolsModpack pack, ModGroupJson tGroup, int pageIndex, string keyPrefix,
        string mpdPath, UpgradesNeeded upgradesNeeded)
    {
        var options = tGroup.OptionList ?? throw new InvalidDataException("Modpack group has no OptionList.");
        if (options.Count == 0) return;

        var needsTexFix = (upgradesNeeded & UpgradesNeeded.NeedsTexFix) == UpgradesNeeded.NeedsTexFix;
        var needsMdlFix = (upgradesNeeded & UpgradesNeeded.NeedsMdlFix) == UpgradesNeeded.NeedsMdlFix;
        var mpdExists = File.Exists(mpdPath);

        var info = new TtmpGroupInfo
        {
            Name = tGroup.GroupName ?? "",
            SelectionType = tGroup.SelectionType == "Single" ? "Single" : "Multi",
            PageIndex = pageIndex,
        };

        for (var oi = 0; oi < options.Count; oi++)
        {
            var o = options[oi];
            var files = new Dictionary<string, FileSource>(StringComparer.Ordinal);
            var option = new ModOption
            {
                GroupName = info.Name,
                OptionName = o.Name ?? "",
                Key = keyPrefix == "default" ? "default" : $"{keyPrefix}/option:{oi}",
                Files = files,
            };

            foreach (var mj in o.ModsJsons ?? throw new InvalidDataException("Modpack option has no ModsJsons."))
            {
                // "Data may not be unzipped here if we're in import mode."
                if (!mpdExists) continue;
                var fullPath = mj.FullPath ?? throw new InvalidDataException("Modpack file entry has no FullPath.");
                var source = new TtmpFileSource(mpdPath, mj.ModOffset, mj.ModSize);

                if (fullPath.EndsWith(".meta", StringComparison.Ordinal) || fullPath.EndsWith(".rgsp", StringComparison.Ordinal))
                {
                    // NOT PORTED: TexTools converts these to option Manipulations here.
                    if (!pack.MetaFiles.TryGetValue(option, out var list)) pack.MetaFiles[option] = list = [];
                    list.Add((fullPath, source));
                    continue;
                }

                FileSource finfo = source;
                if (needsTexFix && fullPath.EndsWith(".tex", StringComparison.Ordinal))
                {
                    try
                    {
                        finfo = new MemoryFileSource(OldTexFix.FixOldTexData(source.Read()));
                    }
                    catch (Exception)
                    {
                        // File majorly broken, skip it.
                        continue;
                    }
                }
                else if (needsMdlFix && fullPath.EndsWith(".mdl", StringComparison.Ordinal))
                {
                    // NOT PORTED: EndwalkerUpgrade.FixOldModel (a full TTModel re-import).
                    pack.Warnings.Add($"Very old TexTools model: TexTools would also rebuild it through its model importer, which isn't available here; it gets the standard Dawntrail model update only ({fullPath}).");
                }

                // Duplicate paths: the last entry wins, keeping the first one's position.
                files[fullPath] = finfo;
            }

            info.Options.Add((option, o.Description ?? "", o.IsChecked));
            pack.Data.Options.Add(option);
        }

        if (info.SelectionType == "Single" && !info.Options.Any(x => x.Selected))
        {
            info.Options[0] = (info.Options[0].Option, info.Options[0].Description, true);
        }
        pack.Groups.Add(info);
    }

    /// <summary>TTMP.GetModpackType. Note the case-sensitive extension checks, as in TexTools.</summary>
    public static EModpackType GetModpackType(string path)
    {
        if (path.EndsWith(".pmp", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal) || Directory.Exists(path))
            return EModpackType.Pmp;
        if (path.EndsWith(".ttmp", StringComparison.Ordinal)) return EModpackType.TtmpOriginal;
        if (!path.EndsWith(".ttmp2", StringComparison.Ordinal)) return EModpackType.Invalid;

        using var zip = ZipFile.OpenRead(path);
        var mplEntry = zip.Entries.First(x => x.FullName.EndsWith(".mpl", StringComparison.Ordinal));
        using var reader = new StreamReader(mplEntry.Open());
        var mpj = JsonSerializer.Deserialize<ModPackJson>(reader.ReadToEnd(), MplOptions);
        if (mpj is null) return EModpackType.Invalid;

        // TexTools' MinimumFrameworkVersion check only runs when the version does NOT parse, and then compares null:
        // it never rejects anything. Not repeated here.
        var ttmpVersion = mpj.TTMPVersion ?? throw new InvalidDataException("Modpack has no TTMPVersion.");
        if (ttmpVersion.EndsWith('w')) return EModpackType.TtmpWizard;
        if (ttmpVersion.EndsWith('s')) return EModpackType.TtmpSimple;
        if (ttmpVersion.EndsWith('b')) return EModpackType.TtmpBackup;
        return EModpackType.Invalid;
    }

    /// <summary>TTMP.DoesModpackNeedFix(string).</summary>
    public static UpgradesNeeded DoesModpackNeedFix(string? version)
    {
        if (string.IsNullOrEmpty(version)) version = "0.0";
        int major = 0, minor = 0;
        var parts = version.Split('.');
        int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
        if (parts.Length > 1)
            int.TryParse(new string(parts[1].TakeWhile(char.IsDigit).ToArray()), NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
        if (major < 2) return UpgradesNeeded.NeedsTexFix | UpgradesNeeded.NeedsMdlFix;
        if (major == 2 && minor == 0) return UpgradesNeeded.NeedsTexFix;
        return UpgradesNeeded.None;
    }

    /// <summary>TTMP.GetModpackList (and GetLegacyModpackMpl for .ttmp).</summary>
    private static ModPackJson GetModpackList(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() == ".ttmp") return GetLegacyModpackMpl(path);

        using var zip = ZipFile.OpenRead(path);
        var mplEntry = zip.Entries.First(x => x.FullName.EndsWith(".mpl", StringComparison.Ordinal));
        using var reader = new StreamReader(mplEntry.Open());
        return JsonSerializer.Deserialize<ModPackJson>(reader.ReadToEnd(), MplOptions)
               ?? throw new InvalidDataException("Modpack list is empty.");
    }

    /// <summary>TTMP.GetLegacyModpackMpl: one JSON object per line, optionally after a "version" line.</summary>
    private static ModPackJson GetLegacyModpackMpl(string modpackPath)
    {
        var entries = new List<OriginalModPackJson>();
        using (var archive = ZipFile.OpenRead(modpackPath))
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".mpl", StringComparison.Ordinal)))
            {
                using var reader = new StreamReader(entry.Open());
                var line = reader.ReadLine() ?? throw new InvalidDataException("Empty legacy modpack list.");
                if (line.ToLowerInvariant().Contains("version"))
                {
                    // Skip this line and read the next
                    line = reader.ReadLine() ?? throw new InvalidDataException("Empty legacy modpack list.");
                }
                entries.Add(ParseLegacyLine(line));
                while (reader.Peek() >= 0)
                {
                    entries.Add(ParseLegacyLine(reader.ReadLine()!));
                }
            }
        }

        return new ModPackJson
        {
            Author = "Unknown",
            Description = "",
            Name = Path.GetFileNameWithoutExtension(modpackPath),
            Version = "1.0",
            TTMPVersion = "0.1s",
            Url = "",
            SimpleModsList = entries.Select(e => new ModsJson
            {
                FullPath = e.FullPath, DatFile = e.DatFile, Name = e.Name, Category = e.Category,
                ModSize = e.ModSize, ModOffset = e.ModOffset,
            }).ToList(),
        };
    }

    private static OriginalModPackJson ParseLegacyLine(string line) =>
        (line.Length == 0 ? null : JsonSerializer.Deserialize<OriginalModPackJson>(line, MplOptions))
        ?? throw new InvalidDataException("Invalid line in legacy modpack list.");

    private static void ExtractMpd(string ttmpPath, string mpdPath)
    {
        using var zip = ZipFile.OpenRead(ttmpPath);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('/', '\\').Equals("TTMPD.mpd", StringComparison.OrdinalIgnoreCase));
        entry?.ExtractToFile(mpdPath, true);
    }
}
