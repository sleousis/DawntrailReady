using DawntrailReady.Core.Textures;

namespace DawntrailReady.Core.Tests.Textures;

public class TexTests
{
    private static byte[] Pixels(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7 + 3);
        }
        return data;
    }

    [Fact]
    public async Task ConvertToDDS_Fast8888_WritesHeaderSwizzledPixelsAndNearestMips()
    {
        var rgba = Pixels(8, 4);
        var input = (byte[])rgba.Clone();

        var dds = await Tex.ConvertToDDS(input, XivTexFormat.A8R8G8B8, true, 8, 4);

        // mipCount = (int)log2(min(8, 4)) = 2: 8x4 and 4x2.
        Assert.Equal(128 + 8 * 4 * 4 + 4 * 2 * 4, dds.Length);
        Assert.Equal("DDS "u8.ToArray(), dds[..4]);
        Assert.Equal(124, BitConverter.ToInt32(dds, 4));
        Assert.Equal(0x21007, BitConverter.ToInt32(dds, 8));
        Assert.Equal(4, BitConverter.ToInt32(dds, 12));
        Assert.Equal(8, BitConverter.ToInt32(dds, 16));
        Assert.Equal(32, BitConverter.ToInt32(dds, 20));
        Assert.Equal(2, BitConverter.ToInt32(dds, 28));
        Assert.Equal(32, BitConverter.ToInt32(dds, 76));
        Assert.Equal(0x41, BitConverter.ToInt32(dds, 80));
        Assert.Equal(0, BitConverter.ToInt32(dds, 84));
        Assert.Equal(32, BitConverter.ToInt32(dds, 88));
        Assert.Equal(0x00ff0000, BitConverter.ToInt32(dds, 92));
        Assert.Equal(0x0000ff00, BitConverter.ToInt32(dds, 96));
        Assert.Equal(0x000000ff, BitConverter.ToInt32(dds, 100));
        Assert.Equal(0xff000000u, BitConverter.ToUInt32(dds, 104));

        // Like TexTools, the input array itself is swizzled to BGRA.
        for (int i = 0; i < rgba.Length; i += 4)
        {
            Assert.Equal(rgba[i + 2], input[i]);
            Assert.Equal(rgba[i], input[i + 2]);
        }
        Assert.Equal(input, dds[128..(128 + 128)]);

        // Mip 1 pixel (x, y) is mip 0 pixel (2x, 2y).
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var src = ((y * 2 * 8) + x * 2) * 4;
                var dst = 128 + 128 + ((y * 4) + x) * 4;
                Assert.Equal(input[src..(src + 4)], dds[dst..(dst + 4)]);
            }
        }
    }

    [Fact]
    public async Task ConvertToDDS_Fast8888_CapsAreOverwrittenByTheAlphaMask()
    {
        // TexTools writes dwCaps at offset 104, which is also the pixel format's alpha mask; the mask is
        // written last, and the real dwCaps field (108) stays zero.
        var withMips = await Tex.ConvertToDDS(Pixels(8, 4), XivTexFormat.A8R8G8B8, true, 8, 4);
        var single = await Tex.ConvertToDDS(Pixels(8, 1), XivTexFormat.A8R8G8B8, true, 8, 1);

        Assert.Equal(0xff000000u, BitConverter.ToUInt32(withMips, 104));
        Assert.Equal(0, BitConverter.ToInt32(withMips, 108));
        Assert.Equal(0, BitConverter.ToInt32(single, 108));
        Assert.Equal(1, BitConverter.ToInt32(single, 28));
    }

    [Fact]
    public async Task Fast8888Dds_ToUncompressedTex_HasTexToolsHeaderAndRoundTrips()
    {
        var rgba = Pixels(8, 4);

        var dds = await Tex.ConvertToDDS((byte[])rgba.Clone(), XivTexFormat.A8R8G8B8, true, 8, 4);
        var tex = Tex.DDSToUncompressedTex(dds);

        Assert.Equal(80 + 128 + 32, tex.Length);
        Assert.Equal(0x00800000u, BitConverter.ToUInt32(tex, 0)); // Attributes: (short)0, (short)128
        Assert.Equal(0x1450u, BitConverter.ToUInt32(tex, 4));     // A8R8G8B8
        Assert.Equal(8, BitConverter.ToUInt16(tex, 8));
        Assert.Equal(4, BitConverter.ToUInt16(tex, 10));
        Assert.Equal(1, BitConverter.ToUInt16(tex, 12));          // Depth
        Assert.Equal(2, tex[14]);                                 // MipCount
        Assert.Equal(0, tex[15]);                                 // ArraySize
        Assert.Equal(0u, BitConverter.ToUInt32(tex, 16));         // LoD mips 0, 1, 1
        Assert.Equal(1u, BitConverter.ToUInt32(tex, 20));
        Assert.Equal(1u, BitConverter.ToUInt32(tex, 24));
        Assert.Equal(80u, BitConverter.ToUInt32(tex, 28));        // Mip offsets
        Assert.Equal(80u + 128u, BitConverter.ToUInt32(tex, 32));
        for (int i = 36; i < 80; i++)
        {
            Assert.Equal(0, tex[i]);
        }
        Assert.Equal(dds[128..], tex[80..]);

        var xivTex = XivTex.FromUncompressedTex(tex);
        Assert.Equal(XivTexFormat.A8R8G8B8, xivTex.TextureFormat);
        Assert.Equal(2, xivTex.MipMapCount);
        Assert.Equal(0, xivTex.Layers);
        Assert.Equal(rgba, await xivTex.GetRawPixels());
        Assert.Equal(tex, xivTex.ToUncompressedTex());
    }

    [Theory]
    [InlineData(512, 512, 9)]
    [InlineData(1024, 512, 9)]
    [InlineData(256, 1024, 8)]
    [InlineData(1, 1, 1)]
    public async Task Fast8888_MipCountIsLog2OfSmallerSide(int width, int height, int expectedMips)
    {
        var dds = await Tex.ConvertToDDS(new byte[width * height * 4], XivTexFormat.A8R8G8B8, true, width, height);

        Assert.Equal(expectedMips, BitConverter.ToInt32(dds, 28));
        var tex = Tex.DDSToUncompressedTex(dds);
        Assert.Equal(expectedMips, tex[14]);
    }

    [Fact]
    public async Task DDSToUncompressedTex_RejectsNonPowerOfTwoWithMips()
    {
        var dds = await Tex.ConvertToDDS(new byte[12 * 8 * 4], XivTexFormat.A8R8G8B8, true, 12, 8);

        var ex = Assert.Throws<Exception>(() => Tex.DDSToUncompressedTex(dds));
        Assert.StartsWith("Resolution must be a multiple of 2.", ex.Message);
    }

    [Fact]
    public async Task ConvertToDDS_NativeFormatsAreNotPorted()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Tex.ConvertToDDS(new byte[64], XivTexFormat.DXT5, true, 4, 4));
        await Assert.ThrowsAsync<NotSupportedException>(() => Tex.ConvertToDDS(new byte[64], XivTexFormat.BC7, true, 4, 4));
        await Assert.ThrowsAsync<NotSupportedException>(() => Tex.ConvertToDDS(new byte[64], XivTexFormat.A8R8G8B8, true, 4, 4, allowFast8888: false));
        await Assert.ThrowsAsync<InvalidDataException>(() => Tex.ConvertToDDS(new byte[64], XivTexFormat.DXT3, true, 4, 4));
    }

    [Fact]
    public void CreateTexFileHeader_Dxt1_UsesBlockSizedMips()
    {
        var header = Tex.CreateTexFileHeader(XivTexFormat.DXT1, 16, 16, 5);

        Assert.Equal(80, header.Length);
        Assert.Equal(13344u, BitConverter.ToUInt32(header, 4));
        Assert.Equal(5, header[14]);
        Assert.Equal(2u, BitConverter.ToUInt32(header, 24));
        // 16x16: 128, 8x8: 32, 4x4: 8, 2x2 and 1x1 padded to 4x4: 8, 8.
        uint[] expected = [80, 208, 240, 248, 256];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], BitConverter.ToUInt32(header, 28 + i * 4));
        }
        Assert.Equal(0u, BitConverter.ToUInt32(header, 28 + 5 * 4));
    }

    [Fact]
    public void CreateTexFileHeader_SingleMip_LodMipsAreZero()
    {
        var header = Tex.CreateTexFileHeader(XivTexFormat.BC5, 64, 64, 1);

        Assert.Equal(0u, BitConverter.ToUInt32(header, 16));
        Assert.Equal(0u, BitConverter.ToUInt32(header, 20));
        Assert.Equal(0u, BitConverter.ToUInt32(header, 24));
        Assert.Throws<InvalidDataException>(() => Tex.CreateTexFileHeader(XivTexFormat.BC5, 64, 64, 14));
    }

    [Fact]
    public void TexToolsBitsPerPixelQuirksAreKept()
    {
        Assert.Equal(32, XivTexFormat.DXT3.GetBitsPerPixel());
        Assert.Equal(32, XivTexFormat.L8.GetBitsPerPixel());
        Assert.Equal(8, XivTexFormat.A8.GetBitsPerPixel());
    }

    [Fact]
    public void FixUpBrokenMipOffsets_EditsSharedArraysButNotCallersMipCount()
    {
        var header = new Tex.TexHeader
        {
            TextureFormat = (uint)XivTexFormat.A8R8G8B8,
            Width = 4,
            Height = 4,
            Depth = 1,
            MipCount = 3,
            LoDMips = [0, 1, 2],
            MipMapOffsets = new uint[13],
        };
        header.MipMapOffsets[0] = 80;
        header.MipMapOffsets[1] = 999;
        header.MipMapOffsets[2] = 1234;

        // Room for mips 0 (64 bytes) and 1 (16 bytes) only.
        var result = Tex.TexHeader.FixUpBrokenMipOffsets(header, 80 + 64 + 16);

        Assert.True(result.HeaderChanged);
        Assert.Equal(80 + 64 + 16, result.CalculatedTexSize);
        Assert.Equal(3, header.MipCount);
        Assert.Equal(new uint[] { 80, 144, 0 }, header.MipMapOffsets[..3]);
        Assert.Equal(new uint[] { 0, 1, 1 }, header.LoDMips);
    }

    [Fact]
    public void TexHeader_ReadAndWriteRoundTrip()
    {
        var bytes = Tex.CreateTexFileHeader(XivTexFormat.BC7, 256, 128, 8);
        bytes[14] |= 0x30; // mip flag bits

        using var br = new BinaryReader(new MemoryStream(bytes));
        var header = Tex.TexHeader.ReadTexHeader(br);

        Assert.Equal(8, header.MipCount);
        Assert.Equal(3, header.MipFlag);
        Assert.Equal(bytes, header.ToBytes());
    }

    [Fact]
    public async Task ResizeXivTx_8888_TopMipIsBicubicResizeStoredAsBgra()
    {
        var rgba = Pixels(96, 64);
        var bgra = (byte[])rgba.Clone();
        await TextureHelpers.SwizzleRB(bgra, 96, 64);
        var tex = new XivTex { TextureFormat = XivTexFormat.A8R8G8B8, Width = 96, Height = 64, MipMapCount = 7, Layers = 1, TexData = bgra };

        var expected = await TextureHelpers.ResizeImage(rgba, 96, 64, 128, 64);
        await Tex.ResizeXivTx(tex, 128, 64);

        Assert.Equal(128, tex.Width);
        Assert.Equal(64, tex.Height);
        Assert.Equal(XivTexFormat.A8R8G8B8, tex.TextureFormat);
        Assert.Equal(Tex.GetMipCount(128, 64), tex.MipMapCount);
        Assert.Equal(8, tex.MipMapCount);
        Assert.Equal(DDS.CalculateMipMapSizes(XivTexFormat.A8R8G8B8, 128, 64).Take(8).Sum(), tex.TexData.Length);
        Assert.Equal(expected, await tex.GetRawPixels());
        Assert.Equal(80 + tex.TexData.Length, tex.ToUncompressedTex().Length);
    }

    [Fact]
    public async Task ResizeXivTx_BelowCompressorMinimum_Throws()
    {
        var tex = new XivTex { TextureFormat = XivTexFormat.A8R8G8B8, Width = 48, Height = 48, MipMapCount = 1, TexData = new byte[48 * 48 * 4] };

        await Assert.ThrowsAsync<InvalidDataException>(() => Tex.ResizeXivTx(tex, 32, 32));
    }

    [Fact]
    public async Task ResizeXivTx_UnsupportedFormat_Throws()
    {
        var tex = new XivTex { TextureFormat = XivTexFormat.L8, Width = 96, Height = 96, MipMapCount = 1, TexData = new byte[96 * 96] };

        await Assert.ThrowsAsync<InvalidDataException>(() => Tex.ResizeXivTx(tex, 128, 128));
    }

    [Fact]
    public async Task EyeMask_ProducesFourTimesTheMaskSize()
    {
        var baseDiffuse = new XivTex { TextureFormat = XivTexFormat.A8R8G8B8, Width = 32, Height = 32, MipMapCount = 1, TexData = Pixels(32, 32) };
        var frame = new XivTex { TextureFormat = XivTexFormat.A8R8G8B8, Width = 32, Height = 32, MipMapCount = 1, TexData = Pixels(32, 32) };

        var result = await EyeMask.ConvertEyeMaskToDiffuse(Pixels(16, 16), 16, 16, baseDiffuse, frame);

        Assert.Equal(64, result.Width);
        Assert.Equal(64, result.Height);
        Assert.Equal(64 * 64 * 4, result.PixelData.Length);
    }
}
