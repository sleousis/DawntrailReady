using DawntrailReady.Core.Textures;

namespace DawntrailReady.Core.Tests.Textures;

public class DecodeTests
{
    private static byte[] Pixel(byte[] image, int width, int x, int y)
    {
        var o = ((y * width) + x) * 4;
        return image[o..(o + 4)];
    }

    // Colour endpoints red (0xF800) and blue (0x001F); each row uses selectors 0, 1, 2, 3.
    private static readonly byte[] RedBlueColourBlock = [0x00, 0xF8, 0x1F, 0x00, 0xE4, 0xE4, 0xE4, 0xE4];

    [Fact]
    public async Task Bc1_FourColourBlock()
    {
        var image = await DDS.ConvertPixelData(RedBlueColourBlock, 4, 4, XivTexFormat.DXT1);

        Assert.Equal(64, image.Length);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(image, 4, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(image, 4, 1, 0));
        Assert.Equal(new byte[] { 170, 0, 85, 255 }, Pixel(image, 4, 2, 0));
        Assert.Equal(new byte[] { 85, 0, 170, 255 }, Pixel(image, 4, 3, 3));
    }

    [Fact]
    public async Task Bc1_PunchThroughBlock()
    {
        // e0 (blue) <= e1 (red): 3 colours + transparent black.
        byte[] block = [0x1F, 0x00, 0x00, 0xF8, 0xE4, 0xE4, 0xE4, 0xE4];

        var image = await DDS.ConvertPixelData(block, 4, 4, XivTexFormat.DXT1);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(image, 4, 0, 1));
        Assert.Equal(new byte[] { 127, 0, 127, 255 }, Pixel(image, 4, 2, 1));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(image, 4, 3, 1));
    }

    [Fact]
    public async Task Bc1_PartialBlockIsClipped()
    {
        var image = await DDS.ConvertPixelData(RedBlueColourBlock, 2, 2, XivTexFormat.DXT1);

        Assert.Equal(16, image.Length);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(image, 2, 1, 1));
    }

    [Fact]
    public async Task Bc3_EightStepAlphaWithColourBlock()
    {
        // Alpha 255/0 (8 steps); texel 0 selector 0, texel 1 selector 2, texel 2 selector 7, the rest 1.
        // Selector bits (3 per texel, LSB first): 0b000, 0b010, 0b111, then 0b001 for texels 3..15.
        ulong alphaBits = (0UL) | (2UL << 3) | (7UL << 6);
        for (int t = 3; t < 16; t++)
        {
            alphaBits |= 1UL << (3 * t);
        }
        var block = new byte[16];
        block[0] = 255;
        block[1] = 0;
        for (int i = 0; i < 6; i++)
        {
            block[2 + i] = (byte)(alphaBits >> (8 * i));
        }
        RedBlueColourBlock.CopyTo(block, 8);

        var image = await DDS.ConvertPixelData(block, 4, 4, XivTexFormat.DXT5);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(image, 4, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 218 }, Pixel(image, 4, 1, 0)); // (6 * 255) / 7 = 218
        Assert.Equal(new byte[] { 170, 0, 85, 36 }, Pixel(image, 4, 2, 0));  // 255 / 7 = 36
        Assert.Equal(new byte[] { 85, 0, 170, 0 }, Pixel(image, 4, 3, 0));
    }

    [Fact]
    public async Task Bc4_ExpandsToGrey()
    {
        // 6-step block (10 <= 20): selector 6 is 0, 7 is 255, 2 is (4 * 10 + 20) / 5 = 12.
        ulong bits = 6UL | (7UL << 3) | (2UL << 6) | (1UL << 9);
        byte[] block = [10, 20, (byte)bits, (byte)(bits >> 8), 0, 0, 0, 0];

        var image = await DDS.ConvertPixelData(block, 4, 4, XivTexFormat.BC4);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(image, 4, 0, 0));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(image, 4, 1, 0));
        Assert.Equal(new byte[] { 12, 12, 12, 255 }, Pixel(image, 4, 2, 0));
        Assert.Equal(new byte[] { 20, 20, 20, 255 }, Pixel(image, 4, 3, 0));
        Assert.Equal(new byte[] { 10, 10, 10, 255 }, Pixel(image, 4, 0, 1));
    }

    [Fact]
    public async Task Bc5_RedAndGreenWithZeroBlueAndOpaqueAlpha()
    {
        // Red: 200/100 (8 steps), texel 0 selector 2 -> (6 * 200 + 100) / 7 = 185, texel 1 selector 7 -> (200 + 600) / 7 = 114.
        ulong redBits = 2UL | (7UL << 3);
        // Green: 10/20 (6 steps), texel 0 selector 7 -> 255, texel 1 selector 3 -> (3 * 10 + 2 * 20) / 5 = 14.
        ulong greenBits = 7UL | (3UL << 3);
        var block = new byte[16];
        block[0] = 200;
        block[1] = 100;
        block[2] = (byte)redBits;
        block[3] = (byte)(redBits >> 8);
        block[8] = 10;
        block[9] = 20;
        block[10] = (byte)greenBits;
        block[11] = (byte)(greenBits >> 8);

        var image = await DDS.ConvertPixelData(block, 4, 4, XivTexFormat.BC5);

        Assert.Equal(new byte[] { 185, 255, 0, 255 }, Pixel(image, 4, 0, 0));
        Assert.Equal(new byte[] { 114, 14, 0, 255 }, Pixel(image, 4, 1, 0));
        Assert.Equal(new byte[] { 200, 10, 0, 255 }, Pixel(image, 4, 2, 0));
    }

    [Fact]
    public async Task Bc7_Mode6Block()
    {
        // Endpoint 0: r=127, g=0, b=64, a=127, p0=1 -> (255, 1, 129, 255).
        // Endpoint 1: r=0, g=127, b=0, a=127, p1=0 -> (0, 254, 0, 254).
        ulong lo = 0x40UL | (127UL << 7) | (0UL << 14) | (0UL << 21) | (127UL << 28) | (64UL << 35) | (0UL << 42)
                   | (127UL << 49) | (127UL << 56) | (1UL << 63);
        // Pixel 0 selector 0, pixel 1 selector 15, pixel 2 selector 8 (weight 34).
        ulong hi = 0UL | (15UL << 4) | (8UL << 8);
        var block = new byte[16];
        BitConverter.GetBytes(lo).CopyTo(block, 0);
        BitConverter.GetBytes(hi).CopyTo(block, 8);

        var image = await DDS.ConvertPixelData(block, 4, 4, XivTexFormat.BC7);

        Assert.Equal(new byte[] { 255, 1, 129, 255 }, Pixel(image, 4, 0, 0));
        Assert.Equal(new byte[] { 0, 254, 0, 254 }, Pixel(image, 4, 1, 0));
        // (e0 * 30 + e1 * 34 + 32) >> 6
        Assert.Equal(new byte[] { 120, 135, 60, 254 }, Pixel(image, 4, 2, 0));
        Assert.Equal(new byte[] { 255, 1, 129, 255 }, Pixel(image, 4, 3, 3));
    }

    [Fact]
    public async Task Bc7_ReservedModeDecodesToTransparentBlack()
    {
        var block = new byte[16];
        block[5] = 0xFF;

        var image = await DDS.ConvertPixelData(block, 4, 4, XivTexFormat.BC7);

        Assert.All(image, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task A8R8G8B8_BgraBecomesRgba()
    {
        byte[] bgra = [1, 2, 3, 4, 5, 6, 7, 8];

        var image = await DDS.ConvertPixelData(bgra, 2, 1, XivTexFormat.A8R8G8B8);

        Assert.Equal(new byte[] { 3, 2, 1, 4, 7, 6, 5, 8 }, image);
    }

    [Fact]
    public async Task L8AndA8_BecomeOpaqueGrey()
    {
        Assert.Equal(new byte[] { 9, 9, 9, 255, 200, 200, 200, 255 }, await DDS.ConvertPixelData([9, 200], 2, 1, XivTexFormat.L8));
        Assert.Equal(new byte[] { 9, 9, 9, 255 }, await DDS.ConvertPixelData([9], 1, 1, XivTexFormat.A8));
    }

    [Fact]
    public async Task A4R4G4B4_And_A1R5G5B5()
    {
        // A=F, R=8, G=C, B=4.
        var p4444 = BitConverter.GetBytes((ushort)0xF8C4);
        Assert.Equal(new byte[] { 128, 192, 64, 240 }, await DDS.ConvertPixelData(p4444, 1, 1, XivTexFormat.A4R4G4B4));

        // A=1, R=31, G=0, B=15.
        var p5551 = BitConverter.GetBytes((ushort)(0x8000 | (31 << 10) | 15));
        Assert.Equal(new byte[] { 248, 0, 120, 255 }, await DDS.ConvertPixelData(p5551, 1, 1, XivTexFormat.A1R5G5B5));
    }

    [Fact]
    public async Task A16B16G16R16F_RoundsHalfToEvenAndClamps()
    {
        // R=1.0, G=0.5 (127.5 -> 128), B=-1.0 (-> 0), A=2.0 (-> 255).
        var data = new byte[8];
        BitConverter.GetBytes((ushort)0x3C00).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)0x3800).CopyTo(data, 2);
        BitConverter.GetBytes((ushort)0xBC00).CopyTo(data, 4);
        BitConverter.GetBytes((ushort)0x4000).CopyTo(data, 6);

        Assert.Equal(new byte[] { 255, 128, 0, 255 }, await DDS.ConvertPixelData(data, 1, 1, XivTexFormat.A16B16G16R16F));
    }

    [Fact]
    public async Task UnconvertedFormatsReturnRawData()
    {
        byte[] raw = [1, 2, 3, 4];

        Assert.Same(raw, await DDS.ConvertPixelData(raw, 1, 1, XivTexFormat.X8R8G8B8));
        Assert.Same(raw, await DDS.ConvertPixelData(raw, 1, 1, XivTexFormat.R32F));
    }

    [Fact]
    public async Task TargetLayer_SlicesOneLayer()
    {
        byte[] l8 = [10, 20];

        var layer1 = await DDS.ConvertPixelData(l8, 1, 1, XivTexFormat.L8, layers: 2, targetLayer: 1);

        Assert.Equal(new byte[] { 20, 20, 20, 255 }, layer1);
    }
}
