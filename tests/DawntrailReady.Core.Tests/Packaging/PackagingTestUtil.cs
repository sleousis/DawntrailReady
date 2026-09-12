using System.IO.Compression;
using System.Text;
using DawntrailReady.Core.Packaging;

namespace DawntrailReady.Core.Tests.Packaging;

/// <summary>A throw-away folder under the system temp path.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtr-pkg-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string rel, string text) => File(rel, Encoding.UTF8.GetBytes(text));

    public string File(string rel, byte[] bytes)
    {
        var full = Combine(rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, bytes);
        return full;
    }

    public string Combine(string rel) => System.IO.Path.Combine(Path, rel.Replace('\\', System.IO.Path.DirectorySeparatorChar));

    public byte[] Bytes(string rel) => System.IO.File.ReadAllBytes(Combine(rel));

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch (IOException) { }
    }
}

internal static class PackagingTestUtil
{
    public static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Game path to bytes for comparing option file sets.</summary>
    public static Dictionary<string, string> Contents(ModOption o) =>
        o.Files!.ToDictionary(kv => kv.Key, kv => Encoding.UTF8.GetString(kv.Value.Read()));

    /// <summary>A standard (type 2) SqPack file holding <paramref name="raw"/> in one stored (uncompressed) block.</summary>
    public static byte[] Type2(byte[] raw)
    {
        const int headerSize = 128;
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(headerSize);
        bw.Write(2);
        bw.Write(raw.Length);
        bw.Write(0);
        bw.Write(0);
        bw.Write(1);                       // block count
        bw.Write(0);                       // block offset
        bw.Write((ushort)(16 + raw.Length));
        bw.Write((ushort)raw.Length);
        while (ms.Length < headerSize) bw.Write((byte)0);
        bw.Write(16);                      // block header size
        bw.Write(0);
        bw.Write(32000);                   // stored, not deflated
        bw.Write(raw.Length);
        bw.Write(raw);
        while (ms.Length % 128 != 0) bw.Write((byte)0);
        return ms.ToArray();
    }

    /// <summary>Concatenates type-2 blobs into an .mpd; returns (offset, size) per input.</summary>
    public static (byte[] Mpd, List<(long Offset, int Size)> Entries) Mpd(params byte[][] files)
    {
        using var ms = new MemoryStream();
        var entries = new List<(long, int)>();
        foreach (var f in files)
        {
            var blob = Type2(f);
            entries.Add((ms.Position, blob.Length));
            ms.Write(blob);
        }
        return (ms.ToArray(), entries);
    }

    public static void Zip(string path, params (string Name, byte[] Data)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(data);
        }
    }
}
