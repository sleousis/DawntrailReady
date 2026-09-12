// Port of xivModdingFramework Textures/FileTypes/DDS.cs (TexTools, GPL-3.0): format dictionaries,
// CalculateMipMapSizes and ConvertPixelData with its per-format readers. DDS writing (MakeDDS/CreateDDSHeader),
// CompressDDSBody and the texconv.exe helpers are not ported; the upgrade does not reach them.

using System.Text;

namespace DawntrailReady.Core.Textures;

/// <summary>
/// This class deals with dds file types
/// </summary>
public static class DDS
{
    // Flags value indicating DWFourCC has real data.
    internal const uint _DDS_PixelFormatOffset = 76;
    internal const uint _DWFourCCFlag = 0x04;

    internal static uint _DX10 = (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("DX10"), 0);

    /// <summary>
    /// A dictionary containing the int representations of known DDS FourCC Values to Xiv Tex Enum
    /// </summary>
    internal static readonly Dictionary<uint, XivTexFormat> DdsTypeToXivTex = new()
    {
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("DXT1"), 0), XivTexFormat.DXT1 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("DXT3"), 0), XivTexFormat.DXT3 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("DXT5"), 0), XivTexFormat.DXT5 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("ATI1"), 0), XivTexFormat.BC4 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("BC4U"), 0), XivTexFormat.BC4 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("ATI2"), 0), XivTexFormat.BC5 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("BC5U"), 0), XivTexFormat.BC5 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("BC7L"), 0), XivTexFormat.BC7 },
        { (uint)BitConverter.ToInt32(Encoding.ASCII.GetBytes("BC7\0"), 0), XivTexFormat.BC7 },

        // Floating point formats
        { 112, XivTexFormat.G16R16F },
        { 113, XivTexFormat.A16B16G16R16F },
        { 114, XivTexFormat.R32F },
        { 115, XivTexFormat.G32R32F },
        { 116, XivTexFormat.A32B32G32R32F },

        //Uncompressed RGBA
        { 0, XivTexFormat.A8R8G8B8 },
    };

    /// <summary>
    /// A dictionary containing the int representations of known DXGI header extension enum values to Xiv Tex Enum
    /// </summary>
    internal static readonly Dictionary<uint, XivTexFormat> DxgiTypeToXivTex = new()
    {
        { (uint)DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM, XivTexFormat.DXT1 },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM, XivTexFormat.DXT3 },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM, XivTexFormat.DXT5 },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM, XivTexFormat.BC4 },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM, XivTexFormat.BC5 },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM, XivTexFormat.BC7 },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, XivTexFormat.A16B16G16R16F },
        { (uint)DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, XivTexFormat.A8R8G8B8 },
    };

    internal static uint GetDDSType(XivTexFormat format)
    {
        return DdsTypeToXivTex.FirstOrDefault(x => x.Value == format).Key;
    }

    internal static uint GetDxgiType(XivTexFormat format)
    {
        return DxgiTypeToXivTex.FirstOrDefault(x => x.Value == format).Key;
    }

    // Calculate a sequence of mipmap sizes given a texture format and size
    public static List<int> CalculateMipMapSizes(XivTexFormat format, int width, int height)
    {
        var offsets = new List<int>();

        int minDimension = format.GetMipMinDimension();
        int mipBitsPerPixel = format.GetBitsPerPixel();
        int mipWidth = width;
        int mipHeight = height;
        int mipLength = Math.Max(minDimension, mipWidth) * Math.Max(minDimension, mipHeight) * mipBitsPerPixel / 8;

        offsets.Add(mipLength);

        while (mipWidth > 1 || mipHeight > 1)
        {
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
            mipLength = Math.Max(minDimension, mipWidth) * Math.Max(minDimension, mipHeight) * mipBitsPerPixel / 8;
            offsets.Add(mipLength);
        }

        return offsets;
    }

    /// <summary>
    /// Converts the given DDS Compressed pixel data into 8.8.8.8 RGBA pixel data.
    /// As in TexTools, X8R8G8B8, R32F, G16R16F, G32R32F, A32B32G32R32F and D16 data is returned unconverted.
    /// </summary>
    public static async Task<byte[]> ConvertPixelData(byte[] data, int width, int height, XivTexFormat format, int layers = 1, int targetLayer = -1)
    {
        return await Task.Run(async () =>
        {
            byte[] imageData;
            if (layers == 0)
            {
                layers = 1;
            }

            switch (format)
            {
                case XivTexFormat.DXT1:
                    imageData = DxtUtil.DecompressDxt1(data, width, height * layers);
                    break;
                case XivTexFormat.DXT3:
                    imageData = DxtUtil.DecompressDxt3(data, width, height * layers);
                    break;
                case XivTexFormat.DXT5:
                    imageData = DxtUtil.DecompressDxt5(data, width, height * layers);
                    break;
                case XivTexFormat.BC4:
                    imageData = DxtUtil.DecompressBc4(data, width, height * layers);
                    break;
                case XivTexFormat.BC5:
                    imageData = DxtUtil.DecompressBc5(data, width, height * layers);
                    break;
                case XivTexFormat.BC7:
                    imageData = DxtUtil.DecompressBc7(data, width, height * layers);
                    break;
                case XivTexFormat.A4R4G4B4:
                    imageData = await Read4444Image(data, width, height * layers);
                    break;
                case XivTexFormat.A1R5G5B5:
                    imageData = await Read5551Image(data, width, height * layers);
                    break;
                case XivTexFormat.A8R8G8B8:
                    imageData = await SwapRBColors(data, width, height * layers);
                    break;
                case XivTexFormat.L8:
                case XivTexFormat.A8:
                    imageData = await Read8bitImage(data, width, height * layers);
                    break;
                case XivTexFormat.A16B16G16R16F:
                    imageData = await ReadHalfFloatImage(data, width, height * layers);
                    break;
                case XivTexFormat.X8R8G8B8:
                case XivTexFormat.R32F:
                case XivTexFormat.G16R16F:
                case XivTexFormat.G32R32F:
                case XivTexFormat.A32B32G32R32F:
                case XivTexFormat.D16:
                default:
                    imageData = data;
                    break;
            }

            if (targetLayer >= 0)
            {
                var bytesPerLayer = imageData.Length / layers;
                var offset = bytesPerLayer * targetLayer;

                byte[] nData = new byte[bytesPerLayer];
                Array.Copy(imageData, offset, nData, 0, bytesPerLayer);

                imageData = nData;
            }

            return imageData;
        });
    }

    /// <summary>
    /// Creates bitmap from decompressed A1R5G5B5 texture data.
    /// </summary>
    internal static async Task<byte[]> Read5551Image(byte[] textureData, int width, int height)
    {
        var convertedBytes = new List<byte>();

        await Task.Run(() =>
        {
            using var ms = new MemoryStream(textureData);
            using var br = new BinaryReader(ms);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = br.ReadUInt16() & 0xFFFF;

                    var red = ((pixel & 0x7E00) >> 10) * 8;
                    var green = ((pixel & 0x3E0) >> 5) * 8;
                    var blue = ((pixel & 0x1F)) * 8;
                    var alpha = ((pixel & 0x8000) >> 15) * 255;

                    convertedBytes.Add((byte)red);
                    convertedBytes.Add((byte)green);
                    convertedBytes.Add((byte)blue);
                    convertedBytes.Add((byte)alpha);
                }
            }
        });

        return convertedBytes.ToArray();
    }

    /// <summary>
    /// Creates bitmap from decompressed A4R4G4B4 texture data.
    /// </summary>
    internal static async Task<byte[]> Read4444Image(byte[] textureData, int width, int height)
    {
        var convertedBytes = new List<byte>();

        await Task.Run(() =>
        {
            using var ms = new MemoryStream(textureData);
            using var br = new BinaryReader(ms);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = br.ReadUInt16() & 0xFFFF;
                    var red = ((pixel & 0xF)) * 16;
                    var green = ((pixel & 0xF0) >> 4) * 16;
                    var blue = ((pixel & 0xF00) >> 8) * 16;
                    var alpha = ((pixel & 0xF000) >> 12) * 16;

                    convertedBytes.Add((byte)blue);
                    convertedBytes.Add((byte)green);
                    convertedBytes.Add((byte)red);
                    convertedBytes.Add((byte)alpha);
                }
            }
        });

        return convertedBytes.ToArray();
    }

    /// <summary>
    /// Creates bitmap from decompressed A8/L8 texture data.
    /// </summary>
    internal static async Task<byte[]> Read8bitImage(byte[] textureData, int width, int height)
    {
        var convertedBytes = new List<byte>();

        await Task.Run(() =>
        {
            using var ms = new MemoryStream(textureData);
            using var br = new BinaryReader(ms);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = br.ReadByte() & 0xFF;

                    convertedBytes.Add((byte)pixel);
                    convertedBytes.Add((byte)pixel);
                    convertedBytes.Add((byte)pixel);
                    convertedBytes.Add(255);
                }
            }
        });

        return convertedBytes.ToArray();
    }

    // TexTools reads SharpDX.Half values; SharpDX's half -> float table conversion is exact, as is System.Half's.
    internal static async Task<byte[]> ReadHalfFloatImage(byte[] textureData, int width, int height)
    {
        var convertedBytes = new List<byte>();

        await Task.Run(() =>
        {
            using var ms = new MemoryStream(textureData);
            using var br = new BinaryReader(ms);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var r = (float)BitConverter.UInt16BitsToHalf(br.ReadUInt16());
                    var g = (float)BitConverter.UInt16BitsToHalf(br.ReadUInt16());
                    var b = (float)BitConverter.UInt16BitsToHalf(br.ReadUInt16());
                    var a = (float)BitConverter.UInt16BitsToHalf(br.ReadUInt16());

                    // 255 * value, clamped to 0-255.
                    var byteR = (byte)Math.Max(0, Math.Min(255, Math.Round(r * 255.0f)));
                    var byteG = (byte)Math.Max(0, Math.Min(255, Math.Round(g * 255.0f)));
                    var byteB = (byte)Math.Max(0, Math.Min(255, Math.Round(b * 255.0f)));
                    var byteA = (byte)Math.Max(0, Math.Min(255, Math.Round(a * 255.0f)));

                    convertedBytes.Add(byteR);
                    convertedBytes.Add(byteG);
                    convertedBytes.Add(byteB);
                    convertedBytes.Add(byteA);
                }
            }
        });

        return convertedBytes.ToArray();
    }

    /// <summary>
    /// Creates bitmap from decompressed Linear texture data.
    /// </summary>
    internal static async Task<byte[]> SwapRBColors(byte[] textureData, int width, int height)
    {
        var data = new byte[width * height * 4];
        await TextureHelpers.ModifyPixels((int offset) =>
        {
            data[offset + 0] = textureData[offset + 2];
            data[offset + 1] = textureData[offset + 1];
            data[offset + 2] = textureData[offset + 0];
            data[offset + 3] = textureData[offset + 3];
        }, width, height);

        return data;
    }

    /// <summary>
    /// The DXGI_FORMAT values TexTools' dictionaries use (TexTools declares the whole enum).
    /// </summary>
    public enum DXGI_FORMAT : uint
    {
        DXGI_FORMAT_UNKNOWN = 0,
        DXGI_FORMAT_R16G16B16A16_FLOAT = 10,
        DXGI_FORMAT_BC1_UNORM = 71,
        DXGI_FORMAT_BC2_UNORM = 74,
        DXGI_FORMAT_BC3_UNORM = 77,
        DXGI_FORMAT_BC4_UNORM = 80,
        DXGI_FORMAT_BC5_UNORM = 83,
        DXGI_FORMAT_B8G8R8A8_UNORM = 87,
        DXGI_FORMAT_BC7_UNORM = 98,
    }
}
