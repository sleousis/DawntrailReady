using System.IO.Compression;
using DawntrailReady.Core.SqPack;

namespace DawntrailReady.Core.Tests.SqPack;

public class SqPackFileTests
{
    [Fact]
    public void Type2_CompressedAndStoredBlocks_AtAnOffsetInsideABiggerStream()
    {
        var a = Payload(1000, 1);
        var b = Payload(300, 2);
        var c = Payload(16000, 3);
        var file = Type2((a, true), (b, false), (c, true));

        // Surround it like a TTMPD.mpd entry: other data before and after.
        var mpd = new byte[37 + file.Length + 50];
        Array.Fill(mpd, (byte)0xCC);
        file.CopyTo(mpd, 37);

        using var stream = new MemoryStream(mpd);
        var result = SqPackFile.Decompress(stream, 37, file.Length);

        Assert.Equal([.. a, .. b, .. c], result);
        Assert.True(stream.CanRead); // left open
    }

    [Fact]
    public void Type2_WrongTypeThroughType2Reader_Throws()
    {
        var file = Type2((Payload(10, 1), false));
        file[4] = 3;

        var ex = Assert.Throws<Exception>(() => Dat.ReadSqPackType2(file));
        Assert.Equal("Requested Type 2 file is not a valid type 2 file.", ex.Message);
    }

    [Fact]
    public void UnknownType_ThrowsNotImplemented()
    {
        var file = Type2((Payload(10, 1), false));
        file[4] = 7;

        using var stream = new MemoryStream(file);
        Assert.Throws<NotImplementedException>(() => SqPackFile.Decompress(stream, 0, file.Length));
    }

    [Fact]
    public void Type3_RebuildsTheUncompressedMdlHeaderAndConcatenatesBuffers()
    {
        var vi = Payload(0x88 * 2, 11);
        var md = Payload(700, 12);
        var vb = Payload(3000, 13);
        var eg = Payload(64, 14);
        var ib = Payload(600, 15);
        byte[][] blocks = [Block(vi, true), Block(md, false), Block(vb, true), Block(eg, false), Block(ib, true)];
        var o = new int[blocks.Length + 1];
        for (var i = 0; i < blocks.Length; i++)
        {
            o[i + 1] = o[i] + blocks[i].Length;
        }
        var sum = vi.Length + md.Length + vb.Length + eg.Length + ib.Length;

        const int headerLength = 256;
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(headerLength);
        bw.Write(3);
        bw.Write(sum);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0x01000005); // version
        bw.Write(vi.Length);
        bw.Write(md.Length);
        Write3(bw, vb.Length, 0, 0);
        Write3(bw, eg.Length, 0, 0);
        Write3(bw, ib.Length, 0, 0);
        bw.Write(blocks[0].Length);
        bw.Write(blocks[1].Length);
        Write3(bw, blocks[2].Length, 0, 0);
        Write3(bw, blocks[3].Length, 0, 0);
        Write3(bw, blocks[4].Length, 0, 0);
        bw.Write(o[0]);
        bw.Write(o[1]);
        Write3(bw, o[2], o[5], o[5]);
        Write3(bw, o[3], o[5], o[5]);
        Write3(bw, o[4], o[5], o[5]);
        Write3Short(bw, 0, 1); // vertex info, model data block indexes
        Write3Short(bw, 2, 3, 3);
        Write3Short(bw, 3, 4, 4);
        Write3Short(bw, 4, 5, 5);
        Write3Short(bw, 1, 1); // block counts
        Write3Short(bw, 1, 0, 0);
        Write3Short(bw, 1, 0, 0);
        Write3Short(bw, 1, 0, 0);
        bw.Write((ushort)2); // mesh count
        bw.Write((ushort)3); // material count
        bw.Write((byte)1); // LoD count
        bw.Write((byte)0x42); // flags
        bw.Write((byte)0x12);
        bw.Write((byte)0x34);
        foreach (var block in blocks)
        {
            bw.Write((ushort)block.Length);
        }
        while (ms.Length < headerLength)
        {
            bw.Write((byte)0xEE); // unread header tail
        }
        foreach (var block in blocks)
        {
            bw.Write(block);
        }

        var result = Dat.ReadSqPackFile(ms.ToArray());

        var end = (uint)(68 + sum);
        var vbStart = (uint)(68 + vi.Length + md.Length);
        var ibStart = (uint)(vbStart + vb.Length + eg.Length);
        using var expected = new MemoryStream();
        var ew = new BinaryWriter(expected);
        ew.Write(0x01000005);
        ew.Write(vi.Length);
        ew.Write(md.Length);
        ew.Write((ushort)2);
        ew.Write((ushort)3);
        Write3(ew, (int)vbStart, (int)end, (int)end);
        Write3(ew, (int)ibStart, (int)end, (int)end);
        Write3(ew, vb.Length, 0, 0);
        Write3(ew, ib.Length, 0, 0);
        ew.Write((byte)1);
        ew.Write((byte)0x42);
        ew.Write((byte)0x12);
        ew.Write((byte)0x34);
        Assert.Equal(68, expected.Length);
        ew.Write([.. vi, .. md, .. vb, .. eg, .. ib]);

        Assert.Equal(expected.ToArray(), result);
    }

    [Fact]
    public void Type4_CopiesTexHeaderAndInflatesMips()
    {
        var texHeader = Payload(80, 9);
        var mip0 = Payload(4096, 4);
        var mip1a = Payload(1024, 5);
        var mip1b = Payload(512, 6);
        var file = Type4(texHeader, [Block(mip0, true)], [Block(mip1a, false), Block(mip1b, true)], 80 + 4096 + 1536);

        using var stream = new MemoryStream(file);
        var result = SqPackFile.Decompress(stream, 0, file.Length);

        Assert.Equal([.. texHeader, .. mip0, .. mip1a, .. mip1b], result);
    }

    [Fact]
    public void Type4_OldTexToolsBlockSpacing_IsToleratedLikeTexTools()
    {
        // Block A is followed by 20 zero bytes (longer than its padding: the zero-skip loop finds block B),
        // block B by 3 zero bytes (inside its padding: the reader rewinds to block C's 0x10).
        var texHeader = Payload(80, 9);
        var a = Payload(100, 1);
        var b = Payload(200, 2);
        var c = Payload(50, 3);
        var d = Payload(900, 4);
        byte[] mip0Blocks =
        [
            .. Block(a, false, pad: false), .. new byte[20],
            .. Block(b, false, pad: false), .. new byte[3],
            .. Block(c, false),
        ];
        var file = Type4(texHeader, [mip0Blocks], [Block(d, true)], 80 + 100 + 200 + 50 + 900, mip0Parts: 3);

        var result = Dat.ReadSqPackFile(file);

        Assert.Equal([.. texHeader, .. a, .. b, .. c, .. d], result);
    }

    [Fact]
    public void Decompressor_ShortStream_LeavesTheRestZero()
    {
        var data = Payload(100, 1);
        var result = Dat.Decompressor(Deflate(data), 120);

        Assert.Equal([.. data, .. new byte[20]], result);
    }

    private static byte[] Payload(int length, int seed)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 7) + (seed * 13) + (i / 50));
        }
        return bytes;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            ds.Write(data);
        }
        return ms.ToArray();
    }

    // One SqPack data block: {16, 0, compressed size or 32000 when stored, uncompressed size}, data, zeros to 128.
    private static byte[] Block(byte[] payload, bool compress, bool pad = true)
    {
        var body = compress ? Deflate(payload) : payload;
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(16);
        bw.Write(0);
        bw.Write(compress ? body.Length : 32000);
        bw.Write(payload.Length);
        bw.Write(body);
        while (pad && ms.Length % 128 != 0)
        {
            bw.Write((byte)0);
        }
        return ms.ToArray();
    }

    private static byte[] Type2(params (byte[] Payload, bool Compress)[] parts)
    {
        const int headerLength = 128;
        var blocks = parts.Select(p => Block(p.Payload, p.Compress)).ToList();
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(headerLength);
        bw.Write(2);
        bw.Write(parts.Sum(p => p.Payload.Length));
        bw.Write(0);
        bw.Write(0);
        bw.Write(parts.Length);
        var offset = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            bw.Write(offset);
            bw.Write((ushort)blocks[i].Length);
            bw.Write((ushort)parts[i].Payload.Length);
            offset += blocks[i].Length;
        }
        while (ms.Length < headerLength)
        {
            bw.Write((byte)0);
        }
        foreach (var block in blocks)
        {
            bw.Write(block);
        }
        return ms.ToArray();
    }

    private static byte[] Type4(byte[] texHeader, byte[][] mip0, byte[][] mip1, int uncompressedSize, int? mip0Parts = null)
    {
        const int headerLength = 128;
        var mip0Bytes = mip0.SelectMany(x => x).ToArray();
        var mip1Bytes = mip1.SelectMany(x => x).ToArray();
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(headerLength);
        bw.Write(4);
        bw.Write(uncompressedSize);
        bw.Write(0);
        bw.Write(0);
        bw.Write(2); // mip count
        // {offset from header end, compressed length, uncompressed size, first block index, block count}
        bw.Write(80);
        bw.Write(mip0Bytes.Length);
        bw.Write(0);
        bw.Write(0);
        bw.Write(mip0Parts ?? mip0.Length);
        bw.Write(80 + mip0Bytes.Length);
        bw.Write(mip1Bytes.Length);
        bw.Write(0);
        bw.Write(mip0Parts ?? mip0.Length);
        bw.Write(mip1.Length);
        while (ms.Length < headerLength)
        {
            bw.Write((byte)0);
        }
        bw.Write(texHeader);
        bw.Write(mip0Bytes);
        bw.Write(mip1Bytes);
        return ms.ToArray();
    }

    private static void Write3(BinaryWriter bw, int a, int b, int c)
    {
        bw.Write(a);
        bw.Write(b);
        bw.Write(c);
    }

    private static void Write3Short(BinaryWriter bw, params ushort[] values)
    {
        foreach (var v in values)
        {
            bw.Write(v);
        }
    }
}
