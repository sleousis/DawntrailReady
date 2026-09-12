using System.Text.Json.Nodes;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Tests.Packaging.PackagingTestUtil;

namespace DawntrailReady.Core.Tests.Packaging;

public class PenumbraWriteTests
{
    // Tab-indented like Penumbra 1.x, with fields this code knows nothing about.
    private const string Meta = "{\n" +
        "\t\"FileVersion\": 4,\n" +
        "\t\"Name\": \"Mod\",\n" +
        "\t\"Description\": \"🗲 keep me \\u00e9\",\n" +
        "\t\"Zzz\": { \"keep\": 111050674405376 },\n" +
        "\t\"DefaultData\": { \"Files\": { \"chara/a.tex\": \"files\\\\a.tex\" }, \"FileSwaps\": {}, \"Manipulations\": [] },\n" +
        "\t\"Groups\": [ { \"Type\": \"Single\", \"Id\": \"g-1\", \"Name\": \"G\", \"Options\": [\n" +
        "\t\t{ \"Id\": \"o-1\", \"Name\": \"O1\", \"Files\": { \"chara/m.mtrl\": \"o1\\\\chara\\\\m.mtrl\", \"chara/shared.tex\": \"common\\\\s.tex\" } },\n" +
        "\t\t{ \"Id\": \"o-2\", \"Name\": \"O2\", \"Files\": { \"chara/m.mtrl\": \"o2\\\\chara\\\\m.mtrl\", \"chara/shared.tex\": \"common\\\\s.tex\" } } ] } ]\n" +
        "}\n";

    private static TempDir MakeMod()
    {
        var dir = new TempDir();
        dir.File("meta.json", Meta);
        dir.File("files\\a.tex", "A");
        dir.File("o1\\chara\\m.mtrl", "M1");
        dir.File("o2\\chara\\m.mtrl", "M2");
        dir.File("common\\s.tex", "S");
        return dir;
    }

    private static ModOption Opt(ModpackData d, string name) => d.Options.Single(o => o.OptionName == name);

    private static string Rel(JsonNode meta, int option, string gamePath) =>
        meta["Groups"]![0]!["Options"]![option]!["Files"]![gamePath]!.GetValue<string>();

    [Fact]
    public void No_Changes_Writes_Nothing()
    {
        using var dir = MakeMod();
        var before = dir.Bytes("meta.json");

        var r = PenumbraModFolder.WriteChanges(dir.Path, PenumbraModFolder.Read(dir.Path));

        Assert.Equal((0, 0, false), (r.FilesWritten, r.FilesAdded, r.JsonChanged));
        Assert.Equal(before, dir.Bytes("meta.json"));
    }

    [Fact]
    public void Sole_Reference_Is_Written_In_Place()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/m.mtrl"] = new MemoryFileSource(B("NEW"));

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.Equal((1, 0, false), (r.FilesWritten, r.FilesAdded, r.JsonChanged));
        Assert.Equal("NEW", System.Text.Encoding.UTF8.GetString(dir.Bytes("o1\\chara\\m.mtrl")));
        Assert.Equal("M2", System.Text.Encoding.UTF8.GetString(dir.Bytes("o2\\chara\\m.mtrl")));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Shared_File_Still_Used_Elsewhere_Gets_A_Sibling()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/shared.tex"] = new MemoryFileSource(B("S1"));

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.Equal((0, 1, true), (r.FilesWritten, r.FilesAdded, r.JsonChanged));
        Assert.Equal("S", System.Text.Encoding.UTF8.GetString(dir.Bytes("common\\s.tex")));
        Assert.Equal("S1", System.Text.Encoding.UTF8.GetString(dir.Bytes("common\\s_dt.tex")));
        var meta = JsonNode.Parse(File.ReadAllText(dir.Combine("meta.json")))!;
        Assert.Equal("common\\s_dt.tex", Rel(meta, 0, "chara/shared.tex"));
        Assert.Equal("common\\s.tex", Rel(meta, 1, "chara/shared.tex"));

        // Re-read: each option resolves to the bytes the upgrade gave it.
        var again = PenumbraModFolder.Read(dir.Path);
        Assert.Equal("S1", Contents(Opt(again, "O1"))["chara/shared.tex"]);
        Assert.Equal("S", Contents(Opt(again, "O2"))["chara/shared.tex"]);
    }

    [Fact]
    public void Shared_File_Replaced_Identically_Everywhere_Is_Written_In_Place()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/shared.tex"] = new MemoryFileSource(B("SX"));
        Opt(data, "O2").Files!["chara/shared.tex"] = new MemoryFileSource(B("SX"));

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.Equal((1, 0, false), (r.FilesWritten, r.FilesAdded, r.JsonChanged));
        Assert.Equal("SX", System.Text.Encoding.UTF8.GetString(dir.Bytes("common\\s.tex")));
    }

    [Fact]
    public void Shared_File_Replaced_Differently_Gets_Numbered_Siblings()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/shared.tex"] = new MemoryFileSource(B("S1"));
        Opt(data, "O2").Files!["chara/shared.tex"] = new MemoryFileSource(B("S2"));

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.Equal((0, 2), (r.FilesWritten, r.FilesAdded));
        var meta = JsonNode.Parse(File.ReadAllText(dir.Combine("meta.json")))!;
        Assert.Equal("common\\s_dt.tex", Rel(meta, 0, "chara/shared.tex"));
        Assert.Equal("common\\s_dt2.tex", Rel(meta, 1, "chara/shared.tex"));
        Assert.Equal("S", System.Text.Encoding.UTF8.GetString(dir.Bytes("common\\s.tex")));
    }

    [Fact]
    public void New_Game_Paths_Go_Next_To_The_Option_Files_And_Are_Deduplicated()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/new.tex"] = new MemoryFileSource(B("X"));
        Opt(data, "O2").Files!["chara/new.tex"] = new MemoryFileSource(B("X"));

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.Equal((0, 1, true), (r.FilesWritten, r.FilesAdded, r.JsonChanged));
        var meta = JsonNode.Parse(File.ReadAllText(dir.Combine("meta.json")))!;
        Assert.Equal("o1\\chara\\new.tex", Rel(meta, 0, "chara/new.tex"));
        Assert.Equal("o1\\chara\\new.tex", Rel(meta, 1, "chara/new.tex"));
        Assert.Equal("X", System.Text.Encoding.UTF8.GetString(dir.Bytes("o1\\chara\\new.tex")));
    }

    [Fact]
    public void Existing_Mod_Files_Are_Referenced_Not_Copied_And_Removals_Drop_Keys()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/copy.tex"] = data.Options[0].Files!["chara/a.tex"];
        Opt(data, "O2").Files!.Remove("chara/shared.tex");

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.Equal((0, 0, true), (r.FilesWritten, r.FilesAdded, r.JsonChanged));
        var meta = JsonNode.Parse(File.ReadAllText(dir.Combine("meta.json")))!;
        Assert.Equal("files\\a.tex", Rel(meta, 0, "chara/copy.tex"));
        Assert.Null(meta["Groups"]![0]!["Options"]![1]!["Files"]!["chara/shared.tex"]);
        Assert.True(File.Exists(dir.Combine("common\\s.tex")));
    }

    [Fact]
    public void Json_Keeps_Unknown_Fields_Order_And_Formatting()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/m.mtrl"] = new MemoryFileSource(B("S")); // same bytes as common\s.tex: no dedupe with unchanged files
        Opt(data, "O2").Files!["chara/m.mtrl"] = data.Options[0].Files!["chara/a.tex"];

        PenumbraModFolder.WriteChanges(dir.Path, data);

        var text = File.ReadAllText(dir.Combine("meta.json"));
        Assert.Contains("\n\t\"Name\"", text);
        Assert.Contains("🗲 keep me é", text);
        Assert.Contains("111050674405376", text);
        var meta = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(["FileVersion", "Name", "Description", "Zzz", "DefaultData", "Groups"], meta.Select(kv => kv.Key));
        var o2 = meta["Groups"]![0]!["Options"]![1]!.AsObject();
        Assert.Equal(["Id", "Name", "Files"], o2.Select(kv => kv.Key));
        Assert.Equal(["chara/m.mtrl", "chara/shared.tex"], o2["Files"]!.AsObject().Select(kv => kv.Key));
        Assert.Equal("files\\a.tex", Rel(meta, 1, "chara/m.mtrl"));
        Assert.Equal("g-1", meta["Groups"]![0]!["Id"]!.GetValue<string>());
    }

    [Fact]
    public void Failure_Restores_Every_File_And_Deletes_New_Ones()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        Opt(data, "O1").Files!["chara/m.mtrl"] = new MemoryFileSource(B("NEW"));
        Opt(data, "O1").Files!["chara/brand/new.tex"] = new MemoryFileSource(B("N"));
        var metaBefore = dir.Bytes("meta.json");
        File.SetAttributes(dir.Combine("meta.json"), FileAttributes.ReadOnly);

        Assert.ThrowsAny<Exception>(() => PenumbraModFolder.WriteChanges(dir.Path, data));

        File.SetAttributes(dir.Combine("meta.json"), FileAttributes.Normal);
        Assert.Equal("M1", System.Text.Encoding.UTF8.GetString(dir.Bytes("o1\\chara\\m.mtrl")));
        Assert.False(File.Exists(dir.Combine("o1\\chara\\brand\\new.tex")));
        Assert.False(Directory.Exists(dir.Combine("o1\\chara\\brand")));
        Assert.Equal(metaBefore, dir.Bytes("meta.json"));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Imc_Options_And_Unknown_Keys_Are_Left_Alone()
    {
        using var dir = MakeMod();
        var data = PenumbraModFolder.Read(dir.Path);
        data.Options.Add(new ModOption { GroupName = "X", OptionName = "Y", Key = "group:9/option:9", Files = new() { ["chara/z.tex"] = new MemoryFileSource(B("Z")) } });

        var r = PenumbraModFolder.WriteChanges(dir.Path, data);

        Assert.False(r.JsonChanged);
        Assert.Single(r.Warnings);
    }
}
